# The Link's tests without Hades: builds src + tests/LinkTests.cs into a
# library, then checks the inbox (read back with real Lua), the installer on a
# fake Hades folder, and the protocol against tests/fake-server.js.
#   powershell -ExecutionPolicy Bypass -File tests\link-test.ps1 [-WsModule <path to ws>]
param([string]$WsModule = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('c90hl-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$failed = 0
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $dll = Join-Path $work 'LinkTests.dll'
    $sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) + (Join-Path $PSScriptRoot 'LinkTests.cs')
    & $csc /nologo /target:library "/out:$dll" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed' }
    [Reflection.Assembly]::LoadFrom($dll) | Out-Null
    $t = [Cloud90.HadesLink.LinkTests]

    # 1. Inbox, read back by Lua.
    $inbox = Join-Path $work 'inbox.lua'
    $t::WriteHostileInbox($inbox) | Out-Null
    $want = $t::HostileNameBase64
    node (Join-Path $PSScriptRoot 'inbox-check.js') $inbox $want
    if ($LASTEXITCODE -ne 0) { $failed++ }

    # 2. Installer.
    $failed += $t::Installer((Join-Path $work 'Hades'))

    # 3. Protocol.
    if ($WsModule) { $env:WS_MODULE = $WsModule }
    $env:PORT = '39090'
    $log = Join-Path $work 'server.log'
    $server = Start-Process node -ArgumentList "`"$(Join-Path $PSScriptRoot 'fake-server.js')`"" -RedirectStandardOutput $log -PassThru -NoNewWindow
    Start-Sleep -Milliseconds 800
    $failed += $t::Protocol('http://127.0.0.1:39090', (Join-Path $work 'inbox2.lua'))
    $server.WaitForExit(5000) | Out-Null
    $got = Get-Content $log
    $got | ForEach-Object { "  server: $_" }
    if (-not ($got -match '"type":"hello","game":"hades"')) { "FAIL: no hello"; $failed++ }
    if (-not ($got -match '"type":"ack","id":"e-42","ok":false,"reason":"escudo activo"')) { "FAIL: no ack"; $failed++ }
    if (-not $server.HasExited) { $server.Kill() }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
if ($failed -eq 0) { 'cloud90 hades link tests passed' } else { "$failed failed"; exit 1 }
