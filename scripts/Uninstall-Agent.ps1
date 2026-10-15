#Requires -RunAsAdministrator
param([string]$InstallDirectory="$env:ProgramFiles\SDE-Suporte\Agent",[switch]$RemoveFiles)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Desktop') {
 $env:PSModulePath=(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')+';'+(Join-Path $PSHOME 'Modules')
 Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
}
$path=Join-Path $InstallDirectory 'agent.json'
if (-not (Test-Path -LiteralPath $path)) { return }
$config=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $config.device.port) { return }
$port=[int]$config.device.port
$exe=Join-Path $InstallDirectory 'SDE-Suporte-Cliente.exe'
Get-CimInstance Win32_Process -Filter "Name='SDE-Suporte-Cliente.exe'" | Where-Object ExecutablePath -eq $exe | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
$binding=(& netsh.exe http show sslcert "ipport=0.0.0.0:$port" | Out-String)
if ($binding -match 'c43a8ff9-42a4-4de1-9c45-6a49b6c9f27d') {
 & netsh.exe http delete sslcert "ipport=0.0.0.0:$port" | Out-Null
 & netsh.exe http delete urlacl "url=https://+:$port/" | Out-Null
}
Get-NetFirewallRule -Name SDE-Suporte-Discover-7446 -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Get-NetFirewallRule -Name "SDE-Suporte-LAN-$port" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
$startup=Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SdeSuporte -ErrorAction SilentlyContinue
if ($startup.SdeSuporte -eq "`"$exe`"") { Remove-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SdeSuporte }
$certPath="Cert:\LocalMachine\My\$($config.certificateThumbprint)"
$cert=Get-Item $certPath -ErrorAction SilentlyContinue
if ($cert -and $cert.FriendlyName -eq "SDE-Suporte-LAN-$port") { Remove-Item $certPath -DeleteKey }

$registry='HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SDE-Suporte-Cliente-LAN'
$record=Get-ItemProperty $registry -ErrorAction SilentlyContinue
if($record.InstallLocation -eq $InstallDirectory){Remove-Item $registry -Recurse -Force}
if($RemoveFiles){
 Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
 $tools=Join-Path (Split-Path $InstallDirectory -Parent) 'ClientTools'
 if(Test-Path $tools){Remove-Item -LiteralPath $tools -Recurse -Force}
}
