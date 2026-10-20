$ErrorActionPreference='Stop'
$fixture=Join-Path $env:RUNNER_TEMP ('SDE-Update-'+[guid]::NewGuid())
New-Item "$fixture\scripts","$fixture\Agent","$fixture\UpdateCache" -ItemType Directory -Force | Out-Null
Copy-Item "$PSScriptRoot\..\scripts\Update-Sde.ps1" "$fixture\scripts"
Copy-Item "$PSScriptRoot\..\dist\Agent\SDE-Suporte-Cliente.exe" "$fixture\Agent"
$servedManifest=Join-Path $fixture 'served-update.json'
$manifest=@{version='999.0.0';assets=@{'SDE-Suporte-Cliente-Instalar.exe'=@{sha256=('0'*64);size=1000001}}}
$manifest | ConvertTo-Json -Depth 6 | Set-Content $servedManifest
# Deixe um manifesto local antigo para provar que cada execucao consulta o servidor,
# em vez de reutilizar um cache ainda dentro de uma janela de 24 horas.
@{version='0.0.1';assets=@{}} | ConvertTo-Json -Depth 6 | Set-Content "$fixture\UpdateCache\update.json"
function Invoke-WebRequest {
    param($Uri,$OutFile,$TimeoutSec,[switch]$UseBasicParsing)
    if($Uri -like '*update.json') { Copy-Item $servedManifest $OutFile -Force; return }
    [IO.File]::WriteAllBytes($OutFile,[byte[]]@(4,5,6))
}
try {
    $result=& "$fixture\scripts\Update-Sde.ps1" -CheckOnly
    if ($result -notlike '*999.0.0*') { throw 'Atualizacao futura nao identificada.' }
    if ((Get-Content "$fixture\UpdateCache\update.json" -Raw | ConvertFrom-Json).version -ne '999.0.0') { throw 'Manifesto online nao substituiu o cache antigo.' }
    if (Test-Path "$fixture\UpdateCache\update.lock") { throw 'Lock do atualizador permaneceu apos a verificacao.' }

    [IO.File]::WriteAllBytes("$fixture\UpdateCache\SDE-Suporte-Cliente-Instalar.exe",[byte[]]@(1,2,3))
    # Stub de rede entrega bytes adulterados para o pacote. A validacao de producao
    # precisa rejeita-los antes de qualquer tentativa de instalacao.
    $rejected=$false
    try { & "$fixture\scripts\Update-Sde.ps1" } catch { $rejected=$_.Exception.Message -like '*Integridade*' }
    if (!$rejected) { throw 'Pacote adulterado nao foi rejeitado.' }
    if (Test-Path "$fixture\UpdateCache\SDE-Suporte-Cliente-Instalar.exe") { throw 'Pacote adulterado permaneceu em cache.' }

    # Um aplicativo aberto deve permitir que o pacote valido seja preparado, mas
    # nunca interromper o atendimento para instalar a atualizacao.
    $bytes=[IO.File]::ReadAllBytes("$fixture\Agent\SDE-Suporte-Cliente.exe")
    $asset="$fixture\UpdateCache\SDE-Suporte-Cliente-Instalar.exe"
    [IO.File]::WriteAllBytes($asset,$bytes)
    $manifest.assets.'SDE-Suporte-Cliente-Instalar.exe'.sha256=(Get-FileHash $asset -Algorithm SHA256).Hash
    $manifest.assets.'SDE-Suporte-Cliente-Instalar.exe'.size=$bytes.Length
    $manifest | ConvertTo-Json -Depth 6 | Set-Content $servedManifest
    function Get-Process { param($Name,$ErrorAction) [pscustomobject]@{Id=123;ProcessName='SDE-Suporte-Cliente'} }
    function Start-Process { throw 'O atualizador tentou interromper um aplicativo ativo.' }
    & "$fixture\scripts\Update-Sde.ps1"
    if ((Get-Content "$fixture\UpdateCache\update.log" -Raw) -notlike '*aguardando aplicativos fechados*') { throw 'Atualizacao nao foi adiada durante uso.' }

    $status=Get-Content "$fixture\UpdateCache\status.json" -Raw | ConvertFrom-Json
    if($status.state -ne 'Deferred'){throw 'Estado de adiamento nao foi registrado.'}

    # Reproduce a damaged embedded Client with the exact same console version.
    New-Item "$fixture\Technician","$fixture\Packages" -ItemType Directory -Force | Out-Null
    Copy-Item "$PSScriptRoot\..\dist\Technician\SDE-Suporte-Tecnico.exe" "$fixture\Technician"
    $version=([Diagnostics.FileVersionInfo]::GetVersionInfo("$fixture\Technician\SDE-Suporte-Tecnico.exe")).FileVersion
    $manifest.version=([version]$version).ToString(3)
    $manifest.assets.'SDE-Suporte-Tecnico-Base.exe'=$manifest.assets.'SDE-Suporte-Cliente-Instalar.exe'
    [IO.File]::WriteAllText("$fixture\Packages\SDE-Suporte-Cliente-Instalar.exe",'damaged')
    [IO.File]::WriteAllText("$fixture\Packages\SDE-Suporte-Cliente-Instalar.exe.sha256",('0'*64))
    $manifest | ConvertTo-Json -Depth 6 | Set-Content $servedManifest
    $repair=& "$fixture\scripts\Update-Sde.ps1" -CheckOnly
    if($repair -notlike '*Technician*Reparar=True*'){throw 'Reparo de pacote da mesma versao nao identificado.'}

    $manifest.version='0.0.1'
    $manifest | ConvertTo-Json -Depth 6 | Set-Content $servedManifest
    if (& "$fixture\scripts\Update-Sde.ps1" -CheckOnly) { throw 'Atualizador permitiu downgrade.' }
    Write-Host 'OK: consulta online imediata, downgrade ignorado e pacote adulterado bloqueado.'
} finally { Remove-Item $fixture -Recurse -Force }
