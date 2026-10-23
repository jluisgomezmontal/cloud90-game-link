# The app with real MAME and KOF 2002 against tests/fake-server.js (no
# window): installs the plugin, opens the game, and an effect from the
# "server" must come back acked.
#   powershell -ExecutionPolicy Bypass -File tests\mame\run-app-e2e.ps1 -WsModule <path to ws> [-Mame C:\MAME]
param([string]$WsModule = '', [string]$Mame = 'C:\MAME')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work = Join-Path ([IO.Path]::GetTempPath()) ('c90-app-e2e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$env:CLOUD90_GAME_LINK_DIR = Join-Path $work 'data'
$failed = 0
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $dll = Join-Path $work 'LinkTests.dll'
    $sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) + (Join-Path $root 'tests\LinkTests.cs')
    & $csc /nologo /target:library "/out:$dll" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed' }
    [Reflection.Assembly]::LoadFrom($dll) | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    if ($WsModule) { $env:WS_MODULE = $WsModule }
    $env:PORT = '39091'
    $env:FAKE_EFFECT = 'timer_add'
    $log = Join-Path $work 'server.log'
    $server = Start-Process node -ArgumentList "`"$(Join-Path $root 'tests\fake-server.js')`"" -RedirectStandardOutput $log -PassThru -NoNewWindow
    Start-Sleep -Milliseconds 800
    $failed += [Cloud90.GameLink.LinkTests]::MameEndToEnd('http://127.0.0.1:39091', $Mame, (Join-Path $PSScriptRoot 'play.lua'))
    $server.WaitForExit(5000) | Out-Null
    Get-Content $log | ForEach-Object { "  server: $_" }
    if (-not ((Get-Content $log) -match '"type":"ack","id":"e-42","ok":true')) { 'FAIL: the server got no ok ack'; $failed++ }
    if (-not $server.HasExited) { $server.Kill() }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($failed -eq 0) { 'cloud90 game link + mame e2e passed' } else { "$failed failed"; exit 1 }
