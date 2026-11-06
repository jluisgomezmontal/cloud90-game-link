param([string]$GameDir, [string]$EnterLevelLua)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if (!$GameDir -or !$EnterLevelLua) { throw 'Pass an isolated copy of the runtime (-GameDir) and the upstream normal-input navigation script (-EnterLevelLua)' }
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dll=Join-Path $GameDir 'SmwLiveTests.dll'
$sources=@(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName)+(Join-Path $PSScriptRoot 'SmwLive.cs')
& $csc /nologo /target:library "/out:$dll" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$(Join-Path $root 'mod\smw\Cloud90.lua'),Cloud90.Smw.lua" @sources
if ($LASTEXITCODE -ne 0) {throw 'SMW native test compile failed'}
[Reflection.Assembly]::LoadFrom($dll) | Out-Null
[Cloud90.GameLink.SmwLive]::Run($GameDir, (Join-Path $GameDir 'Super Mario World (USA).sfc'), $EnterLevelLua)
