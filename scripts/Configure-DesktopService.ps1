#Requires -RunAsAdministrator
[CmdletBinding(DefaultParameterSetName='Register')]
param(
 [string]$InstallDirectory="$env:ProgramFiles\SDE-Suporte\Agent",
 [Parameter(ParameterSetName='Stop')][switch]$Stop,
 [Parameter(ParameterSetName='Remove')][switch]$Remove
)
$ErrorActionPreference='Stop'
$name='SDE-Suporte-Desktop'
$exe=Join-Path ([IO.Path]::GetFullPath($InstallDirectory)) 'SDE-Suporte-Desktop.exe'
$canonical=Join-Path $env:ProgramFiles 'SDE-Suporte\Agent\SDE-Suporte-Desktop.exe'
# A SYSTEM service must never execute a user-writable/custom portable copy.
if($exe -ine $canonical) {
 Write-Host 'UAC remoto requer a instalacao padrao em Program Files\SDE-Suporte. Agente comum preservado.'
 return
}
$expected='"'+$exe+'" --desktop-service'
$existing=Get-CimInstance Win32_Service -Filter "Name='$name'"
if($existing -and $existing.PathName -ine $expected){throw 'O nome do servico de desktop pertence a outra instalacao.'}
if($existing) {
 Stop-Service -Name $name -Force -ErrorAction Stop
 (Get-Service -Name $name).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15))
}
if($Remove) {
 if($existing){
  & sc.exe delete $name | Out-Null
  if($LASTEXITCODE -ne 0){throw 'Nao foi possivel remover o servico de desktop.'}
 }
 return
}
if($Stop){return}
if(-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath (Join-Path $InstallDirectory 'agent.json'))){throw 'Cliente ou configuracao ausente para registrar o servico.'}
if(-not $existing) {
 New-Service -Name $name -BinaryPathName $expected -DisplayName 'SDE Suporte - Desktop administrativo' -StartupType Automatic | Out-Null
} else {
 Set-Service -Name $name -StartupType Automatic
}
& sc.exe description $name 'Auxiliar local para a tela UAC durante atendimento SDE autorizado. Nao altera a politica UAC.' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Nao foi possivel descrever o servico de desktop.'}
Start-Service -Name $name
(Get-Service -Name $name).WaitForStatus('Running',[TimeSpan]::FromSeconds(15))
Write-Host 'Servico de desktop administrativo instalado e iniciado.'
