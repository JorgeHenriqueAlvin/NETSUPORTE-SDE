param([switch]$Register,[switch]$Remove,[switch]$CheckOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$taskName='SDE-Suporte-Atualizacao'
if ($Remove) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue; return }
if ($Register) {
    $exe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $action=New-ScheduledTaskAction -Execute $exe -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $trigger=New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes(2)) -RepetitionInterval (New-TimeSpan -Minutes 5)
    $boot=New-ScheduledTaskTrigger -AtStartup
    $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 25)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($trigger,$boot) -User SYSTEM -RunLevel Highest -Settings $settings -Description 'Consulta a cada cinco minutos; instala Cliente ocioso sem atendimento.' -Force | Out-Null
    return
}
$state=Join-Path $root 'UpdateCache'
New-Item $state -ItemType Directory -Force | Out-Null
$log=Join-Path $state 'update.log'
$lock=Join-Path $state 'update.lock'
$maintenance=Join-Path $state 'maintenance.json'
$lockHandle=$null
function Write-UpdateStatus([string]$Role,[string]$Status,[string]$Target,[string]$Installed,[string]$Detail) {
    $path=Join-Path $state 'status.json'
    $statusData=@{at=[DateTimeOffset]::UtcNow.ToString('o');role=$Role;state=$Status;targetVersion=$Target;installedVersion=$Installed;detail=$Detail}
    $tempStatus=$path+'.tmp'
    [IO.File]::WriteAllText($tempStatus,($statusData|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($false)))
    Move-Item $tempStatus $path -Force
}
try {
    try {$lockHandle=[IO.File]::Open($lock,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::Read)}
    catch [IO.IOException] { return }
    $lockHandle.SetLength(0)
    $lockBytes=[Text.Encoding]::UTF8.GetBytes("$(Get-Date -Format o) PID=$PID")
    $lockHandle.Write($lockBytes,0,$lockBytes.Length);$lockHandle.Flush()
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $manifestFile=Join-Path $state 'update.json'
    $temp=Join-Path $state 'manifest.tmp'
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
        $current=[version](([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion)).ToString(3))
        if ([version]$manifest.version -lt $current) { continue }
        $repair=$false
        if($role.Folder -eq 'Technician' -and [version]$manifest.version -eq $current) {
            $clientEntry=$manifest.assets.'SDE-Suporte-Cliente-Instalar.exe'
            if($null -eq $clientEntry -or $clientEntry.sha256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Metadados do Cliente invalidos.'}
            $embedded=Join-Path $root 'Packages\SDE-Suporte-Cliente-Instalar.exe'
            $repair=!(Test-Path $embedded) -or !(Test-Path ($embedded+'.sha256'))
            if(-not $repair) {
                $repair=(Get-FileHash $embedded -Algorithm SHA256).Hash -ine $clientEntry.sha256 -or (Get-Content ($embedded+'.sha256') -Raw).Trim() -ine $clientEntry.sha256
            }
        }
        if([version]$manifest.version -eq $current -and -not $repair) {
            if(-not $CheckOnly){Write-UpdateStatus $role.Folder 'Current' $manifest.version ($current.ToString()) 'Versao instalada confirmada.'}
            continue
        }
        $entry=$manifest.assets.($role.Asset)
        if ($null -eq $entry -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$entry.size -lt 1000000 -or [long]$entry.size -gt 250000000) { throw 'Metadados do pacote invalidos.' }
        if ($CheckOnly) { Write-Output "Atualizacao disponivel: $($role.Folder) $($manifest.version) Reparar=$repair"; continue }
        Write-UpdateStatus $role.Folder 'Downloading' $manifest.version ($current.ToString()) 'Preparando pacote verificado.'
        $package=Join-Path $state $role.Asset
        if (!(Test-Path $package) -or (Get-FileHash $package -Algorithm SHA256).Hash -ne $entry.sha256) {
            $url='https://github.com/JorgeHenriqueAlvin/NETSUPORTE-SDE/releases/download/v'+$manifest.version+'/'+$role.Asset
            Invoke-WebRequest $url -UseBasicParsing -OutFile $package -TimeoutSec 600
        }
        if ((Get-Item $package).Length -ne [long]$entry.size -or (Get-FileHash $package -Algorithm SHA256).Hash -ne $entry.sha256) { Remove-Item $package -Force; throw 'Integridade do instalador rejeitada.' }
        Write-UpdateStatus $role.Folder 'Prepared' $manifest.version ($current.ToString()) 'Pacote baixado e SHA256 verificado.'
        $running=@(Get-Process -Name SDE-Suporte-Cliente,SDE-Suporte-Tecnico -ErrorAction SilentlyContinue)
        if($running.Count -gt 0 -and $role.Folder -eq 'Agent' -and
            -not ($running|Where-Object ProcessName -eq 'SDE-Suporte-Tecnico') -and
            (Test-Path (Join-Path $PSScriptRoot 'Resume-Agent.ps1'))) {
            Write-UpdateStatus $role.Folder 'WaitingForIdle' $manifest.version ($current.ToString()) 'Aguardando agente ocioso, sem atendimento ativo.'
            [IO.File]::WriteAllText($maintenance,(@{at=[DateTimeOffset]::UtcNow.ToString('o');role='Agent';version=$manifest.version}|ConvertTo-Json))
            $deadline=(Get-Date).AddSeconds(85)
            do {
                Start-Sleep -Seconds 2
                $running=@(Get-Process -Name SDE-Suporte-Cliente,SDE-Suporte-Tecnico -ErrorAction SilentlyContinue)
            } while($running.Count -gt 0 -and (Get-Date) -lt $deadline)
        }
        if($running.Count -gt 0) {
            Remove-Item $maintenance -Force -ErrorAction SilentlyContinue
            Write-UpdateStatus $role.Folder 'Deferred' $manifest.version ($current.ToString()) 'Atendimento, console ou agente antigo em uso; tentativa em cinco minutos.'
            Add-Content $log "$(Get-Date -Format o) Atualizacao $($manifest.version) preparada; aguardando aplicativos fechados ou agente ocioso."
            continue
        }
        Write-UpdateStatus $role.Folder 'Installing' $manifest.version ($current.ToString()) 'Instalando pacote oficial.'
        $process=Start-Process $package -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$root+'"')) -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Instalador retornou $($process.ExitCode)." }
        if ([version]([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion) -lt [version]$manifest.version) { throw 'Versao instalada nao corresponde ao pacote.' }
        if($role.Folder -eq 'Technician') {
            $embedded=Join-Path $root 'Packages\SDE-Suporte-Cliente-Instalar.exe'
            $expected=$manifest.assets.'SDE-Suporte-Cliente-Instalar.exe'.sha256
            if(!(Test-Path $embedded) -or (Get-FileHash $embedded -Algorithm SHA256).Hash -ine $expected -or
                !(Test-Path ($embedded+'.sha256')) -or (Get-Content ($embedded+'.sha256') -Raw).Trim() -ine $expected) {throw 'Pacote Cliente nao foi recuperado.'}
        }
        Write-UpdateStatus $role.Folder 'Installed' $manifest.version $manifest.version 'Instalacao e versao verificadas.'
        Add-Content $log "$(Get-Date -Format o) $($role.Folder) atualizado para $($manifest.version)."
    }
} catch {
    Write-UpdateStatus $(if($role){$role.Folder}else{'Unknown'}) 'Failed' $(if($manifest){$manifest.version}else{''}) '' $_.Exception.Message
    Add-Content $log "$(Get-Date -Format o) $($_.Exception.Message)"
    throw
} finally {
    if($null -ne $lockHandle){
        Remove-Item $maintenance -Force -ErrorAction SilentlyContinue
        $lockHandle.Dispose()
        Remove-Item $lock -Force -ErrorAction SilentlyContinue
    }
}
