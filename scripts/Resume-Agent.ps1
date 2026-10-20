# Runs in the employee's existing session; never creates an elevated desktop.
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root 'UpdateCache'
$until=(Get-Date).AddMinutes(15)
do {
 Start-Sleep -Seconds 2
 $busy=(Test-Path "$cache\maintenance.json") -or (Test-Path "$cache\update.lock")
 if($busy -and (Test-Path "$cache\update.lock")) {
  $busy=((Get-Date)-(Get-Item "$cache\update.lock").LastWriteTime).TotalMinutes -lt 15
 }
} while($busy -and (Get-Date) -lt $until)
$exe=Join-Path $root 'Agent\SDE-Suporte-Cliente.exe'
if(Test-Path $exe){Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent)}
