#Requires -RunAsAdministrator
$root=Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath "$PSScriptRoot\Uninstall-Agent.ps1") {
 & "$PSScriptRoot\Uninstall-Agent.ps1" -InstallDirectory "$root\Agent"
}

Get-NetFirewallRule -Name SDE-Suporte-Console-7445 -ErrorAction SilentlyContinue | Remove-NetFirewallRule
