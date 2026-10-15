#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ClientIPv4,[Parameter(Mandatory)][string]$TechnicianIPv4,[Parameter(Mandatory)][string]$ExportDirectory)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Desktop'){
 $env:PSModulePath=(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')+';'+(Join-Path $PSHOME 'Modules')
 Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
}
if($PSVersionTable.PSEdition -ne 'Desktop'){throw 'Execute no Windows PowerShell 5.1, nao no PowerShell 7.'}
function PrivateIPv4([string]$value){
 $ip=$null;if(-not [Net.IPAddress]::TryParse($value,[ref]$ip) -or $ip.AddressFamily -ne 'InterNetwork'){throw 'IPv4 invalido.'}
 $b=$ip.GetAddressBytes();if(-not ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))){throw 'Use IPv4 privado da rede interna.'}
 $ip.IPAddressToString
}
$ClientIPv4=PrivateIPv4 $ClientIPv4;$TechnicianIPv4=PrivateIPv4 $TechnicianIPv4
if(-not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $ClientIPv4)){throw "Este computador nao tem o IP $ClientIPv4. Execute na maquina de teste correta."}
$stateFolder=Join-Path $env:ProgramData 'SDE-Suporte-WinRM-Pilot'
$stateFile=Join-Path $stateFolder 'client.json'
New-Item -ItemType Directory -Path $ExportDirectory -Force | Out-Null
if(Test-Path -LiteralPath $stateFile){
 $state=Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
 if($state.ClientIPv4 -ne $ClientIPv4 -or $state.TechnicianIPv4 -ne $TechnicianIPv4){throw 'Ja existe outro piloto configurado. Remova-o antes de trocar os IPs.'}
 $cert=Get-Item "Cert:\LocalMachine\My\$($state.Thumbprint)"
 if($cert.NotAfter -lt (Get-Date)){throw 'Certificado do piloto expirou. Remova e configure novamente.'}
} else {
 Start-Service WinRM
 $existing=@(Get-WSManInstance winrm/config/Listener -Enumerate)
 if(@($existing | Where-Object Transport -eq 'HTTPS').Count){throw 'Ja existe WinRM HTTPS nesta maquina. A configuracao existente foi preservada; solicite revisao da TI.'}
 $endpoint=Get-PSSessionConfiguration -Name Microsoft.PowerShell -ErrorAction SilentlyContinue
 if(@($existing | Where-Object Transport -eq 'HTTP').Count -and $endpoint -and $endpoint.AccessMode -ne 'Remote'){
  throw 'Ha um listener HTTP existente com restricao de acesso. A configuracao foi preservada; solicite revisao da TI.'
 }
 foreach($name in @('SDE-WinRM-Pilot-Allow','SDE-WinRM-Pilot-Block')){if(Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue){throw 'Existe regra antiga do piloto sem estado salvo. Solicite revisao da TI.'}}
 New-Item -ItemType Directory -Path $stateFolder -Force | Out-Null
 & icacls.exe $stateFolder /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Nao foi possivel proteger a pasta de configuracao.'}
 Set-Service WinRM -StartupType Automatic
 $san="DNS=$env:COMPUTERNAME&IPAddress=$ClientIPv4"
 try{$fqdn=[Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName;if(@([Net.Dns]::GetHostAddresses($fqdn) | ForEach-Object IPAddressToString) -contains $ClientIPv4){$san+='&DNS='+$fqdn}}catch{}
 $cert=New-SelfSignedCertificate -Type Custom -Subject "CN=$env:COMPUTERNAME" -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature,KeyEncipherment -NotAfter (Get-Date).AddDays(90) -FriendlyName 'SDE WinRM Pilot' -TextExtension @("2.5.29.17={text}$san",'2.5.29.37={text}1.3.6.1.5.5.7.3.1','2.5.29.19={text}CA=0')
 $state=[pscustomobject]@{ClientIPv4=$ClientIPv4;TechnicianIPv4=$TechnicianIPv4;Thumbprint=$cert.Thumbprint;EndpointCreated=($null -eq $endpoint);OriginalSddl=if($endpoint){[string]$endpoint.SecurityDescriptorSddl}else{''};ConfiguredSddl='';ListenerCreated=$false;OriginalServiceStart=[string](Get-Service WinRM).StartType}
 $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stateFile -Encoding UTF8
 try {
  # Block every remote source except the chosen technician, even if another allow rule exists.
  $b=([Net.IPAddress]::Parse($TechnicianIPv4)).GetAddressBytes()
  [uint64]$number=[uint64]$b[0]*16777216+[uint64]$b[1]*65536+[uint64]$b[2]*256+$b[3]
  function Address([uint64]$n){'{0}.{1}.{2}.{3}' -f (($n -shr 24) -band 255),(($n -shr 16) -band 255),(($n -shr 8) -band 255),($n -band 255)}
  $blocked=@(('0.0.0.0-'+(Address ($number-1))),((Address ($number+1))+'-255.255.255.255'),'::/0')
  New-NetFirewallRule -Name SDE-WinRM-Pilot-Block -DisplayName 'SDE piloto WinRM HTTPS - bloquear outras origens' -Direction Inbound -Action Block -Protocol TCP -LocalPort 5986 -RemoteAddress $blocked -Profile Any | Out-Null
  New-NetFirewallRule -Name SDE-WinRM-Pilot-Allow -DisplayName 'SDE piloto WinRM HTTPS - tecnico autorizado' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5986 -LocalAddress $ClientIPv4 -RemoteAddress $TechnicianIPv4 -Profile Any | Out-Null
  if($null -eq $endpoint){Register-PSSessionConfiguration -Name Microsoft.PowerShell -AccessMode Remote -SecurityDescriptorSddl 'O:NSG:BAD:P(A;;GA;;;BA)(A;;GA;;;SY)' -NoServiceRestart -Force | Out-Null}
  else{Set-PSSessionConfiguration -Name Microsoft.PowerShell -AccessMode Remote -NoServiceRestart -Force | Out-Null}
  $state.ConfiguredSddl=[string](Get-PSSessionConfiguration -Name Microsoft.PowerShell).SecurityDescriptorSddl
  $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stateFile -Encoding UTF8
  New-WSManInstance winrm/config/Listener -SelectorSet @{Transport='HTTPS';Address='*'} -ValueSet @{Hostname=$env:COMPUTERNAME;CertificateThumbprint=$cert.Thumbprint;Port=5986} | Out-Null
  $state.ListenerCreated=$true;$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stateFile -Encoding UTF8
  Restart-Service WinRM -Force
 }catch{throw ('Configuracao incompleta: '+$_.Exception.Message+'. Execute a remocao do piloto antes de tentar novamente.')}
}
$export=Join-Path $ExportDirectory 'Cliente-WinRM.cer'
Export-Certificate -Cert $cert -FilePath $export -Type CERT -Force | Out-Null
$sha=[Security.Cryptography.SHA256]::Create()
try{$fingerprint=([BitConverter]::ToString($sha.ComputeHash($cert.RawData))).Replace('-','')}finally{$sha.Dispose()}
$info=[pscustomobject]@{ClientIPv4=$ClientIPv4;TechnicianIPv4=$TechnicianIPv4;Sha256=$fingerprint;Thumbprint=$cert.Thumbprint;Expires=$cert.NotAfter.ToString('o')}
$info | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ExportDirectory 'Dados-Publicos.json') -Encoding UTF8
Write-Host "CLIENTE CONFIGURADO: $ClientIPv4. Tecnico autorizado na porta 5986: $TechnicianIPv4"
Write-Host 'Confira este SHA256 diretamente nesta tela antes de confiar no certificado no Tecnico:'
Write-Host $fingerprint
Write-Host "Copie a pasta de certificado publico: $ExportDirectory"
$info
