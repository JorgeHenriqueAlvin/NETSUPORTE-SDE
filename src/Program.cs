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
            var path=Path.Combine(AppContext.BaseDirectory,"agent.json");
            Application.Run(File.Exists(path)?new Agent(path):new MissingSetup());
#else
            Application.Run(new Technician());
#endif
        }
        catch(Exception e){MessageBox.Show("Não foi possível iniciar.\n"+e.Message,"SDE Suporte",MessageBoxButtons.OK,MessageBoxIcon.Error);}
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
            catch(Exception e){MessageBox.Show(e.Message);}
        };
    }
}
