#Requires -RunAsAdministrator
param([Parameter(Mandatory)][string]$DeploymentProfile,[Parameter(Mandatory)][string]$PayloadDirectory,
 [string]$InstallDirectory="$env:ProgramFiles\SDE-Suporte\Agent",[int]$Port=7443,[switch]$NoStartup,[switch]$NonInteractive,
 [string]$BinaryDirectory='', [switch]$NoSeparateUninstall)
$ErrorActionPreference='Stop'
try {
 $profile=Get-Content -LiteralPath $DeploymentProfile -Raw -Encoding UTF8 | ConvertFrom-Json
 $permission=$profile.approval.permission
 $files=@();$networks=@('LocalSubnet')
 if($permission.teamWide) { $networks+=@('10.0.0.0/8','172.16.0.0/12','192.168.0.0/16') }
 foreach ($tech in $permission.technicians) {
  $file=Join-Path $PayloadDirectory ($tech.id+'.sdetech')
  [IO.File]::WriteAllText($file,($tech | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
  $files+=$file
  foreach ($address in $tech.discoveryAddresses) { $ip=$null;if([Net.IPAddress]::TryParse($address,[ref]$ip) -and $ip.AddressFamily -eq 'InterNetwork'){$networks+=$address} }
 }
 $directory=$InstallDirectory
 $tools=Join-Path (Split-Path $directory -Parent) 'ClientTools' 
 New-Item -ItemType Directory -Path $tools -Force | Out-Null
 Copy-Item "$PayloadDirectory\Uninstall-Agent.ps1" $tools -Force
 if(-not $BinaryDirectory){$BinaryDirectory=Join-Path $PayloadDirectory 'Agent'}
 & "$PayloadDirectory\Install-Agent.ps1" -Sector $permission.sector -TechnicianFiles $files -ExportPath (Join-Path $PayloadDirectory 'computer.sdehost') -InstallDirectory $directory -BinaryDirectory $BinaryDirectory -TechnicianNetworks @($networks | Select-Object -Unique) -Port $Port -NoStartup:$NoStartup
 $seal=Start-Process "$directory\SDE-Suporte-Cliente.exe" -ArgumentList @('--seal',"`"$DeploymentProfile`"") -PassThru -Wait
 if($seal.ExitCode -ne 0) { throw 'Nao foi possivel validar a autorizacao do instalador.' }
 Get-NetFirewallRule -Name SDE-Suporte-Discover-7446 -ErrorAction SilentlyContinue | Remove-NetFirewallRule
 New-NetFirewallRule -Name SDE-Suporte-Discover-7446 -DisplayName 'SDE Suporte - descoberta automatica' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 7446 -Profile Domain,Private -RemoteAddress @($networks | Select-Object -Unique) | Out-Null
 if(-not $NoStartup -and -not $NoSeparateUninstall) {
  $registry='HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\SDE-Suporte-Cliente-LAN'
  New-Item $registry -Force | Out-Null
  $uninstaller=Join-Path $tools 'Uninstall-Agent.ps1'
  $escapedScript=$uninstaller.Replace("'","''")
  $escapedDirectory=$directory.Replace("'","''")
  $command="Start-Process -FilePath powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File `"$escapedScript`" -InstallDirectory `"$escapedDirectory`" -RemoveFiles'"
  $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
  foreach($entry in @{DisplayName='SDE Suporte - Cliente';DisplayVersion='0.6.0';Publisher='CMTIC/SDE';InstallLocation=$directory;UninstallString="powershell.exe -NoProfile -EncodedCommand $encoded"}.GetEnumerator()) {
   New-ItemProperty $registry -Name $entry.Key -Value $entry.Value -PropertyType String -Force | Out-Null
  }
 }
 # The normal user process is started by the bootstrapper after elevation ends.
 Write-Host 'Agente instalado e autorizado para descoberta automatica.'
} catch {
 if($NonInteractive){Write-Host $_.Exception.Message}else{
  Add-Type -AssemblyName System.Windows.Forms
  [System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'Instalacao nao concluida') | Out-Null
 }
 exit 1
}
