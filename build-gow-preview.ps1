param([Parameter(Mandatory=$true)][string]$BaseExecutable, [string]$Rdr2SdkPath, [string]$CelesteEverestLib, [string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$base = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $BaseExecutable).Path)
$required = @('Cloud90.lua','Cloud90.Smw.lua','cloud90-mame-init.lua','cloud90-mame-mvsc.lua','cloud90-mame-mslugx.lua','cloud90-mame-session.lua','cloud90-mame-plugin.json','Cloud90.Cuphead.dll','Cuphead.BepInEx.zip','Cuphead.Newtonsoft.Json.dll','Cloud90.GTASA.asi','GTASA.dinput8.dll','GTASA.Notices.txt')
$names = $base.GetManifestResourceNames()
foreach ($name in $required) { if ($names -notcontains $name) { throw "Base client is incomplete: $name" } }
$resourceDir = Join-Path $root ('dist\gow-preview-resources-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $resourceDir | Out-Null
$resources = @{}
foreach ($name in $names) {
    if ($name -notmatch '^[A-Za-z0-9_.-]+$') { throw 'Invalid resource name' }
    $path = Join-Path $resourceDir $name
    $stream = $base.GetManifestResourceStream($name)
    try { $output = [IO.File]::Create($path); try { $stream.CopyTo($output) } finally { $output.Dispose() } } finally { $stream.Dispose() }
    $resources[$name] = $path
}
# Refresh all source-owned script resources; reuse the base client's compiled game modules.
$scripts = @{'Cloud90.lua'='mod\Cloud90.lua';'Cloud90.Smw.lua'='mod\smw\Cloud90.lua';'cloud90-mame-init.lua'='mame\plugins\cloud90\init.lua';'cloud90-mame-mvsc.lua'='mame\plugins\cloud90\mvsc.lua';'cloud90-mame-mslugx.lua'='mame\plugins\cloud90\mslugx.lua';'cloud90-mame-session.lua'='mame\plugins\cloud90\session.lua';'cloud90-mame-plugin.json'='mame\plugins\cloud90\plugin.json'}
foreach($name in $scripts.Keys) { $resources[$name] = Join-Path $root $scripts[$name] }
foreach($art in Get-ChildItem (Join-Path $root 'assets') -File | Where-Object Extension -in '.png','.jpg','.ico') { $resources['Art.'+$art.Name] = $art.FullName }
$outputName = 'Cloud90GameLink-GowPreview.exe'
if ($Rdr2SdkPath) {
    & (Join-Path $root 'mod\rdr2\build.ps1') -SdkPath $Rdr2SdkPath
    $resources['Cloud90.RDR2.Diagnostics.asi'] = Join-Path $root 'mod\rdr2\dist\Cloud90.RDR2.Diagnostics.asi'
    $outputName = 'Cloud90GameLink.exe'
}
if ($CelesteEverestLib) {
    & (Join-Path $root 'mod\celeste\build.ps1') -EverestLib $CelesteEverestLib -Dotnet $Dotnet
    $resources['Cloud90.Celeste.zip'] = Join-Path $root 'mod\celeste\dist\Cloud90.Celeste.zip'
    $outputName = 'Cloud90GameLink.exe'
}
$argsList = @('/nologo','/target:winexe','/platform:anycpu','/optimize+',('/out:'+(Join-Path $root ('dist\' + $outputName))),('/win32icon:'+(Join-Path $root 'assets\cloud90.ico')),('/win32manifest:'+(Join-Path $root 'app.manifest')),'/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Security.dll','/r:System.Core.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll')
foreach($name in $resources.Keys) { $argsList += '/resource:'+$resources[$name]+','+$name }
$argsList += @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName)
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') @argsList
if ($LASTEXITCODE -ne 0) { throw 'Preview compilation failed' }
Write-Output "Built dist\$outputName (unsigned experimental preview)"
