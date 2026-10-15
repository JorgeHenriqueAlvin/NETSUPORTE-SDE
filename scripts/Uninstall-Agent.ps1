#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$Hostname)
$ErrorActionPreference = 'Stop'
Remove-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SdeSuporte -ErrorAction SilentlyContinue
Get-NetFirewallRule -DisplayName 'SDE Suporte piloto HTTPS' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
& netsh.exe http delete urlacl "url=https://${Hostname}:7443/"
# Remova somente o vínculo SSL criado para este aplicativo, após conferir o AppId.
$binding = & netsh.exe http show sslcert 'ipport=0.0.0.0:7443'
if (($binding | Out-String) -match 'c43a8ff9-42a4-4de1-9c45-6a49b6c9f27d') { & netsh.exe http delete sslcert 'ipport=0.0.0.0:7443' }
Write-Host 'Feche o agente e remova C:\Program Files\SDE-Suporte. Logs locais foram preservados.'
