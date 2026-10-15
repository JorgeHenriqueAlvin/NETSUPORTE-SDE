#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
try {
 Write-Host 'SDE Suporte - configuracao do agente SICM.INTRANET'
 Write-Host 'Use somente certificado emitido pela TI e sub-rede real dos tecnicos.'
 $system = Get-CimInstance Win32_ComputerSystem
 if ($system.Domain -ine 'SICM.INTRANET') { throw 'Esta maquina nao pertence ao dominio SICM.INTRANET.' }
 $fqdn = "$($system.DNSHostName).$($system.Domain)"
 Write-Host "Computador: $fqdn"
 $sector = Read-Host 'Nome do setor deste computador'
 if ([string]::IsNullOrWhiteSpace($sector)) { throw 'Setor obrigatorio.' }
 $accounts = Read-Host 'Contas autorizadas, separadas por virgula [SICM\jorge.alvin]'
 if ([string]::IsNullOrWhiteSpace($accounts)) { $accounts = 'SICM\jorge.alvin' }
 $users = @($accounts.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
 $thumb = Read-Host 'Impressao digital do certificado de servidor em LocalMachine\My'
 $network = Read-Host 'Sub-redes permitidas dos tecnicos, separadas por virgula (ex.: rede real /24)'
 $subnets = @($network.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
 if (-not $thumb -or $subnets.Count -eq 0) { throw 'Certificado e sub-redes sao obrigatorios.' }
 $direct = Read-Host 'Permitir atendimento sem confirmacao por sessao? Digite SIM para habilitar'
 & "$PSScriptRoot\Install-Agent.ps1" -Hostname $fqdn -Sector $sector -AllowedUsers $users -CertificateThumbprint $thumb -TechnicianSubnets $subnets -AuthorizedUnattended:($direct -ceq 'SIM')
 Write-Host 'Configuracao concluida. Faca logoff/login do funcionario para iniciar o agente.'
} catch {
 Write-Host "CONFIGURACAO NAO CONCLUIDA: $($_.Exception.Message)" -ForegroundColor Red
 Read-Host 'Pressione Enter para fechar'
 exit 1
}
Read-Host 'Pressione Enter para fechar'
