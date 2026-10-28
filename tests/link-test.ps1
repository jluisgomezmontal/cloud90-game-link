# The Link's tests without Hades: builds src + tests/LinkTests.cs into a
# library, then checks the inbox (read back with real Lua), the installer on a
# fake Hades folder, and the protocol against tests/fake-server.js.
#   powershell -ExecutionPolicy Bypass -File tests\link-test.ps1 [-WsModule <path to ws>]
param([string]$WsModule = '', [string]$JsonDll = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ('c90hl-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$env:CLOUD90_GAME_LINK_DIR = Join-Path $work 'data'
$failed = 0
$server = $null
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $dll = Join-Path $work 'LinkTests.dll'
    $sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) + (Join-Path $PSScriptRoot 'LinkTests.cs') + (Join-Path $PSScriptRoot 'CupheadTests.cs') + (Join-Path $root 'mod\cuphead\Protocol.cs')
    $jsonRefs = @()
    if ($JsonDll) {
        # Avoid Get-FileHash autoload issues in a legacy PowerShell child of pwsh.
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $jsonHash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($JsonDll))).Replace('-','').ToLowerInvariant() }
        finally { $sha.Dispose() }
        if ($jsonHash -ne '0f89b82c6b76816b17286951ba83201c71b6611f3e75e907552247dfc72034f2') { throw 'Wrong Newtonsoft.Json test dependency' }
        Copy-Item -LiteralPath $JsonDll -Destination (Join-Path $work 'Newtonsoft.Json.dll')
        $jsonRefs += "/r:$(Join-Path $work 'Newtonsoft.Json.dll')"
        $sources += @((Join-Path $root 'mod\cuphead\BridgeJson.cs'), (Join-Path $PSScriptRoot 'BridgeJsonTests.cs'))
    }
    & $csc /nologo /target:library "/out:$dll" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @jsonRefs @sources
    if ($LASTEXITCODE -ne 0) { throw 'csc failed' }
    [Reflection.Assembly]::LoadFrom($dll) | Out-Null
    if ($JsonDll) {
        [Reflection.Assembly]::LoadFrom((Join-Path $work 'Newtonsoft.Json.dll')) | Out-Null
        $failed += [BridgeJsonTests]::Run()
    }
    $t = [Cloud90.GameLink.LinkTests]

    # 1. Inbox, read back by Lua.
    $inbox = Join-Path $work 'inbox.lua'
    $t::WriteHostileInbox($inbox) | Out-Null
    $want = $t::HostileNameBase64
    node (Join-Path $PSScriptRoot 'inbox-check.js') $inbox $want
    if ($LASTEXITCODE -ne 0) { $failed++ }

    # 2. Installer.
    $failed += $t::Installer((Join-Path $work 'Hades'))

    # 2b. MAME.
     $failed += $t::Mame((Join-Path $work 'Mame'))
    $failed += $t::NetworkSecurity()
    $failed += $t::CtrInbox((Join-Path $work 'ctrnf'))
    $failed += [Cloud90.GameLink.CupheadTests]::Run((Join-Path $work 'cuphead'))

    # 3. Protocol.
    if ($WsModule) { $env:WS_MODULE = $WsModule }
    foreach ($mode in @('normal', 'oversize', 'ctr', 'cuphead')) {
        $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $listener.Stop()
        $env:PORT = [string]$port
        $env:FAKE_MODE = $mode
        $log = Join-Path $work "$mode-server.log"
        $server = Start-Process node -ArgumentList "`"$(Join-Path $PSScriptRoot 'fake-server.js')`"" -RedirectStandardOutput $log -PassThru -WindowStyle Hidden
        try {
            $deadline = (Get-Date).AddSeconds(5)
            while (-not ((Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern 'LISTENING' -Quiet))) {
                if ($server.HasExited -or (Get-Date) -gt $deadline) { throw 'Fake WebSocket server did not start' }
                Start-Sleep -Milliseconds 50
            }
            if ($mode -eq 'normal') { $failed += $t::Protocol("http://127.0.0.1:$port", (Join-Path $work 'inbox2.lua')) }
            elseif ($mode -eq 'ctr') { $failed += $t::CtrProtocol("http://127.0.0.1:$port", (Join-Path $work 'ctr-protocol.json')) }
            elseif ($mode -eq 'cuphead') { $failed += $t::CupheadProtocol("http://127.0.0.1:$port", (Join-Path $work 'cuphead-protocol.json')) }
            else { $failed += $t::OversizedProtocol("http://127.0.0.1:$port") }
            $server.WaitForExit(5000) | Out-Null
            $got = Get-Content -LiteralPath $log -Encoding UTF8
            $got | ForEach-Object { "  server: $_" }
            $helloGame = if ($mode -eq 'ctr') { 'ctrnf' } elseif ($mode -eq 'cuphead') { 'cuphead' } else { 'kof2002' }
            if (-not ($got -match ('"type":"hello","game":"' + $helloGame + '","version":"cloud90-game-link-1.4.0"'))) { "FAIL: no hello"; $failed++ }
            if ($mode -eq 'ctr' -and -not ($got -match '"type":"ack","id":"e-42","ok":true')) { "FAIL: no CTR ack"; $failed++ }
            if ($mode -eq 'cuphead' -and -not ($got -match '"type":"ack","id":"e-42","ok":true')) { "FAIL: no Cuphead ack"; $failed++ }
            if (($mode -eq 'cuphead' -or $mode -eq 'ctr') -and -not ($got -match '"type":"ack","id":"e-stale","ok":false,"reason":"intento anterior"')) { "FAIL: old-attempt Cuphead command was not rejected"; $failed++ }
            if ($mode -eq 'normal' -and -not ($got -match '"type":"ack","id":"e-42","ok":false,"reason":"escudo activo"')) { "FAIL: no ack"; $failed++ }
        }
        finally {
            if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
            $server = $null
        }
    }
    $failed += $t::StartupWindow()
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $cleanupPath = [IO.Path]::GetFullPath($work)
    if (-not $cleanupPath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($cleanupPath).StartsWith('c90hl-test-')) { throw 'Unsafe test cleanup path' }
    Remove-Item -LiteralPath $cleanupPath -Recurse -Force -ErrorAction SilentlyContinue
}
if ($failed -eq 0) { 'cloud90 hades link tests passed' } else { "$failed failed"; exit 1 }
