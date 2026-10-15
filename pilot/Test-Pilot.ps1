$ErrorActionPreference='Stop'
$ip=(Get-NetIPAddress -AddressFamily IPv4 | Where-Object {$_.IPAddress -match '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.)' -and $_.AddressState -eq 'Preferred'} | Select-Object -First 1).IPAddress
if(-not $ip){throw 'Interface IPv4 privada de teste nao encontrada.'}
$export=Join-Path $env:TEMP 'SDE-WinRM-Pilot-Test'
$profiles=Get-NetFirewallProfile | Select-Object Name,Enabled
try {
 Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True
 $info=& "$PSScriptRoot\Prepare-Client.ps1" -ClientIPv4 $ip -TechnicianIPv4 $ip -ExportDirectory $export -Port 15986
 $info=$info | Select-Object -Last 1
 if(-not (Test-Path "$export\Cliente-WinRM.cer") -or $info.Sha256.Length -ne 64){throw 'Certificado publico nao exportado.'}
 $certificate=New-Object Security.Cryptography.X509Certificates.X509Certificate2("$export\Cliente-WinRM.cer")
 if($certificate.HasPrivateKey){throw 'Exportou chave privada indevidamente.'}
 $allow=Get-NetFirewallRule -Name SDE-WinRM-Pilot-Allow | Get-NetFirewallAddressFilter
 if($allow.RemoteAddress -notcontains $ip){throw 'Origem do firewall nao foi limitada ao tecnico.'}
 $failed=$false
 try{& "$PSScriptRoot\Trust-Technician.ps1" -ClientIPv4 $ip -TechnicianIPv4 $ip -CertificatePath "$export\Cliente-WinRM.cer" -Port 15986 -ExpectedSha256 ('0'*64)}catch{$failed=$true}
 if(-not $failed -or (Test-Path "Cert:\LocalMachine\Root\$($certificate.Thumbprint)")){throw 'Certificado com SHA256 errado foi aceito.'}
 & "$PSScriptRoot\Trust-Technician.ps1" -ClientIPv4 $ip -TechnicianIPv4 $ip -CertificatePath "$export\Cliente-WinRM.cer" -Port 15986 -ExpectedSha256 $info.Sha256
 # Rerunning must preserve ownership and avoid creating another certificate/listener.
 $again=& "$PSScriptRoot\Prepare-Client.ps1" -ClientIPv4 $ip -TechnicianIPv4 $ip -ExportDirectory $export -Port 15986
 if(($again | Select-Object -Last 1).Thumbprint -ne $info.Thumbprint){throw 'Reexecucao trocou o certificado.'}
 Write-Host 'OK: SAN de IP, chave privada nao exportada, firewall limitado, SHA incorreto bloqueado e HTTPS WinRM validado.'
}finally{
 & "$PSScriptRoot\Remove-Pilot.ps1" -Role Technician
 & "$PSScriptRoot\Remove-Pilot.ps1" -Role Client
 Remove-Item $export -Recurse -Force -ErrorAction SilentlyContinue
 foreach($profile in $profiles){Set-NetFirewallProfile -Profile $profile.Name -Enabled $profile.Enabled}
}
