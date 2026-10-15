# Requer PowerShell como administrador. Certificado emitido pela CA interna, instalado previamente.
# Exemplo completo no README. Não desativa validação TLS nem autenticação do Windows.
#Requires -RunAsAdministrator
param(
 [Parameter(Mandatory)][string]$Hostname,
 [Parameter(Mandatory)][string]$Sector,
 [Parameter(Mandatory)][string[]]$AllowedGroups,
 [Parameter(Mandatory)][string]$CertificateThumbprint,
 [Parameter(Mandatory)][string[]]$TechnicianSubnets,
 [switch]$AuthorizedUnattended
)
$ErrorActionPreference = 'Stop'
if ($Hostname -notmatch '^[a-zA-Z0-9][a-zA-Z0-9.-]+\.[a-zA-Z0-9.-]+$') { throw 'Informe o FQDN real do computador.' }
if ($TechnicianSubnets.Count -eq 0 -or ($TechnicianSubnets | Where-Object { $_ -in @('Any','0.0.0.0/0','::/0','Internet') })) { throw 'Informe somente sub-redes específicas dos técnicos.' }
$thumb = $CertificateThumbprint.Replace(' ','')
$cert = Get-Item "Cert:\LocalMachine\My\$thumb"
if (-not $cert.HasPrivateKey -or $cert.NotAfter -lt (Get-Date)) { throw 'Certificado expirado ou sem chave privada.' }
if ($Hostname -notin @($cert.DnsNameList | ForEach-Object { $_.Unicode })) { throw 'O SAN do certificado deve conter o FQDN exato.' }
$sids = @($AllowedGroups | ForEach-Object { ([System.Security.Principal.NTAccount]::new($_)).Translate([System.Security.Principal.SecurityIdentifier]).Value })
if ($sids.Count -eq 0) { throw 'Informe pelo menos um grupo de técnicos do domínio.' }
$root = Split-Path $PSScriptRoot -Parent
$exe = "$root\dist\Agent\SDE-Suporte-Cliente.exe"
if (-not (Test-Path $exe)) { throw 'Execute Build.ps1 primeiro.' }
$target = "$env:ProgramFiles\SDE-Suporte"
New-Item $target -ItemType Directory -Force | Out-Null
# Diretório de código e política gravável apenas por administradores/SYSTEM.
& icacls.exe $target /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível proteger a pasta.' }
Copy-Item $exe "$target\SDE-Suporte-Cliente.exe" -Force
@{ Hostname=$Hostname; Sector=$Sector; AllowedGroupSids=$sids; Port=7443; RequireConsent=(-not $AuthorizedUnattended.IsPresent) } | ConvertTo-Json | Set-Content "$target\agent.json" -Encoding UTF8
$interactive = ([System.Security.Principal.SecurityIdentifier]::new('S-1-5-4')).Translate([System.Security.Principal.NTAccount]).Value
& netsh.exe http add urlacl "url=https://${Hostname}:7443/" "user=$interactive"
if ($LASTEXITCODE -ne 0) { throw 'URL já reservada ou erro. Inspecione netsh http show urlacl antes de alterar reservas existentes.' }
& netsh.exe http add sslcert 'ipport=0.0.0.0:7443' "certhash=$thumb" 'appid={c43a8ff9-42a4-4de1-9c45-6a49b6c9f27d}' 'certstorename=MY'
if ($LASTEXITCODE -ne 0) { throw 'Falha no vínculo TLS. Confira netsh http show sslcert. A reserva URL pode precisar ser removida antes de repetir.' }
New-NetFirewallRule -DisplayName 'SDE Suporte piloto HTTPS' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 7443 -Profile Domain -RemoteAddress $TechnicianSubnets | Out-Null
New-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SdeSuporte' -Value "`"$target\SDE-Suporte-Cliente.exe`"" -PropertyType String -Force | Out-Null
Write-Host 'Instalado. Entre novamente no Windows para iniciar o agente na sessão do funcionário.'
