#Requires -RunAsAdministrator
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$Sector,
 [Parameter(Mandatory)][string[]]$TechnicianFiles,
 [Parameter(Mandatory)][string]$ExportPath,
 [string[]]$TechnicianNetworks = @('LocalSubnet'),
 [bool]$RequireConsent = $true,
 [int]$Port = 7443,
 [string]$InstallDirectory = "$env:ProgramFiles\SDE-Suporte\Agent",
 [string]$BinaryDirectory = '',
 [string]$ConnectionHost = '',
 [switch]$NoStartup
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSEdition -eq 'Desktop') {
 $env:PSModulePath=(Join-Path $env:ProgramFiles 'WindowsPowerShell\Modules')+';'+(Join-Path $PSHOME 'Modules')
 Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
}
if ([string]::IsNullOrWhiteSpace($Sector) -or $Sector.Length -gt 100) { throw 'Informe o setor (ate 100 caracteres).' }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Porta invalida.' }
if ($TechnicianFiles.Count -eq 0) { throw 'Selecione a identificacao publica de pelo menos um tecnico.' }
$sha=[System.Security.Cryptography.SHA256]::Create()
$techs=@($TechnicianFiles | ForEach-Object {
 $tech=Get-Content -LiteralPath $_ -Raw -Encoding UTF8 | ConvertFrom-Json
 $publicBytes=[Convert]::FromBase64String($tech.publicKey)
 $id=([BitConverter]::ToString($sha.ComputeHash($publicBytes))).Replace('-','')
 if ($tech.id -cne $id -or [string]::IsNullOrWhiteSpace($tech.name) -or $tech.name.Length -gt 80 -or $tech.name -match '[\x00-\x1F\x7F]') { throw "Identificacao de tecnico invalida: $_" }
 @{id=$id;name=[string]$tech.name;publicKey=[string]$tech.publicKey;discoveryAddresses=@($tech.discoveryAddresses)}
})
if (($techs | Group-Object id | Where-Object Count -gt 1)) { throw 'Identificacao de tecnico duplicada.' }
if ($TechnicianNetworks.Count -eq 0 -or ($TechnicianNetworks | Where-Object { $_ -in @('Any','0.0.0.0/0','::/0','Internet') })) { throw 'Use LocalSubnet ou as sub-redes especificas dos tecnicos.' }
if (-not $ConnectionHost) {
 $addresses=@(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.AddressState -eq 'Preferred' })
 if ($addresses.Count -eq 0) { throw 'Nenhum endereco IPv4 da rede encontrado.' }
 $ConnectionHost=[string]$addresses[0].IPAddress
}
if ([Uri]::CheckHostName($ConnectionHost) -eq 'Unknown' -or $ConnectionHost -match '[:/\\]') { throw 'Endereco da rede invalido.' }
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
& icacls.exe $InstallDirectory /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel proteger a pasta do agente.' }
$exe=Join-Path $InstallDirectory 'SDE-Suporte-Cliente.exe'
Get-CimInstance Win32_Process -Filter "Name='SDE-Suporte-Cliente.exe'" | Where-Object ExecutablePath -eq $exe | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
if ($BinaryDirectory -and [IO.Path]::GetFullPath($BinaryDirectory).TrimEnd('\') -ine [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')) { Copy-Item -Path "$BinaryDirectory\*" -Destination $InstallDirectory -Recurse -Force }
if (-not (Test-Path -LiteralPath $exe)) { throw 'Instale o aplicativo antes de configurar.' }
$appid='{c43a8ff9-42a4-4de1-9c45-6a49b6c9f27d}'
$binding=(& netsh.exe http show sslcert "ipport=0.0.0.0:$Port" | Out-String)
if ($binding -match '[0-9a-fA-F]{40}') {
 if ($binding -notmatch 'c43a8ff9-42a4-4de1-9c45-6a49b6c9f27d') { throw 'A porta HTTPS ja esta ocupada por outro aplicativo.' }
 & netsh.exe http delete sslcert "ipport=0.0.0.0:$Port" | Out-Null
 if ($LASTEXITCODE -ne 0) { throw 'Nao foi possivel atualizar o vinculo HTTPS.' }
}
# The key stays in the Windows machine certificate store, never in an exported PFX.
$cert=New-SelfSignedCertificate -DnsName $env:COMPUTERNAME -CertStoreLocation 'Cert:\LocalMachine\My' -Type SSLServerAuthentication -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(3) -FriendlyName "SDE-Suporte-LAN-$Port"
& netsh.exe http add sslcert "ipport=0.0.0.0:$Port" "certhash=$($cert.Thumbprint)" "appid=$appid" 'certstorename=MY' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao vincular o certificado HTTPS.' }
$url="https://+:$Port/"
& netsh.exe http delete urlacl "url=$url" 2>&1 | Out-Null
& netsh.exe http add urlacl "url=$url" 'sddl=D:(A;;GX;;;BU)(A;;GX;;;BA)(A;;GX;;;SY)' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao reservar o endereco do agente.' }
$rule="SDE-Suporte-LAN-$Port"
Get-NetFirewallRule -Name $rule -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $rule -DisplayName "SDE Suporte LAN $Port" -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Domain,Private -RemoteAddress $TechnicianNetworks | Out-Null
$deviceId=[guid]::NewGuid().ToString()
$previousFile=Join-Path $InstallDirectory 'agent.json'
if(Test-Path $previousFile){try{$previous=Get-Content $previousFile -Raw -Encoding UTF8 | ConvertFrom-Json;$parsed=[guid]::Empty;if([guid]::TryParse($previous.device.id,[ref]$parsed)){$deviceId=$parsed.ToString()}}catch{}}
$device=@{id=$deviceId;name=$env:COMPUTERNAME;sector=$Sector;host=$ConnectionHost;port=$Port;certificateSha256=([BitConverter]::ToString($sha.ComputeHash($cert.RawData))).Replace('-','')}
$config=@{device=$device;technicians=$techs;requireConsent=$RequireConsent;certificateThumbprint=$cert.Thumbprint}
$utf8=New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $InstallDirectory 'agent.json'),($config | ConvertTo-Json -Depth 8),$utf8)
[IO.File]::WriteAllText($ExportPath,($device | ConvertTo-Json -Depth 5),$utf8)
if (-not $NoStartup) {
 New-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SdeSuporte -Value "`"$exe`"" -PropertyType String -Force | Out-Null
 $startup=(Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SdeSuporte).SdeSuporte
 if($startup -ne "`"$exe`""){throw 'Nao foi possivel ativar o inicio automatico do Cliente no Windows.'}
}
$sha.Dispose()
Write-Host "Agente configurado. Arquivo do computador: $ExportPath"
