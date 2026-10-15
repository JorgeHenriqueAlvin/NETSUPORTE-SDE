using System.Security.Cryptography;
using System.Text.Json;
namespace SdeSuporte;
sealed class Technician:Form
{
    readonly string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte");
    readonly List<Device> devices=[];
    readonly ListBox list=new(){Dock=DockStyle.Fill};
    readonly RemoteScreen screen=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(15,27,45),SizeMode=PictureBoxSizeMode.Zoom,TabStop=true};
    readonly Label status=new(){Dock=DockStyle.Bottom,Height=32};
    readonly TextBox search=new(){Dock=DockStyle.Top,PlaceholderText="Pesquisar computador, setor ou IP"};
    readonly TextBox text=new(){Width=220,PlaceholderText="Texto para digitar no computador"};
    readonly Button connect=new(){Text="Conectar",AutoSize=true};
    readonly System.Windows.Forms.Timer timer=new(){Interval=350};
    readonly SemaphoreSlim operations=new(1,1);
    readonly TechnicianIdentity identity;
    ConsoleDiscovery? discovery;
    readonly List<TechnicianKey> authorities=[];
    readonly Dictionary<string,DateTimeOffset> online=[];
    readonly Dictionary<string,DateTimeOffset> probed=[];
    readonly HashSet<string> checking=[];
    readonly SemaphoreSlim probes=new(8,8);
    readonly System.Windows.Forms.Timer presence=new(){Interval=4000};
    readonly Label scanStatus=new(){Dock=DockStyle.Bottom,Height=27,Text="Buscando agentes da equipe na rede..."};
    readonly System.Windows.Forms.Timer scanClock=new(){Interval=120000};
    readonly CancellationTokenSource lifetime=new();
    CancellationTokenSource? scanning;
    bool dirty;
    RemoteClient? client;
    bool loading,closing;
    DateTime lastMove;
    readonly HashSet<int> held=[];
    public Technician()
    {
        Directory.CreateDirectory(folder);identity=LoadIdentity();LoadTeamCredential();
        Text=$"SDE Suporte — {identity.Public.Name}";Width=1360;Height=850;MinimumSize=new(900,560);
        Font=new Font("Segoe UI",10);BackColor=Color.White;KeyPreview=false;
        var sidebar=new Panel{Dock=DockStyle.Left,Width=330,Padding=new Padding(8)};
        sidebar.Controls.Add(list);sidebar.Controls.Add(search);sidebar.Controls.Add(scanStatus);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=95,Padding=new Padding(6),AutoScroll=true};
        Button Add(string label,Func<Task> action){var b=new Button{Text=label,AutoSize=true};b.Click+=async(_,_)=>await Safe(action);toolbar.Controls.Add(b);return b;}
        Add("Atualizar máquinas",async()=>{if(discovery is not null)await discovery.Search();await ScanLocal();});
        Add("Varredura de IP",ChooseScan);
        Add("Parar varredura",()=>{scanning?.Cancel();return Task.CompletedTask;});
        Add("Equipe",()=>{MessageBox.Show($"Equipe: {TeamAccess.Root.Name}\nTécnico: {identity.Public.Name}\n\nUse o instalador Técnico autorizado nos computadores da TI. Ele configura a autorização automaticamente.\nOs funcionários usam o instalador Cliente.","Equipe autorizada");return Task.CompletedTask;});
        var advanced=new ContextMenuStrip();
        void Advanced(string label,Action action)=>advanced.Items.Add(label,null,async(_,_)=>await Safe(()=>{action();return Task.CompletedTask;}));
        Advanced("Importar equipe antiga (.sdeteam)",ImportTeam);Advanced("Exportar identificação antiga",ExportIdentity);
        Advanced("Importar computador antigo",ImportDevices);Advanced("Alterar IP do computador",EditAddress);
        Advanced("Remover da lista",()=>{if(list.SelectedItem is Device d){devices.Remove(d);SaveDevices();Filter(search.Text);}});
        Add("Opções avançadas",()=>{advanced.Show(Cursor.Position);return Task.CompletedTask;});
        connect.Click+=async(_,_)=>await Safe(Connect);toolbar.Controls.Add(connect);
        Add("Encerrar",Disconnect);Add("Verificar conexão",Probe);
        toolbar.Controls.Add(text);Add("Enviar texto",async()=>{await Input(new("text",Text:text.Text));text.Clear();});
        Controls.Add(screen);Controls.Add(sidebar);Controls.Add(toolbar);Controls.Add(status);
        search.TextChanged+=(_,_)=>Filter(search.Text);
        if(File.Exists(Path.Combine(folder,"computers.json")))devices.AddRange(Json.Read<Device[]>(Path.Combine(folder,"computers.json")));
        if(File.Exists(Path.Combine(folder,"authorities.json")))authorities.AddRange(Json.Read<TechnicianKey[]>(Path.Combine(folder,"authorities.json")));
        authorities.RemoveAll(a=>a.Id==identity.Public.Id);authorities.Add(identity.Public);
        authorities.RemoveAll(a=>a.Id==TeamAccess.Root.Id);authorities.Add(TeamAccess.Root);
        list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=54;
        list.DrawItem+=(_,e)=>{
            if(e.Index<0)return;e.DrawBackground();var d=(Device)list.Items[e.Index];
            bool available=online.TryGetValue(d.Id,out var seen)&&DateTimeOffset.UtcNow-seen<TimeSpan.FromSeconds(45);
            var color=(e.State&DrawItemState.Selected)!=0?SystemColors.HighlightText:Color.FromArgb(25,45,65);
            TextRenderer.DrawText(e.Graphics,d.Name,Font,new Rectangle(e.Bounds.X+5,e.Bounds.Y+4,e.Bounds.Width-110,23),color,TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics,available?"Conectado":"Desconectado",Font,new Rectangle(e.Bounds.Right-105,e.Bounds.Y+8,100,20),available?Color.ForestGreen:color,TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics,$"{d.Sector} • {d.Host}",Font,new Rectangle(e.Bounds.X+5,e.Bounds.Y+27,e.Bounds.Width-10,22),color,TextFormatFlags.EndEllipsis);e.DrawFocusRectangle();
        };
        Filter("");status.Text=$"Técnico: {identity.Public.Name} | Instale o agente nos funcionários. As máquinas da equipe aparecem automaticamente.";
        Shown+=async(_,_)=>{
            try{discovery=new ConsoleDiscovery(identity,()=>authorities.Select(a=>a.Id).ToArray(),QueueDiscovered);}catch(Exception e){scanStatus.Text="Descoberta UDP indisponível: "+e.Message;}
            await Safe(ScanLocal);
        };
        scanClock.Tick+=async(_,_)=>{if(scanning is null&&!closing)await Safe(ScanLocal);};scanClock.Start();
        presence.Tick+=(_,_)=>{
            list.Invalidate();if(dirty){SaveDevices();dirty=false;}
            foreach(var device in devices.ToArray())if(!probed.TryGetValue(device.Id,out var at)||DateTimeOffset.UtcNow-at>TimeSpan.FromSeconds(12))Discovered(device);
        };presence.Start();
        timer.Tick+=async(_,_)=>{if(!loading&&client?.Session is not null)await Safe(Frame);};
        screen.MouseDown+=async(_,e)=>{screen.Focus();var p=Map(e.X,e.Y);if(p is not null)await Safe(()=>Input(new("down",p.Value.X,p.Value.Y,Key:e.Button==MouseButtons.Right?2:1)));};
        screen.MouseUp+=async(_,e)=>{var p=Map(e.X,e.Y,true);if(p is not null)await Safe(()=>Input(new("up",p.Value.X,p.Value.Y,Key:e.Button==MouseButtons.Right?2:1)));};
        screen.MouseMove+=async(_,e)=>{if(client?.Session is null||DateTime.UtcNow-lastMove<TimeSpan.FromMilliseconds(90))return;lastMove=DateTime.UtcNow;var p=Map(e.X,e.Y);if(p is not null)await Safe(()=>Input(new("move",p.Value.X,p.Value.Y)));};
        screen.MouseWheel+=async(_,e)=>await Safe(()=>Input(new("wheel",Delta:e.Delta)));
        screen.KeyDown+=async(_,e)=>{e.SuppressKeyPress=true;if(held.Add(e.KeyValue))await Safe(()=>Input(new("keyDown",Key:e.KeyValue)));};
        screen.KeyUp+=async(_,e)=>{e.SuppressKeyPress=true;if(held.Remove(e.KeyValue))await Safe(()=>Input(new("keyUp",Key:e.KeyValue)));};
        screen.LostFocus+=async(_,_)=>{foreach(int k in held.ToArray())await Safe(()=>Input(new("keyUp",Key:k)));held.Clear();};
        FormClosing+=async(_,e)=>{if(closing)return;e.Cancel=true;closing=true;lifetime.Cancel();scanning?.Cancel();Enabled=false;try{await Disconnect();}catch{}finally{Close();}};
        FormClosed+=(_,_)=>{discovery?.Dispose();presence.Dispose();scanClock.Dispose();advanced.Dispose();if(dirty)SaveDevices();timer.Dispose();client?.Dispose();identity.Dispose();screen.Image?.Dispose();};
    }
    void LoadTeamCredential()
    {
        var path=Path.Combine(folder,"team-credential.json");
        if(File.Exists(path))
        {
            try{var saved=Json.Read<TeamCredential>(path);var technician=TeamAccess.Verify(saved,TeamAccess.Root);
                if(technician?.Id==identity.Public.Id&&technician.Name==identity.Public.Name){identity.Credential=saved;return;}}
            catch{}
        }
        while(true)
        {
            var code=NameDialog.Ask("Autorizar console da equipe","Cole o código da equipe fornecido pela TI (somente na primeira abertura)","",true)
                ??throw new OperationCanceledException("Autorização do console cancelada.");
            try{identity.Credential=TeamAccess.Authorize(code,identity,TeamAccess.Root);Json.Write(path,identity.Credential);return;}
            catch(Exception){MessageBox.Show("Código da equipe inválido. Copie o código completo do arquivo fornecido pela TI.","Autorizar técnico",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
    }
    void QueueDiscovered(Device device)
    {
        if(IsDisposed||closing)return;
        try{BeginInvoke(new Action(()=>{if(!closing)Discovered(device);}));}catch(InvalidOperationException){}
    }
    async Task ChooseScan()
    {
        string initial=string.Join("; ",IPv4Range.LocalCidrs());
        var input=NameDialog.Ask("Varredura de IP","Rede ou faixa de IP (separe várias faixas por ponto e vírgula)",initial);
        if(input is null)return;
        var ranges=input.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>IPv4Range.Parse(s)).ToArray();
        if(ranges.Length==0)throw new ArgumentException("Informe uma rede ou faixa de IP.");
        if(ranges.Sum(r=>r.Count)>65536)throw new ArgumentException("Use até 65.536 endereços por pesquisa.");
        if(scanning is not null){MessageBox.Show("Uma varredura já está em andamento. Use Parar varredura antes de iniciar outra.");return;}
        Json.Write(Path.Combine(folder,"scan-ranges.json"),ranges.Select(r=>r.Label).ToArray());
        await Scan(ranges);
    }
    Task ScanLocal()
    {
        if(scanning is not null||closing)return Task.CompletedTask;
        var path=Path.Combine(folder,"scan-ranges.json");
        var networks=File.Exists(path)?Json.Read<string[]>(path):IPv4Range.LocalCidrs(true);
        return Scan(networks.Select(s=>IPv4Range.Parse(s)).ToArray());
    }
    async Task Scan(IPv4Range[] ranges)
    {
        if(ranges.Length==0){scanStatus.Text="Use Varredura de IP para informar a rede.";return;}
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);scanning=cancellation;
        var progress=new Progress<ScanProgress>(p=>{if(!closing)scanStatus.Text=$"IPs: {p.Checked}/{p.Total} • Agentes: {p.Found}";});
        try
        {
            await NetworkScanner.Scan(ranges,authorities.Select(a=>a.Id).ToArray(),identity.Public.Id,QueueDiscovered,progress,cancellation.Token);
            if(!closing)status.Text=$"Busca concluída: {devices.Count} computador(es) na lista. Selecione uma máquina e clique em Conectar.";
        }catch(OperationCanceledException){if(!closing)scanStatus.Text="Varredura interrompida.";}
        finally{scanning=null;}
    }
    TechnicianIdentity LoadIdentity()=>TechnicianProfiles.Load(folder,()=>NameDialog.Ask("Identificação do técnico","Nome que aparecerá no computador atendido",Environment.UserName)
        ??throw new OperationCanceledException("Criação da identificação cancelada."));
    void ImportTeam()
    {
        using var open=new OpenFileDialog{Filter="Equipe SDE (*.sdeteam)|*.sdeteam"};if(open.ShowDialog()!=DialogResult.OK)return;
        var issuer=Json.Read<TechnicianKey>(open.FileName);Enrollment.VerifyPublic(issuer);
        if(MessageBox.Show($"Confiar em computadores cadastrados por {issuer.Name}?\nIdentificação: {issuer.Id[..16]}","Importar equipe",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
        authorities.RemoveAll(a=>a.Id==issuer.Id);authorities.Add(issuer);Json.Write(Path.Combine(folder,"authorities.json"),authorities);
        if(discovery is not null)_=discovery.Search();
    }
    async void Discovered(Device d)
    {
        if(checking.Contains(d.Id)||probed.TryGetValue(d.Id,out var last)&&DateTimeOffset.UtcNow-last<TimeSpan.FromSeconds(7))return;
        probed[d.Id]=DateTimeOffset.UtcNow;checking.Add(d.Id);await probes.WaitAsync();
        try
        {
            if(closing||IsDisposed)return;
            using var candidate=new RemoteClient(d,identity,TimeSpan.FromSeconds(2));using var response=await candidate.Request(HttpMethod.Get,"/info");
            using var info=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(info.RootElement.GetProperty("id").GetString()!=d.Id)return;
            if(closing||IsDisposed)return;
            online[d.Id]=DateTimeOffset.UtcNow;
            var existing=devices.FirstOrDefault(x=>x.Id==d.Id);
            if(existing!=d){devices.RemoveAll(x=>x.Id==d.Id);devices.Add(d);dirty=true;Filter(search.Text);}
            list.Invalidate();
        }catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.TooManyRequests){online[d.Id]=DateTimeOffset.UtcNow;if(!IsDisposed)list.Invalidate();}
        catch{online.Remove(d.Id);if(!IsDisposed&&!closing)list.Invalidate();}
        finally{checking.Remove(d.Id);probes.Release();}
    }
    void ExportIdentity()
    {
        using var dialog=new SaveFileDialog{Title="Identificação pública — autorizar no agente",Filter="Identificação do técnico (*.sdetech)|*.sdetech",FileName="Tecnico-"+identity.Public.Id[..8]+".sdetech"};
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        Json.Write(dialog.FileName,identity.Public);MessageBox.Show("Identificação exportada.\nNa máquina do cliente, instale o agente e selecione esse arquivo para autorizar seu acesso.\nEle contém apenas sua chave pública.","SDE Suporte");
    }
    void ImportDevices()
    {
        using var dialog=new OpenFileDialog{Filter="Computador SDE (*.sdehost)|*.sdehost",Multiselect=true};
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        foreach(var path in dialog.FileNames)
        {
            var d=Json.Read<Device>(path);d.Validate();
            if(MessageBox.Show($"Importar {d.Name} ({d.Host})?\nSetor: {d.Sector}\nIdentificação do certificado: {d.CertificateSha256[..16]}…\nUse somente arquivos obtidos da instalação autorizada.","Importar computador",MessageBoxButtons.YesNo)!=DialogResult.Yes)continue;
            devices.RemoveAll(x=>x.Id==d.Id);devices.Add(d);
        }
        SaveDevices();Filter("");
    }
    void EditAddress()
    {
        if(list.SelectedItem is not Device d)throw new Exception("Selecione o computador.");
        var address=NameDialog.Ask("Endereço na rede","IP ou nome do computador",d.Host);if(address is null)return;
        var updated=d with{Host=address.Trim()};updated.Validate();devices.RemoveAll(x=>x.Id==d.Id);devices.Add(updated);SaveDevices();Filter("");
    }
    void SaveDevices()=>Json.Write(Path.Combine(folder,"computers.json"),devices);
    void Filter(string query){string? selected=(list.SelectedItem as Device)?.Id;list.Items.Clear();foreach(var d in devices.Where(d=>d.ToString().Contains(query,StringComparison.CurrentCultureIgnoreCase)).OrderBy(d=>d.Name)){list.Items.Add(d);if(d.Id==selected)list.SelectedItem=d;}}
    async Task Safe(Func<Task> action)
    {
        try{await action();}catch(Exception e){timer.Stop();status.Text="Falha: "+e.Message;if(!closing)MessageBox.Show(e.Message,"SDE Suporte",MessageBoxButtons.OK,MessageBoxIcon.Information);}
    }
    async Task Probe()
    {
        if(list.SelectedItem is not Device d)throw new Exception("Selecione o computador.");
        using var test=new RemoteClient(d,identity);using var r=await test.Request(HttpMethod.Get,"/info");
        status.Text="Conexão verificada: "+await r.Content.ReadAsStringAsync();
    }
    async Task Connect()
    {
        connect.Enabled=false;
        try
        {
            await Disconnect();if(list.SelectedItem is not Device d)throw new Exception("Selecione um computador na lista. Use Varredura de IP se ele ainda não apareceu.");
            client=new(d,identity);await client.Start();online[d.Id]=DateTimeOffset.UtcNow;list.Invalidate();status.Text=$"ATENDIMENTO: {d.Name} • {d.Sector} | Técnico: {identity.Public.Name}";
            await Frame();timer.Start();screen.Focus();
        }finally{connect.Enabled=true;}
    }
    async Task Disconnect()
    {
        timer.Stop();await operations.WaitAsync();
        try{if(client is not null){try{await client.Stop();}finally{client.Dispose();client=null;}}}
        finally{operations.Release();held.Clear();var old=screen.Image;screen.Image=null;old?.Dispose();status.Text="Atendimento encerrado.";}
    }
    async Task Frame()
    {
        if(client?.Session is null||!await operations.WaitAsync(0))return;
        loading=true;
        try{using var r=await client.Request(HttpMethod.Get,"/frame");using var stream=await r.Content.ReadAsStreamAsync();using var image=Image.FromStream(stream);var old=screen.Image;screen.Image=new Bitmap(image);old?.Dispose();}
        finally{loading=false;operations.Release();}
    }
    async Task Input(InputCommand command)
    {
        if(client?.Session is null)return;
        await operations.WaitAsync();try{if(client?.Session is not null){using var r=await client.Request(HttpMethod.Post,"/input",command);}}finally{operations.Release();}
    }
    Point? Map(int x,int y,bool clamp=false)
    {
        if(screen.Image is not Image image||client?.Session is null)return null;
        double scale=Math.Min((double)screen.ClientSize.Width/image.Width,(double)screen.ClientSize.Height/image.Height);
        int px=(int)((x-(screen.ClientSize.Width-image.Width*scale)/2)/scale),py=(int)((y-(screen.ClientSize.Height-image.Height*scale)/2)/scale);
        if(clamp)return new Point(Math.Clamp(px,0,image.Width-1),Math.Clamp(py,0,image.Height-1));
        return px>=0&&py>=0&&px<image.Width&&py<image.Height?new Point(px,py):null;
    }
}
sealed class RemoteScreen:PictureBox
{
    public RemoteScreen(){SetStyle(ControlStyles.Selectable,true);TabStop=true;}
    protected override bool IsInputKey(Keys keyData)=>true;
}
sealed class NameDialog:Form
{
    public static string? Ask(string title,string label,string initial,bool secret=false)
    {
        using var form=new NameDialog{Text=title,Width=500,Height=190,StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var input=new TextBox{Dock=DockStyle.Top,Text=initial,UseSystemPasswordChar=secret};var ok=new Button{Text="Confirmar",DialogResult=DialogResult.OK,Dock=DockStyle.Bottom,Height=36};
        form.Controls.Add(input);form.Controls.Add(new Label{Dock=DockStyle.Top,Text=label,Height=36});form.Controls.Add(ok);form.AcceptButton=ok;
        return form.ShowDialog()==DialogResult.OK&&!string.IsNullOrWhiteSpace(input.Text)?input.Text.Trim():null;
    }
}
