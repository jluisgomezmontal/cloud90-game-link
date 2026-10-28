<#
.SYNOPSIS
Builds dist\Cloud90GameLink.exe with the C# compiler that comes with
Windows (.NET Framework 4.x), with the Hades mod and the MAME plugin
embedded. -SigningKey also writes Cloud90GameLink.exe.sig (RSA-SHA256, the
same key file as Nebula's releases).
#>
param([string]$SigningKey = '', [string]$CupheadGameDir = '', [string]$BepInExDir = '', [string]$BepInExArchive = '', [string]$JsonDll = '', [switch]$ReuseGameModules, [string]$OutputName = 'Cloud90GameLink.exe')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or !$OutputName.EndsWith('.exe')) { throw 'OutputName must be an exe file name' }
$out = Join-Path $dist $OutputName
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$cupheadResources = @()
if (!$ReuseGameModules) { & (Join-Path $root 'mod\gtasa\build.ps1') }
$gtaResources = @("/resource:$(Join-Path $root 'mod\gtasa\dist\Cloud90.GTASA.asi'),Cloud90.GTASA.asi", "/resource:$(Join-Path $root 'mod\gtasa\dist\dinput8.dll'),GTASA.dinput8.dll", "/resource:$(Join-Path $root 'mod\gtasa\THIRD-PARTY-NOTICES.txt'),GTASA.Notices.txt")
if ($CupheadGameDir) {
    if (!$ReuseGameModules) { & (Join-Path $root 'mod\cuphead\build.ps1') -GameDir $CupheadGameDir -BepInExDir $BepInExDir -JsonDll $JsonDll }
    if (!$BepInExArchive -or !(Test-Path -LiteralPath $BepInExArchive)) { throw 'A full Cuphead release requires the pinned BepInEx archive' }
    if ((Get-FileHash -LiteralPath $BepInExArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne '6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755') { throw 'Wrong BepInEx archive' }
    $cupheadResources = @("/resource:$(Join-Path $root 'mod\cuphead\dist\Cloud90.Cuphead.dll'),Cloud90.Cuphead.dll", "/resource:$BepInExArchive,Cuphead.BepInEx.zip")
    $cupheadResources += "/resource:$(Join-Path $root 'mod\cuphead\dist\Newtonsoft.Json.dll'),Cuphead.Newtonsoft.Json.dll"
    foreach ($license in Get-ChildItem (Join-Path $root 'licenses') -Filter *.txt) {
        $cupheadResources += "/resource:$($license.FullName),Cuphead.License.$($license.Name)"
    }
}
$artResources = @(Get-ChildItem (Join-Path $root 'assets') -File | Where-Object { $_.Extension -in @('.png', '.jpg', '.ico') } | ForEach-Object { "/resource:$($_.FullName),Art.$($_.Name)" })
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', "/out:$out", "/win32icon:$(Join-Path $root 'assets\cloud90.ico')",
    "/win32manifest:$(Join-Path $root 'app.manifest')",
    "/resource:$(Join-Path $root 'mod\Cloud90.lua'),Cloud90.lua",
    "/resource:$(Join-Path $root 'mame\plugins\cloud90\init.lua'),cloud90-mame-init.lua",
    "/resource:$(Join-Path $root 'mame\plugins\cloud90\plugin.json'),cloud90-mame-plugin.json",
    '/r:System.Web.Extensions.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Security.dll', '/r:System.Core.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll') + $cupheadResources + $gtaResources + $artResources + $sources
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Write-Host "Built $out"

if ($SigningKey) {
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    $rsa.FromXmlString((Get-Content -Raw $SigningKey))
    $signature = $rsa.SignData([IO.File]::ReadAllBytes($out), 'SHA256')
    [IO.File]::WriteAllBytes("$out.sig", $signature)
    Write-Host "Signed $out"
}
$hash = (Get-FileHash $out -Algorithm SHA256).Hash.ToLower()
Write-Host "SHA-256 $hash"
