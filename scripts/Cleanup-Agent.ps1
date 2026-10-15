#Requires -RunAsAdministrator
$root=Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath "$PSScriptRoot\Uninstall-Agent.ps1") {
 & "$PSScriptRoot\Uninstall-Agent.ps1" -InstallDirectory "$root\Agent"
}
