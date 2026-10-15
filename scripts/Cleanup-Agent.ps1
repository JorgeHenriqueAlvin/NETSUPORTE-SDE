#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$configPath = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'agent.json'
if (Test-Path $configPath) {
 $config = Get-Content $configPath -Raw | ConvertFrom-Json
 & "$PSScriptRoot\Uninstall-Agent.ps1" -Hostname $config.Hostname
}
