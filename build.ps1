<#
.SYNOPSIS
Builds dist\Cloud90GameLink.exe with the C# compiler that comes with
Windows (.NET Framework 4.x), with the Hades mod and the MAME plugin
embedded. -SigningKey also writes Cloud90GameLink.exe.sig (RSA-SHA256, the
same key file as Nebula's releases).
#>
param([string]$SigningKey = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $dist 'Cloud90GameLink.exe'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', "/out:$out",
    "/win32manifest:$(Join-Path $root 'app.manifest')",
    "/resource:$(Join-Path $root 'mod\Cloud90.lua'),Cloud90.lua",
    "/resource:$(Join-Path $root 'mame\plugins\cloud90\init.lua'),cloud90-mame-init.lua",
    "/resource:$(Join-Path $root 'mame\plugins\cloud90\plugin.json'),cloud90-mame-plugin.json",
    '/r:System.Web.Extensions.dll', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Security.dll', '/r:System.Core.dll') + $sources
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
