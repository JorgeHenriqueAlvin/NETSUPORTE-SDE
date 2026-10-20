using System.Security.Cryptography;
using System.Text.Json;
namespace SdeSuporte;
sealed class Technician:Form
{
    readonly string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte");
    readonly List<Device> devices=[];
    Dictionary<string,MachineMetadata> metadata=new();
    Dictionary<string,MachineObservation> observations=new();
    readonly Dictionary<string,string> connectionErrors=new();
    readonly Dictionary<string,DateTimeOffset> inventoryChecked=new();
    string MetadataPath=>Path.Combine(folder,"machine-metadata.json");
    string ObservationsPath=>Path.Combine(folder,"machine-observations.json");
    MachineMetadata Admin(Device d)=>metadata.GetValueOrDefault(d.Id)??new();
    string MachineLabel(Device d)
    {
        var a=Admin(d);var o=observations.GetValueOrDefault(d.Id);
        string hostname=MachineNames.Display(d,o);
        var last=online.TryGetValue(d.Id,out var live)?live:o?.Seen;
        string alias=string.IsNullOrWhiteSpace(a.Alias)?"":$" • Apelido: {a.Alias}";
        string description=!string.IsNullOrWhiteSpace(a.Description)?a.Description:!string.IsNullOrWhiteSpace(o?.Description)?o.Description:"Descrição não informada";
        string location=string.Join(" • ",new[]{a.Sector.Length>0?a.Sector:d.Sector,a.Location}.Where(v=>!string.IsNullOrWhiteSpace(v)));
        string details=connectionErrors.TryGetValue(d.Id,out var error)?"Consulta: "+error:$"Última comunicação: {last?.ToLocalTime().ToString("g")??"aguardando resposta do agente"}";
        return $"{hostname}{alias}\nIP: {d.Host} • {location}\n{MachineDescriptions.Summary(o)}\n{description}\nAgente: {o?.AgentVersion??"não informado"} • {details}";
    }
    void EditMetadata()
    {
        if(list.SelectedItem is not Device d)throw new InvalidOperationException("Selecione uma máquina.");
        var a=Admin(d);
        using var dialog=new Form{Text="Identificação administrativa",Width=540,Height=350,StartPosition=FormStartPosition.CenterParent};
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=5,Padding=new Padding(12)};
        var values=new[]{a.Alias,a.Description,a.Sector,a.Location};var labels=new[]{"Apelido","Descrição administrativa","Setor","Local"};var boxes=new List<TextBox>();
        for(int i=0;i<4;i++){panel.Controls.Add(new Label{Text=labels[i],AutoSize=true},0,i);var box=new TextBox{Text=values[i],Width=300,MaxLength=256};boxes.Add(box);panel.Controls.Add(box,1,i);}
        var save=new Button{Text="Salvar",DialogResult=DialogResult.OK};panel.Controls.Add(save,1,4);dialog.Controls.Add(panel);dialog.AcceptButton=save;
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        metadata[d.Id]=new(boxes[0].Text.Trim(),boxes[1].Text.Trim(),boxes[2].Text.Trim(),boxes[3].Text.Trim());
        Json.Write(MetadataPath,metadata);Filter(search.Text);
    }
    readonly ListBox list=new(){Dock=DockStyle.Fill};
    readonly TabControl tabs=new(){Dock=DockStyle.Fill};
    readonly TabPage remotePage=new("Tela remota");
    readonly TabPage chatPage=new("Chat do atendimento");
    readonly TabPage noticesPage=new("Avisos globais");
    readonly MessagePanel chatPanel=new();
    readonly TextBox noticeText=new(){Dock=DockStyle.Top,Multiline=true,Height=100,MaxLength=2000,ScrollBars=ScrollBars.Vertical};
    readonly CheckedListBox noticeTargets=new(){Dock=DockStyle.Left,Width=270,CheckOnClick=true};
    readonly ListBox noticeResults=new(){Dock=DockStyle.Fill,HorizontalScrollbar=true};
    string? noticeImage;
    readonly PictureBox noticePreview=new(){Dock=DockStyle.Top,Height=150,SizeMode=PictureBoxSizeMode.Zoom,Visible=false};
    readonly Button sendNotice=new(){Dock=DockStyle.Top,Height=45,Text="Enviar aviso aos clientes selecionados"};
    readonly CheckBox autoFullScreen=new(){AutoSize=true,Text="Abrir em tela cheia",Checked=true};
    readonly System.Windows.Forms.Timer chatClock=new(){Interval=1000};
    FullScreenWindow? fullScreen;
    bool pollingChat,chatAvailable;
    long chatCursor;
    readonly ComboBox monitorPicker=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=280};
    string selectedMonitor="primary",monitorSignature="",frameLayout="";
    int frameOffsetX,frameOffsetY;
    bool updatingMonitors;
    readonly RemoteScreen screen=new(){Dock=DockStyle.Fill,BackColor=Color.FromArgb(15,27,45),SizeMode=PictureBoxSizeMode.Zoom,TabStop=true};
    readonly Label status=new(){Dock=DockStyle.Bottom,Height=32};
    readonly TextBox search=new(){Dock=DockStyle.Top,PlaceholderText="Pesquisar nome, apelido, descrição, setor, local ou IP"};
    readonly CheckBox onlyOnline=new(){Dock=DockStyle.Top,Height=30,Text="Mostrar somente conectados"};
    readonly Label machineCount=new(){Dock=DockStyle.Bottom,Height=30};
    bool IsOnline(Device d)=>online.TryGetValue(d.Id,out var seen)&&DateTimeOffset.UtcNow-seen<TimeSpan.FromSeconds(45);
    readonly TextBox text=new(){Width=220,PlaceholderText="Texto para digitar no computador"};
    readonly Button connect=new(){Text="Conectar",AutoSize=true};
    readonly System.Windows.Forms.Timer timer=new(){Interval=100};
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
    bool dirty,searchingAgents;
    RemoteClient? client;
    bool loading,closing;
    DateTime lastMove;
    readonly HashSet<int> held=[];
    public Technician(TechnicianIdentity? injectedIdentity=null)
    {
        Directory.CreateDirectory(folder);identity=injectedIdentity??LoadIdentity();if(injectedIdentity is null)LoadTeamCredential();
        Text=$"SDE Suporte — {identity.Public.Name}";Width=1360;Height=850;MinimumSize=new(900,560);
        Font=new Font("Segoe UI",10);BackColor=Color.White;DoubleBuffered=true;KeyPreview=false;
        var sidebar=new Panel{Dock=DockStyle.Left,Width=330,Padding=new Padding(8)};
        sidebar.Controls.Add(list);sidebar.Controls.Add(onlyOnline);sidebar.Controls.Add(search);sidebar.Controls.Add(scanStatus);sidebar.Controls.Add(machineCount);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=104,Padding=new Padding(10),AutoScroll=true};
        Button Add(string label,Func<Task> action){var b=new Button{Text=label,AutoSize=true};b.Click+=async(_,_)=>await Safe(action);toolbar.Controls.Add(b);return b;}
        Add("Buscar agentes instalados",async()=>{search.Clear();await SearchAgents();});
        Add("Varredura de IP",ChooseScan);
        Add("Configurações da máquina",MachineDetails);
        Add("Editar identificação",()=>{EditMetadata();return Task.CompletedTask;});
        Add("Buscar Raspberry Pi",FindLinuxAgents);
        Add("Adicionar Raspberry/Linux",AddLinuxAgent);
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
        toolbar.Controls.Add(autoFullScreen);Add("Tela cheia (F11)",()=>{ToggleFullScreen();return Task.CompletedTask;});
        var monitorBar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=52,Padding=new Padding(8)};
        monitorBar.Controls.Add(new Label{Text="Monitor do cliente",AutoSize=true,Margin=new Padding(0,7,10,0)});monitorBar.Controls.Add(monitorPicker);
        monitorPicker.SelectedIndexChanged+=(_,_)=>{if(!updatingMonitors&&monitorPicker.SelectedItem is RemoteMonitor m){selectedMonitor=m.Id;screen.Focus();}};
        remotePage.Controls.Add(screen);remotePage.Controls.Add(monitorBar);chatPage.Controls.Add(chatPanel);
        var deploymentPage=new TabPage("Implantar Clientes");deploymentPage.Controls.Add(new DeploymentPanel());
        tabs.TabPages.AddRange([remotePage,chatPage,noticesPage,deploymentPage,UpdatesPage()]);
        var noticeHeader=new Label{Dock=DockStyle.Top,Height=100,Text="Marque os clientes ou use Selecionar todos. Envie texto, foto ou os dois.\nO funcionário verá apenas “Aviso da TI”, sem o nome do remetente. Entrega não significa leitura.",Padding=new Padding(8)};
        noticesPage.Padding=new Padding(12);noticesPage.Controls.Add(noticeResults);noticesPage.Controls.Add(noticeTargets);noticesPage.Controls.Add(sendNotice);noticesPage.Controls.Add(noticeText);noticesPage.Controls.Add(noticePreview);
        var photoBar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=45};
        var attach=new Button{Text="Adicionar foto",AutoSize=true};var removePhoto=new Button{Text="Remover foto",AutoSize=true};
        attach.Click+=async(_,_)=>await Safe(()=>{using var dialog=new OpenFileDialog{Filter="Fotos JPEG ou PNG|*.jpg;*.jpeg;*.png"};if(dialog.ShowDialog()!=DialogResult.OK)return Task.CompletedTask;var normalized=NoticeImages.FromFile(dialog.FileName);var photo=NoticeImages.Decode(normalized);noticeImage=normalized;var previous=noticePreview.Image;noticePreview.Image=photo;previous?.Dispose();noticePreview.Visible=true;return Task.CompletedTask;});
        removePhoto.Click+=(_,_)=>{noticeImage=null;var previous=noticePreview.Image;noticePreview.Image=null;previous?.Dispose();noticePreview.Visible=false;};
        var allTargets=new Button{Text="Selecionar todos",AutoSize=true};var clearTargets=new Button{Text="Limpar seleção",AutoSize=true};var refreshTargets=new Button{Text="Atualizar clientes",AutoSize=true};
        void Targets(){var chosen=noticeTargets.CheckedItems.Cast<Device>().Select(d=>d.Id).ToHashSet();noticeTargets.Items.Clear();foreach(var d in devices.Where(d=>online.TryGetValue(d.Id,out var seen)&&DateTimeOffset.UtcNow-seen<TimeSpan.FromSeconds(45)).OrderBy(d=>d.Name))noticeTargets.Items.Add(d,chosen.Contains(d.Id));}
        allTargets.Click+=(_,_)=>{Targets();for(int i=0;i<noticeTargets.Items.Count;i++)noticeTargets.SetItemChecked(i,true);};
        clearTargets.Click+=(_,_)=>{for(int i=0;i<noticeTargets.Items.Count;i++)noticeTargets.SetItemChecked(i,false);};refreshTargets.Click+=(_,_)=>Targets();
        tabs.SelectedIndexChanged+=(_,_)=>{if(tabs.SelectedTab==noticesPage)Targets();};
        photoBar.Controls.AddRange([attach,removePhoto]);
        var targetTools=new FlowLayoutPanel{Dock=DockStyle.Top,Height=110};targetTools.Controls.AddRange([allTargets,clearTargets,refreshTargets]);
        var targetColumn=new Panel{Dock=DockStyle.Left,Width=275};noticeTargets.Dock=DockStyle.Fill;targetColumn.Controls.Add(noticeTargets);targetColumn.Controls.Add(targetTools);noticesPage.Controls.Add(photoBar);noticesPage.Controls.Add(noticeHeader);
        var noticeContent=new Panel{Dock=DockStyle.Fill};foreach(Control item in noticesPage.Controls.Cast<Control>().ToArray())noticeContent.Controls.Add(item);
        noticesPage.Controls.Add(noticeContent);noticesPage.Controls.Add(targetColumn);
        sendNotice.Click+=async(_,_)=>await Safe(Broadcast);
        chatPanel.SetAvailable(false);chatPanel.SetTitle("Conecte a uma máquina para conversar com o funcionário.");
        chatPanel.Send=SendChat;
        chatClock.Tick+=async(_,_)=>{if(pollingChat||client?.Session is null||!chatAvailable)return;pollingChat=true;try{await PollChat();}catch(Exception e){if(!closing)chatPanel.SetTitle("Chat indisponível: "+e.Message);}finally{pollingChat=false;}};
        Controls.Add(tabs);Controls.Add(sidebar);Controls.Add(toolbar);Controls.Add(status);
        Premium.Apply(this);connect.BackColor=Color.FromArgb(45,105,230);connect.ForeColor=Color.White;
        status.Padding=new Padding(12,5,0,0);status.BackColor=Color.FromArgb(226,233,244);
        list.BorderStyle=BorderStyle.None;search.Margin=new Padding(8);
        tabs.ItemSize=new Size(170,40);tabs.SizeMode=TabSizeMode.Fixed;
        tabs.DrawMode=TabDrawMode.OwnerDrawFixed;tabs.DrawItem+=(_,e)=>{var selected=e.Index==tabs.SelectedIndex;using var fill=new SolidBrush(selected?Color.White:Color.FromArgb(230,236,246));e.Graphics.FillRectangle(fill,e.Bounds);TextRenderer.DrawText(e.Graphics,tabs.TabPages[e.Index].Text,Font,e.Bounds,selected?Color.FromArgb(45,105,230):Color.FromArgb(65,81,108),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);if(selected){using var line=new Pen(Color.FromArgb(45,105,230),3);e.Graphics.DrawLine(line,e.Bounds.Left,e.Bounds.Bottom-2,e.Bounds.Right,e.Bounds.Bottom-2);}};
        var brand=new Label{Text="SDE  /  SUPORTE\nCentral de atendimento",Dock=DockStyle.Top,Height=70,Padding=new Padding(10),Font=new Font("Segoe UI Semibold",13),ForeColor=Color.White,BackColor=Color.FromArgb(18,30,50)};sidebar.Controls.Add(brand);
        if(File.Exists(MetadataPath))metadata=Json.Read<Dictionary<string,MachineMetadata>>(MetadataPath);
        if(File.Exists(ObservationsPath))observations=Json.Read<Dictionary<string,MachineObservation>>(ObservationsPath);
        search.TextChanged+=(_,_)=>Filter(search.Text);
        onlyOnline.CheckedChanged+=(_,_)=>Filter(search.Text);
        if(File.Exists(Path.Combine(folder,"computers.json")))devices.AddRange(Json.Read<Device[]>(Path.Combine(folder,"computers.json")));
        if(File.Exists(Path.Combine(folder,"authorities.json")))authorities.AddRange(Json.Read<TechnicianKey[]>(Path.Combine(folder,"authorities.json")));
        authorities.RemoveAll(a=>a.Id==identity.Public.Id);authorities.Add(identity.Public);
        authorities.RemoveAll(a=>a.Id==TeamAccess.Root.Id);authorities.Add(TeamAccess.Root);
        list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=185;
        list.DrawItem+=(_,e)=>{
            if(e.Index<0)return;e.DrawBackground();var d=(Device)list.Items[e.Index];
            bool available=online.TryGetValue(d.Id,out var seen)&&DateTimeOffset.UtcNow-seen<TimeSpan.FromSeconds(45);
            var color=(e.State&DrawItemState.Selected)!=0?SystemColors.HighlightText:Color.FromArgb(25,45,65);
            TextRenderer.DrawText(e.Graphics,MachineLabel(d)+"\n"+(available?"Agente conectado":"Agente sem comunicação recente"),Font,new Rectangle(e.Bounds.X+5,e.Bounds.Y+4,e.Bounds.Width-10,e.Bounds.Height-8),color,TextFormatFlags.EndEllipsis|TextFormatFlags.WordBreak);e.DrawFocusRectangle();
        };
        Filter("");status.Text=$"Técnico: {identity.Public.Name} | Instale o agente nos funcionários. As máquinas da equipe aparecem automaticamente.";
        Shown+=async(_,_)=>{
            try{discovery=new ConsoleDiscovery(identity,()=>authorities.Select(a=>a.Id).ToArray(),QueueDiscovered);}catch(Exception e){scanStatus.Text="Descoberta UDP indisponível: "+e.Message;}
            await Safe(SearchAgents);
        };
        scanClock.Tick+=async(_,_)=>{if(scanning is null&&!closing)await Safe(ScanLocal);};scanClock.Start();
        presence.Tick+=(_,_)=>{
            Filter(search.Text);if(dirty){SaveDevices();dirty=false;}
            foreach(var device in devices.ToArray())if(!probed.TryGetValue(device.Id,out var at)||DateTimeOffset.UtcNow-at>TimeSpan.FromSeconds(12))Discovered(device);
        };presence.Start();
        timer.Tick+=async(_,_)=>{if(!loading&&client?.Session is not null&&(fullScreen is not null||tabs.SelectedTab==remotePage))await Safe(Frame);};
        screen.MouseDown+=async(_,e)=>{screen.Focus();var p=Map(e.X,e.Y);if(p is not null)await Safe(()=>Input(new("down",p.Value.X,p.Value.Y,Key:e.Button==MouseButtons.Right?2:1)));};
        screen.MouseUp+=async(_,e)=>{var p=Map(e.X,e.Y,true);if(p is not null)await Safe(()=>Input(new("up",p.Value.X,p.Value.Y,Key:e.Button==MouseButtons.Right?2:1)));};
        screen.MouseMove+=async(_,e)=>{if(client?.Session is null||DateTime.UtcNow-lastMove<TimeSpan.FromMilliseconds(90))return;lastMove=DateTime.UtcNow;var p=Map(e.X,e.Y);if(p is not null)await Safe(()=>Input(new("move",p.Value.X,p.Value.Y)));};
        screen.MouseWheel+=async(_,e)=>await Safe(()=>Input(new("wheel",Delta:e.Delta)));
        screen.KeyDown+=async(_,e)=>{e.SuppressKeyPress=true;if(e.KeyCode==Keys.F11){ToggleFullScreen();return;}if(held.Add(e.KeyValue))await Safe(()=>Input(new("keyDown",Key:e.KeyValue)));};
        screen.KeyUp+=async(_,e)=>{e.SuppressKeyPress=true;if(e.KeyCode==Keys.F11)return;if(held.Remove(e.KeyValue))await Safe(()=>Input(new("keyUp",Key:e.KeyValue)));};
        screen.LostFocus+=async(_,_)=>{foreach(int k in held.ToArray())await Safe(()=>Input(new("keyUp",Key:k)));held.Clear();};
        FormClosing+=async(_,e)=>{if(closing)return;e.Cancel=true;closing=true;lifetime.Cancel();scanning?.Cancel();Enabled=false;try{await Disconnect();}catch{}finally{Close();}};
        FormClosed+=(_,_)=>{discovery?.Dispose();presence.Dispose();scanClock.Dispose();chatClock.Dispose();advanced.Dispose();if(dirty)SaveDevices();timer.Dispose();client?.Dispose();identity.Dispose();screen.Image?.Dispose();noticePreview.Image?.Dispose();};
    }
    TabPage UpdatesPage()
    {
        var page=new TabPage("Atualizações");
        var title=new Label{Dock=DockStyle.Top,Height=65,Text="Seu suporte sempre atualizado",Font=new Font("Segoe UI Semibold",19),Padding=new Padding(12)};
        var installed=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)??"desconhecida";
        var description=new Label{Dock=DockStyle.Top,Height=160,Padding=new Padding(12),Text=$"Versão instalada: {installed}\n\nA atualização automática é habilitada pelo instalador. Novas versões são verificadas no boot e a cada hora no GitHub, com verificação de integridade.\n\nAs atualizações são instaladas quando Cliente e Técnico estão fechados, inclusive na próxima inicialização. A máquina precisa de acesso à internet para baixar as versões."};
        var result=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
        var check=new Button{Dock=DockStyle.Top,Height=45,Text="Verificar nova versão"};
        check.Click+=async(_,_)=>{check.Enabled=false;try{using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};using var data=JsonDocument.Parse(await http.GetStringAsync("https://github.com/JorgeHenriqueAlvin/NETSUPORTE-SDE/releases/latest/download/update.json"));string version=data.RootElement.GetProperty("version").GetString()??"";var current=System.Reflection.Assembly.GetExecutingAssembly().GetName().Version??new Version(0,0);result.Text=Version.TryParse(version,out var next)&&next>current?$"Versão {version} disponível. O atualizador fará a instalação automaticamente quando os aplicativos estiverem fechados.":$"Você está usando a versão mais recente ({current.ToString(3)}).";}catch(Exception){result.Text="Não foi possível consultar o GitHub. O suporte na rede local continua disponível; o atualizador tentará novamente depois.";}finally{if(!check.IsDisposed)check.Enabled=true;}};
        var recover=new Button{Dock=DockStyle.Top,Height=45,Text="Executar atualizador confiável / recuperar pacote"};
        recover.Click+=(_,_)=>{
            try{
                var start=new System.Diagnostics.ProcessStartInfo("schtasks.exe"){UseShellExecute=false,CreateNoWindow=true};
                foreach(var arg in new[]{"/Run","/TN","SDE-Suporte-Atualizacao"})start.ArgumentList.Add(arg);
                using var process=System.Diagnostics.Process.Start(start);
                result.Text="Solicitação enviada à tarefa de atualização registrada. Feche o Técnico para permitir a instalação. Consulte o log do atualizador para confirmar o resultado. Se o pacote da versão atual estiver danificado, reinstale o Técnico oficial da mesma versão.";
            }catch(Exception e){result.Text="Não foi possível iniciar o atualizador: "+e.Message;}
        };
        page.Controls.Add(result);page.Controls.Add(recover);page.Controls.Add(check);page.Controls.Add(description);page.Controls.Add(title);return page;
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
    Task AddLinuxAgent()
    {
        using var dialog=new AddLinuxAgentDialog(authorities.Select(a=>a.Id).ToArray(),identity,lifetime.Token);
        if(dialog.ShowDialog(this)!=DialogResult.OK||dialog.SelectedDevice is not Device device||closing)return Task.CompletedTask;
        AddVerifiedDevice(device);
        return Task.CompletedTask;
    }
    Task MachineDetails()
    {
        if(list.SelectedItem is not Device device)throw new InvalidOperationException("Selecione uma máquina na lista.");
        using var dialog=new MachineDetailsDialog(device,identity,lifetime.Token);
        dialog.ShowDialog(this);return Task.CompletedTask;
    }
    Task FindLinuxAgents()
    {
        using var dialog=new FindLinuxAgentsDialog(authorities.Select(a=>a.Id).ToArray(),identity,lifetime.Token);
        if(dialog.ShowDialog(this)==DialogResult.OK&&dialog.SelectedDevice is Device device&&!closing)AddVerifiedDevice(device);
        return Task.CompletedTask;
    }
    void AddVerifiedDevice(Device device)
    {
        var previous=devices.FirstOrDefault(d=>d.Id==device.Id);
        devices.RemoveAll(d=>d.Id==device.Id);devices.Add(device);
        try{SaveDevices();}catch{devices.RemoveAll(d=>d.Id==device.Id);if(previous is not null)devices.Add(previous);throw;}
        online[device.Id]=DateTimeOffset.UtcNow;probed[device.Id]=DateTimeOffset.UtcNow;
        search.Clear();Filter("");list.SelectedItem=device;list.Invalidate();
        status.Text=$"{device.Name} salvo na lista ({device.Host}). Selecione Conectar para solicitar atendimento.";
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
    async Task SearchAgents()
    {
        if(scanning is not null||searchingAgents){status.Text="Busca em andamento. Aguarde ou use Parar varredura.";return;}
        searchingAgents=true;
        try
        {
            if(discovery is not null)await discovery.Search();
            // Recheck saved hosts too: these may be outside the locally inferred subnets.
            await Task.WhenAll(devices.ToArray().Select(d=>CheckDevice(d,true)));
            await ScanLocal();
        }
        finally{searchingAgents=false;}
    }
    Task ScanLocal()
    {
        if(scanning is not null||closing)return Task.CompletedTask;
        var path=Path.Combine(folder,"scan-ranges.json");
        var networks=IPv4Range.LocalCidrs(true).Concat(File.Exists(path)?Json.Read<string[]>(path):Array.Empty<string>()).Distinct().ToArray();
        return Scan(networks.Select(s=>IPv4Range.Parse(s)).ToArray());
    }
    async Task Scan(IPv4Range[] ranges)
    {
        if(ranges.Length==0){scanStatus.Text="Use Varredura de IP para informar a rede.";return;}
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);scanning=cancellation;
        var progress=new Progress<ScanProgress>(p=>{if(!closing)scanStatus.Text=$"IPs: {p.Checked}/{p.Total} • Agentes: {p.Found}";});
        try
        {
            var discovered=new System.Collections.Concurrent.ConcurrentDictionary<string,Device>();
            await NetworkScanner.Scan(ranges,authorities.Select(a=>a.Id).ToArray(),identity.Public.Id,d=>{discovered[d.Id]=d;QueueDiscovered(d);},progress,cancellation.Token);
            await Task.WhenAll(discovered.Values.Select(d=>CheckDevice(d,true)));
            if(!closing){Filter(search.Text);scanStatus.Text="Busca de agentes concluída.";status.Text=$"Busca concluída: {devices.Count(IsOnline)} agente(s) com comunicação recente • {devices.Count} máquina(s) salvas. Se não encontrou o agente, confira se está aberto e use Varredura de IP para outras redes.";}
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
    async void Discovered(Device d)=>await CheckDevice(d);
    async Task CheckDevice(Device d,bool force=false)
    {
        if(client?.Session is not null&&list.SelectedItem is Device active&&active.Id==d.Id)return;
        if(checking.Contains(d.Id)||!force&&probed.TryGetValue(d.Id,out var last)&&DateTimeOffset.UtcNow-last<TimeSpan.FromSeconds(7))return;
        probed[d.Id]=DateTimeOffset.UtcNow;checking.Add(d.Id);await probes.WaitAsync();
        try
        {
            if(closing||IsDisposed)return;
            using var candidate=new RemoteClient(d,identity,TimeSpan.FromSeconds(5));using var response=await candidate.Request(HttpMethod.Get,"/info");
            using var info=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if(info.RootElement.GetProperty("id").GetString()!=d.Id)throw new InvalidDataException("O IP responde com outro cadastro de agente; este registro pode ser antigo.");
            if(closing||IsDisposed)return;
            online[d.Id]=DateTimeOffset.UtcNow;connectionErrors.Remove(d.Id);
            var root=info.RootElement;
            var previous=observations.GetValueOrDefault(d.Id);
            string Field(string key)=>root.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
            observations[d.Id]=new(Field("hostname") is {Length:>0} host?host:MachineNames.Display(d,previous),Field("description"),!string.IsNullOrWhiteSpace(previous?.RamTotal)?previous.OS:Field("os"),Field("osVersion"),Field("version"),DateTimeOffset.UtcNow,previous?.RamTotal??"");
            // Inventory is authenticated and pinned like /info; cache expensive hardware
            // reads rather than querying them with every presence heartbeat.
            if(root.TryGetProperty("capabilities",out var capabilities)&&capabilities.ValueKind==JsonValueKind.Array
                &&capabilities.EnumerateArray().Any(c=>c.ValueKind==JsonValueKind.String&&c.GetString()=="inventory")
                &&(!inventoryChecked.TryGetValue(d.Id,out var checkedAt)||DateTimeOffset.UtcNow-checkedAt>TimeSpan.FromMinutes(5)))
            {
                inventoryChecked[d.Id]=DateTimeOffset.UtcNow;
                try
                {
                    using var inventoryResponse=await candidate.Request(HttpMethod.Get,"/inventory");
                    var inventory=JsonSerializer.Deserialize<Dictionary<string,string>>(await inventoryResponse.Content.ReadAsStringAsync(),Json.Options);
                    if(closing||IsDisposed)return;
                    if(inventory is not null)observations[d.Id]=MachineDescriptions.ApplyInventory(observations[d.Id],inventory);
                }
                catch{ /* A failed inventory read does not invalidate a successful presence check. */ }
            }
            Json.Write(ObservationsPath,observations);
            var existing=devices.FirstOrDefault(x=>x.Id==d.Id);
            if(existing!=d){devices.RemoveAll(x=>x.Id==d.Id);devices.Add(d);dirty=true;}
            Filter(search.Text);
        }catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.TooManyRequests){online[d.Id]=DateTimeOffset.UtcNow;if(!IsDisposed)list.Invalidate();}
        catch(Exception e)
        {
            online.Remove(d.Id);
            connectionErrors[d.Id]=e is HttpRequestException h&&h.StatusCode is not null?$"HTTP {(int)h.StatusCode}: verifique autorização e versão do Cliente":e is InvalidDataException?e.Message:e is TaskCanceledException?"Agente não respondeu a tempo; confira se está aberto":e is HttpRequestException?"Sem conexão segura com o agente; confira IP, Cliente e firewall":"Não foi possível ler os dados do agente";
            if(!IsDisposed&&!closing)Filter(search.Text);
        }
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
    void Filter(string query)
    {
        if(closing)return;
        string? selected=(list.SelectedItem as Device)?.Id;
        int top=list.TopIndex;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach(var d in devices.Where(d=>(!onlyOnline.Checked||IsOnline(d))&&(MachineLabel(d)+d.ToString()).Contains(query,StringComparison.CurrentCultureIgnoreCase))
                .OrderByDescending(IsOnline).ThenBy(d=>observations.GetValueOrDefault(d.Id)?.Hostname??d.Name,StringComparer.CurrentCultureIgnoreCase))
            {list.Items.Add(d);if(d.Id==selected)list.SelectedItem=d;}
            if(list.Items.Count>0)list.TopIndex=Math.Min(Math.Max(top,0),list.Items.Count-1);
            machineCount.Text=$"{devices.Count(IsOnline)} conectados • {devices.Count} salvos • {list.Items.Count} exibidos";
        }
        finally{list.EndUpdate();}
    }
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
            selectedMonitor="primary";monitorSignature="";monitorPicker.Items.Clear();monitorPicker.Enabled=true;frameLayout="";frameOffsetX=frameOffsetY=0;
            client=new(d,identity,TimeSpan.FromSeconds(45));status.Text="Aguardando autorização do funcionário (até 30 segundos)...";await client.Start();online[d.Id]=DateTimeOffset.UtcNow;list.Invalidate();status.Text=$"ATENDIMENTO: {d.Name} • {d.Sector} | Técnico: {identity.Public.Name}";
            chatAvailable=true; // Supported by all currently distributed clients; first image takes priority.
            chatCursor=0;chatPanel.ClearHistory();chatPanel.SetAvailable(chatAvailable);
            chatPanel.SetTitle(chatAvailable?$"Conversa com {d.Name} • Técnico: {identity.Public.Name}":"Atualize o Cliente para usar o chat.");
            tabs.SelectedTab=remotePage;await Frame();timer.Start();chatClock.Start();
            if(autoFullScreen.Checked)ToggleFullScreen();screen.Focus();
        }finally{connect.Enabled=true;}
    }
    async Task Disconnect()
    {
        timer.Stop();chatClock.Stop();ExitFullScreen();await operations.WaitAsync();
        try{if(client is not null){try{await client.Stop().WaitAsync(TimeSpan.FromSeconds(2));}finally{client.Dispose();client=null;}}}
        finally{operations.Release();held.Clear();var old=screen.Image;screen.Image=null;old?.Dispose();status.Text="Atendimento encerrado.";chatAvailable=false;chatPanel.SetAvailable(false);chatPanel.SetTitle("Atendimento encerrado. Conecte novamente para conversar.");}
    }
    async Task Frame()
    {
        if(client?.Session is null||loading)return;
        var active=client;var requestedMonitor=selectedMonitor;loading=true;
        try{using var r=await active.Request(HttpMethod.Get,"/frame?monitor="+Uri.EscapeDataString(requestedMonitor));using var stream=await r.Content.ReadAsStreamAsync();using var image=Image.FromStream(stream);var old=screen.Image;if(ReferenceEquals(client,active)&&active.Session is not null){screen.Image=new Bitmap(image);old?.Dispose();online[active.DeviceId]=DateTimeOffset.UtcNow;
                frameOffsetX=HeaderInt(r,"X-SDE-Offset-X");frameOffsetY=HeaderInt(r,"X-SDE-Offset-Y");
                frameLayout=r.Headers.TryGetValues("X-SDE-Monitors",out var layout)?layout.First():"";
                RefreshMonitors(r,requestedMonitor);}}
        catch(Exception)when(!ReferenceEquals(client,active)||active.Session is null){}
        finally{loading=false;}
    }
    static int HeaderInt(HttpResponseMessage response,string name)=>response.Headers.TryGetValues(name,out var values)&&int.TryParse(values.FirstOrDefault(),out var number)?number:0;
    void RefreshMonitors(HttpResponseMessage response,string requested)
    {
        if(selectedMonitor!=requested||!response.Headers.TryGetValues("X-SDE-Monitors",out var values))return;
        string signature=values.First();
        string current=response.Headers.TryGetValues("X-SDE-Monitor",out var ids)?Uri.UnescapeDataString(ids.First()):"all";
        if(signature==monitorSignature&&current==selectedMonitor)return;
        var monitors=JsonSerializer.Deserialize<RemoteMonitor[]>(Convert.FromBase64String(signature),Json.Options)??[];
        updatingMonitors=true;
        try
        {
            monitorPicker.Items.Clear();monitorPicker.Items.Add(new RemoteMonitor("all","Todos os monitores",0,0,0,0,false));
            monitorPicker.Items.AddRange(monitors.Cast<object>().ToArray());
            monitorPicker.SelectedItem=monitorPicker.Items.Cast<RemoteMonitor>().FirstOrDefault(m=>m.Id==current);
            selectedMonitor=current;monitorSignature=signature;
        }finally{updatingMonitors=false;}
    }
    async Task Input(InputCommand command)
    {
        if(client?.Session is null)return;
        command=command with{Layout=frameLayout};
        await operations.WaitAsync();
        try{if(client?.Session is not null){using var r=await client.Request(HttpMethod.Post,"/input",command);}}
        catch(HttpRequestException e)when(e.StatusCode==System.Net.HttpStatusCode.Conflict&&e.Message.Contains("monitores",StringComparison.OrdinalIgnoreCase))
        {held.Clear();status.Text="Monitores alterados. Aguarde a imagem atualizada e repita o comando.";await Frame();}
        finally{operations.Release();}
    }
    static bool HasCapability(JsonDocument info,string name)=>info.RootElement.TryGetProperty("capabilities",out var capabilities)
        &&capabilities.ValueKind==JsonValueKind.Array&&capabilities.EnumerateArray().Any(c=>c.GetString()==name);
    void ToggleFullScreen()
    {
        if(fullScreen is not null){ExitFullScreen();return;}
        if(client?.Session is null){MessageBox.Show("Conecte a uma máquina antes de abrir a tela cheia.");return;}
        fullScreen=new FullScreenWindow(screen,ExitFullScreen,()=>{ExitFullScreen();tabs.SelectedTab=chatPage;},()=>Safe(Disconnect),monitorPicker);
        fullScreen.FormClosing+=(_,e)=>{e.Cancel=true;ExitFullScreen();};
        fullScreen.Show(this);screen.Focus();
    }
    void ExitFullScreen()
    {
        if(fullScreen is null)return;
        var previous=fullScreen;fullScreen=null;remotePage.Controls.Add(screen);remotePage.Controls.SetChildIndex(screen,0);var bar=remotePage.Controls.OfType<FlowLayoutPanel>().Single();bar.Controls.Add(monitorPicker);previous.Dispose();
        tabs.SelectedTab=remotePage;screen.Focus();
    }
    async Task SendChat(string value)
    {
        await operations.WaitAsync();
        try
        {
            if(client?.Session is null||!chatAvailable)throw new InvalidOperationException("Conecte a um Cliente atualizado para conversar.");
            using var response=await client.Request(HttpMethod.Post,"/chat",new MessageText(value));
            // Read in sequence via polling, avoiding duplicate own messages.
        }finally{operations.Release();}
        await PollChat();
    }
    async Task PollChat()
    {
        if(!await operations.WaitAsync(0))return;
        try
        {
            if(client?.Session is null||!chatAvailable)return;
            using var response=await client.Request(HttpMethod.Get,"/chat?after="+chatCursor);
            var page=JsonSerializer.Deserialize<ChatPage>(await response.Content.ReadAsStringAsync(),Json.Options)??throw new InvalidDataException("Resposta de chat inválida.");
            foreach(var message in page.Messages.OrderBy(m=>m.Sequence))if(message.Sequence>chatCursor){chatPanel.Append(message);chatCursor=message.Sequence;}
        }finally{operations.Release();}
    }
    async Task Broadcast()
    {
        string value=NoticeImages.Caption(noticeText.Text,noticeImage is not null);
        var recipients=noticeTargets.CheckedItems.Cast<Device>().Where(d=>online.TryGetValue(d.Id,out var seen)&&DateTimeOffset.UtcNow-seen<TimeSpan.FromSeconds(45)).ToArray();
        if(recipients.Length==0)throw new InvalidOperationException("Marque pelo menos um Cliente online ou clique em Selecionar todos.");
        if(MessageBox.Show(this,$"Enviar este aviso para {recipients.Length} computador(es) online?\n\n{value}\n\nFoto anexada: {(noticeImage is null?"não":"sim")}\nIdentificação exibida: Aviso da TI","Confirmar aviso global",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        sendNotice.Enabled=false;noticeTargets.Enabled=false;noticeText.ReadOnly=true;noticeResults.Items.Clear();
        var message=new AnnouncementRequest(Guid.NewGuid().ToString(),value,noticeImage);
        int delivered=0;using var limit=new SemaphoreSlim(8,8);
        try
        {
            await Task.WhenAll(recipients.Select(async device=>{
                await limit.WaitAsync();
                try
                {
                    if(closing)return;
                    using var recipient=new RemoteClient(device,identity,TimeSpan.FromSeconds(8));
                    using var response=await recipient.Request(HttpMethod.Get,"/info");
                    using var info=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    if(closing)return;
                    if(!HasCapability(info,"announcements")){noticeResults.Items.Add($"{device.Name} ({device.Host}): atualize o Cliente para receber avisos.");return;}
                    if(message.ImageBase64 is not null&&!HasCapability(info,"announcement-images")){noticeResults.Items.Add($"{device.Name}: atualize o Cliente para receber fotos.");return;}
                    using var result=await recipient.Request(HttpMethod.Post,"/announcement",message);
                    delivered++;if(closing)return;noticeResults.Items.Add($"{device.Name} ({device.Host}): entregue ao agente.");
                }
                catch(Exception e){if(!closing)noticeResults.Items.Add($"{device.Name} ({device.Host}): falha — {e.Message}");}
                finally{limit.Release();}
            }));
            if(!closing)noticeResults.Items.Add($"Concluído: {delivered}/{recipients.Length} entregues. Não há confirmação de leitura.");
        }finally{if(!closing){sendNotice.Enabled=true;noticeTargets.Enabled=true;noticeText.ReadOnly=false;}}
    }
    Point? Map(int x,int y,bool clamp=false)
    {
        if(screen.Image is not Image image||client?.Session is null)return null;
        double scale=Math.Min((double)screen.ClientSize.Width/image.Width,(double)screen.ClientSize.Height/image.Height);
        int px=(int)((x-(screen.ClientSize.Width-image.Width*scale)/2)/scale),py=(int)((y-(screen.ClientSize.Height-image.Height*scale)/2)/scale);
        if(clamp)return MonitorCoordinates.Offset(new Point(Math.Clamp(px,0,image.Width-1),Math.Clamp(py,0,image.Height-1)),frameOffsetX,frameOffsetY);
        return px>=0&&py>=0&&px<image.Width&&py<image.Height?MonitorCoordinates.Offset(new Point(px,py),frameOffsetX,frameOffsetY):null;
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

