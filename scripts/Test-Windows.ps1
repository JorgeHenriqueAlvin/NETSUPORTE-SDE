$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$fixture=Join-Path $env:RUNNER_TEMP 'SDE-LAN-Fixture'
$agentDir=Join-Path $fixture 'Agent'
$hostFile=Join-Path $fixture 'teste.sdehost'
dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- protocol
if ($LASTEXITCODE -ne 0) { throw 'Testes de assinatura falharam.' }
dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- prepare $fixture
if ($LASTEXITCODE -ne 0) { throw 'Preparacao de teste falhou.' }
& "$PSScriptRoot\Install-Agent.ps1" -Sector 'Teste' -TechnicianFiles "$fixture\tecnico.sdetech" -ExportPath $hostFile -Port 17443 -ConnectionHost '127.0.0.1' -TechnicianNetworks '127.0.0.1' -InstallDirectory $agentDir -BinaryDirectory "$root\dist\Agent" -NoStartup
$process=Start-Process "$agentDir\SDE-Suporte-Cliente.exe" -PassThru
try {
 dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- live $fixture $hostFile
 if ($LASTEXITCODE -ne 0) { throw 'Teste de acesso real falhou. Verifique escritorio interativo do Windows.' }
} finally {
 if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
 & "$PSScriptRoot\Uninstall-Agent.ps1" -InstallDirectory $agentDir
}
