#define AppVersion "0.4.0"
[Setup]
AppId={{6014C993-C8FA-48AA-A6B9-20821E06FA12}
AppName=SDE Suporte
AppVersion={#AppVersion}
AppPublisher=CMTIC/SDE
DefaultDirName={autopf}\SDE-Suporte
DefaultGroupName=SDE Suporte
OutputDir=..\release
OutputBaseFilename=SDE-Suporte-Instalador-Windows-x64
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
Name: "technician"; Description: "Console do técnico"
Name: "client"; Description: "Agente do cliente"
Name: "full"; Description: "Técnico e agente nesta máquina"
Name: "custom"; Description: "Personalizada"; Flags: iscustom
[Components]
Name: "technician"; Description: "Console para técnicos autorizados"; Types: technician full
Name: "client"; Description: "Agente para receber atendimento"; Types: client full
[Files]
Source: "..\dist\Technician\*"; DestDir: "{app}\Technician"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: technician
Source: "..\dist\Agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: client
Source: "..\scripts\Install-Agent.ps1"; DestDir: "{app}\scripts"; Components: client
Source: "..\scripts\Uninstall-Agent.ps1"; DestDir: "{app}\scripts"; Components: client
Source: "..\scripts\Configure-Agent.ps1"; DestDir: "{app}\scripts"; Components: client
Source: "..\scripts\Cleanup-Agent.ps1"; DestDir: "{app}\scripts"
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\SDE Suporte - Técnico"; Filename: "{app}\Technician\SDE-Suporte-Tecnico.exe"; Components: technician
Name: "{group}\Configurar agente"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-Agent.ps1"""; Components: client
Name: "{group}\Iniciar agente"; Filename: "{app}\Agent\SDE-Suporte-Cliente.exe"; Components: client
Name: "{group}\Instruções"; Filename: "{app}\README.md"
[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Configure-Agent.ps1"""; Description: "Configurar agente na rede local (sem domínio)"; Flags: postinstall skipifsilent; Components: client
[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\Cleanup-Agent.ps1"""; Flags: waituntilterminated
[UninstallDelete]
Type: files; Name: "{app}\Agent\SDE-Suporte-Cliente.exe"
Type: files; Name: "{app}\Agent\agent.json"

