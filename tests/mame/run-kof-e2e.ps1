# The cloud90 MAME plugin on real MAME with KOF 2002, no window, no sound:
#   powershell -ExecutionPolicy Bypass -File tests\mame\run-kof-e2e.ps1 [-Mame C:\MAME]
# Needs the player's own kof2002.zip + neogeo.zip in <Mame>\roms.
param([string]$Mame = 'C:\MAME')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work = Join-Path ([IO.Path]::GetTempPath()) ('c90-kof-e2e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$env:CLOUD90_DIR = $work.Replace('\', '/')
$plugins = (Join-Path $root 'mame\plugins') + ';' + (Join-Path $Mame 'plugins')
$args = @('kof2002', '-video', 'none', '-sound', 'none', '-nothrottle', '-skip_gameinfo',
    '-pluginspath', $plugins, '-plugin', 'cloud90',
    '-autoboot_script', (Join-Path $PSScriptRoot 'kof_e2e.lua'), '-seconds_to_run', '120')
$p = Start-Process (Join-Path $Mame 'mame.exe') -ArgumentList $args -WorkingDirectory $Mame -PassThru -NoNewWindow `
    -RedirectStandardOutput (Join-Path $work 'out.txt') -RedirectStandardError (Join-Path $work 'err.txt')
$p.WaitForExit(300000) | Out-Null
'--- e2e.log'; Get-Content (Join-Path $work 'e2e.log') -ErrorAction SilentlyContinue
'--- acks.txt'; Get-Content (Join-Path $work 'acks.txt') -ErrorAction SilentlyContinue
'--- err'; Get-Content (Join-Path $work 'err.txt') -Tail 10 -ErrorAction SilentlyContinue
$acks = Get-Content (Join-Path $work 'acks.txt') -ErrorAction SilentlyContinue
$log = Get-Content (Join-Path $work 'e2e.log') -ErrorAction SilentlyContinue
$expect = [ordered]@{
    'hello'                       = ($acks -match '^hello 1\.0\.0 kof2002').Count -gt 0
    'state 1 in the round'        = ($acks -match '^state 1').Count -gt 0
    'damage ok'                   = ($acks -match '^ack e1 ok').Count -eq 1
    'power_full ok, 3 stocks'     = ($acks -match '^ack e2 ok').Count -eq 1 -and ($log -match 'stocks=3').Count -eq 1
    'invincible holds health'     = ($acks -match '^ack e3 ok').Count -eq 1
    'timer frozen'                = (($log -match 'freeze start timer=(..)') -and ($log -match 'freeze later timer=(..)'))
    'shield refuses damage'       = ($acks -match '^ack e6 escudo activo').Count -eq 1
    'ko refused in safe mode'     = ($acks -match '^ack e7 ').Count -eq 1
    'unknown effect'              = ($acks -match '^ack e9 efecto desconocido').Count -eq 1
    'invert ok'                   = ($acks -match '^ack e10 ok').Count -eq 1
}
$failed = 0
foreach ($k in $expect.Keys) { if (-not $expect[$k]) { "FAIL: $k"; $failed++ } }
# The timer must not move while frozen.
$start = ($log | Select-String 'freeze start timer=(..)').Matches.Groups[1].Value
$later = ($log | Select-String 'freeze later timer=(..)').Matches.Groups[1].Value
if ($start -ne $later) { "FAIL: timer moved while frozen ($start -> $later)"; $failed++ }
# Invincible: health doesn't go below what it was when it started.
$before = [int]($log | Select-String 'before invincible p1hp=(-?\d+)').Matches.Groups[1].Value
$during = [int]($log | Select-String 'during invincible p1hp=(-?\d+)').Matches.Groups[1].Value
if ($during -lt $before) { "FAIL: health dropped while invincible ($before -> $during)"; $failed++ }
# Healing the rival: ok, or refused when the rival is at full health.
if (($acks -match '^ack e8 (ok|no disponible ahora)$').Count -ne 1) { 'FAIL: rival_heal'; $failed++ }
if (($acks -match '^ack e7 modo sin morir$').Count -ne 1) { 'FAIL: ko not refused in safe mode'; $failed++ }
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
if ($failed -eq 0) { 'cloud90 mame kof2002 e2e passed' } else { "$failed failed"; exit 1 }
