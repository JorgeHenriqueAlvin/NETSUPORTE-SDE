[CmdletBinding()]
param([Parameter(Mandatory)][string]$ManifestPath)
$ErrorActionPreference='Stop'
# Dispara imediatamente a tarefa de atualização "SDE-Suporte-Atualizacao" em
# cada máquina selecionada, sem esperar o ciclo horário. Não copia nenhum
# arquivo — apenas executa a tarefa que já existe localmente desde a instalação
# do Cliente. Requer apenas conta administradora e porta 445.

$credential=$null
$results=New-Object System.Collections.Generic.List[object]

function Plain-Password([pscredential]$cred){
 $ptr=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($cred.Password)
 try{[Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)}
 finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)}
}

try{
 $request=Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 if(@($request.targets).Count -lt 1 -or @($request.targets).Count -gt 512){throw 'Selecione de 1 a 512 destinos.'}
 $credential=Get-Credential -Message 'Conta administradora local no alvo (ex.: .\administrador ou DOMINIO\usuario).'
 if($null -eq $credential){throw 'Autenticacao cancelada.'}
 $user=$credential.UserName;$password=Plain-Password $credential

 $index=0
 foreach($target in $request.targets){
  $index++
  $address=[string]$target.address;$hostName=[string]$target.host
  Write-Progress -Activity 'Disparando atualizacao imediata' -Status "$index/$(@($request.targets).Count): $hostName" -PercentComplete (($index-1)*100/@($request.targets).Count)
  try{
   $ip=$null
   if(-not [Net.IPAddress]::TryParse($address,[ref]$ip) -or $ip.AddressFamily -ne 'InterNetwork'){throw 'IPv4 invalido.'}
   $b=$ip.GetAddressBytes()
   if(-not ($b[0] -eq 10 -or ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) -or ($b[0] -eq 192 -and $b[1] -eq 168))){throw 'Use somente enderecos privados.'}
   $hostArg=if([string]::IsNullOrWhiteSpace($hostName)){$address}else{$hostName}
   & schtasks.exe /Run /S $hostArg /U $user /P $password /TN 'SDE-Suporte-Atualizacao' 2>&1 | Out-Null
   if($LASTEXITCODE -ne 0){throw 'Tarefa SDE-Suporte-Atualizacao nao encontrada ou acesso negado. Verifique se o Cliente ja esta instalado.'}
   $results.Add([pscustomobject]@{host=$hostName;address=$address;state='UpdateTriggered';detail='Atualizador disparado. Instalacao ocorre quando Cliente/Tecnico estiverem fechados.'})
   Write-Host "$hostArg : atualizador disparado"
  }catch{
   $detail=$_.Exception.Message
   if($credential -and $credential.UserName){$detail=$detail.Replace($credential.UserName,'[conta administrativa]')}
   if($password){$detail=$detail.Replace($password,'[senha]')}
   $results.Add([pscustomobject]@{host=$hostName;address=$address;state='Failed';detail=$detail})
   Write-Host "$hostName : falhou. $detail"
  }finally{
   $json=ConvertTo-Json -InputObject @($results.ToArray()) -Depth 6
   [IO.File]::WriteAllText([string]$request.resultPath,$json,(New-Object Text.UTF8Encoding($false)))
  }
 }
 Write-Progress -Activity 'Disparando atualizacao imediata' -Completed
}catch{Write-Error $_;exit 1}
finally{$credential=$null;$password=$null}
