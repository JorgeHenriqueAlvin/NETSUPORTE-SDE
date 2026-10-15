$ErrorActionPreference='Stop'
$fixture=Join-Path $env:RUNNER_TEMP ('SDE-Update-'+[guid]::NewGuid())
New-Item "$fixture\scripts","$fixture\Agent","$fixture\UpdateCache" -ItemType Directory -Force | Out-Null
Copy-Item "$PSScriptRoot\..\scripts\Update-Sde.ps1" "$fixture\scripts"
Copy-Item "$PSScriptRoot\..\dist\Agent\SDE-Suporte-Cliente.exe" "$fixture\Agent"
$manifest=@{version='999.0.0';assets=@{'SDE-Suporte-Cliente-Instalar.exe'=@{sha256=('0'*64);size=1000001}}}
$manifest | ConvertTo-Json -Depth 6 | Set-Content "$fixture\UpdateCache\update.json"
try {
    $result=& "$fixture\scripts\Update-Sde.ps1" -CheckOnly
    if ($result -notlike '*999.0.0*') { throw 'Atualizacao futura nao identificada.' }
    [IO.File]::WriteAllBytes("$fixture\UpdateCache\SDE-Suporte-Cliente-Instalar.exe",[byte[]]@(1,2,3))
    # Stub only network transfer. Production validation must reject these bytes before installation.
    function Invoke-WebRequest { param($Uri,$OutFile,$TimeoutSec,[switch]$UseBasicParsing) [IO.File]::WriteAllBytes($OutFile,[byte[]]@(4,5,6)) }
    $rejected=$false
    try { & "$fixture\scripts\Update-Sde.ps1" } catch { $rejected=$_.Exception.Message -like '*Integridade*' }
    if (!$rejected) { throw 'Pacote adulterado nao foi rejeitado.' }
    if (Test-Path "$fixture\UpdateCache\SDE-Suporte-Cliente-Instalar.exe") { throw 'Pacote adulterado permaneceu em cache.' }
    # A valid, running application must defer an otherwise valid downloaded update.
    $bytes=[IO.File]::ReadAllBytes("$fixture\Agent\SDE-Suporte-Cliente.exe")
    $asset="$fixture\UpdateCache\SDE-Suporte-Cliente-Instalar.exe"
    [IO.File]::WriteAllBytes($asset,$bytes)
    $manifest.assets.'SDE-Suporte-Cliente-Instalar.exe'.sha256=(Get-FileHash $asset -Algorithm SHA256).Hash
    $manifest.assets.'SDE-Suporte-Cliente-Instalar.exe'.size=$bytes.Length
    $manifest | ConvertTo-Json -Depth 6 | Set-Content "$fixture\UpdateCache\update.json"
    function Get-Process { param($Name,$ErrorAction) [pscustomobject]@{Id=123;ProcessName='SDE-Suporte-Cliente'} }
    function Start-Process { throw 'O atualizador tentou interromper um aplicativo ativo.' }
    & "$fixture\scripts\Update-Sde.ps1"
    if ((Get-Content "$fixture\UpdateCache\update.log" -Raw) -notlike '*aguardando aplicativos fechados*') { throw 'Atualizacao nao foi adiada durante uso.' }
    $manifest.version='0.0.1' 
    $manifest | ConvertTo-Json -Depth 6 | Set-Content "$fixture\UpdateCache\update.json"
    if (& "$fixture\scripts\Update-Sde.ps1" -CheckOnly) { throw 'Atualizador permitiu downgrade.' }
    Write-Host 'OK: nova versao detectada, downgrade ignorado e pacote adulterado bloqueado.'
} finally { Remove-Item $fixture -Recurse -Force }
