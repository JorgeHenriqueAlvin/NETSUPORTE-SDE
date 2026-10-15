using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using SdeSuporte;
namespace SdeSuporte.Bootstrapper;
static class Program
{
    [STAThread]static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string directory=Path.Combine(Path.GetTempPath(),"SDE-Instalacao-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);
            foreach(var sid in new[]{WindowsIdentity.GetCurrent().User!,new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})
                acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(acl);
            var profile=InstallerPackage.Extract(Environment.ProcessPath!,directory);
            if(args.Contains("--check")){Environment.ExitCode=0;return;}
            if(MessageBox.Show($"Instalar agente do SDE Suporte neste computador?\nSetor: {profile.Approval.Permission.Sector}\nPreparado por: {profile.Approval.Permission.Issuer.Name}\n\nApós instalar, a máquina aparecerá nos consoles autorizados da rede.","SDE Suporte — instalar cliente",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            var psi=new ProcessStartInfo("powershell.exe"){UseShellExecute=true,Verb="runas"};
            psi.Arguments=$"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(directory,"AutoConfigure-Agent.ps1")}\" -DeploymentProfile \"{Path.Combine(directory,"deployment.json")}\" -PayloadDirectory \"{directory}\"";
            using var setup=Process.Start(psi)??throw new Exception("Não foi possível iniciar a instalação.");setup.WaitForExit();
            if(setup.ExitCode!=0)throw new Exception("A configuração não foi concluída. Verifique a mensagem da instalação.");
            using var startup=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            var agent=(startup?.GetValue("SdeSuporte") as string)?.Trim('"');
            if(agent is not null&&File.Exists(agent))Process.Start(new ProcessStartInfo(agent){UseShellExecute=false});
            MessageBox.Show("Agente instalado. A máquina aparecerá automaticamente nos consoles autorizados quando eles estiverem abertos e a rede permitir a descoberta.","SDE Suporte");
        }
        catch(Exception e){Environment.ExitCode=1;if(!args.Contains("--check"))MessageBox.Show(e.Message,"Instalação não concluída",MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{try{Directory.Delete(directory,true);}catch{}}
    }
}
