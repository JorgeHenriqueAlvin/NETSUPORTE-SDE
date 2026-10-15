param([switch]$Register,[switch]$Remove,[switch]$CheckOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$taskName='SDE-Suporte-Atualizacao'
if ($Remove) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue; return }
if ($Register) {
    $exe=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $action=New-ScheduledTaskAction -Execute $exe -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $trigger=New-ScheduledTaskTrigger -Once -At ((Get-Date).AddMinutes((Get-Random -Minimum 15 -Maximum 75))) -RepetitionInterval (New-TimeSpan -Hours 1)
    $boot=New-ScheduledTaskTrigger -AtStartup
    $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 25)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($trigger,$boot) -User SYSTEM -RunLevel Highest -Settings $settings -Description 'Atualiza os aplicativos SDE quando estiverem fechados. Verifica SHA256 do pacote oficial.' -Force | Out-Null
    return
}
$state=Join-Path $root 'UpdateCache'
New-Item $state -ItemType Directory -Force | Out-Null
# The cache and executable script inherit Program Files administrator-only write permissions.
$log=Join-Path $state 'update.log'
try {
    [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
    $manifestFile=Join-Path $state 'update.json'
    if (!(Test-Path $manifestFile) -or (Get-Item $manifestFile).LastWriteTimeUtc -lt [DateTime]::UtcNow.AddHours(-24)) {
        $temp=Join-Path $state 'manifest.tmp'
        Invoke-WebRequest 'https://github.com/JorgeHenriqueAlvin/NETSUPORTE-SDE/releases/latest/download/update.json' -UseBasicParsing -OutFile $temp -TimeoutSec 45
        $candidate=Get-Content $temp -Raw | ConvertFrom-Json
        if ($candidate.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versao de atualizacao invalida.' }
        Move-Item $temp $manifestFile -Force
    }
    $manifest=Get-Content $manifestFile -Raw | ConvertFrom-Json
    if ($manifest.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versao invalida.' }
    foreach ($role in @(@{Folder='Agent';Exe='SDE-Suporte-Cliente.exe';Asset='SDE-Suporte-Cliente-Instalar.exe'},@{Folder='Technician';Exe='SDE-Suporte-Tecnico.exe';Asset='SDE-Suporte-Tecnico-Base.exe'})) {
        $binary=Join-Path $root ($role.Folder+'\'+$role.Exe)
        if (!(Test-Path $binary)) { continue }
        $current=[version]([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion)
        if ([version]$manifest.version -le $current) { continue }
        $entry=$manifest.assets.($role.Asset)
        if ($entry.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$entry.size -lt 1000000 -or [long]$entry.size -gt 250000000) { throw 'Metadados do pacote invalidos.' }
        if ($CheckOnly) { Write-Output "Atualizacao disponivel: $($role.Folder) $($manifest.version)"; continue }
        $package=Join-Path $state $role.Asset
        if (!(Test-Path $package) -or (Get-FileHash $package -Algorithm SHA256).Hash -ne $entry.sha256) {
            $url='https://github.com/JorgeHenriqueAlvin/NETSUPORTE-SDE/releases/download/v'+$manifest.version+'/'+$role.Asset
            Invoke-WebRequest $url -UseBasicParsing -OutFile $package -TimeoutSec 600
        }
        if ((Get-Item $package).Length -ne [long]$entry.size -or (Get-FileHash $package -Algorithm SHA256).Hash -ne $entry.sha256) { Remove-Item $package -Force; throw 'Integridade do instalador rejeitada.' }
        # Do not interrupt support, chat, or any running employee/technician application.
        if (Get-Process -Name SDE-Suporte-Cliente,SDE-Suporte-Tecnico -ErrorAction SilentlyContinue) {
            Add-Content $log "$(Get-Date -Format o) Atualizacao preparada; aguardando aplicativos fechados ou proxima reinicializacao."
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
}
