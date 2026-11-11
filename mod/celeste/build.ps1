param([Parameter(Mandatory)][string]$EverestLib, [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$lib = (Resolve-Path -LiteralPath $EverestLib).Path
foreach ($name in @('Celeste.dll', 'FNA.dll', 'MMHOOK_Celeste.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $lib $name))) { throw "Falta $name en el SDK de Everest." }
}
& $Dotnet build (Join-Path $PSScriptRoot 'Cloud90.Celeste.csproj') -c Release "-p:EverestLib=$lib"
if ($LASTEXITCODE -ne 0) { throw 'La compilación de Celeste falló.' }
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$files = @((Join-Path $PSScriptRoot 'everest.yaml'), (Join-Path $PSScriptRoot 'bin/Release/net8.0/Cloud90.Celeste.dll'))
Compress-Archive -LiteralPath $files -DestinationPath (Join-Path $dist 'Cloud90.Celeste.zip') -Force
