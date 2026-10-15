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
    readonly TextBox text=new(){Width=220,PlaceholderText="Texto para digitar no computador"};
    readonly Button connect=new(){Text="Conectar",AutoSize=true};
    readonly System.Windows.Forms.Timer timer=new(){Interval=350};
    readonly SemaphoreSlim operations=new(1,1);
    readonly TechnicianIdentity identity;
    RemoteClient? client;
    bool loading,closing;
    DateTime lastMove;
    readonly HashSet<int> held=[];
    public Technician()
    {
        Directory.CreateDirectory(folder);identity=LoadIdentity();
        Text=$"SDE Suporte — {identity.Public.Name}";Width=1360;Height=850;MinimumSize=new(900,560);
        Font=new Font("Segoe UI",10);BackColor=Color.White;KeyPreview=false;
        var sidebar=new Panel{Dock=DockStyle.Left,Width=330,Padding=new Padding(8)};
        var search=new TextBox{Dock=DockStyle.Top,PlaceholderText="Pesquisar computador, setor ou IP"};
        sidebar.Controls.Add(list);sidebar.Controls.Add(search);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=95,Padding=new Padding(6),AutoScroll=true};
        Button Add(string label,Func<Task> action){var b=new Button{Text=label,AutoSize=true};b.Click+=async(_,_)=>await Safe(action);toolbar.Controls.Add(b);return b;}
        Add("1. Exportar minha identificação",()=>{ExportIdentity();return Task.CompletedTask;});
        Add("2. Importar computador",()=>{ImportDevices();return Task.CompletedTask;});
        Add("Alterar IP",()=>{EditAddress();return Task.CompletedTask;});
        Add("Remover da lista",()=>{if(list.SelectedItem is Device d){devices.Remove(d);SaveDevices();Filter(search.Text);}return Task.CompletedTask;});
        connect.Click+=async(_,_)=>await Safe(Connect);toolbar.Controls.Add(connect);
        Add("Encerrar",Disconnect);Add("Verificar conexão",Probe);
        toolbar.Controls.Add(text);Add("Enviar texto",async()=>{await Input(new("text",Text:text.Text));text.Clear();});
        Controls.Add(screen);Controls.Add(sidebar);Controls.Add(toolbar);Controls.Add(status);
        search.TextChanged+=(_,_)=>Filter(search.Text);
        if(File.Exists(Path.Combine(folder,"computers.json")))devices.AddRange(Json.Read<Device[]>(Path.Combine(folder,"computers.json")));
        Filter("");status.Text=$"Técnico: {identity.Public.Name} | Exporte sua identificação, autorize no agente e importe o computador.";
        timer.Tick+=async(_,_)=>{if(!loading&&client?.Session is not null)await Safe(Frame);};
        screen.MouseDown+=async(_,e)=>{screen.Focus();var p=Map(e.X,e.Y);if(p is not null)await Safe(()=>Input(new("down",p.Value.X,p.Value.Y,Key:e.Button==MouseButtons.Right?2:1)));};
        screen.MouseUp+=async(_,e)=>{var p=Map(e.X,e.Y,true);if(p is not null)await Safe(()=>Input(new("up",p.Value.X,p.Value.Y,Key:e.Button==MouseButtons.Right?2:1)));};
        screen.MouseMove+=async(_,e)=>{if(client?.Session is null||DateTime.UtcNow-lastMove<TimeSpan.FromMilliseconds(90))return;lastMove=DateTime.UtcNow;var p=Map(e.X,e.Y);if(p is not null)await Safe(()=>Input(new("move",p.Value.X,p.Value.Y)));};
        screen.MouseWheel+=async(_,e)=>await Safe(()=>Input(new("wheel",Delta:e.Delta)));
        screen.KeyDown+=async(_,e)=>{e.SuppressKeyPress=true;if(held.Add(e.KeyValue))await Safe(()=>Input(new("keyDown",Key:e.KeyValue)));};
        screen.KeyUp+=async(_,e)=>{e.SuppressKeyPress=true;if(held.Remove(e.KeyValue))await Safe(()=>Input(new("keyUp",Key:e.KeyValue)));};
        screen.LostFocus+=async(_,_)=>{foreach(int k in held.ToArray())await Safe(()=>Input(new("keyUp",Key:k)));held.Clear();};
        FormClosing+=async(_,e)=>{if(closing)return;e.Cancel=true;closing=true;Enabled=false;try{await Disconnect();}catch{}finally{Close();}};
        FormClosed+=(_,_)=>{timer.Dispose();client?.Dispose();identity.Dispose();screen.Image?.Dispose();};
    }
    TechnicianIdentity LoadIdentity()
    {
        var path=Path.Combine(folder,"identity.json");
        string name,keyName;
        if(File.Exists(path)){var data=Json.Read<LocalIdentity>(path);name=data.Name;keyName=data.KeyName;}
        else
        {
            name=NameDialog.Ask("Identificação do técnico","Nome que aparecerá no computador atendido",Environment.UserName)??throw new OperationCanceledException("Criação da identificação cancelada.");
            keyName="SDE-Suporte-"+Guid.NewGuid().ToString("N");
            using var created=CngKey.Create(CngAlgorithm.ECDsaP256,keyName,new CngKeyCreationParameters{ExportPolicy=CngExportPolicies.None});
            Json.Write(path,new LocalIdentity(name,keyName));
        }
        return new(new ECDsaCng(CngKey.Open(keyName)),name);
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
    void Filter(string query){list.Items.Clear();foreach(var d in devices.Where(d=>d.ToString().Contains(query,StringComparison.CurrentCultureIgnoreCase)).OrderBy(d=>d.Name))list.Items.Add(d);}
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
            await Disconnect();if(list.SelectedItem is not Device d)throw new Exception("Importe e selecione um computador.");
            client=new(d,identity);await client.Start();status.Text=$"ATENDIMENTO: {d.Name} • {d.Sector} | Técnico: {identity.Public.Name}";
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
    record LocalIdentity(string Name,string KeyName);
}
sealed class RemoteScreen:PictureBox
{
    public RemoteScreen(){SetStyle(ControlStyles.Selectable,true);TabStop=true;}
    protected override bool IsInputKey(Keys keyData)=>true;
}
sealed class NameDialog:Form
{
    public static string? Ask(string title,string label,string initial)
    {
        using var form=new NameDialog{Text=title,Width=500,Height=190,StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MinimizeBox=false,MaximizeBox=false};
        var input=new TextBox{Dock=DockStyle.Top,Text=initial};var ok=new Button{Text="Confirmar",DialogResult=DialogResult.OK,Dock=DockStyle.Bottom,Height=36};
        form.Controls.Add(input);form.Controls.Add(new Label{Dock=DockStyle.Top,Text=label,Height=36});form.Controls.Add(ok);form.AcceptButton=ok;
        return form.ShowDialog()==DialogResult.OK&&!string.IsNullOrWhiteSpace(input.Text)?input.Text.Trim():null;
    }
}
