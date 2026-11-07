param([string]$Mame='C:\MAME',[string]$OutputDir='')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (!$OutputDir) {$OutputDir=Join-Path ([IO.Path]::GetTempPath()) ('c90-mslugx-e2e-'+[Guid]::NewGuid().ToString('N'))}
$OutputDir=[IO.Path]::GetFullPath($OutputDir)
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$env:CLOUD90_DIR=$OutputDir.Replace('\','/')
$env:CLOUD90_SESSION=[Guid]::NewGuid().ToString('N')
$env:CLOUD90_GAME_ID='mslugx'
$pluginPath=(Join-Path $root 'mame\plugins')+';'+(Join-Path $Mame 'plugins')
$mameArgs=@('mslugx','-rompath',('"'+(Join-Path $Mame 'roms')+'"'),'-video','none','-sound','none','-nothrottle','-skip_gameinfo',
 '-cfg_directory',('"'+(Join-Path $OutputDir 'cfg')+'"'),'-nvram_directory',('"'+(Join-Path $OutputDir 'nvram')+'"'),
 '-state_directory',('"'+(Join-Path $OutputDir 'states')+'"'),'-snapshot_directory',('"'+(Join-Path $OutputDir 'snap')+'"'),
 '-pluginspath',('"'+$pluginPath+'"'),'-plugin','cloud90','-autoboot_script',('"'+(Join-Path $PSScriptRoot 'mslugx_e2e.lua')+'"'),'-seconds_to_run','140')
$p=Start-Process (Join-Path $Mame 'mame.exe') -ArgumentList $mameArgs -WorkingDirectory $Mame -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $OutputDir 'out.log') -RedirectStandardError (Join-Path $OutputDir 'err.log')
if (!$p.WaitForExit(60000)) {$p.Kill();throw 'MSX native test timed out'}
Get-Content (Join-Path $OutputDir 'e2e.log') -ErrorAction SilentlyContinue
Get-Content (Join-Path $OutputDir 'err.log') -Tail 8 -ErrorAction SilentlyContinue
if (!(Test-Path (Join-Path $OutputDir 'e2e.log')) -or !(Select-String -LiteralPath (Join-Path $OutputDir 'e2e.log') -Pattern '^RESULT failures=0$' -Quiet)) {throw "MSX test failed; evidence: $OutputDir"}
'MSX native MAME integration passed'

