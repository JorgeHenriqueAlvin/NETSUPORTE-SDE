[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
$credential=$null
$results=New-Object System.Collections.Generic.List[object]
try {
 $request=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 if($request.mode -notin @('Https','Kerberos')){throw 'Modo de autenticacao invalido.'}
 if(@($request.targets).Count -lt 1 -or @($request.targets).Count -gt 512){throw 'Selecione de 1 a 512 destinos.'}
 $expected=[string]$request.sha256
 if($expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $request.installer -Algorithm SHA256).Hash -ine $expected){throw 'Integridade do instalador Cliente invalida.'}
 $remoteScript=Join-Path $PSScriptRoot 'Install-RemoteAgent.ps1'
 if(-not (Test-Path -LiteralPath $remoteScript)){throw 'Componente de implantacao ausente. Reinstale o Tecnico.'}
 # Credentials exist only in this process memory, never in manifests, command lines or reports.
 $credential=Get-Credential -Message 'Conta com permissao de administrador nas maquinas selecionadas. Ex.: SICM\usuario ou COMPUTADOR\administrador'
 if($null -eq $credential){throw 'Autenticacao cancelada.'}
 $index=0
 foreach($target in $request.targets) {
  $session=$null;$stage=$null;$address=[string]$target.address;$hostName=[string]$target.host
  try {
   $index++;Write-Progress -Activity 'Instalando o Cliente SDE Suporte' -Status "$index/$(@($request.targets).Count): $hostName" -PercentComplete (($index-1)*100/@($request.targets).Count)
   $ip=$null
   if(-not [Net.IPAddress]::TryParse($address,[ref]$ip) -or $ip.AddressFamily -ne 'InterNetwork'){throw 'IPv4 invalido.'}
   $b=$ip.GetAddressBytes()
   if(-not ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))){throw 'Use somente enderecos privados da rede interna.'}
   if([Uri]::CheckHostName($hostName) -eq 'Unknown' -or $hostName.Contains(':')){throw 'Nome de destino invalido.'}
   if(-not (@([Net.Dns]::GetHostAddresses($hostName) | ForEach-Object IPAddressToString) -contains $address)){throw 'O nome DNS nao corresponde ao IP selecionado. Corrija a coluna Nome para autenticar.'}
   $options=New-PSSessionOption -OpenTimeout 15000 -OperationTimeout 240000 -NoMachineProfile
   if($request.mode -eq 'Https') {
    # Normal Windows certificate and host validation; never add TrustedHosts or skip TLS validation.
    $session=New-PSSession -ComputerName $hostName -Port 5986 -UseSSL -Authentication Negotiate -Credential $credential -SessionOption $options
   } else {
    $literal=$null
    if([Net.IPAddress]::TryParse($hostName,[ref]$literal)){throw 'Kerberos exige nome DNS; nao aceita destino por IP.'}
    $session=New-PSSession -ComputerName $hostName -Port 5985 -Authentication Kerberos -Credential $credential -SessionOption $options
   }
   $stage=Invoke-Command -Session $session -ScriptBlock {
    $principal=New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'A conta nao recebeu permissao administrativa nesta maquina. Verifique as politicas de acesso remoto do Windows.'}
    if(-not [Environment]::Is64BitOperatingSystem){throw 'O Cliente exige Windows de 64 bits.'}
    $folder=Join-Path $env:ProgramData ('SDE-Suporte\Deployment\'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    & icacls.exe $folder /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Nao foi possivel proteger a pasta temporaria de instalacao.'}
    $folder
   }
   $remoteInstaller=Join-Path $stage 'SDE-Suporte-Cliente-Instalar.exe'
   Copy-Item -LiteralPath $request.installer -Destination $remoteInstaller -ToSession $session
   $outcome=Invoke-Command -Session $session -FilePath $remoteScript -ArgumentList @($remoteInstaller,$expected)
   $results.Add([pscustomobject]@{host=$hostName;address=$address;state=[string]$outcome.State;detail=[string]$outcome.Detail})
   Write-Host "$hostName : $($outcome.State) - $($outcome.Detail)"
  } catch {
   # Do not disclose the submitted account or credential material in the saved report.
   $detail=$_.Exception.Message
   if($credential -and $credential.UserName){$detail=$detail.Replace($credential.UserName,'[conta administrativa]')}
   $results.Add([pscustomobject]@{host=$hostName;address=$address;state='Failed';detail=$detail})
   Write-Host "$hostName : falhou. $detail"
  } finally {
   if($session){
    if($stage){try{Invoke-Command -Session $session -ScriptBlock {param($folder) Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue} -ArgumentList $stage}catch{}}
    Remove-PSSession $session -ErrorAction SilentlyContinue
   }
   $json=ConvertTo-Json -InputObject @($results.ToArray()) -Depth 6
   [IO.File]::WriteAllText([string]$request.resultPath,$json,(New-Object Text.UTF8Encoding($false)))
  }
 }
 Write-Progress -Activity 'Instalando o Cliente SDE Suporte' -Completed
} catch {Write-Error $_;exit 1} finally {$credential=$null}
