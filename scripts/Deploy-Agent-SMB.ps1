[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
# Implantação alternativa sem WinRM:
#  - Copia o instalador via compartilhamento administrativo ADMIN$ (porta 445).
#  - Executa via Agendador de Tarefas remoto (schtasks /S) rodando como SYSTEM.
#  - Lê o resultado de um arquivo gravado na própria máquina alvo.
# Exige: conta administradora local válida no alvo, ADMIN$ acessível, serviço
# "Task Scheduler" ativo (padrão) e, para contas admin que não são o builtin
# Administrator, LocalAccountTokenFilterPolicy=1 — ou use uma conta de domínio
# com direitos locais.

$credential=$null
$results=New-Object System.Collections.Generic.List[object]

function Normalize-UserForNet([pscredential]$cred){
 $u=$cred.UserName
 if($u.StartsWith('.\\')){return $u.Substring(2)}
 return $u
}

function Plain-Password([pscredential]$cred){
 $ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($cred.Password)
 try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)}
 finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)}
}

try{
 $request=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 if(@($request.targets).Count -lt 1 -or @($request.targets).Count -gt 512){throw 'Selecione de 1 a 512 destinos.'}
 $expected=[string]$request.sha256
 if($expected -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash -LiteralPath $request.installer -Algorithm SHA256).Hash -ine $expected){throw 'Integridade do instalador Cliente invalida.'}
 $installer=$request.installer

 $credential=Get-Credential -Message 'Conta administrativa local no alvo (ex.: .\administrador ou DOMINIO\usuario).'
 if($null -eq $credential){throw 'Autenticacao cancelada.'}
 $user=Normalize-UserForNet $credential
 $password=Plain-Password $credential

 $index=0
 foreach($target in $request.targets){
  $index++
  $address=[string]$target.address;$hostName=[string]$target.host
  Write-Progress -Activity 'Instalando Cliente SDE Suporte (SMB)' -Status "$index/$(@($request.targets).Count): $hostName" -PercentComplete (($index-1)*100/@($request.targets).Count)
  $share=$null;$taskName="SDE-Install-"+[guid]::NewGuid().ToString('N').Substring(0,12)
  try{
   # Valida IP e nome
   $ip=$null
   if(-not [Net.IPAddress]::TryParse($address,[ref]$ip) -or $ip.AddressFamily -ne 'InterNetwork'){throw 'IPv4 invalido.'}
   $b=$ip.GetAddressBytes()
   if(-not ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))){throw 'Use somente enderecos privados.'}
   # Usa IP ou nome para o alvo (schtasks aceita ambos)
   $hostArg=if([string]::IsNullOrWhiteSpace($hostName)){$address}else{$hostName}

   # 1) Monta ADMIN$
   $share="\\$hostArg\ADMIN$"
   & net.exe use $share $password /user:$user 2>&1 | Out-Null
   if($LASTEXITCODE -ne 0){throw "Nao foi possivel acessar $share. Verifique a conta administradora, firewall (porta 445) e Compartilhamento de Arquivos."}

   # 2) Copia instalador e script de execução para C:\Windows\Temp
   $remoteDir="$share\Temp\SdeDeploy-"+[guid]::NewGuid().ToString('N').Substring(0,8)
   New-Item -ItemType Directory -Path $remoteDir -Force | Out-Null
   $remoteInstaller=Join-Path $remoteDir 'SDE-Suporte-Cliente-Instalar.exe'
   Copy-Item -LiteralPath $installer -Destination $remoteInstaller -Force
   $localDir='C:\Windows\Temp\'+(Split-Path $remoteDir -Leaf)
   $localInstaller=Join-Path $localDir 'SDE-Suporte-Cliente-Instalar.exe'
   $localLog=Join-Path $localDir 'install.log'
   $localResult=Join-Path $localDir 'result.txt'
   $runnerPath=Join-Path $remoteDir 'run.cmd'
   @"
@echo off
"$localInstaller" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART > "$localLog" 2>&1
echo %ERRORLEVEL% > "$localResult"
"@ | Set-Content -LiteralPath $runnerPath -Encoding Ascii

   # 3) Cria e executa a tarefa remota como SYSTEM
   & schtasks.exe /Create /S $hostArg /U $user /P $password /RU SYSTEM /SC ONCE /ST 23:59 /TN $taskName /TR ("`""+("C:\Windows\Temp\"+(Split-Path $remoteDir -Leaf)+"\run.cmd")+"`"") /F 2>&1 | Out-Null
   if($LASTEXITCODE -ne 0){throw 'Nao foi possivel criar tarefa remota (RPC/Agendador bloqueado).'}
   & schtasks.exe /Run /S $hostArg /U $user /P $password /TN $taskName 2>&1 | Out-Null
   if($LASTEXITCODE -ne 0){throw 'Nao foi possivel iniciar a tarefa remota.'}

   # 4) Aguarda terminar (polling pelo arquivo result.txt via SMB)
   $resultRemote=Join-Path $remoteDir 'result.txt'
   $deadline=(Get-Date).AddMinutes(5);$exit=$null
   while((Get-Date) -lt $deadline){
    Start-Sleep -Seconds 3
    if(Test-Path -LiteralPath $resultRemote){
     $raw=(Get-Content -LiteralPath $resultRemote -Raw -ErrorAction SilentlyContinue)
     if($raw -and $raw.Trim() -match '^-?\d+$'){$exit=[int]$raw.Trim();break}
    }
   }
   if($null -eq $exit){throw 'Instalador nao terminou em 5 minutos.'}
   if($exit -ne 0){
    $log=''
    $logRemote=Join-Path $remoteDir 'install.log'
    if(Test-Path -LiteralPath $logRemote){$log=(Get-Content -LiteralPath $logRemote -Raw -ErrorAction SilentlyContinue)}
    throw ("Instalador retornou $exit. "+($log -replace '\s+',' ').Substring(0,[Math]::Min(200,$log.Length)))
   }
   $results.Add([pscustomobject]@{host=$hostName;address=$address;state='InstalledWaitingForLogon';detail='Instalado via SMB. Agente ativo no proximo login do funcionario.'})
   Write-Host "$hostArg : instalado"
  }catch{
   $detail=$_.Exception.Message
   if($credential -and $credential.UserName){$detail=$detail.Replace($credential.UserName,'[conta administrativa]')}
   if($password){$detail=$detail.Replace($password,'[senha]')}
   $results.Add([pscustomobject]@{host=$hostName;address=$address;state='Failed';detail=$detail})
   Write-Host "$hostName : falhou. $detail"
  }finally{
   if($share){
    try{& schtasks.exe /Delete /S $hostArg /U $user /P $password /TN $taskName /F 2>&1 | Out-Null}catch{}
    try{if($remoteDir -and (Test-Path -LiteralPath $remoteDir)){Remove-Item -LiteralPath $remoteDir -Recurse -Force -ErrorAction SilentlyContinue}}catch{}
    try{& net.exe use $share /delete /y 2>&1 | Out-Null}catch{}
   }
   $json=ConvertTo-Json -InputObject @($results.ToArray()) -Depth 6
   [IO.File]::WriteAllText([string]$request.resultPath,$json,(New-Object Text.UTF8Encoding($false)))
  }
 }
 Write-Progress -Activity 'Instalando Cliente SDE Suporte (SMB)' -Completed
}catch{Write-Error $_;exit 1}
finally{$credential=$null;$password=$null}
