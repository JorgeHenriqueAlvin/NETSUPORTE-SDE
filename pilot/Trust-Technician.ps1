#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][string]$TechnicianIPv4,[Parameter(Mandatory)][string]$ClientIPv4,[Parameter(Mandatory)][string]$CertificatePath,[string]$ExpectedSha256)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Desktop'){
 $env:PSModulePath=(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')+';'+(Join-Path $PSHOME 'Modules')
 Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
}
if(-not (Get-NetIPAddress -AddressFamily IPv4 | Where-Object IPAddress -eq $TechnicianIPv4)){throw "Execute no Tecnico com IP $TechnicianIPv4."}
$cert=New-Object Security.Cryptography.X509Certificates.X509Certificate2($CertificatePath)
if($cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date)){throw 'Certificado invalido, expirado ou com chave privada. Use somente o .cer publico do piloto.'}
$sha=[Security.Cryptography.SHA256]::Create()
try{$actual=([BitConverter]::ToString($sha.ComputeHash($cert.RawData))).Replace('-','')}finally{$sha.Dispose()}
Write-Host "SHA256 do arquivo recebido: $actual"
if(-not $ExpectedSha256){$ExpectedSha256=Read-Host 'Digite o SHA256 exibido na tela do CLIENTE (ou conferido diretamente la)'}
$expected=($ExpectedSha256 -replace '[\s:-]','').ToUpperInvariant()
if($expected -notmatch '^[0-9A-F]{64}$' -or $expected -cne $actual){throw 'SHA256 nao corresponde. Nenhum certificado foi importado.'}
$folder=Join-Path $env:ProgramData 'SDE-Suporte-WinRM-Pilot';New-Item -ItemType Directory -Path $folder -Force | Out-Null
& icacls.exe $folder /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if($LASTEXITCODE -ne 0){throw 'Nao foi possivel proteger o registro do certificado.'}
$stateFile=Join-Path $folder 'technician.json' 
$already=Test-Path "Cert:\LocalMachine\Root\$($cert.Thumbprint)"
$owned=-not $already
if(Test-Path -LiteralPath $stateFile){$saved=Get-Content $stateFile -Raw | ConvertFrom-Json;if($saved.Thumbprint -ne $cert.Thumbprint){throw 'Existe outro certificado de piloto registrado. Remova antes de trocar.'};$owned=$owned -or $saved.ImportedByPilot}
if(-not $already){Import-Certificate -FilePath $CertificatePath -CertStoreLocation 'Cert:\LocalMachine\Root' | Out-Null}
@{Thumbprint=$cert.Thumbprint;ImportedByPilot=$owned;ClientIPv4=$ClientIPv4} | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
Write-Host 'Certificado publico conferido e registrado. Nenhuma senha foi gravada.'
$tcp=Test-NetConnection -ComputerName $ClientIPv4 -Port 5986 -WarningAction SilentlyContinue
if(-not $tcp.TcpTestSucceeded){throw 'A porta 5986 nao respondeu. Confira IPs, roteamento e firewall da rede.'}
Test-WSMan -ComputerName $ClientIPv4 -Port 5986 -UseSSL -ErrorAction Stop | Out-Null
Write-Host 'CANAL HTTPS VALIDADO. Abra o Tecnico 0.8, aba Implantar Clientes, modo HTTPS.'
Write-Host 'Informe 10.x/172.x/192.168.x do cliente na busca e autentique com sua conta administrativa no aplicativo.'
