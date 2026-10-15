#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Client','Technician')][string]$Role)
$ErrorActionPreference='Stop'
$folder=Join-Path $env:ProgramData 'SDE-Suporte-WinRM-Pilot'
$file=Join-Path $folder ($Role.ToLower()+'.json')
if(-not (Test-Path -LiteralPath $file)){Write-Host 'Nenhuma configuracao deste piloto foi encontrada.';return}
$state=Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
if($Role -eq 'Technician'){
 if($state.ImportedByPilot){Remove-Item "Cert:\LocalMachine\Root\$($state.Thumbprint)" -ErrorAction SilentlyContinue}
}else{
 Start-Service WinRM
 $listeners=@(Get-WSManInstance winrm/config/Listener -Enumerate | Where-Object { $_.Transport -eq 'HTTPS' -and $_.CertificateThumbprint -eq $state.Thumbprint })
 foreach($listener in $listeners){Remove-WSManInstance winrm/config/Listener -SelectorSet @{Transport='HTTPS';Address=[string]$listener.Address}}
 foreach($name in @('SDE-WinRM-Pilot-Allow','SDE-WinRM-Pilot-Block')){Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule}
 $cert=Get-Item "Cert:\LocalMachine\My\$($state.Thumbprint)" -ErrorAction SilentlyContinue
 if($cert -and $cert.FriendlyName -eq 'SDE WinRM Pilot'){Remove-Item $cert.PSPath -DeleteKey}
 # Restore only this endpoint's ACL when no administrator changed it after the pilot.
 $current=Get-PSSessionConfiguration -Name Microsoft.PowerShell -ErrorAction SilentlyContinue
 if($current -and $state.ConfiguredSddl -and $current.SecurityDescriptorSddl -eq $state.ConfiguredSddl){
  if($state.EndpointCreated){Unregister-PSSessionConfiguration -Name Microsoft.PowerShell -NoServiceRestart -Force | Out-Null}
  elseif($state.OriginalSddl){Set-PSSessionConfiguration -Name Microsoft.PowerShell -SecurityDescriptorSddl $state.OriginalSddl -NoServiceRestart -Force | Out-Null}
 }
 Restart-Service WinRM -Force
 Write-Host 'O servico WinRM foi mantido ativo; configuracoes existentes de outros sistemas foram preservadas.'
}
Remove-Item -LiteralPath $file
Write-Host 'Configuracao HTTPS e/ou confianca deste piloto removida.'
