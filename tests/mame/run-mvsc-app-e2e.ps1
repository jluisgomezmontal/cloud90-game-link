param([string]$Mame='C:\MAME',[string]$OutputDir='')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (!$OutputDir){$OutputDir=Join-Path ([IO.Path]::GetTempPath()) ('c90-mvsc-app-'+[Guid]::NewGuid().ToString('N'))}
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$isolated=Join-Path $OutputDir 'mame';New-Item -ItemType Directory -Force $isolated | Out-Null
# An isolated executable link and read-only-in-practice ROM directory reference:
# all generated cfg/NVRAM/plugin/state files stay under the test directory.
$exe=Join-Path $isolated 'mame.exe'
if (!(Test-Path -LiteralPath $exe)){New-Item -ItemType HardLink -Path $exe -Value (Join-Path $Mame 'mame.exe') | Out-Null}
$roms=Join-Path $isolated 'roms'
if (!(Test-Path -LiteralPath $roms)){New-Item -ItemType Junction -Path $roms -Value (Join-Path $Mame 'roms') | Out-Null}
$env:CLOUD90_GAME_LINK_DIR=Join-Path $OutputDir 'data'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$dll=Join-Path $OutputDir 'MvscLive.dll'
$sources=@(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName)+(Join-Path $root 'tests\MvscLive.cs')
& $csc /nologo /target:library "/out:$dll" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/resource:$(Join-Path $root 'mame\plugins\cloud90\init.lua'),cloud90-mame-init.lua" "/resource:$(Join-Path $root 'mame\plugins\cloud90\mvsc.lua'),cloud90-mame-mvsc.lua" "/resource:$(Join-Path $root 'mame\plugins\cloud90\plugin.json'),cloud90-mame-plugin.json" @sources
if($LASTEXITCODE -ne 0){throw 'Native app test compile failed'}
[Reflection.Assembly]::LoadFrom($dll)|Out-Null
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$port=$listener.LocalEndpoint.Port;$listener.Stop()
$env:PORT=[string]$port;$env:FAKE_MODE='mvsc-live';$env:FAKE_EFFECT='timer_freeze'
$log=Join-Path $OutputDir 'server.log'
$server=Start-Process node -ArgumentList ('"'+(Join-Path $root 'tests\fake-server.js')+'"') -PassThru -WindowStyle Hidden -RedirectStandardOutput $log
try {
    $until=(Get-Date).AddSeconds(5)
    while (!(Test-Path -LiteralPath $log) -or !(Select-String -LiteralPath $log -Pattern LISTENING -Quiet)) {if((Get-Date)-gt $until){throw 'Test server not ready'};Start-Sleep -Milliseconds 50}
    [Cloud90.GameLink.MvscLive]::Run("http://127.0.0.1:$port",$isolated,(Join-Path $PSScriptRoot 'mvsc_play.lua'),((Join-Path $isolated 'plugins')+';'+(Join-Path $Mame 'plugins')))
    $server.WaitForExit(5000)|Out-Null
    Get-Content -LiteralPath $log
    if (!(Select-String -LiteralPath $log -SimpleMatch '"type":"ack","id":"e-42","ok":true' -Quiet)){throw 'No game ACK reached the server'}
}finally {if(!$server.HasExited){Stop-Process -Id $server.Id -Force}}
