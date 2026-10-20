using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SdeSuporte;

static class Tests
{
    static void Check(bool value,string label){if(!value)throw new Exception("FALHOU: "+label);Console.WriteLine("OK: "+label);}
    static async Task<int> Main(string[] args)
    {
        try
        {
            if(args[0]=="client-package")
            {
                var installer=args[1];var version=Version.Parse(args[2]);
                Check(ClientPackage.Verify(installer,version).Length==64,"pacote válido");
                var scratch=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString());Directory.CreateDirectory(scratch);
                try{
                    string copy=Path.Combine(scratch,"client.exe");
                    void Reject(string path,string label){bool rejected=false;try{ClientPackage.Verify(path,version);}catch(IOException){rejected=true;}Check(rejected,label);}
                    Reject(copy,"pacote ausente");File.Copy(installer,copy);
                    Reject(copy,"hash ausente");File.WriteAllText(copy+".sha256",new string('0',64));Reject(copy,"hash divergente");
                    File.Copy(installer+".sha256",copy+".sha256",true);
                    using(var locked=File.Open(copy,FileMode.Open,FileAccess.Read,FileShare.None))Reject(copy,"falha de leitura");
                    bool incompatible=false;try{ClientPackage.Verify(copy,new Version(99,0,0));}catch(IOException){incompatible=true;}Check(incompatible,"pacote incompatível");
                    var original=new Device(Guid.NewGuid().ToString(),"host","TI","192.168.1.10",7443,new string('A',64));
                    var administrative=new Dictionary<string,MachineMetadata>{{original.Id,new("Apelido","Descrição","Setor","Local")}};
                    Check(MachineNames.Display(original,null)=="host","nome cadastrado aparece sem consulta ao agente");
                    var observation=new MachineObservation("PC-ATUAL","","","","",DateTimeOffset.UtcNow);
                    Check(MachineNames.Display(original,observation)=="PC-ATUAL","hostname autenticado tem prioridade sobre nome cadastrado");
                    Check(MachineNames.Display(original,observation with{Hostname="Nome não identificado"})=="host","cache antigo sem hostname usa nome cadastrado");
                    Check(MachineNames.Display(original,observation with{Hostname=" "})=="host","hostname vazio usa nome cadastrado");
                    var hardware=MachineDescriptions.ApplyInventory(observation,new Dictionary<string,string>{{"Nome da máquina / host","CLIENTE-01"},{"Sistema operacional","Windows 11 Pro"},{"RAM total","15,80 GiB"}});
                    Check(hardware.Hostname=="CLIENTE-01"&&MachineDescriptions.Summary(hardware).Contains("Windows 11 Pro • RAM total: 15,80 GiB"),"descricao exibe nome, sistema e RAM do inventario remoto");
                    Check(MachineDescriptions.ApplyInventory(hardware,new Dictionary<string,string>{{"RAM total",""}}).RamTotal=="15,80 GiB","inventario incompleto preserva RAM previamente coletada");
                    var legacy=JsonSerializer.Deserialize<MachineObservation>("{\"hostname\":\"LEGADO\",\"description\":\"\",\"os\":\"Windows 10\",\"version\":\"\",\"agentVersion\":\"0.11.0\",\"seen\":\"2026-10-20T00:00:00Z\"}",Json.Options);
                    Check(MachineDescriptions.Summary(legacy).Contains("RAM total: não informada"),"cache anterior sem RAM continua legivel sem inventar memoria");
                    var loggedIn=hardware with{SessionUser=@"SDE\maria.silva"};
                    Check(MachineDescriptions.UserLine(loggedIn,true)==@"Usuário conectado: SDE\maria.silva","lista exibe usuario da sessao separado do hostname");
                    Check(MachineDescriptions.UserLine(loggedIn,false)==@"Último usuário informado: SDE\maria.silva","maquina offline nao apresenta usuario antigo como conectado");
                    Check(MachineDescriptions.UserLine(legacy,true)=="Usuário conectado: não informado","agente e cache antigos sem usuario nao inventam login");
                    Check(MachineDescriptions.ApplyInventory(loggedIn,new Dictionary<string,string>{{"Usuário conectado",@"SDE\outro"}}).SessionUser==loggedIn.SessionUser,"inventario de hardware nao sobrescreve usuario da verificacao de presenca");
                    var persisted=Path.Combine(scratch,"metadata.json");Json.Write(persisted,administrative);
                    var moved=original with{Host="192.168.1.20"};
                    Check(Json.Read<Dictionary<string,MachineMetadata>>(persisted)[moved.Id]==administrative[original.Id],"metadados persistem por ID após troca de IP");
                }finally{Directory.Delete(scratch,true);}return 0;
            }
            if(args[0] is "prepare" or "prepare-team")
            {
                Directory.CreateDirectory(args[1]);
                using var id=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Técnico de Teste");
                if(args[0]=="prepare-team")
                {
                    using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    var code=TeamAccess.EncodeCode(key.ExportParameters(true).D!);
                    using var root=new TechnicianIdentity(key,"Equipe de Teste");
                    Json.Write(Path.Combine(args[1],"deployment.json"),Enrollment.Create(root,[],"Teste",true));
                    Json.Write(Path.Combine(args[1],"team-root.json"),root.Public);
                    File.WriteAllText(Path.Combine(args[1],"root-test.code"),code);
                }
                else Json.Write(Path.Combine(args[1],"deployment.json"),Enrollment.Create(id,[],"Teste"));
                Json.Write(Path.Combine(args[1],"tecnico.sdetech"),id.Public);File.WriteAllBytes(Path.Combine(args[1],"private-test.pk8"),id.ExportPrivate());return 0;
            }
            if(args[0]=="protocol") {var inventory=MachineInventory.Collect();Check(inventory["Nome da máquina / host"]==Environment.MachineName&&inventory.ContainsKey("RAM total")&&inventory.ContainsKey("Sistema operacional"),"inventario Windows coleta host, sistema e RAM reais");Check(inventory.ContainsKey("Usuário conectado"),"inventario inclui campo de usuario da sessao");Protocol();EnrollmentTests();TeamTests();RangeTests();MessagingTests();await InventoryTests();TechnicianSetupTests();return 0;}
            if(args[0]=="template"){InstallerPackage.CreateTemplate(args[1],args[2],args[3]);return 0;}
            if(args[0]=="package"){using var creator=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Preparador");InstallerPackage.Personalize(args[1],args[2],Enrollment.Create(creator,[],"Teste"));return 0;}
            if(args[0]=="live-team"){await LiveTeam(args[1],args[2]);return 0;}
            if(args[0]=="consent-live"){await ConsentLive(args[1],args[2]);return 0;}
            if(args[0]=="installed"){await Installed(args[1]);return 0;}
            if(args[0]=="package-technician"){
                var root=Json.Read<TechnicianKey>(Path.Combine(args[2],"team-root.json"));
                TechnicianSetup.Create(args[1],args[3],File.ReadAllText(Path.Combine(args[2],"root-test.code")),root);
                Check(TechnicianSetup.Read(args[3],root) is not null,"instalador Inno recebeu autorizacao embutida valida");return 0;
            }
            await Live(args[1],args[2]);return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static async Task InventoryTests()
    {
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
        Check(await NetworkInventory.Port(IPAddress.Loopback,port,CancellationToken.None),"inventario encontra porta Windows sem protocolo ou agente SDE");
        listener.Stop();Check(!await NetworkInventory.Port(IPAddress.Loopback,port,CancellationToken.None),"inventario diferencia porta fechada");
        var host=await NetworkInventory.Probe(IPAddress.Loopback,CancellationToken.None);
        Check(host?.Address=="127.0.0.1","descoberta encontra maquina acessivel mesmo sem agente instalado");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try{await NetworkInventory.Scan([IPv4Range.Parse("127.0.0.1/32",true)],_=>{},null,cancelled.Token);throw new Exception("inventario ignorou cancelamento");}
        catch(OperationCanceledException){Console.WriteLine("OK: descoberta sem agente respeita cancelamento");}
    }
    static void MessagingTests()
    {
        Check(MonitorCoordinates.Offset(new Point(50,60),1920,200)==new Point(1970,260),"entrada do monitor selecionado corresponde ao desktop virtual");
        Check(MonitorCoordinates.Offset(new Point(0,0),0,0)==Point.Empty,"monitor a esquerda do principal preserva origem virtual");
        using(var sample=new Bitmap(320,180))
        {
            using(var graphics=Graphics.FromImage(sample))graphics.Clear(Color.CornflowerBlue);
            string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");sample.Save(path,System.Drawing.Imaging.ImageFormat.Png);
            try{var photo=NoticeImages.FromFile(path);using var decoded=NoticeImages.Decode(photo);Check(decoded.Width==320&&decoded.Height==180,"foto normalizada preserva proporcao e pode ser exibida");}finally{File.Delete(path);}
        }
        try{NoticeImages.Decode("<svg>conteudo</svg>");throw new Exception("foto invalida aceita");}catch(ArgumentException){Console.WriteLine("OK: arquivo que nao e imagem rejeitado");}
        Check(NoticeImages.Caption("",true)=="","foto pode ser enviada sem legenda");
        var history=new MessageHistory();
        for(int i=0;i<205;i++)history.Add("Funcionário","Mensagem "+i);
        Check(history.After(0).Messages.Length==200&&history.After(204).Messages.Single().Sequence==205,"historico limitado preserva sequencia e cursor");
        foreach(var value in new[]{"","   ",new string('a',2001),"texto\0oculto"})
        {try{MessageHistory.Validate(value);throw new Exception("mensagem invalida aceita");}catch(ArgumentException){Console.WriteLine("OK: mensagem vazia, excessiva ou de controle rejeitada");}}
        Check(MessageHistory.Validate("  texto\nlinha  ")=="texto\nlinha","mensagens aceitam texto simples e linhas");
        Exception? error=null;
        var thread=new Thread(()=>{
            try
            {
                ApplicationConfiguration.Initialize();
                using var owner=new Form{Width=800,Height=600};var display=new RemoteScreen{Dock=DockStyle.Fill};owner.Controls.Add(display);owner.Show();
                using(var full=new FullScreenWindow(display,()=>{},()=>{},()=>Task.CompletedTask))
                {
                    full.Show(owner);Application.DoEvents();
                    Check(display.Parent==full&&full.FormBorderStyle==FormBorderStyle.None&&full.WindowState==FormWindowState.Maximized,"visualizacao ocupa tela cheia mantendo superficie remota");
                    owner.Controls.Add(display);full.Close();
                }
                Check(!display.IsDisposed&&display.Parent==owner,"superficie remota preservada ao voltar da tela cheia");
                using var chat=new MessagePanel();owner.Controls.Add(chat);chat.BringToFront();chat.SetAvailable(true);
                string? sent=null;chat.Send=value=>{sent=value;return Task.CompletedTask;};
                var composer=chat.Controls.OfType<Panel>().Single();composer.Controls.OfType<TextBox>().Single().Text="Resposta do funcionário";
                composer.Controls.OfType<Button>().Single().PerformClick();Application.DoEvents();
                Check(sent=="Resposta do funcionário","interface permite resposta do funcionario sem console tecnico");
                using(var notice=new EmployeeAnnouncements()){notice.Receive(new(0,"NOME PRIVADO DO TECNICO","Aviso de teste",DateTimeOffset.UtcNow));var rendered=notice.HistoryText;Check(rendered.Contains("AVISO DA TI")&&rendered.Contains("Aviso de teste")&&!rendered.Contains("NOME PRIVADO"),"aviso global nao exibe nome do tecnico");}
                using(var previewIdentity=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Técnico de teste"))
                using(var console=new Technician(previewIdentity))
                {
                    console.Show();Application.DoEvents();
                    var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                    var fixtures=(List<Device>)typeof(Technician).GetField("devices",flags)!.GetValue(console)!;fixtures.Clear();
                    var presence=(Dictionary<string,DateTimeOffset>)typeof(Technician).GetField("online",flags)!.GetValue(console)!;
                    for(int i=0;i<3;i++){var d=new Device(Guid.NewGuid().ToString(),"Cliente "+i,"Teste","127.0.0.1",17443,new string('0',64));fixtures.Add(d);if(i<2)presence[d.Id]=DateTimeOffset.UtcNow;}
                    var tabs=console.Controls.OfType<TabControl>().Single();tabs.SelectedIndex=2;Application.DoEvents();
                    IEnumerable<Control> Descendants(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descendants(c)));
                    Check(Descendants(console).OfType<Button>().Any(b=>b.Text=="Adicionar Raspberry/Linux"),"console oferece adicionar Raspberry/Linux pelo IP");
                    Check(Descendants(console).OfType<Button>().Any(b=>b.Text=="Configurações da máquina"),"console oferece configuracoes da maquina");
                    Check(Descendants(console).OfType<Button>().Any(b=>b.Text=="Buscar Raspberry Pi"),"console oferece busca automatica de Raspberry");
                    Check(Descendants(console).OfType<Button>().Any(b=>b.Text=="Buscar agentes instalados"),"console oferece busca de agentes instalados");
                    var agentList=(ListBox)typeof(Technician).GetField("list",flags)!.GetValue(console)!;
                    var filter=typeof(Technician).GetMethod("Filter",flags)!;
                    var onlineOnly=(CheckBox)typeof(Technician).GetField("onlyOnline",flags)!.GetValue(console)!;
                    filter.Invoke(console,new object[]{""});
                    Check(agentList.Items.Count==3&&presence.ContainsKey(((Device)agentList.Items[0]).Id),"lista preserva salvos e prioriza conectados");
                    onlineOnly.Checked=true;
                    Check(agentList.Items.Count==2,"filtro mostra apenas agentes com comunicacao recente");
                    filter.Invoke(console,new object[]{"Cliente 1"});
                    Check(agentList.Items.Count==1&&((Device)agentList.Items[0]).Name=="Cliente 1","pesquisa textual combina com filtro de conectados");
                    presence[fixtures[1].Id]=DateTimeOffset.UtcNow.AddMinutes(-1);
                    filter.Invoke(console,new object[]{""});
                    Check(agentList.Items.Count==1,"agente sem comunicacao recente deixa filtro de conectados");
                    presence[fixtures[1].Id]=DateTimeOffset.UtcNow;onlineOnly.Checked=false;
                    using(var finder=new FindLinuxAgentsDialog([previewIdentity.Public.Id],previewIdentity,CancellationToken.None)){
                        Check(finder.SelectedDevice is null,"busca inicia sem selecao nao verificada");
                        Check(!Descendants(finder).OfType<Button>().Single(b=>b.Text=="Adicionar selecionado").Enabled,"busca exige resultado verificado antes de adicionar");
                    }
                    using(var linuxDialog=new AddLinuxAgentDialog([previewIdentity.Public.Id],previewIdentity,CancellationToken.None)){
                        linuxDialog.Show(console);Application.DoEvents();
                        Check(linuxDialog.SelectedDevice is null,"dialogo inicia sem computador nao verificado");
                        Check(!Descendants(linuxDialog).OfType<Button>().Single(b=>b.Text=="Adicionar à lista").Enabled,"salvar desabilitado antes de verificar agente");
                        var ipField=Descendants(linuxDialog).OfType<TextBox>().Single(b=>!b.Multiline);ipField.Text="10.37.3.163";
                        Check(!Descendants(linuxDialog).OfType<Button>().Single(b=>b.Text=="Adicionar à lista").Enabled,"alterar IP exige novo teste");
                        linuxDialog.Close();
                    }
                    var targets=(CheckedListBox)typeof(Technician).GetField("noticeTargets",flags)!.GetValue(console)!;
                    Descendants(console).OfType<Button>().Single(b=>b.Text=="Selecionar todos").PerformClick();Check(targets.CheckedItems.Count==2,"selecionar todos marca somente clientes online");
                    Descendants(console).OfType<Button>().Single(b=>b.Text=="Limpar seleção").PerformClick();Check(targets.CheckedItems.Count==0,"limpar selecao desmarca todos os destinatarios");
                    using var bitmap=new Bitmap(console.Width,console.Height);console.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    string qa=Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")??Path.GetTempPath(),"SDE-Visual-QA");Directory.CreateDirectory(qa);bitmap.Save(Path.Combine(qa,"console.png"));
                    Check(console.Controls.OfType<TabControl>().Single().TabPages.Count==5,"console inclui atualizacoes e abas de suporte");
                }
                owner.Close();
            }catch(Exception e){error=e;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(error is not null)throw error;
    }
    static async Task MessagingLive(RemoteClient client,Device device,TechnicianIdentity identity)
    {
        using(var sent=await client.Request(HttpMethod.Post,"/chat",new MessageText("Olá, funcionário <texto simples>"))){}
        using(var response=await client.Request(HttpMethod.Get,"/chat?after=0"))
        {
            var page=JsonSerializer.Deserialize<ChatPage>(await response.Content.ReadAsStringAsync(),Json.Options)!;
            Check(page.Messages.Single().Sender==identity.Public.Name&&page.Messages.Single().Text.Contains("<texto simples>"),"chat real preserva nome autenticado e texto simples");
        }
        IntPtr window=FindWindow(null,"SDE Suporte — mensagens");Check(window!=IntPtr.Zero,"chat do funcionario aparece ao receber mensagem");
        IntPtr input=IntPtr.Zero,send=IntPtr.Zero;
        EnumChildWindows(window,(child,_)=>{
            var className=new StringBuilder(256);GetClassName(child,className,256);
            string nativeClass=className.ToString();
            if(nativeClass.Contains("EDIT",StringComparison.OrdinalIgnoreCase)&&!nativeClass.Contains("RICH",StringComparison.OrdinalIgnoreCase))input=child;
            var title=new StringBuilder(256);SendRead(child,0x000D,(IntPtr)256,title);
            if(title.ToString()=="Enviar")send=child;
            Console.WriteLine("UI chat: "+nativeClass+" | "+title);return true;
        },IntPtr.Zero);
        Check(input!=IntPtr.Zero&&send!=IntPtr.Zero,"campos de resposta do funcionario disponiveis");
        SetForegroundWindow(window);
        SendText(input,0x000C,IntPtr.Zero,"Resposta do funcionário");SendClick(send,0x00F5,IntPtr.Zero,IntPtr.Zero);
        using(var response=await client.Request(HttpMethod.Get,"/chat?after=1"))
        {
            var page=JsonSerializer.Deserialize<ChatPage>(await response.Content.ReadAsStringAsync(),Json.Options)!;
            Check(page.Messages.Single().Text=="Resposta do funcionário"&&page.Messages.Single().Sender==Environment.UserName,"resposta real do funcionario chega ao tecnico pelo chat");
        }
        using var otherSession=new RemoteClient(device,identity);
        try{using var response=await otherSession.Request(HttpMethod.Get,"/chat");throw new Exception("chat sem token aceito");}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: chat exige token do atendimento ativo");}
        try{using var bad=await otherSession.Request(HttpMethod.Post,"/announcement",new AnnouncementRequest(Guid.NewGuid().ToString(),"","nao-e-uma-imagem"));throw new Exception("foto invalida recebida");}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.BadRequest){Console.WriteLine("OK: agente rejeita foto invalida sem exibir");}
        using var boardPhoto=new Bitmap(80,60);using var photoData=new MemoryStream();boardPhoto.Save(photoData,System.Drawing.Imaging.ImageFormat.Jpeg);
        var notice=new AnnouncementRequest(Guid.NewGuid().ToString(),"Aviso geral de teste",Convert.ToBase64String(photoData.ToArray()));
        using(var response=await otherSession.Request(HttpMethod.Post,"/announcement",notice))Check(response.IsSuccessStatusCode,"aviso global autenticado funciona sem iniciar atendimento");
        using(var duplicate=await otherSession.Request(HttpMethod.Post,"/announcement",notice))
        using(var payload=JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync()))Check(payload.RootElement.GetProperty("duplicate").GetBoolean(),"reenvio do mesmo aviso nao duplica janela ou mensagem");
        try{using var response=await otherSession.Request(HttpMethod.Post,"/announcement",notice with{Id=Guid.NewGuid().ToString()});throw new Exception("rajada aceita");}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.TooManyRequests){Console.WriteLine("OK: limite entre avisos impede rajadas");}
        using var stranger=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Não autorizado");
        using var denied=new RemoteClient(device,stranger);
        try{using var response=await denied.Request(HttpMethod.Post,"/announcement",notice);throw new Exception("aviso nao autorizado aceito");}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: aviso global de tecnico sem autorizacao bloqueado");}
        try{using var response=await client.Request(HttpMethod.Post,"/chat",new MessageText(new string('x',2001)));throw new Exception("chat excessivo aceito");}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.BadRequest){Console.WriteLine("OK: tamanho de mensagem validado no agente");}
    }
    static void TechnicianSetupTests()
    {
        var directory=Path.Combine(Path.GetTempPath(),"SDE-Tech-Setup-Test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var profiles=new List<string>();
        try
        {
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string code=TeamAccess.EncodeCode(key.ExportParameters(true).D!);
            using var root=new TechnicianIdentity(key,"Equipe de teste");
            string source=Path.Combine(directory,"base.exe"),package=Path.Combine(directory,"authorized.exe");
            byte[] stub=new byte[2048];RandomNumberGenerator.Fill(stub);stub[0]=0x4d;stub[1]=0x5a;File.WriteAllBytes(source,stub);
            Check(TechnicianSetup.Read(source,root.Public) is null,"modelo publico nao contem autorizacao privada");
            TechnicianSetup.Create(source,package,code,root.Public);
            var grant=TechnicianSetup.Read(package,root.Public)!;Check(grant.TeamId==root.Public.Id,"autorizacao automatica reconhece a equipe");
            for(int i=0;i<6;i++)
            {
                var folder=Path.Combine(directory,"profile-"+i);profiles.Add(folder);
                TechnicianProfiles.Authorize(folder,grant,root.Public);
                using var identity=TechnicianProfiles.Load(folder,()=>throw new Exception("Nao deve pedir nome nem codigo."));
                var credential=Json.Read<TeamCredential>(Path.Combine(folder,"team-credential.json"));
                Check(TeamAccess.Verify(credential,root.Public)?.Id==identity.Public.Id,"tecnico "+(i+1)+" autorizado automaticamente pelo instalador");
                Check(identity.Public.Name==Environment.UserName,"identificacao automatica usa usuario do Windows");
                TechnicianProfiles.Authorize(folder,grant,root.Public);
                using var reopened=TechnicianProfiles.Load(folder,()=>throw new Exception("Nao deve perguntar ao reabrir."));
                Check(reopened.Public.Id==identity.Public.Id,"reinstalacao preserva identidade do tecnico");
                Check(!Directory.GetFiles(folder).Any(f=>File.ReadAllText(f).Contains(code)),"perfil instalado nao guarda a autorizacao privada do instalador");
            }
            using var other=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Outra equipe");
            try{TechnicianSetup.Read(package,other.Public);throw new Exception("outra equipe aceita");}catch(InvalidDataException){Console.WriteLine("OK: instalador de outra equipe rejeitado");}
            try{TechnicianSetup.Validate(grant with{Expires=DateTimeOffset.UtcNow.AddMinutes(-1)},root.Public);throw new Exception("autorizacao expirada aceita");}catch(InvalidDataException){Console.WriteLine("OK: autorizacao expirada rejeitada");}
            using(var altered=File.OpenWrite(package)){altered.Position=100;altered.WriteByte((byte)(stub[100]^0xff));}
            try{TechnicianSetup.Read(package,root.Public);throw new Exception("instalador alterado aceito");}catch(InvalidDataException){Console.WriteLine("OK: alteracao do executavel invalida instalador autorizado");}
        }
        finally
        {
            foreach(var profile in profiles)try{using var data=JsonDocument.Parse(File.ReadAllText(Path.Combine(profile,"identity.json")));using var key=CngKey.Open(data.RootElement.GetProperty("keyName").GetString()!);key.Delete();}catch{}
            Directory.Delete(directory,true);
        }
    }
    static void TeamTests()
    {
        Enrollment.VerifyPermission(TeamAccess.Deployment.Approval);Console.WriteLine("OK: autorizacao publica distribuida pertence a equipe SDE");
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string code=TeamAccess.EncodeCode(key.ExportParameters(true).D!);
        using var root=new TechnicianIdentity(key,"Equipe");
        using var imported=TeamAccess.OpenCode(code,root.Public.Name);
        Check(imported.Public.Id==root.Public.Id,"codigo da equipe importa chave correta no Windows");
        var deviceId=Guid.NewGuid().ToString();var verifier=new RequestVerifier([root.Public],deviceId,root.Public);
        for(int i=0;i<6;i++)
        {
            using var technician=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Tecnico "+(i+1));
            var credential=TeamAccess.Authorize(code,technician,root.Public);
            string time=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            string signature=technician.Sign("GET","/info",[],"",time,nonce,deviceId);
            Check(verifier.Verify(technician.Public.Id,deviceId,"GET","/info",[],"",time,nonce,signature,TeamAccess.Encode(credential))?.Name==technician.Public.Name,"tecnico "+(i+1)+" autorizado com mesmo codigo, sem mudar agente");
            Check(TeamAccess.Verify(credential with{Technician=technician.Public with{Name="Nome adulterado"}},root.Public) is null,"nome adulterado no certificado de tecnico rejeitado");
            var discovery=new DiscoveryRequest(technician.Public.Id,long.Parse(time),nonce,technician.SignDetached(DiscoveryNetwork.RequestBytes(technician.Public.Id,long.Parse(time),nonce)),credential);
            Check(DiscoveryNetwork.VerifyRequest(discovery,[root.Public],root.Public),"descoberta aceita tecnico da equipe");
            Check(!DiscoveryNetwork.VerifyRequest(discovery with{Credential=null},[root.Public],root.Public),"descoberta bloqueia console sem autorizacao");
        }
        using var stranger=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Outra equipe");
        var forged=TeamAccess.Issue(stranger,stranger.Public);
        Check(TeamAccess.Verify(forged,root.Public) is null,"certificado de outra equipe rejeitado");
        try{TeamAccess.Authorize(code,stranger,stranger.Public);throw new Exception("codigo de outra equipe aceito");}catch(InvalidDataException){Console.WriteLine("OK: codigo de outra equipe rejeitado");}
    }
    static void RangeTests()
    {
        var subnet=IPv4Range.Parse("10.37.2.0/23");
        Check(subnet.Count==510&&subnet.Addresses().First().ToString()=="10.37.2.1"&&subnet.Addresses().Last().ToString()=="10.37.3.254","varredura contempla mais de 400 IPs e mascara real");
        Check(IPv4Range.Parse("192.168.1.5-192.168.1.8").Count==4,"faixa inicial-final inclusiva");
        Check(IPv4Range.Parse("10.1.1.4/31").Count==2&&IPv4Range.Parse("10.1.1.4/32").Count==1,"redes /31 e /32 tratadas corretamente");
        foreach(var invalid in new[]{"8.8.8.0/24","10.0.0.0/8","192.168.1.9-192.168.1.1","10.37.2.0/banana"})
        {try{IPv4Range.Parse(invalid);throw new Exception("faixa invalida aceita");}catch(ArgumentException){Console.WriteLine("OK: faixa publica, excessiva ou invalida rejeitada");}}
    }
    static async Task LiveTeam(string fixture,string configFile)
    {
        var config=Json.Read<AgentConfig>(configFile);var root=Json.Read<TechnicianKey>(Path.Combine(fixture,"team-root.json"));
        string code=File.ReadAllText(Path.Combine(fixture,"root-test.code"));
        using var first=TechnicianIdentity.FromPrivate(File.ReadAllBytes(Path.Combine(fixture,"private-test.pk8")),"Técnico de Teste");
        first.Credential=TeamAccess.Authorize(code,first,root);
        Device? found=null;
        for(int i=0;i<15&&found is null;i++)
        {await NetworkScanner.Scan([IPv4Range.Parse("127.0.0.1/32",true)],[root.Id],first.Public.Id,d=>found=d,port:config.Device.Port);if(found is null)await Task.Delay(500);}
        Check(found?.Id==config.Device.Id,"varredura IP encontra agente real via HTTPS sem broadcast");
        Check(await NetworkScanner.Discover(IPAddress.Loopback,config.Device.Port,[],first.Public.Id,CancellationToken.None) is null,"varredura rejeita agente de equipe desconhecida");
        var online=new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery=new ConsoleDiscovery(first,()=>[root.Id],d=>online.TrySetResult(d),17445);
        await discovery.SearchTo(new IPEndPoint(IPAddress.Loopback,7446));
        Check((await online.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id==config.Device.Id,"novo tecnico encontra agente por UDP autenticado sem cadastro individual");
        string before=File.ReadAllText(configFile);
        for(int i=0;i<6;i++)
        {
            using var technician=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Tecnico da equipe "+(i+1));
            technician.Credential=TeamAccess.Authorize(code,technician,root);
            using var client=new RemoteClient(found!,technician);
            using var info=await client.Request(HttpMethod.Get,"/info");
            using(var payload=JsonDocument.Parse(await info.Content.ReadAsStringAsync()))
            {
                Check(payload.RootElement.TryGetProperty("sessionUser",out var login)&&login.ValueKind==JsonValueKind.String,"agente real transmite usuario da sessao na presenca autenticada");
                string expectedUser=MachineInventory.SessionUser();
                if(expectedUser.Length>0)Check(login.GetString()==expectedUser,"login remoto corresponde a sessao Windows do agente de teste");
            }
            await client.Start();Check(client.Session?.TechnicianName==technician.Public.Name,"agente real reconhece nome do tecnico "+(i+1));await client.Stop();
        }
        Check(before==File.ReadAllText(configFile),"seis tecnicos conectam sem reinstalar nem alterar configuracao do cliente");
        var automaticFolder=Path.Combine(fixture,"automatic-profile");
        var automaticGrant=new TechnicianSetupGrant(root.Id,code,DateTimeOffset.UtcNow.AddYears(1),"");
        TechnicianProfiles.Authorize(automaticFolder,automaticGrant,root);
        using(var automatic=TechnicianProfiles.Load(automaticFolder,()=>throw new Exception("Instalador nao deve pedir codigo.")))
        {
            automatic.Credential=Json.Read<TeamCredential>(Path.Combine(automaticFolder,"team-credential.json"));
            using var automaticClient=new RemoteClient(found!,automatic);
            await automaticClient.Start();Check(automaticClient.Session?.TechnicianName==Environment.UserName,"autorizacao automatica do instalador conecta ao agente com usuario Windows");await automaticClient.Stop();
        }
        using var unauthorized=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Sem codigo");
        using var denied=new RemoteClient(found!,unauthorized);
        try{using var response=await denied.Request(HttpMethod.Post,"/session");throw new Exception("sem codigo iniciou sessao");}
        catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: instalar console sem codigo nao concede controle remoto");}
        using var stop=new CancellationTokenSource();stop.Cancel();
        try{await NetworkScanner.Scan([IPv4Range.Parse("127.0.0.1/32",true)],[root.Id],first.Public.Id,_=>{},cancellation:stop.Token,port:config.Device.Port);throw new Exception("varredura nao cancelou");}
        catch(OperationCanceledException){Console.WriteLine("OK: varredura pode ser cancelada");}
    }
    static async Task ConsentLive(string fixture,string configFile)
    {
        var config=Json.Read<AgentConfig>(configFile);Check(config.RequireConsent,"agente exige consentimento por padrao");
        using var identity=TechnicianIdentity.FromPrivate(File.ReadAllBytes(Path.Combine(fixture,"private-test.pk8")),"Técnico de Teste");
        using var client=new RemoteClient(config.Device,identity,TimeSpan.FromSeconds(45));
        for(int i=0;i<20;i++){try{using var ready=await client.Request(HttpMethod.Get,"/info");break;}catch{await Task.Delay(300);}}
        async Task<IntPtr> Prompt(){for(int i=0;i<100;i++){var w=FindWindow(null,"SDE Suporte — autorizar atendimento");if(w!=IntPtr.Zero)return w;await Task.Delay(100);}throw new Exception("Janela de autorizacao nao apareceu.");}
        void Click(IntPtr window,string name){IntPtr button=IntPtr.Zero;EnumChildWindows(window,(child,_)=>{var value=new StringBuilder(256);GetWindowText(child,value,256);if(value.ToString()==name)button=child;return true;},IntPtr.Zero);Check(button!=IntPtr.Zero,"botao de consentimento disponivel: "+name);SendClick(button,0x00F5,IntPtr.Zero,IntPtr.Zero);}
        var refusal=client.Start();var dialog=await Prompt();
        using(var unauthorized=new RemoteClient(config.Device,identity))
        {try{using var frame=await unauthorized.Request(HttpMethod.Get,"/frame");throw new Exception("imagem obtida antes de consentimento");}catch(HttpRequestException e)when(e.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden){Console.WriteLine("OK: nenhuma imagem antes da autorizacao");}}
        Click(dialog,"Recusar");try{await refusal;throw new Exception("recusa ignorada");}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: recusa bloqueia atendimento");}
        var approval=client.Start();Click(await Prompt(),"Autorizar");await approval;Check(client.Session is not null,"funcionario autoriza atendimento real");await client.Stop();
        var timeout=client.Start();await Prompt();try{await timeout;throw new Exception("ausencia de resposta liberou acesso");}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: ausencia de resposta bloqueia acesso sem bypass");}
        var policyPath=Path.Combine(Path.GetDirectoryName(configFile)!,"access-policy.json");
        Json.Write(policyPath,new{deviceId=config.Device.Id,mode="PreAutorizado",approvedBy="Administrador de teste",reason="Maquina previamente autorizada para teste administrativo"});
        try{await client.Start();Check(client.Session is not null,"politica administrativa previa autoriza nova solicitacao sem ignorar recusa anterior");await client.Stop();}finally{File.Delete(policyPath);}
        string log=File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte","logs","audit.jsonl"));
        Check(new[]{"access_requested","consent_declined","consent_approved","consent_timeout","preauthorized_access","started","technician_stop"}.All(log.Contains),"solicitacoes, respostas e encerramento registrados no log");
    }
    static async Task Installed(string configFile)
    {
        var config=Json.Read<AgentConfig>(configFile);
        Check(config.RequireConsent,"instalador ativa confirmacao do funcionario");
        Check(config.Enrollment?.Approval.Permission.TeamWide==true,"instalador cliente ja inclui equipe sem arquivos manuais");
        Check(config.Enrollment!.Approval.Permission.Issuer.Id==TeamAccess.Root.Id,"cliente instalado usa autoridade publica da empresa");
        Device? found=null;for(int i=0;i<15&&found is null;i++){found=await NetworkScanner.Discover(IPAddress.Loopback,config.Device.Port,[TeamAccess.Root.Id],"scanner",CancellationToken.None);if(found is null)await Task.Delay(500);}
        Check(found?.Id==config.Device.Id,"cliente instalado fica disponivel para varredura IP");
    }
    static void Protocol()
    {
        using var id=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Jorge Henrique");
        using var other=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Outro");
        using(var persistent=CngKey.Create(CngAlgorithm.ECDsaP256,"SDE-Test-"+Guid.NewGuid().ToString("N"),new CngKeyCreationParameters{ExportPolicy=CngExportPolicies.None}))
        {
            try {
                using var signer=new TechnicianIdentity(new ECDsaCng(persistent),"Tecnico CNG");
                var proof=signer.Sign("GET","/info",[],"","1",new string('A',32),"device");
                Check(Convert.FromBase64String(proof).Length==64,"chave persistente nao exportavel assina requisicao");
            }finally{persistent.Delete();}
        }
        var device=Guid.NewGuid().ToString();var v=new RequestVerifier([id.Public],device);
        byte[] body=Encoding.UTF8.GetBytes("comando");string now=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string sig=id.Sign("POST","/input",body,"session",now,nonce,device);
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"session",now,nonce,sig)?.Name=="Jorge Henrique","assinatura autentica e nome aprovado");
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"session",now,nonce,sig) is null,"replay rejeitado");
        Check(v.Verify(other.Public.Id,device,"POST","/input",body,"session",now,nonce,sig) is null,"tecnico nao autorizado bloqueado");
        nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));sig=id.Sign("POST","/input",body,"session",now,nonce,device);
        Check(v.Verify(id.Public.Id,device,"POST","/input",Encoding.UTF8.GetBytes("alterado"),"session",now,nonce,sig) is null,"corpo alterado bloqueado");
        Check(v.Verify(id.Public.Id,device,"POST","/session",body,"session",now,nonce,sig) is null,"rota alterada bloqueada");
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"outro-token",now,nonce,sig) is null,"token alterado bloqueado");
        Check(v.Verify(id.Public.Id,Guid.NewGuid().ToString(),"POST","/input",body,"session",now,nonce,sig) is null,"requisicao para outro computador bloqueada");
        string old=(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-120).ToString();sig=id.Sign("POST","/input",body,"session",old,nonce,device);
        Check(v.Verify(id.Public.Id,device,"POST","/input",body,"session",old,nonce,sig) is null,"assinatura expirada bloqueada");
        try{_=new RequestVerifier([],device);throw new Exception("politica vazia aceita");}catch(InvalidDataException){Console.WriteLine("OK: politica vazia bloqueada");}
    }
    static void EnrollmentTests()
    {
        using var admin=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Preparador");
        var profile=Enrollment.Create(admin,[],"Setor de Teste");
        var device=new Device(Guid.NewGuid().ToString(),"CLIENTE","Setor de Teste","127.0.0.1",7443,new string('A',64));
        var proof=Enrollment.Seal(device,profile);
        Enrollment.VerifyDevice(proof,[admin.Public.Id],admin.Public.Id);Console.WriteLine("OK: cadastro automatico autorizado pela equipe");
        try{Enrollment.VerifyDevice(proof with{Device=device with{CertificateSha256=new string('B',64)}},[admin.Public.Id],admin.Public.Id);throw new Exception("certificado alterado aceito");}catch(InvalidDataException){Console.WriteLine("OK: certificado de cadastro adulterado rejeitado");}
        try{Enrollment.VerifyDevice(proof,[],admin.Public.Id);throw new Exception("autoridade desconhecida aceita");}catch(InvalidDataException){Console.WriteLine("OK: equipe desconhecida nao cadastra maquinas");}
        try{Enrollment.VerifyDevice(proof,[admin.Public.Id],"desconhecido");throw new Exception("tecnico desconhecido aceito");}catch(InvalidDataException){Console.WriteLine("OK: descoberta respeita lista de tecnicos");}
        var modified=profile.Approval with{Permission=profile.Approval.Permission with{Sector="Outro setor"}};
        try{Enrollment.VerifyPermission(modified);throw new Exception("setor alterado aceito");}catch(InvalidDataException){Console.WriteLine("OK: autorizacao de setor adulterada rejeitada");}
    }
    static async Task Live(string fixture,string hostFile)
    {
        var expected=Json.Read<AgentConfig>(hostFile).Device;expected.Validate();
        var metadata=Json.Read<TechnicianKey>(Path.Combine(fixture,"tecnico.sdetech"));
        using var id=TechnicianIdentity.FromPrivate(File.ReadAllBytes(Path.Combine(fixture,"private-test.pk8")),metadata.Name);
        var discovered=new TaskCompletionSource<Device>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var discovery=new ConsoleDiscovery(id,()=>[id.Public.Id],d=>{if(d.Id==expected.Id)discovered.TrySetResult(d);},17445);
        for(int attempt=0;attempt<20&&!discovered.Task.IsCompleted;attempt++){await discovery.SearchTo(new IPEndPoint(IPAddress.Loopback,7446));await Task.Delay(500);}
        var d=await discovered.Task.WaitAsync(TimeSpan.FromSeconds(5));Check(d.Id==expected.Id,"agente aparece por descoberta sem importar arquivo da maquina");
        using var client=new RemoteClient(d,id);
        bool ready=false;
        for(int i=0;i<20;i++){try{using var response=await client.Request(HttpMethod.Get,"/info");ready=true;break;}catch{await Task.Delay(500);}}
        Check(ready,"agente HTTPS responde sem dominio");
        using(var wrongPin=new RemoteClient(d with{CertificateSha256=new string('0',64)},id))
        {try{using var r=await wrongPin.Request(HttpMethod.Get,"/info");throw new Exception("certificado nao conferido");}catch(HttpRequestException){Console.WriteLine("OK: certificado diferente recusado");}}
        using(var stranger=new TechnicianIdentity(ECDsa.Create(ECCurve.NamedCurves.nistP256),"Nao autorizado"))
        using(var denied=new RemoteClient(d,stranger))
        {try{using var r=await denied.Request(HttpMethod.Get,"/info");throw new Exception("nao autorizado aceito");}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: tecnico nao autorizado recusado pelo agente real");}}
        var targetReady=new TaskCompletionSource<(Form,TextBox)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{
            ApplicationConfiguration.Initialize();
            var form=new Form{Text="SDE Suporte - alvo do teste",Width=600,Height=300,TopMost=true,StartPosition=FormStartPosition.CenterScreen};
            var input=new TextBox{Dock=DockStyle.Fill,Multiline=true};form.Controls.Add(input);
            var surface=new RemoteScreen{Dock=DockStyle.Bottom,Height=30};form.Controls.Add(surface);
            form.Shown+=(_,_)=>{surface.Focus();Check(surface.Focused,"tela remota pode receber teclado");};
            form.Shown+=(_,_)=>targetReady.SetResult((form,input));Application.Run(form);
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();
        var (form,textbox)=await targetReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await client.Start();Check(client.Session?.TechnicianName==metadata.Name,"sessao identifica tecnico aprovado");
            using(var presenceProbe=new RemoteClient(d,id))
            {
                await Task.WhenAll(Enumerable.Range(0,4).Select(async i=>{using var concurrent=await (i==0?client.Request(HttpMethod.Get,"/frame"):presenceProbe.Request(HttpMethod.Get,"/info"));}));
                Console.WriteLine("OK: verificacao de presenca simultanea nao interrompe captura");
            }
            var captureWatch=System.Diagnostics.Stopwatch.StartNew();
            using(var frame=await client.Request(HttpMethod.Get,"/frame"))
            {var bytes=await frame.Content.ReadAsByteArrayAsync();Check(bytes.Length>1000&&bytes[0]==0xff&&bytes[1]==0xd8,"captura real da tela produz JPEG");}
            Console.WriteLine("MEDICAO: captura JPEG local em "+captureWatch.ElapsedMilliseconds+" ms.");
            using(var monitorFrame=await client.Request(HttpMethod.Get,"/frame?monitor=primary"))
            {
                var descriptors=JsonSerializer.Deserialize<RemoteMonitor[]>(Convert.FromBase64String(monitorFrame.Headers.GetValues("X-SDE-Monitors").Single()),Json.Options)!;
                var chosen=descriptors.Single(m=>m.Id==Uri.UnescapeDataString(monitorFrame.Headers.GetValues("X-SDE-Monitor").Single()));
                using(var exact=await client.Request(HttpMethod.Get,"/frame?monitor="+Uri.EscapeDataString(chosen.Id)))Check(Uri.UnescapeDataString(exact.Headers.GetValues("X-SDE-Monitor").Single())==chosen.Id,"selecao do monitor pelo identificador Windows autenticado");
                using var imageStream=await monitorFrame.Content.ReadAsStreamAsync();using var picture=Image.FromStream(imageStream);
                Check(picture.Width==chosen.Width&&picture.Height==chosen.Height,"captura selecionada corresponde a resolucao do monitor detectado");
                try{using var stale=await client.Request(HttpMethod.Post,"/input",new InputCommand("move",Layout:"layout-antigo"));throw new Exception("layout antigo aceito");}
                catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Conflict){Console.WriteLine("OK: alteracao dos monitores bloqueia coordenadas de imagem antiga");}
                using var removed=await client.Request(HttpMethod.Get,"/frame?monitor=monitor-removido");
                Check(removed.Headers.Contains("X-SDE-Monitors"),"monitor removido retorna automaticamente ao principal");
            }
            form.Invoke(()=>{form.Activate();textbox.Focus();SetForegroundWindow(form.Handle);});await Task.Delay(500);
            IntPtr handle=(IntPtr)form.Invoke(()=>form.Handle);
            Check(GetForegroundWindow()==handle,"alvo exclusivo do teste em primeiro plano");
            using(var command=await client.Request(HttpMethod.Post,"/input",new InputCommand("text",Text:"SDE-TEST-123"))){}
            await Task.Delay(500);string value=(string)textbox.Invoke(()=>textbox.Text);
            Check(value=="SDE-TEST-123","digitacao remota chega ao aplicativo de teste");
            await MessagingLive(client,d,id);
            var oldSession=client.Session!;await client.Stop();
            await client.Start();
            using(var cleared=await client.Request(HttpMethod.Get,"/chat"))
            {var page=JsonSerializer.Deserialize<ChatPage>(await cleared.Content.ReadAsStringAsync(),Json.Options)!;Check(page.Messages.Length==0,"novo atendimento nao recebe conversa da sessao anterior");}
            await client.Stop();
            Check(client.Session is null,"encerramento da sessao");
            try{using var r=await client.Request(HttpMethod.Get,"/frame");throw new Exception("frame sem sessao");}catch(HttpRequestException e)when(e.StatusCode==HttpStatusCode.Forbidden){Console.WriteLine("OK: tela bloqueada apos encerrar");}
            using(var tokenClient=new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,cert,_,_)=>cert is not null&&Convert.ToHexString(SHA256.HashData(cert.RawData))==d.CertificateSha256}))
            {
                string time=DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                using var req=new HttpRequestMessage(HttpMethod.Get,$"https://{d.Host}:{d.Port}/frame");
                req.Headers.Add("X-Device",d.Id);req.Headers.Add("X-Technician",id.Public.Id);req.Headers.Add("X-Time",time);req.Headers.Add("X-Nonce",nonce);req.Headers.Add("X-Session",oldSession.Id);
                req.Headers.Add("X-Signature",id.Sign("GET","/frame",[],oldSession.Id,time,nonce,d.Id));
                using var r=await tokenClient.SendAsync(req);Check(r.StatusCode==HttpStatusCode.Forbidden,"token antigo nao reabre sessao");
            }
        }
        finally{try{await client.Stop();}catch{}form.Invoke(()=>form.Close());thread.Join(5000);}
    }
    delegate bool WindowCallback(IntPtr window,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string? className,string? windowName);
    [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr window,WindowCallback callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=CharSet.Unicode)]static extern IntPtr SendRead(IntPtr window,int message,IntPtr count,StringBuilder text);
    [DllImport("user32.dll",EntryPoint="SendMessageW",CharSet=CharSet.Unicode)]static extern IntPtr SendText(IntPtr window,int message,IntPtr parameter,string text);
    [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr SendClick(IntPtr window,int message,IntPtr first,IntPtr second);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
}

