#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $env:RUNNER_TEMP 'SDE-Desktop-Fixture'
$agentDir=Join-Path $env:ProgramFiles 'SDE-Suporte\Agent'
if(Test-Path $agentDir){throw 'O teste de servico exige runner limpo, sem instalacao SDE existente.'}
$agent=$null
try {
 dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- prepare $fixture
 if($LASTEXITCODE -ne 0){throw 'Falha ao preparar credenciais descartaveis.'}
 & "$root\scripts\AutoConfigure-Agent.ps1" -DeploymentProfile "$fixture\deployment.json" -PayloadDirectory "$root\scripts" -BinaryDirectory "$root\dist\Agent" -InstallDirectory $agentDir -Port 17443 -NoStartup -NonInteractive -RequireConsent $false
 if($LASTEXITCODE -ne 0){throw 'Falha ao instalar auxiliar do teste.'}
 if((Get-Service SDE-Suporte-Desktop).Status -ne 'Running'){throw 'Servico nao iniciou.'}
 $agent=Start-Process "$agentDir\SDE-Suporte-Cliente.exe" -PassThru
 dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- desktop-service $fixture "$agentDir\agent.json"
 if($LASTEXITCODE -ne 0){throw 'Integracao com auxiliar SYSTEM falhou.'}
 # Stop/re-register exercises an update without leaving privileged workers alive.
 & "$root\scripts\Configure-DesktopService.ps1" -InstallDirectory $agentDir -Stop
 if((Get-Service SDE-Suporte-Desktop).Status -ne 'Stopped'){throw 'Servico nao parou para atualizacao.'}
 & "$root\scripts\Configure-DesktopService.ps1" -InstallDirectory $agentDir
 if((Get-Service SDE-Suporte-Desktop).Status -ne 'Running'){throw 'Servico nao reiniciou.'}
} finally {
 if($agent){$agent.Refresh();if(-not $agent.HasExited){Stop-Process -Id $agent.Id -Force}}
 if(Test-Path $agentDir){& "$root\scripts\Uninstall-Agent.ps1" -InstallDirectory $agentDir -RemoveFiles}
}
if(Get-Service SDE-Suporte-Desktop -ErrorAction SilentlyContinue){throw 'Servico permaneceu depois de desinstalar.'}
