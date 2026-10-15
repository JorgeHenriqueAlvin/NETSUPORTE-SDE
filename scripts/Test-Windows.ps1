$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $env:RUNNER_TEMP 'SDE-LAN-Fixture'
$agentDir=Join-Path $fixture 'Agent'
dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- protocol
if($LASTEXITCODE -ne 0){throw 'Testes de assinatura e cadastro falharam.'}
dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- prepare $fixture
if($LASTEXITCODE -ne 0){throw 'Preparacao de teste falhou.'}
$payload=Join-Path $fixture 'payload'
Expand-Archive -LiteralPath "$root\dist\agent-payload.zip" -DestinationPath $payload -Force
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$payload\AutoConfigure-Agent.ps1" -DeploymentProfile "$fixture\deployment.json" -PayloadDirectory $payload -InstallDirectory $agentDir -Port 17443 -NoStartup -NonInteractive -RequireConsent $false
if($LASTEXITCODE -ne 0){throw 'Instalacao automatica em Windows PowerShell 5.1 falhou.'}
$process=Start-Process "$agentDir\SDE-Suporte-Cliente.exe" -PassThru
try {
 dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- live $fixture "$agentDir\agent.json"
 if($LASTEXITCODE -ne 0){throw 'Teste de descoberta e acesso real falhou.'}
 Stop-Process -Id $process.Id -Force
 $configPath=Join-Path $agentDir 'agent.json'
 $attended=Get-Content $configPath -Raw | ConvertFrom-Json
 $attended.requireConsent=$true
 [IO.File]::WriteAllText($configPath,($attended | ConvertTo-Json -Depth 20),(New-Object Text.UTF8Encoding($false)))
 $process=Start-Process "$agentDir\SDE-Suporte-Cliente.exe" -PassThru
 dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- consent-live $fixture $configPath
 if($LASTEXITCODE -ne 0){throw 'Confirmacao, recusa, timeout e logs falharam.'}
}finally{
 if(-not $process.HasExited){Stop-Process -Id $process.Id -Force}
 & "$PSScriptRoot\Uninstall-Agent.ps1" -InstallDirectory $agentDir
}
$personalized=Join-Path $fixture 'Cliente-Instalar.exe'
dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- package "$root\dist\Technician\agent.sdetemplate" $personalized
if($LASTEXITCODE -ne 0){throw 'Falha ao personalizar instalador do cliente.'}
$check=Start-Process $personalized -ArgumentList '--check' -PassThru -Wait
if($check.ExitCode -ne 0){throw 'Instalador personalizado nao passou na verificacao de pacote.'}

# Test the new team flow independently with disposable credentials, never production codes.
$teamFixture=Join-Path $env:RUNNER_TEMP 'SDE-Team-Fixture'
$teamAgent=Join-Path $teamFixture 'Agent'
dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- prepare-team $teamFixture
if($LASTEXITCODE -ne 0){throw 'Falha ao preparar equipe de teste.'}
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$payload\AutoConfigure-Agent.ps1" -DeploymentProfile "$teamFixture\deployment.json" -PayloadDirectory $payload -InstallDirectory $teamAgent -Port 17443 -NoStartup -NonInteractive -RequireConsent $false
if($LASTEXITCODE -ne 0){throw 'Instalacao automatica por equipe falhou.'}
$teamProcess=Start-Process "$teamAgent\SDE-Suporte-Cliente.exe" -PassThru
try {
 dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- live-team $teamFixture "$teamAgent\agent.json"
 if($LASTEXITCODE -ne 0){throw 'Varredura de IP e acesso dos seis tecnicos falharam.'}
}finally{
 if(-not $teamProcess.HasExited){Stop-Process -Id $teamProcess.Id -Force}
 & "$PSScriptRoot\Uninstall-Agent.ps1" -InstallDirectory $teamAgent
}
