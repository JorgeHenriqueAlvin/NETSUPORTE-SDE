#define AppVersion "0.6.0"
#ifndef SetupRole
  #define SetupRole "combined"
#endif
[Setup]
AppId={{6014C993-C8FA-48AA-A6B9-20821E06FA12}
AppName=SDE Suporte
AppVersion={#AppVersion}
AppPublisher=CMTIC/SDE
DefaultDirName={autopf}\SDE-Suporte
DefaultGroupName=SDE Suporte
OutputDir=..\release
#if SetupRole == "client"
OutputBaseFilename=SDE-Suporte-Cliente-Instalar
#elif SetupRole == "technician"
OutputBaseFilename=SDE-Suporte-Tecnico-Instalar
#else
OutputBaseFilename=SDE-Suporte-Instalador-Windows-x64
#endif
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=SDE Suporte
CloseApplications=yes
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[Types]
#if SetupRole != "client"
Name: "technician"; Description: "Console do técnico"
#endif
#if SetupRole != "technician"
Name: "client"; Description: "Agente do funcionário (configuração automática)"
#endif
#if SetupRole == "combined"
Name: "full"; Description: "Técnico e agente nesta máquina"
Name: "custom"; Description: "Personalizada"; Flags: iscustom
#endif
[Components]
#if SetupRole == "technician"
Name: "technician"; Description: "Console para técnicos autorizados"; Types: technician; Flags: fixed
#elif SetupRole == "client"
Name: "client"; Description: "Agente para receber atendimento"; Types: client; Flags: fixed
#else
Name: "technician"; Description: "Console para técnicos autorizados"; Types: technician full
Name: "client"; Description: "Agente para receber atendimento"; Types: client full
#endif
[Files]
#if SetupRole != "client"
Source: "..\dist\Technician\*"; DestDir: "{app}\Technician"; Excludes: "agent.sdetemplate"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: technician
Source: "..\scripts\Initialize-Console.ps1"; DestDir: "{app}\scripts"; Components: technician
#endif
#if SetupRole != "technician"
Source: "..\dist\Agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: client
Source: "..\scripts\Install-Agent.ps1"; DestDir: "{app}\scripts"; Components: client
Source: "..\scripts\Uninstall-Agent.ps1"; DestDir: "{app}\scripts"; Components: client
Source: "..\scripts\AutoConfigure-Agent.ps1"; DestDir: "{app}\scripts"; Components: client
Source: "..\src\TeamDeployment.json"; DestDir: "{app}\scripts"; DestName: "team-deployment.json"; Components: client
#endif
Source: "..\scripts\Cleanup-Agent.ps1"; DestDir: "{app}\scripts"
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\QUICKSTART.md"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
#if SetupRole != "client"
Name: "{group}\SDE Suporte - Técnico"; Filename: "{app}\Technician\SDE-Suporte-Tecnico.exe"; Components: technician
Name: "{autodesktop}\SDE Suporte - Técnico"; Filename: "{app}\Technician\SDE-Suporte-Tecnico.exe"; Components: technician
#endif
Name: "{group}\Instruções"; Filename: "{app}\QUICKSTART.md"
[Run]
#if SetupRole != "client"
Filename: "{app}\Technician\SDE-Suporte-Tecnico.exe"; Description: "Abrir console do técnico"; Components: technician; Flags: postinstall nowait runasoriginaluser skipifsilent
#endif
#if SetupRole != "technician"
Filename: "{app}\Agent\SDE-Suporte-Cliente.exe"; Components: client; Flags: nowait runasoriginaluser skipifsilent
#endif
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Cleanup-Agent.ps1"""; Flags: waituntilterminated
[UninstallDelete]
Type: files; Name: "{app}\Agent\agent.json"
Type: files; Name: "{app}\Agent\setup-error.txt"
Type: files; Name: "{app}\scripts\computer.sdehost"
Type: files; Name: "{app}\scripts\*.sdetech"
Type: filesandordirs; Name: "{app}\ClientTools"

[Code]
procedure RunConfiguration(ScriptArguments: String);
var
  ExitCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass ' + ScriptArguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException('Não foi possível preparar o SDE Suporte.');
  if ExitCode <> 0 then
    RaiseException('A configuração do SDE Suporte falhou. Verifique as permissões de administrador e tente novamente.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then Exit;
#if SetupRole != "client"
  if WizardIsComponentSelected('technician') then
    RunConfiguration(ExpandConstant('-File "{app}\scripts\Initialize-Console.ps1"'));
#endif
#if SetupRole != "technician"
  if WizardIsComponentSelected('client') then
    RunConfiguration(ExpandConstant('-File "{app}\scripts\AutoConfigure-Agent.ps1" -DeploymentProfile "{app}\scripts\team-deployment.json" -PayloadDirectory "{app}\scripts" -BinaryDirectory "{app}\Agent" -InstallDirectory "{app}\Agent" -NoSeparateUninstall -NonInteractive'));
#endif
end;


