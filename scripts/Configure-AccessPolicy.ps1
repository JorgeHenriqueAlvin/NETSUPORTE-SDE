#Requires -RunAsAdministrator
param([ValidateSet('Confirmacao','PreAutorizado')][string]$Mode='Confirmacao',
 [string]$Reason='', [string]$InstallDirectory="$env:ProgramFiles\SDE-Suporte\Agent")
$ErrorActionPreference='Stop'
if ($Mode -eq 'PreAutorizado') {
 if ($Reason.Trim().Length -lt 10) { throw 'Informe uma justificativa de pelo menos 10 caracteres para a autorizacao previa da TI.' }
 Add-Type -AssemblyName System.Windows.Forms
 $answer=[Windows.Forms.MessageBox]::Show('Autorizar previamente tecnicos da equipe a acessar esta maquina SEM confirmacao por atendimento? Essa decisao sera registrada.','Politica administrativa SDE','YesNo','Warning')
 if ($answer -ne 'Yes') { throw 'Configuracao cancelada.' }
}
$config=Get-Content -LiteralPath "$InstallDirectory\agent.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$policy=@{deviceId=$config.device.id;mode=$Mode;approvedBy=[Security.Principal.WindowsIdentity]::GetCurrent().Name;reason=$Reason;at=[DateTimeOffset]::UtcNow.ToString('o')}
$json=$policy | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText("$InstallDirectory\access-policy.json",$json,(New-Object Text.UTF8Encoding($false)))
Add-Content -LiteralPath "$InstallDirectory\access-policy-audit.jsonl" -Value ($policy | ConvertTo-Json -Compress)
Write-Host 'Politica local registrada. Sera aplicada na proxima solicitacao de atendimento. Nao altera recusas ou solicitacoes pendentes.'
