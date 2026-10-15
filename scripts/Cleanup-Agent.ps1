#Requires -RunAsAdministrator
$root=Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot\Uninstall-Agent.ps1" -InstallDirectory "$root\Agent"
