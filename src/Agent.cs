using System.Drawing.Imaging;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
namespace SdeSuporte;
sealed class Agent:Form
{
    readonly AgentConfig config;
    readonly RequestVerifier verifier;
    readonly HttpListener listener=new();
    readonly SemaphoreSlim serial=new(1,1);
    static readonly ImageCodecInfo jpeg=ImageCodecInfo.GetImageEncoders().Single(x=>x.FormatID==ImageFormat.Jpeg.Guid);
    readonly Label status=new(){Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter};
    readonly NotifyIcon tray=new(){Icon=SystemIcons.Information,Visible=true,Text="SDE Suporte — disponível"};
    readonly System.Windows.Forms.Timer clock=new(){Interval=1000};
    readonly string log;
    AgentDiscovery? discovery;
    RemoteSession? session;
    MessageHistory chatHistory=new();
    EmployeeChat? chat;
    EmployeeAnnouncements? announcements;
    readonly Dictionary<string,DateTimeOffset> announcementIds=[];
    readonly Dictionary<string,DateTimeOffset> lastAnnouncement=[];
    DateTimeOffset activity;
    bool unavailable;
    readonly HashSet<int> heldKeys=[];
    readonly HashSet<int> heldMouse=[];
    public Agent(string path)
    {
        config=Json.Read<AgentConfig>(path);config.Device.Validate();verifier=new(config.Technicians,config.Device.Id,
            config.Enrollment?.Approval.Permission.TeamWide==true?config.Enrollment.Approval.Permission.Issuer:null);
        Text="SDE Suporte — atendimento";Width=580;Height=130;TopMost=true;ControlBox=false;ShowInTaskbar=false;
        FormBorderStyle=FormBorderStyle.FixedToolWindow;StartPosition=FormStartPosition.Manual;
        Location=new Point(Screen.PrimaryScreen!.WorkingArea.Right-Width-12,12);
        BackColor=Color.FromArgb(12,40,68);status.ForeColor=Color.White;status.Font=new Font("Segoe UI",11);Controls.Add(status);
        var logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte","logs");
        Directory.CreateDirectory(logs);log=Path.Combine(logs,"audit.jsonl");
        listener.Prefixes.Add($"https://+:{config.Device.Port}/");listener.AuthenticationSchemes=AuthenticationSchemes.Anonymous;
        listener.Start();
        if(config.Enrollment is not null)discovery=new AgentDiscovery(config);
        var menu=new ContextMenuStrip();
        menu.Items.Add("Mensagens do atendimento",null,(_,_)=>{if(chat is not null){chat.Show();chat.Activate();}});
        menu.Items.Add("Avisos da TI",null,(_,_)=>{if(announcements is not null){announcements.Show();announcements.Activate();}});
        tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>{if(chat is not null){chat.Show();chat.Activate();}};
        Shown+=(_,_)=>{Hide();_=Listen();};
        clock.Tick+=(_,_)=>{if(session is not null && (session.Expires<=DateTimeOffset.UtcNow||DateTimeOffset.UtcNow-activity>TimeSpan.FromSeconds(20)))End("expired_or_disconnected");};clock.Start();
        Microsoft.Win32.SystemEvents.SessionSwitch+=SessionChanged;
        FormClosed+=(_,_)=>{End("agent_closed");Microsoft.Win32.SystemEvents.SessionSwitch-=SessionChanged;listener.Close();discovery?.Dispose();chat?.Dispose();announcements?.Dispose();menu.Dispose();tray.Dispose();clock.Dispose();};
    }
    void SessionChanged(object sender,Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if(InvokeRequired){BeginInvoke(new Action(()=>SessionChanged(sender,e)));return;}
        if(e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock or Microsoft.Win32.SessionSwitchReason.SessionLogoff or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect)
        {unavailable=true;End("workstation_unavailable");}
        if(e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect or Microsoft.Win32.SessionSwitchReason.RemoteConnect)unavailable=false;
    }
    void Audit(string action,string technician,string detail="")=>File.AppendAllText(log,JsonSerializer.Serialize(new{at=DateTimeOffset.UtcNow,action,technician,machine=config.Device.Name,config.Device.Sector,detail},new JsonSerializerOptions(Json.Options){WriteIndented=false})+"\n");
    void End(string reason)
    {
        var previous=session;session=null;
        if(previous is not null)
        {
            foreach(int k in heldKeys)try{Native.Apply(new("keyUp",Key:k));}catch{}
            foreach(int b in heldMouse)try{Native.Apply(new("up",Key:b));}catch{}
            heldKeys.Clear();heldMouse.Clear();
            try{Audit(reason,previous.TechnicianName);}catch{}
        }
        chat?.Dispose();chat=null;chatHistory=new();
        Hide();tray.Text="SDE Suporte — disponível";
    }
    async Task Listen()
    {
        while(listener.IsListening)
        {
            try{var c=await listener.GetContextAsync();_=Handle(c);}catch(HttpListenerException){break;}catch(ObjectDisposedException){break;}
        }
    }
    async Task Reply(HttpListenerResponse r,int code,object value)
    {
        r.StatusCode=code;r.ContentType="application/json";r.Headers["Cache-Control"]="no-store";
        var data=JsonSerializer.SerializeToUtf8Bytes(value,Json.Options);r.ContentLength64=data.Length;await r.OutputStream.WriteAsync(data);
    }
    async Task Handle(HttpListenerContext c)
    {
        if(!await serial.WaitAsync(0)){try{await Reply(c.Response,429,new{error="Agente ocupado. Tente novamente."});}catch{}finally{c.Response.Close();}return;}
        bool locked=true;
        string user="não autenticado";
        try
        {
            // Public, signed enrollment metadata only. The scanner checks the trusted
            // team signature and binds the advertised pin to the actual TLS certificate.
            if(c.Request.HttpMethod=="GET"&&c.Request.RawUrl=="/discover"&&config.Enrollment is not null)
            {await Reply(c.Response,200,new DiscoveryMessage("SDE-DISCOVERY-1",config.Enrollment));return;}
            int bodyLimit=c.Request.Url!.AbsolutePath is "/chat" or "/announcement"?16384:8192;
            if((c.Request.HasEntityBody && c.Request.ContentLength64<0) || c.Request.ContentLength64>bodyLimit){await Reply(c.Response,413,new{error="Requisição grande ou sem tamanho."});return;}
            byte[] body=new byte[(int)Math.Max(0,c.Request.ContentLength64)];
            using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5)))await c.Request.InputStream.ReadExactlyAsync(body,timeout.Token);
            string H(string key)=>c.Request.Headers[key]??"";
            var tech=verifier.Verify(H("X-Technician"),H("X-Device"),c.Request.HttpMethod,c.Request.RawUrl??"/",body,H("X-Session"),H("X-Time"),H("X-Nonce"),H("X-Signature"),H("X-Team-Credential"));
            if(tech is null){await Reply(c.Response,403,new{error="Técnico não autorizado. Verifique o código da equipe no console."});return;}
            user=tech.Name;var path=c.Request.Url!.AbsolutePath;var method=c.Request.HttpMethod;
            if(path=="/info" && method=="GET") {await Reply(c.Response,200,new{config.Device.Id,config.Device.Name,config.Device.Sector,version="0.9.0",capabilities=new[]{"chat","announcements"},available=!unavailable&&Native.DesktopAvailable()});return;}
            if(path=="/announcement"&&method=="POST")
            {
                var notice=JsonSerializer.Deserialize<AnnouncementRequest>(body,Json.Options);
                if(notice is null||!Guid.TryParseExact(notice.Id,"D",out _)){await Reply(c.Response,400,new{error="Aviso inválido."});return;}
                string value;
                try{value=MessageHistory.Validate(notice.Text);}catch(ArgumentException e){await Reply(c.Response,400,new{error=e.Message});return;}
                string key=tech.Id+notice.Id;
                if(announcementIds.ContainsKey(key)){await Reply(c.Response,200,new{received=true,duplicate=true});return;}
                var now=DateTimeOffset.UtcNow;
                if(lastAnnouncement.TryGetValue(tech.Id,out var previous)&&now-previous<TimeSpan.FromSeconds(3))
                {await Reply(c.Response,429,new{error="Aguarde três segundos entre avisos."});return;}
                foreach(var expired in announcementIds.Where(x=>now-x.Value>TimeSpan.FromHours(1)).Select(x=>x.Key).ToArray())announcementIds.Remove(expired);
                if(announcementIds.Count>=512)announcementIds.Remove(announcementIds.MinBy(x=>x.Value).Key);
                // One reusable, plain-text window; receiving a notice never authorizes screen/input access.
                Audit("announcement_received",user,notice.Id);
                announcements??=new EmployeeAnnouncements();
                announcements.Receive(new(0,user,value,now));
                announcementIds[key]=now;lastAnnouncement[tech.Id]=now;
                await Reply(c.Response,200,new{received=true,duplicate=false});return;
            }
            if(path=="/session" && method=="POST")
            {
                if(session is not null){await Reply(c.Response,409,new{error="Computador já está em atendimento."});return;}
                if(unavailable||!Native.DesktopAvailable()){await Reply(c.Response,409,new{error="O funcionário precisa estar conectado e com a área de trabalho desbloqueada."});return;}
                if(config.RequireConsent){using var consent=new Consent(user);if(consent.ShowDialog()!=DialogResult.Yes){Audit("declined",user);await Reply(c.Response,403,new{error="Atendimento recusado."});return;}}
                if(unavailable||!Native.DesktopAvailable()){await Reply(c.Response,409,new{error="Computador indisponível."});return;}
                var active=new RemoteSession(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),tech.Id,tech.Name,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(1));
                Audit("started",user);session=active;activity=DateTimeOffset.UtcNow;
                chatHistory=new();chat?.Dispose();chat=new EmployeeChat(tech.Name);
                chat.Messages.Send=value=>{
                    if(session?.Id!=active.Id)throw new InvalidOperationException("Atendimento encerrado.");
                    var message=chatHistory.Add(Environment.UserName,value);Audit("chat_employee",active.TechnicianName);
                    chat.Receive(message,false);return Task.CompletedTask;
                };
                status.Text=$"ATENDIMENTO REMOTO EM ANDAMENTO\nTécnico: {active.TechnicianName}\nInício: {active.Started.ToLocalTime():HH:mm:ss} • {config.Device.Sector}";Show();
                tray.Text="SDE Suporte — atendimento ativo";await Reply(c.Response,200,active);return;
            }
            var current=session;
            if(current is null||current.TechnicianId!=tech.Id||current.Id!=H("X-Session")||current.Expires<=DateTimeOffset.UtcNow)
            {await Reply(c.Response,403,new{error="Sessão encerrada. Conecte novamente."});return;}
            if(unavailable||!Native.DesktopAvailable()){End("desktop_unavailable");await Reply(c.Response,409,new{error="Área de trabalho indisponível."});return;}
            activity=DateTimeOffset.UtcNow;
            if(path=="/session/end"&&method=="POST"){End("technician_stop");await Reply(c.Response,200,new{ok=true});return;}
            if(path=="/chat"&&method=="GET")
            {
                if(!long.TryParse(c.Request.QueryString["after"]??"0",out var after)||after<0){await Reply(c.Response,400,new{error="Cursor inválido."});return;}
                await Reply(c.Response,200,chatHistory.After(after));return;
            }
            if(path=="/chat"&&method=="POST")
            {
                var payload=JsonSerializer.Deserialize<MessageText>(body,Json.Options);
                SupportMessage message;
                try{message=chatHistory.Add(user,payload?.Text??"");}catch(ArgumentException e){await Reply(c.Response,400,new{error=e.Message});return;}
                Audit("chat_technician",user);chat?.Receive(message,true);
                await Reply(c.Response,200,message);return;
            }
            if(path=="/frame"&&method=="GET")
            {
                var bounds=SystemInformation.VirtualScreen;
                using var bmp=new Bitmap(bounds.Width,bounds.Height);using var graphics=Graphics.FromImage(bmp);
                graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
                using var data=new MemoryStream();
                var encoder=jpeg;
                using var parameters=new EncoderParameters(1);parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,60L);
                // Release authenticated state serialization before encoding and network transfer.
                serial.Release();locked=false;
                await Task.Run(()=>bmp.Save(data,encoder,parameters));c.Response.ContentType="image/jpeg";c.Response.Headers["Cache-Control"]="no-store";
                c.Response.ContentLength64=data.Length;await c.Response.OutputStream.WriteAsync(data.ToArray());return;
            }
            if(path=="/input"&&method=="POST")
            {
                var input=JsonSerializer.Deserialize<InputCommand>(body,Json.Options);
                if(input is null||input.Text is null||input.Text.Length>1000){await Reply(c.Response,400,new{error="Entrada inválida."});return;}
                Native.Apply(input);
                if(input.Kind=="keyDown")heldKeys.Add(input.Key);if(input.Kind=="keyUp")heldKeys.Remove(input.Key);
                if(input.Kind=="down")heldMouse.Add(input.Key);if(input.Kind=="up")heldMouse.Remove(input.Key);
                await Reply(c.Response,200,new{ok=true});return;
            }
            await Reply(c.Response,404,new{error="Operação inexistente."});
        }
        catch(Exception e){try{Audit("error",user,e.GetType().Name);await Reply(c.Response,500,new{error=e.Message});}catch{}}
        finally{c.Response.Close();if(locked)serial.Release();}
    }
}
sealed class Consent:Form
{
    public Consent(string user)
    {
        Text="SDE Suporte — autorizar atendimento";Width=500;Height=220;ControlBox=false;StartPosition=FormStartPosition.CenterScreen;TopMost=true;
        var yes=new Button{Text="Autorizar",DialogResult=DialogResult.Yes,Dock=DockStyle.Bottom,Height=40};
        var no=new Button{Text="Recusar",DialogResult=DialogResult.No,Dock=DockStyle.Bottom,Height=40};
        Controls.Add(new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Text=$"{user} solicita visualizar e controlar o computador.\nA solicitação expira em 30 segundos."});Controls.Add(yes);Controls.Add(no);ActiveControl=no;CancelButton=no;
        var t=new System.Windows.Forms.Timer{Interval=30000};t.Tick+=(_,_)=>{DialogResult=DialogResult.No;Close();};Shown+=(_,_)=>t.Start();FormClosed+=(_,_)=>t.Dispose();
    }
}
