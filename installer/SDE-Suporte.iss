#define AppVersion "0.12.5"
#ifndef SetupRole
  #define SetupRole "combined"
#endif
#if SetupRole == "combined"
  #define TechnicianComponent "; Components: technician"
  #define ClientComponent "; Components: client"
#else
  #define TechnicianComponent ""
  #define ClientComponent ""
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
OutputBaseFilename=SDE-Suporte-Tecnico-Base
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
UsePreviousSetupType=no
[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
[Types]
#if SetupRole == "combined"
Name: "technician"; Description: "Console do técnico"
Name: "client"; Description: "Agente do funcionário (configuração automática)"
Name: "full"; Description: "Técnico e agente nesta máquina"
Name: "custom"; Description: "Personalizada"; Flags: iscustom
#endif
[Components]
#if SetupRole == "combined"
Name: "technician"; Description: "Console para técnicos autorizados"; Types: technician full
Name: "client"; Description: "Agente para receber atendimento"; Types: client full
#endif
[Files]
#if SetupRole != "client"
Source: "..\dist\Technician\*"; DestDir: "{app}\Technician"; Excludes: "agent.sdetemplate"; Flags: ignoreversion recursesubdirs createallsubdirs{#TechnicianComponent}
Source: "..\scripts\Initialize-Console.ps1"; DestDir: "{app}\scripts"{#TechnicianComponent}
Source: "..\scripts\Deploy-Agent.ps1"; DestDir: "{app}\scripts"{#TechnicianComponent}
Source: "..\scripts\Install-RemoteAgent.ps1"; DestDir: "{app}\scripts"{#TechnicianComponent}
Source: "..\scripts\Deploy-Agent-SMB.ps1"; DestDir: "{app}\scripts"{#TechnicianComponent}
Source: "..\scripts\Deploy-AccessPolicy-SMB.ps1"; DestDir: "{app}\scripts"{#TechnicianComponent}
Source: "..\scripts\Deploy-UpdateNow-SMB.ps1"; DestDir: "{app}\scripts"{#TechnicianComponent}
Source: "..\release\SDE-Suporte-Cliente-Instalar.exe"; DestDir: "{app}\Packages"{#TechnicianComponent}
Source: "..\release\SDE-Suporte-Cliente-Instalar.exe.sha256"; DestDir: "{app}\Packages"{#TechnicianComponent}
#endif
#if SetupRole != "technician"
Source: "..\dist\Agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs{#ClientComponent}
Source: "..\scripts\Configure-AccessPolicy.ps1"; DestDir: "{app}\scripts"{#ClientComponent}
Source: "..\scripts\Install-Agent.ps1"; DestDir: "{app}\scripts"{#ClientComponent}
Source: "..\scripts\Uninstall-Agent.ps1"; DestDir: "{app}\scripts"{#ClientComponent}
Source: "..\scripts\AutoConfigure-Agent.ps1"; DestDir: "{app}\scripts"{#ClientComponent}
Source: "..\src\TeamDeployment.json"; DestDir: "{app}\scripts"; DestName: "team-deployment.json"{#ClientComponent}
#endif
Source: "..\scripts\Update-Sde.ps1"; DestDir: "{app}\scripts"
Source: "..\scripts\Cleanup-Agent.ps1"; DestDir: "{app}\scripts"
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\QUICKSTART.md"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
#if SetupRole != "client"
Name: "{group}\SDE Suporte - Técnico"; Filename: "{app}\Technician\SDE-Suporte-Tecnico.exe"{#TechnicianComponent}
Name: "{autodesktop}\SDE Suporte - Técnico"; Filename: "{app}\Technician\SDE-Suporte-Tecnico.exe"{#TechnicianComponent}
#endif
#if SetupRole != "technician"
Name: "{group}\SDE Suporte - Cliente"; Filename: "{app}\Agent\SDE-Suporte-Cliente.exe"{#ClientComponent}
#endif
Name: "{group}\Instruções"; Filename: "{app}\QUICKSTART.md"
[Run]
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Cleanup-Agent.ps1"""; Flags: waituntilterminated; RunOnceId: "CleanupSdeSuporte"
[UninstallDelete]
Type: files; Name: "{app}\Agent\agent.json"
Type: files; Name: "{app}\Agent\access-policy.json"
Type: files; Name: "{app}\Agent\setup-error.txt"
Type: files; Name: "{app}\scripts\computer.sdehost"
Type: files; Name: "{app}\scripts\*.sdetech"
Type: files; Name: "{app}\scripts\agent-setup.log"
Type: filesandordirs; Name: "{app}\ClientTools"
Type: filesandordirs; Name: "{app}\UpdateCache"

[Code]
var
  ConfigurationFailed: Boolean;

procedure RunConfiguration(ScriptArguments: String);
var
  ExitCode: Integer;
begin
  ConfigurationFailed := True;
  Log('SDE: preparando configuracao: ' + ScriptArguments);
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass ' + ScriptArguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException('Não foi possível preparar o SDE Suporte.');
  if ExitCode <> 0 then
    RaiseException('A configuração do SDE Suporte falhou. Verifique as permissões de administrador e tente novamente.');
  ConfigurationFailed := False;
end;

function GetCustomSetupExitCode: Integer;
begin
  if ConfigurationFailed then Result := 10 else Result := 0;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  StartResult: Integer;
begin
  { Inno processes ordinary Run entries BEFORE ssPostInstall. Start the agent
    only after configuration is complete, under the installing employee. }
  if CurStep = ssDone then begin
#if SetupRole != "client"
    if not WizardSilent and not ConfigurationFailed and
       FileExists(ExpandConstant('{app}\Technician\SDE-Suporte-Tecnico.exe')) then begin
      if not ExecAsOriginalUser(ExpandConstant('{app}\Technician\SDE-Suporte-Tecnico.exe'),
        ExpandConstant('--from-setup "{srcexe}"'), ExpandConstant('{app}\Technician'), SW_SHOWNORMAL, ewNoWait, StartResult) then begin
        ConfigurationFailed := True;
        RaiseException('Não foi possível abrir o console do técnico. Execute novamente o instalador autorizado.');
      end;
    end;
#endif
#if SetupRole != "technician"
    if not WizardSilent and not ConfigurationFailed and
       FileExists(ExpandConstant('{app}\Agent\agent.json')) then begin
      if not ExecAsOriginalUser(ExpandConstant('{app}\Agent\SDE-Suporte-Cliente.exe'), '',
        ExpandConstant('{app}\Agent'), SW_SHOWNORMAL, ewNoWait, StartResult) then begin
        ConfigurationFailed := True;
        RaiseException('O agente foi instalado. Entre novamente no Windows para iniciá-lo automaticamente.');
      end;
    end;
#endif
    Exit;
  end;
  if CurStep <> ssPostInstall then Exit;
  RunConfiguration(ExpandConstant('-File "{app}\scripts\Update-Sde.ps1" -Register'));
#if SetupRole == "technician"
  RunConfiguration(ExpandConstant('-File "{app}\scripts\Initialize-Console.ps1"'));
#elif SetupRole == "combined"
  if WizardIsComponentSelected('technician') then
    RunConfiguration(ExpandConstant('-File "{app}\scripts\Initialize-Console.ps1"'));
#endif
#if SetupRole == "client"
  RunConfiguration(ExpandConstant('-File "{app}\scripts\AutoConfigure-Agent.ps1" -DeploymentProfile "{app}\scripts\team-deployment.json" -PayloadDirectory "{app}\scripts" -BinaryDirectory "{app}\Agent" -InstallDirectory "{app}\Agent" -NoSeparateUninstall -NonInteractive'));
#elif SetupRole == "combined"
  if WizardIsComponentSelected('client') then
    RunConfiguration(ExpandConstant('-File "{app}\scripts\AutoConfigure-Agent.ps1" -DeploymentProfile "{app}\scripts\team-deployment.json" -PayloadDirectory "{app}\scripts" -BinaryDirectory "{app}\Agent" -InstallDirectory "{app}\Agent" -NoSeparateUninstall -NonInteractive'));
#endif
end;


