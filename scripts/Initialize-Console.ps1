#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
Get-NetFirewallRule -Name SDE-Suporte-Console-7445 -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name SDE-Suporte-Console-7445 -DisplayName 'SDE Suporte - computadores na rede' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 7445 -Profile Domain,Private -RemoteAddress LocalSubnet,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16 | Out-Null
