# Execute no Windows com SDK .NET 10.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Instale o SDK .NET 10 para Windows x64.' }
foreach ($role in @('Agent','Technician')) {
 dotnet publish "$root\src\SdeSuporte.$role.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root\dist\$role"
 if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar $role" }
}
Copy-Item "$root\src\machines.example.json" "$root\dist\Technician\machines.json" -Force
Write-Host 'Executáveis separados gerados nas pastas dist\Agent e dist\Technician.'
