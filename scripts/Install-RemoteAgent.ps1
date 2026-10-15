[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$InstallerPath,
 [Parameter(Mandatory)][string]$ExpectedSha256,
 [string]$InstallDirectory="$env:ProgramFiles\SDE-Suporte"
)
$ErrorActionPreference='Stop'
$principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'A instalacao exige permissao de administrador.'}
if(-not [Environment]::Is64BitOperatingSystem){throw 'Use Windows de 64 bits.'}
if($ExpectedSha256 -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash -ine $ExpectedSha256){throw 'O instalador recebido nao passou na verificacao SHA256.'}
$process=Start-Process -FilePath $InstallerPath -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/DIR=`"$InstallDirectory`"") -PassThru -Wait
if($process.ExitCode -ne 0){throw "O instalador retornou falha $($process.ExitCode)."}
$exe=Join-Path $InstallDirectory 'Agent\SDE-Suporte-Cliente.exe'
$configFile=Join-Path $InstallDirectory 'Agent\agent.json'
if(-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $configFile)){throw 'Instalacao terminou sem o agente configurado.'}
$startup=(Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SdeSuporte).SdeSuporte
if($startup -ne "`"$exe`""){throw 'Inicio automatico nao foi configurado.'}
$state='InstalledWaitingForLogon';$detail='Instalado. Disponivel apos o proximo login do funcionario.'
# Never start the GUI in the WinRM/session-0 account. Use an existing interactive user token.
$employee=(Get-CimInstance Win32_ComputerSystem).UserName
if($employee){
 try {
  $action=New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe -Parent)
  $user=New-ScheduledTaskPrincipal -UserId $employee -LogonType Interactive -RunLevel Limited
  $settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
  Register-ScheduledTask -TaskName 'SDE-Suporte-Cliente-InicioRemoto' -Action $action -Principal $user -Settings $settings -Description 'Inicia o Cliente SDE Suporte no usuario ja conectado, apos implantacao autorizada da TI.' -Force | Out-Null
  Start-ScheduledTask -TaskName 'SDE-Suporte-Cliente-InicioRemoto'
  Start-Sleep -Seconds 2
  $config=Get-Content -LiteralPath $configFile -Raw -Encoding UTF8 | ConvertFrom-Json
  $connection=New-Object Net.Sockets.TcpClient
  try {
   $check=$connection.ConnectAsync('127.0.0.1',[int]$config.device.port)
   if($check.Wait(3000) -and $connection.Connected){$state='InstalledAgentStarted';$detail='Instalado; porta do agente disponivel. Atualize a lista de agentes no Tecnico.'}
  }finally{$connection.Dispose()}
 }catch{$detail='Instalado. Nao foi possivel iniciar na sessao atual; aguarde o proximo login do funcionario.'}
}
[pscustomobject]@{State=$state;Detail=$detail}
