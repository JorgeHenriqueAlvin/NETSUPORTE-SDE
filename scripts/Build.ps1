$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
foreach ($role in @('Agent','Technician')) {
 dotnet publish "$root\src\SdeSuporte.$role.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$root\dist\$role"
 if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar $role" }
}
