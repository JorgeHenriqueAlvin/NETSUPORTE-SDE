$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$payload=Join-Path $root 'dist\payload'
New-Item -Path "$payload\Agent" -ItemType Directory -Force | Out-Null
Copy-Item "$root\dist\Agent\*" "$payload\Agent" -Force -Recurse
foreach($script in @('AutoConfigure-Agent.ps1','Install-Agent.ps1','Uninstall-Agent.ps1')) { Copy-Item "$PSScriptRoot\$script" $payload -Force }
Compress-Archive -Path "$payload\*" -DestinationPath "$root\dist\agent-payload.zip" -Force
$stub="$root\dist\Bootstrapper\SDE-Suporte-Cliente-Instalar.exe"
$template="$root\dist\Technician\agent.sdetemplate"
dotnet run --project "$root\tests\SdeSuporte.Tests.csproj" -c Release -- template $stub "$root\dist\agent-payload.zip" $template
if($LASTEXITCODE -ne 0){throw 'Falha ao gerar modelo de instalacao do cliente.'}
