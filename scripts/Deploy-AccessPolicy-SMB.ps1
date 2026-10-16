[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
# Marca máquinas como "Pré-autorizadas" remotamente, sem WinRM:
#  - Usa ADMIN$ + Agendador de Tarefas remoto (como Deploy-Agent-SMB.ps1).
#  - Executa um script na máquina alvo que:
#      1. Lê o caminho do agente a partir do HKLM Run (SdeSuporte).
#      2. Lê agent.json para pegar o device.id.
#      3. Grava access-policy.json ao lado, com Mode=PreAutorizado, reason e timestamp.
#      4. Anexa linha ao access-policy-audit.jsonl.
# Para desmarcar, passe -Mode Confirmacao no manifesto (apaga access-policy.json).

$credential=$null
$results=New-Object System.Collections.Generic.List[object]

function Plain-Password([pscredential]$cred){
 $ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($cred.Password)
 try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)}
 finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)}
}

try{
 $request=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 $mode=[string]$request.policyMode
 if($mode -notin @('PreAutorizado','Confirmacao')){throw 'policyMode inválido.'}
 $reason=[string]$request.reason
 if($mode -eq 'PreAutorizado' -and $reason.Trim().Length -lt 10){throw 'Justificativa deve ter pelo menos 10 caracteres.'}
 if(@($request.targets).Count -lt 1 -or @($request.targets).Count -gt 512){throw 'Selecione de 1 a 512 destinos.'}

 $credential=Get-Credential -Message 'Conta administradora local no alvo (ex.: .\administrador ou DOMINIO\usuario).'
 if($null -eq $credential){throw 'Autenticacao cancelada.'}
 $user=$credential.UserName;$password=Plain-Password $credential
 $approvedBy=[Security.Principal.WindowsIdentity]::GetCurrent().Name

 $index=0
 foreach($target in $request.targets){
  $index++
  $address=[string]$target.address;$hostName=[string]$target.host
  Write-Progress -Activity 'Aplicando politica de acesso' -Status "$index/$(@($request.targets).Count): $hostName" -PercentComplete (($index-1)*100/@($request.targets).Count)
  $share=$null;$remoteDir=$null;$taskName='SDE-Policy-'+[guid]::NewGuid().ToString('N').Substring(0,12)
  try{
   $ip=$null
   if(-not [Net.IPAddress]::TryParse($address,[ref]$ip) -or $ip.AddressFamily -ne 'InterNetwork'){throw 'IPv4 invalido.'}
   $b=$ip.GetAddressBytes()
   if(-not ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))){throw 'Use somente enderecos privados.'}
   $hostArg=if([string]::IsNullOrWhiteSpace($hostName)){$address}else{$hostName}

   $share="\\$hostArg\ADMIN$"
   & net.exe use $share $password /user:$user 2>&1 | Out-Null
   if($LASTEXITCODE -ne 0){throw "Nao foi possivel acessar $share. Verifique conta admin e porta 445."}
   $remoteDir="$share\Temp\SdePolicy-"+[guid]::NewGuid().ToString('N').Substring(0,8)
   New-Item -ItemType Directory -Path $remoteDir -Force | Out-Null
   $localDir='C:\Windows\Temp\'+(Split-Path $remoteDir -Leaf)
   $localResult=Join-Path $localDir 'result.txt'

   # Script a executar como SYSTEM na máquina-alvo.
   $applyScript=@"
`$ErrorActionPreference='Stop'
try{
 `$run=(Get-ItemProperty 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SdeSuporte -ErrorAction Stop).SdeSuporte
 `$exe=`$run.Trim('"')
 `$dir=Split-Path `$exe -Parent
 `$configFile=Join-Path `$dir 'agent.json'
 if(-not (Test-Path `$configFile)){throw 'Agente SDE nao encontrado.'}
 `$config=Get-Content `$configFile -Raw -Encoding UTF8 | ConvertFrom-Json
 `$policyFile=Join-Path `$dir 'access-policy.json'
 `$auditFile=Join-Path `$dir 'access-policy-audit.jsonl'
 if('$mode' -eq 'Confirmacao'){
  if(Test-Path `$policyFile){Remove-Item `$policyFile -Force}
  `$entry=[ordered]@{deviceId=`$config.device.id;mode='Confirmacao';approvedBy='$approvedBy';reason='Reaplicacao remota do modo padrao';at=[DateTimeOffset]::UtcNow.ToString('o')}
  Add-Content -LiteralPath `$auditFile -Value (`$entry|ConvertTo-Json -Compress)
  'OK' | Set-Content -LiteralPath '$localResult'
  exit 0
 }
 `$policy=[ordered]@{deviceId=`$config.device.id;mode='PreAutorizado';approvedBy='$approvedBy';reason='$reason';at=[DateTimeOffset]::UtcNow.ToString('o')}
 `$json=`$policy | ConvertTo-Json -Depth 4
 [IO.File]::WriteAllText(`$policyFile,`$json,(New-Object Text.UTF8Encoding(`$false)))
 Add-Content -LiteralPath `$auditFile -Value (`$policy|ConvertTo-Json -Compress)
 'OK' | Set-Content -LiteralPath '$localResult'
}catch{`$_.Exception.Message | Set-Content -LiteralPath '$localResult';exit 1}
"@
   $scriptRemote=Join-Path $remoteDir 'apply.ps1'
   Set-Content -LiteralPath $scriptRemote -Value $applyScript -Encoding UTF8
   $localScript=Join-Path $localDir 'apply.ps1'
   $runnerCmd="@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$localScript`""
   $runnerPath=Join-Path $remoteDir 'run.cmd'
   Set-Content -LiteralPath $runnerPath -Value $runnerCmd -Encoding Ascii

   & schtasks.exe /Create /S $hostArg /U $user /P $password /RU SYSTEM /SC ONCE /ST 23:59 /TN $taskName /TR ("`""+(Join-Path $localDir 'run.cmd')+"`"") /F 2>&1 | Out-Null
   if($LASTEXITCODE -ne 0){throw 'Nao foi possivel criar tarefa remota.'}
   & schtasks.exe /Run /S $hostArg /U $user /P $password /TN $taskName 2>&1 | Out-Null

   $resultRemote=Join-Path $remoteDir 'result.txt'
   $deadline=(Get-Date).AddSeconds(45);$outcome=$null
   while((Get-Date) -lt $deadline){
    Start-Sleep -Seconds 2
    if(Test-Path -LiteralPath $resultRemote){$outcome=(Get-Content -LiteralPath $resultRemote -Raw -ErrorAction SilentlyContinue);if($outcome){break}}
   }
   if($null -eq $outcome){throw 'Alvo nao retornou resultado em 45 segundos.'}
   $outcome=$outcome.Trim()
   if($outcome -ne 'OK'){throw $outcome}
   $results.Add([pscustomobject]@{host=$hostName;address=$address;state=($mode -eq 'PreAutorizado'?'PreAutorizado':'Confirmacao');detail=('Politica aplicada por '+$approvedBy)})
   Write-Host "$hostArg : politica $mode aplicada"
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
 Write-Progress -Activity 'Aplicando politica de acesso' -Completed
}catch{Write-Error $_;exit 1}
finally{$credential=$null;$password=$null}
