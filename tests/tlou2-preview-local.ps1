param([string]$WsModule, [string]$Executable = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$exe = if ($Executable) { (Resolve-Path -LiteralPath $Executable).Path }
       else { Join-Path $repo 'dist/Cloud90GameLink-Tlou2Preview-Local.exe' }
if (!(Test-Path -LiteralPath $exe)) { throw 'Falta la compilación local de Game Link.' }
if (Get-Process -Name 'Cloud90GameLink*' -ErrorAction SilentlyContinue) {
    throw 'Cierra la ventana de Game Link que ya está abierta y vuelve a ejecutar este comando.'
}
if (!$WsModule) {
    $workspace = Split-Path -Parent (Split-Path -Parent $repo)
    $WsModule = Join-Path $workspace 'backcloud90devstreamkit/node_modules/ws'
}
$env:WS_MODULE = (Resolve-Path -LiteralPath $WsModule).Path
$env:CLOUD90_TEST_TOKEN = 'c90gl_' + [Guid]::NewGuid().ToString('N')
$env:CLOUD90_TEST_PORT = '39182'
$env:CLOUD90_TLOU2_IMMEDIATE_PREVIEW = '1'
$origin = 'http://127.0.0.1:' + $env:CLOUD90_TEST_PORT
$profile = Join-Path $repo ('dist/tlou2-native/local-preview-' + [Guid]::NewGuid().ToString('N'))
$env:CLOUD90_GAME_LINK_DIR = $profile
New-Item -ItemType Directory -Path $profile | Out-Null
Add-Type -AssemblyName System.Security
$sealed = [Security.Cryptography.ProtectedData]::Protect(
    [Text.Encoding]::UTF8.GetBytes($env:CLOUD90_TEST_TOKEN),
    [Text.Encoding]::UTF8.GetBytes('Cloud90GameLink'),
    [Security.Cryptography.DataProtectionScope]::CurrentUser)
[IO.File]::WriteAllLines((Join-Path $profile 'link.ini'), @(
    "server=$origin", 'selected_game=tlou2',
    ('token_protected.tlou2=' + [Convert]::ToBase64String($sealed))
))
$serverFile = Join-Path $PSScriptRoot 'tlou2-preview-server.cjs'
$server = Start-Process -FilePath node.exe -ArgumentList ('"' + $serverFile + '"') -WindowStyle Hidden -PassThru
try {
    $listening = $false
    for ($i = 0; $i -lt 20 -and !$server.HasExited; $i++) {
        try { $listening = (Invoke-WebRequest "$origin/status" -UseBasicParsing -TimeoutSec 1).StatusCode -eq 200 }
        catch { Start-Sleep -Milliseconds 250 }
        if ($listening) { break }
    }
    if (!$listening) { throw "No inició el servidor de prueba en $origin." }
    Start-Process $origin
    Start-Process -FilePath $exe -Wait
}
finally {
    if (!$server.HasExited) { Stop-Process -InputObject $server }
}
