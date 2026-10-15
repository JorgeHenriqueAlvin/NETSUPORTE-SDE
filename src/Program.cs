using System.Net;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SdeSuporte;

static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try {
#if AGENT_ONLY
            Application.Run(new Agent());
#elif TECHNICIAN_ONLY
            Application.Run(new Technician());
#else
            Application.Run(args.Contains("--agent") ? new Agent() : new Technician());
#endif
        }
        catch (Exception e) { MessageBox.Show(e.Message, "SDE Suporte — falha ao iniciar"); }
    }
}
record Config(string Hostname, string Sector, string[] AllowedGroupSids, int Port = 7443, bool RequireConsent = true);
record Session(string Id, string User, DateTimeOffset Expires, DateTimeOffset Started);
record InputCommand(string Kind, int X = 0, int Y = 0, string Text = "");
record Machine(string Hostname, string Sector);

sealed class Agent : Form
{
    readonly Config config;
    readonly HttpListener listener = new();
    readonly SemaphoreSlim serial = new(1,1);
    readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    Session? session;
    bool pending;
    DateTimeOffset lastActivity;
    readonly NotifyIcon tray = new();
    volatile bool unavailable;
    readonly string log;
    readonly System.Windows.Forms.Timer expiry = new() { Interval = 1000 };
    public Agent()
    {
        Text = "SDE Suporte — identificação do atendimento";
        Width = 580; Height = 125; TopMost = true; ControlBox = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Screen.PrimaryScreen!.WorkingArea.Right - Width - 12,12);
        BackColor = Color.FromArgb(12,40,68); status.ForeColor = Color.White;
        status.Font = new Font("Segoe UI",11);
        tray.Icon = SystemIcons.Information; tray.Text = "SDE Suporte — agente instalado"; tray.Visible = true;
        Shown += (_,_) => Hide();
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"agent.json")))
            ?? throw new Exception("Configuração ausente.");
        if (config.AllowedGroupSids.Length == 0) throw new Exception("Configure os SIDs dos grupos autorizados.");
        foreach (var sid in config.AllowedGroupSids) _ = new SecurityIdentifier(sid);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte");
        Directory.CreateDirectory(folder); log = Path.Combine(folder,"audit.jsonl");
        Controls.Add(status);
        status.Text = $"Setor: {config.Sector}\nAguardando solicitação. Nenhuma tela está sendo transmitida.";
        listener.Prefixes.Add($"https://{config.Hostname}:{config.Port}/");
        listener.AuthenticationSchemes = AuthenticationSchemes.Negotiate;
        listener.Start(); _ = Listen();
        expiry.Tick += (_,_) => { if (session is not null && (session.Expires <= DateTimeOffset.UtcNow || DateTimeOffset.UtcNow-lastActivity > TimeSpan.FromSeconds(20))) End("expired_or_disconnected"); };
        expiry.Start();
        Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        FormClosed += (_,_) => Microsoft.Win32.SystemEvents.SessionSwitch -= SessionChanged;
        FormClosed += (_,_) => { End("agent_closed"); listener.Close(); tray.Dispose(); expiry.Dispose(); };
    }
    void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect or Microsoft.Win32.SessionSwitchReason.RemoteConnect) unavailable = false;
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.SessionLogoff or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect)
        { unavailable = true; if (!IsDisposed) BeginInvoke(new Action(() => End("workstation_unavailable"))); }
    }
    void Audit(string action, string user, string detail = "") => File.AppendAllText(log,
        JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, action, user, machine = Environment.MachineName, sector = config.Sector, detail }) + "\n");
    void End(string reason)
    {
        if (session is not null) { Audit(reason,session.User); session = null; }
        status.Text = "Nenhum atendimento ativo.";
        tray.Text = "SDE Suporte — aguardando atendimento"; Hide();
    }
    Task<T> OnUi<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(new Action(() => { try { tcs.SetResult(action()); } catch(Exception e) { tcs.SetException(e); } }));
        return tcs.Task;
    }
    async Task Listen()
    {
        while (listener.IsListening)
        {
            try { var ctx = await listener.GetContextAsync(); _ = Handle(ctx); }
            catch (HttpListenerException) { break; } catch (ObjectDisposedException) { break; }
        }
    }
    async Task Reply(HttpListenerResponse r, int code, object? body = null)
    {
        r.StatusCode = code; r.ContentType = "application/json";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(body ?? new { });
        r.ContentLength64 = bytes.Length; await r.OutputStream.WriteAsync(bytes);
    }
    async Task Handle(HttpListenerContext c)
    {
        // Only a single active request can manipulate the session at once; do not queue unbounded work.
        if (!await serial.WaitAsync(0)) { try { await Reply(c.Response,429); } finally { c.Response.Close(); } return; }
        string user = c.User?.Identity?.Name ?? "unknown";
        try
        {
            if (c.User?.Identity is not WindowsIdentity identity || !identity.IsAuthenticated)
            { await Reply(c.Response,401); return; }
            var principal = new WindowsPrincipal(identity);
            if (!config.AllowedGroupSids.Any(s => principal.IsInRole(new SecurityIdentifier(s))))
            { Audit("denied",user); await Reply(c.Response,403); return; }
            var path = c.Request.Url!.AbsolutePath;
            var method = c.Request.HttpMethod;
            if (path == "/info" && method == "GET")
            { await Reply(c.Response,200,new { machine = Environment.MachineName, config.Sector, os = Environment.OSVersion.ToString(), memoryBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes }); return; }
            if (path == "/session" && method == "POST")
            {
                if (session is not null || pending || unavailable) { await Reply(c.Response,409); return; }
                pending = true;
                bool accepted;
                try {
                    Audit("requested",user);
                    accepted = !config.RequireConsent || await OnUi(() => {
                        using var prompt = new Consent(user);
                        return prompt.ShowDialog() == DialogResult.Yes;
                    });
                } finally { pending = false; }
                if (!accepted || unavailable) { Audit("rejected",user); await Reply(c.Response,403); return; }
                var current = new Session(Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),user,DateTimeOffset.UtcNow.AddMinutes(15),DateTimeOffset.UtcNow);
                await OnUi(() => {
                    if (unavailable) throw new InvalidOperationException("Estação indisponível.");
                    session = current; lastActivity = DateTimeOffset.UtcNow;
                    status.Text = $"ATENDIMENTO REMOTO EM ANDAMENTO\nTécnico: {user}\nInício: {current.Started.ToLocalTime():HH:mm:ss}  •  Setor: {config.Sector}";
                    tray.Text = "SDE Suporte — atendimento ativo"; Show();
                    return true;
                });
                Audit("started",user); await Reply(c.Response,200,current); return;
            }
            var active = session;
            if (active is null || active.Expires <= DateTimeOffset.UtcNow || active.User != user || c.Request.Headers["X-Session"] != active.Id)
            { await Reply(c.Response,403); return; }
            lastActivity = DateTimeOffset.UtcNow;
            if (path == "/session/end" && method == "POST")
            { await OnUi(() => { End("technician_stop"); return true; }); await Reply(c.Response,200); return; }
            if (path == "/frame" && method == "GET")
            {
                var bytes = await OnUi(() => {
                    if (session != active || unavailable) throw new InvalidOperationException("Sessão encerrada.");
                    var bounds = Screen.PrimaryScreen!.Bounds;
                    using var bmp = new Bitmap(bounds.Width,bounds.Height);
                    using var g = Graphics.FromImage(bmp); g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
                    using var ms = new MemoryStream(); bmp.Save(ms,ImageFormat.Jpeg); return ms.ToArray();
                });
                c.Response.ContentType = "image/jpeg"; c.Response.ContentLength64 = bytes.Length;
                await c.Response.OutputStream.WriteAsync(bytes); return;
            }
            if (path == "/input" && method == "POST")
            {
                if (c.Request.ContentLength64 is < 0 or > 8192) { await Reply(c.Response,413); return; }
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var input = await JsonSerializer.DeserializeAsync<InputCommand>(c.Request.InputStream,new JsonSerializerOptions(JsonSerializerDefaults.Web),timeout.Token);
                if (input is null || input.Text is null || input.Text.Length > 1000 || !new[]{"click","rightClick","text","enter","backspace","tab","escape"}.Contains(input.Kind))
                { await Reply(c.Response,400); return; }
                await OnUi(() => { if (session != active || unavailable) throw new InvalidOperationException("Sessão encerrada."); Native.Apply(input); return true; });
                Audit("input",user,input.Kind); await Reply(c.Response,200); return;
            }
            await Reply(c.Response,404);
        }
        catch(Exception e) { try { Audit("error",user,e.GetType().Name); await Reply(c.Response,500,new { error = "Falha no agente. Consulte o log local." }); } catch { } }
        finally { c.Response.Close(); serial.Release(); }
    }
}

sealed class Consent : Form
{
    public Consent(string user)
    {
        Text = "SDE Suporte — autorizar atendimento"; Width = 520; Height = 220;
        ControlBox = false; StartPosition = FormStartPosition.CenterParent;
        var label = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Text = $"{user} solicita visualizar e controlar este computador.\nDuração máxima: 15 minutos.\nEsta solicitação expira em 30 segundos." };
        var yes = new Button { Text = "Autorizar", DialogResult = DialogResult.Yes, Dock = DockStyle.Bottom, Height = 40 };
        var no = new Button { Text = "Recusar", DialogResult = DialogResult.No, Dock = DockStyle.Bottom, Height = 40 };
        Controls.Add(label); Controls.Add(yes); Controls.Add(no); CancelButton = no; ActiveControl = no;
        var timer = new System.Windows.Forms.Timer { Interval = 30000 };
        timer.Tick += (_,_) => { DialogResult = DialogResult.No; Close(); };
        Shown += (_,_) => timer.Start(); FormClosed += (_,_) => timer.Dispose();
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public UNION data; }
    [StructLayout(LayoutKind.Explicit)] struct UNION { [FieldOffset(0)] public MOUSE mouse; [FieldOffset(0)] public KEY key; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSE { public int dx,dy; public uint mouseData,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEY { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count, INPUT[] inputs,int size);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    static void Send(params INPUT[] values) { if (SendInput((uint)values.Length,values,Marshal.SizeOf<INPUT>()) != values.Length) throw new Exception("Windows bloqueou a entrada."); }
    static INPUT Key(ushort vk,ushort scan,uint flags) => new() { type = 1, data = new UNION { key = new KEY { vk = vk, scan = scan, flags = flags } } };
    public static void Apply(InputCommand cmd)
    {
        if (cmd.Kind is "click" or "rightClick") {
            var b = Screen.PrimaryScreen!.Bounds;
            if (cmd.X < 0 || cmd.Y < 0 || cmd.X >= b.Width || cmd.Y >= b.Height) throw new ArgumentOutOfRangeException();
            SetCursorPos(cmd.X + b.X,cmd.Y + b.Y);
            uint down = cmd.Kind == "click" ? 2u : 8u;
            Send(new INPUT { data = new UNION { mouse = new MOUSE { flags = down } } },new INPUT { data = new UNION { mouse = new MOUSE { flags = down * 2 } } });
        } else if (cmd.Kind == "text") {
            foreach(char ch in cmd.Text) Send(Key(0,ch,4),Key(0,ch,6));
        } else {
            ushort vk = cmd.Kind switch { "enter" => 13, "tab" => 9, "backspace" => 8, _ => 27 };
            Send(Key(vk,0,0),Key(vk,0,2));
        }
    }
}

sealed class Technician : Form
{
    readonly HttpClient http = new(new HttpClientHandler { UseDefaultCredentials = true, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
    readonly ListBox machines = new() { Dock = DockStyle.Left, Width = 260, DisplayMember = "Hostname" };
    readonly PictureBox picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(20,30,48) };
    readonly Label status = new() { Dock = DockStyle.Bottom, Height = 30 };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 750 };
    readonly Button connect = new() { Text = "Conectar" };
    readonly TextBox text = new() { Width = 220, PlaceholderText = "Texto para digitar remotamente" };
    string? endpoint, token; bool loading;
    public Technician()
    {
        Text = "SDE Suporte — console do técnico"; Width = 1280; Height = 800;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 80, BackColor = Color.WhiteSmoke };
        var disconnect = new Button { Text = "Encerrar" };
        bar.Controls.AddRange(new Control[]{connect,disconnect,text});
        foreach(var kind in new[]{"text","enter","tab","backspace","escape"}) {
            var button = new Button { Text = kind == "text" ? "Enviar texto" : kind };
            button.Click += async (_,_) => await Safe(async () => { await Input(new(kind,Text:kind == "text" ? text.Text : "")); if(kind == "text") text.Clear(); });
            bar.Controls.Add(button);
        }
        Controls.Add(picture); Controls.Add(machines); Controls.Add(bar); Controls.Add(status);
        var path = Path.Combine(AppContext.BaseDirectory,"machines.json");
        if(File.Exists(path)) foreach(var m in JsonSerializer.Deserialize<Machine[]>(File.ReadAllText(path)) ?? []) machines.Items.Add(m);
        connect.Click += async (_,_) => await Safe(Connect);
        disconnect.Click += async (_,_) => await Safe(Disconnect);
        timer.Tick += async (_,_) => { if (!loading && token is not null) await Safe(Frame); };
        picture.MouseClick += async (_,e) => {
            if (picture.Image is not Image img || token is null) return;
            double scale = Math.Min((double)picture.ClientSize.Width/img.Width,(double)picture.ClientSize.Height/img.Height);
            var x = (e.X-(picture.ClientSize.Width-img.Width*scale)/2)/scale;
            var y = (e.Y-(picture.ClientSize.Height-img.Height*scale)/2)/scale;
            if(x>=0 && y>=0 && x<img.Width && y<img.Height)
                await Safe(() => Input(new(e.Button == MouseButtons.Right ? "rightClick" : "click",(int)x,(int)y)));
        };
        bool closing = false;
        FormClosing += async (_,e) => {
            if (closing) return;
            e.Cancel = true; closing = true; Enabled = false;
            try { await Disconnect(); } catch { } finally { Close(); }
        };
        FormClosed += (_,_) => { timer.Stop(); timer.Dispose(); http.Dispose(); picture.Image?.Dispose(); };
        status.Text = "Conta: " + WindowsIdentity.GetCurrent().Name + " | Selecione um computador.";
    }
    async Task Safe(Func<Task> f) { try { await f(); } catch(Exception e) { timer.Stop(); status.Text = "Falha: " + e.Message; } }
    async Task<HttpResponseMessage> Request(HttpMethod method,string path,object? body=null) {
        using var req = new HttpRequestMessage(method,endpoint + path);
        if(token is not null) req.Headers.Add("X-Session",token);
        if(body is not null) req.Content = JsonContent.Create(body);
        var response = await http.SendAsync(req);
        if(!response.IsSuccessStatusCode) { var code = response.StatusCode; response.Dispose(); throw new Exception($"HTTP {(int)code}. Verifique autorização, confirmação e sessão."); }
        return response;
    }
    async Task Connect() {
        connect.Enabled=false;
        try {
            await Disconnect();
            if(machines.SelectedItem is not Machine m) throw new Exception("Selecione uma máquina.");
            if(Uri.CheckHostName(m.Hostname) != UriHostNameType.Dns) throw new Exception("Use o nome DNS completo, não IP.");
            endpoint = $"https://{m.Hostname}:7443";
            using var response = await Request(HttpMethod.Post,"/session");
            var s = await response.Content.ReadFromJsonAsync<Session>() ?? throw new Exception("Resposta inválida.");
            token = s.Id; status.Text = $"Conectado: {m.Hostname} — {m.Sector} | até {s.Expires.ToLocalTime():HH:mm}";
            timer.Start(); await Frame();
        } finally { connect.Enabled=true; }
    }
    async Task Disconnect() {
        timer.Stop();
        try { if(token is not null) { using var r = await Request(HttpMethod.Post,"/session/end"); } }
        finally { token=null; var old=picture.Image; picture.Image=null; old?.Dispose(); }
    }
    async Task Frame() {
        loading=true;
        try { using var r = await Request(HttpMethod.Get,"/frame"); using var stream = await r.Content.ReadAsStreamAsync(); using var img = Image.FromStream(stream);
            var old=picture.Image; picture.Image=new Bitmap(img); old?.Dispose();
        } finally { loading=false; }
    }
    async Task Input(InputCommand cmd) { if(token is null) throw new Exception("Conecte primeiro."); using var r = await Request(HttpMethod.Post,"/input",cmd); }
}
