using System.Diagnostics;
namespace SdeSuporte;
static class Program
{
    [STAThread] static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
#if AGENT_ONLY
            if(args.Length==2 && args[0]=="--seal")
            {
                try {
                    var file=Path.Combine(AppContext.BaseDirectory,"agent.json");
                    var config=Json.Read<AgentConfig>(file);var profile=Json.Read<DeploymentProfile>(args[1]);
                    if(!config.Technicians.Select(t=>t.Id+"|"+t.Name+"|"+t.PublicKey).Order().SequenceEqual(profile.Approval.Permission.Technicians.Select(t=>t.Id+"|"+t.Name+"|"+t.PublicKey).Order()))throw new Exception("Lista de técnicos divergente.");
                    var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
                    node["enrollment"]=System.Text.Json.JsonSerializer.SerializeToNode(Enrollment.Seal(config.Device,profile),Json.Options);
                    File.WriteAllText(file,node.ToJsonString(Json.Options));
                    Environment.ExitCode=0;
                }catch(Exception e){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"setup-error.txt"),e.Message);Environment.ExitCode=1;}
                return;
            }
            WaitForStartupUpdate();
            using var single=new Mutex(true,@"Local\SDE-Suporte-Agent",out var first);
            if(!first)return;
            var path=Path.Combine(AppContext.BaseDirectory,"agent.json");
            Application.Run(File.Exists(path)?new Agent(path):new MissingSetup());
#else
            var fromSetup=args.Length==2&&args[0]=="--from-setup";
            if(fromSetup)
            {
                var grant=TechnicianSetup.Read(args[1],TeamAccess.Root);
                if(grant is not null)TechnicianProfiles.Authorize(TechnicianProfiles.DefaultDirectory,grant,TeamAccess.Root);
            }
            if(!fromSetup)WaitForStartupUpdate();
            Application.Run(new Technician());
#endif
        }
        catch(Exception e){
            try{var logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte","logs");Directory.CreateDirectory(logs);File.WriteAllText(Path.Combine(logs,"startup-error.txt"),DateTimeOffset.Now+"\n"+e);}catch{}
            MessageBox.Show("Não foi possível iniciar.\n"+e.Message,"SDE Suporte",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }

    static void WaitForStartupUpdate()
    {
        try
        {
            // O atualizador e disparado pelo Windows no boot como SYSTEM. Durante
            // os primeiros minutos damos alguns segundos para essa tarefa criar o
            // lock e, se ela estiver ativa, aguardamos sua verificacao. O limite
            // evita que uma rede lenta impeça o usuario de abrir o aplicativo.
            if(Environment.TickCount64>TimeSpan.FromMinutes(10).TotalMilliseconds)return;
            var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
            var lockFile=Path.Combine(root,"UpdateCache","update.lock");
            var discovery=Stopwatch.StartNew();
            while(discovery.Elapsed<TimeSpan.FromSeconds(5)&&!FreshUpdateLock(lockFile))Thread.Sleep(250);
            if(!FreshUpdateLock(lockFile))return;
            var wait=Stopwatch.StartNew();
            while(wait.Elapsed<TimeSpan.FromSeconds(90)&&FreshUpdateLock(lockFile))Thread.Sleep(500);
        }
        catch
        {
            // Falha ao observar o atualizador nunca deve bloquear o suporte.
        }
    }

    static bool FreshUpdateLock(string path)
    {
        if(!File.Exists(path))return false;
        try{return DateTime.UtcNow-File.GetLastWriteTimeUtc(path)<TimeSpan.FromMinutes(10);}
        catch{return false;}
    }
}
sealed class MissingSetup:Form
{
    public MissingSetup()
    {
        Text="SDE Suporte — concluir instalação";Width=540;Height=230;
        var label=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Text="Este computador ainda não foi configurado para receber suporte.\n\nUse o instalador, selecione Agente do cliente e configure\nos técnicos autorizados. Nenhum acesso está liberado."};
        var button=new Button{Text="Configurar este computador",Dock=DockStyle.Bottom,Height=45};
        Controls.Add(label);Controls.Add(button);
        button.Click+=(_,_)=>{
            var script=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","scripts","Configure-Agent.ps1"));
            if(!File.Exists(script)){MessageBox.Show("Execute SDE-Suporte-Instalador-Windows-x64.exe para instalar o agente.");return;}
            try{Process.Start(new ProcessStartInfo("powershell.exe",$"-NoProfile -ExecutionPolicy Bypass -File \"{script}\""){UseShellExecute=true,Verb="runas"});Close();}
            catch(Exception e){
            try{var logs=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SdeSuporte","logs");Directory.CreateDirectory(logs);File.WriteAllText(Path.Combine(logs,"startup-error.txt"),DateTimeOffset.Now+"\n"+e);}catch{}
            MessageBox.Show(e.Message);}
        };
    }
}
