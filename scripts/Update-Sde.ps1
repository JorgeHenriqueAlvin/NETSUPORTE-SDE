param([switch]$Register,[switch]$Remove,[switch]$CheckOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$taskName='SDE-Suporte-Atualizacao'
if ($Remove) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue; return }
if ($Register) {
    $exe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $action=New-ScheduledTaskAction -Execute $exe -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    # Verifica logo no boot e continua verificando de hora em hora. A primeira
    # verificacao periodica ocorre poucos minutos depois da instalacao.
    $trigger=New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(2)) -RepetitionInterval (New-TimeSpan -Hours 1)
    $boot=New-ScheduledTaskTrigger -AtStartup
    $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 25)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($trigger,$boot) -User SYSTEM -RunLevel Highest -Settings $settings -Description 'Verifica atualizacoes no boot e a cada hora; instala quando Cliente e Tecnico estiverem fechados.' -Force | Out-Null
    return
}
$state=Join-Path $root 'UpdateCache'
New-Item $state -ItemType Directory -Force | Out-Null
# The cache and executable script inherit Program Files administrator-only write permissions.
$log=Join-Path $state 'update.log'
$lock=Join-Path $state 'update.lock'
try {
    [IO.File]::WriteAllText($lock,"$(Get-Date -Format o)`r`nPID=$PID")
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $manifestFile=Join-Path $state 'update.json'
    $temp=Join-Path $state 'manifest.tmp'

    # No boot a rede pode levar alguns segundos para ficar pronta. Nessa janela,
    # fazemos novas tentativas antes de cair para o cache. Fora do boot, uma
    # tentativa online por execucao e suficiente porque a tarefa roda a cada hora.
    $startupWindow=$false
    try {
        $lastBoot=(Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).LastBootUpTime
        $startupWindow=((Get-Date)-$lastBoot).TotalMinutes -lt 10
    } catch {}
    $attempts=if($startupWindow){6}else{1}
    $onlineManifest=$false
    $lastManifestError=$null
    for($attempt=1;$attempt -le $attempts;$attempt++) {
        try {
            Remove-Item $temp -Force -ErrorAction SilentlyContinue
            Invoke-WebRequest 'https://github.com/JorgeHenriqueAlvin/NETSUPORTE-SDE/releases/latest/download/update.json' -UseBasicParsing -OutFile $temp -TimeoutSec 30
            $candidate=Get-Content $temp -Raw | ConvertFrom-Json
            if ($candidate.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versao de atualizacao invalida.' }
            if ($null -eq $candidate.assets) { throw 'Manifesto de atualizacao sem pacotes.' }
            Move-Item $temp $manifestFile -Force
            $onlineManifest=$true
            break
        } catch {
            $lastManifestError=$_.Exception.Message
            Remove-Item $temp -Force -ErrorAction SilentlyContinue
            if($attempt -lt $attempts){Start-Sleep -Seconds 10}
        }
    }
    if(-not $onlineManifest) {
        if(!(Test-Path $manifestFile)) { throw "Nao foi possivel consultar a atualizacao e nao existe cache local. $lastManifestError" }
        Add-Content $log "$(Get-Date -Format o) Consulta online indisponivel; usando manifesto em cache. $lastManifestError"
    }

    $manifest=Get-Content $manifestFile -Raw | ConvertFrom-Json
    if ($manifest.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versao invalida.' }
    foreach ($role in @(@{Folder='Agent';Exe='SDE-Suporte-Cliente.exe';Asset='SDE-Suporte-Cliente-Instalar.exe'},@{Folder='Technician';Exe='SDE-Suporte-Tecnico.exe';Asset='SDE-Suporte-Tecnico-Base.exe'})) {
        $binary=Join-Path $root ($role.Folder+'\'+$role.Exe)
        if (!(Test-Path $binary)) { continue }
        $current=[version]([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion)
        if ([version]$manifest.version -le $current) { continue }
        $entry=$manifest.assets.($role.Asset)
        if ($null -eq $entry -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$entry.size -lt 1000000 -or [long]$entry.size -gt 250000000) { throw 'Metadados do pacote invalidos.' }
        if ($CheckOnly) { Write-Output "Atualizacao disponivel: $($role.Folder) $($manifest.version)"; continue }
        $package=Join-Path $state $role.Asset
        if (!(Test-Path $package) -or (Get-FileHash $package -Algorithm SHA256).Hash -ne $entry.sha256) {
            $url='https://github.com/JorgeHenriqueAlvin/NETSUPORTE-SDE/releases/download/v'+$manifest.version+'/'+$role.Asset
            Invoke-WebRequest $url -UseBasicParsing -OutFile $package -TimeoutSec 600
        }
        if ((Get-Item $package).Length -ne [long]$entry.size -or (Get-FileHash $package -Algorithm SHA256).Hash -ne $entry.sha256) { Remove-Item $package -Force; throw 'Integridade do instalador rejeitada.' }
        # O download pode ser preparado durante o uso, mas a instalacao nunca
        # interrompe um atendimento. No proximo boot o pacote ja estara em cache.
        if (Get-Process -Name SDE-Suporte-Cliente,SDE-Suporte-Tecnico -ErrorAction SilentlyContinue) {
            Add-Content $log "$(Get-Date -Format o) Atualizacao $($manifest.version) preparada; aguardando aplicativos fechados ou proxima reinicializacao."
            continue
        }
        $process=Start-Process $package -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$root+'"')) -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Instalador retornou $($process.ExitCode)." }
        if ([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion) -lt [version]$manifest.version) { throw 'Versao instalada nao corresponde ao pacote.' }
        Add-Content $log "$(Get-Date -Format o) $($role.Folder) atualizado para $($manifest.version)."
    }
} catch {
    Add-Content $log "$(Get-Date -Format o) $($_.Exception.Message)"
    throw
} finally {
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
}
