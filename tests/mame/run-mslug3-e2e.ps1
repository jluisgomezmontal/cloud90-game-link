# The cloud90 MAME plugin on real MAME with Metal Slug 3, no window, no sound:
#   powershell -ExecutionPolicy Bypass -File tests\mame\run-mslug3-e2e.ps1 [-Mame C:\MAME]
# Needs the player's own mslug3.zip + neogeo.zip in <Mame>\roms.
param([string]$Mame = 'C:\MAME')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$work = Join-Path ([IO.Path]::GetTempPath()) ('c90-mslug3-e2e-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$env:CLOUD90_DIR = $work.Replace('\', '/')
$plugins = (Join-Path $root 'mame\plugins') + ';' + (Join-Path $Mame 'plugins')
$args = @('mslug3', '-video', 'none', '-sound', 'none', '-nothrottle', '-skip_gameinfo',
    '-pluginspath', $plugins, '-plugin', 'cloud90',
    '-autoboot_script', (Join-Path $PSScriptRoot 'mslug3_e2e.lua'), '-seconds_to_run', '400')
$p = Start-Process (Join-Path $Mame 'mame.exe') -ArgumentList $args -WorkingDirectory $Mame -PassThru -NoNewWindow `
    -RedirectStandardOutput (Join-Path $work 'out.txt') -RedirectStandardError (Join-Path $work 'err.txt')
$p.WaitForExit(500000) | Out-Null
'--- e2e.log'; Get-Content (Join-Path $work 'e2e.log') -ErrorAction SilentlyContinue
'--- acks.txt'; Get-Content (Join-Path $work 'acks.txt') -ErrorAction SilentlyContinue
'--- err'; Get-Content (Join-Path $work 'err.txt') -Tail 10 -ErrorAction SilentlyContinue
$acks = Get-Content (Join-Path $work 'acks.txt') -ErrorAction SilentlyContinue
$log = Get-Content (Join-Path $work 'e2e.log') -ErrorAction SilentlyContinue
function Num($pattern) { [int](($log | Select-String $pattern).Matches.Groups[1].Value) }
$g0 = Num 'before grenades=(\d+)'; $l0 = Num 'before grenades=\d+ lives=(\d+)'
$expect = [ordered]@{
    'hello'                         = ($acks -match '^hello 1\.1\.0 mslug3').Count -gt 0
    'state 1 while playing'         = ($acks -match '^state 1').Count -gt 0
    'invincible holds the blink'    = (Num '^invul=(\d+)') -ge 100 -and (Num 'invul later=(\d+)') -ge 100
    '+10 grenades, +1 life'         = (Num 'after add grenades=(\d+)') -eq ($g0 + 10) -and (Num 'after add grenades=\d+ lives=(\d+)') -eq ($l0 + 1)
    'no grenades, -1 life'          = (Num 'after remove grenades=(\d+)') -eq 0 -and (Num 'after remove grenades=\d+ lives=(\d+)') -eq $l0
    'shotgun with 30 shells'        = (Num '^weapon=(\d+)') -eq 2 -and (Num '^weapon=\d+ ammo=(\d+)') -eq 30
    '+100 ammo'                     = (Num 'after ammo weapon=\d+ ammo=(\d+)') -eq 130
    'back to the pistol'            = (Num 'after remove weapon=(\d+)') -eq 0 -and (Num 'after remove weapon=\d+ ammo=(\d+)') -eq 65535
    'no weapon to remove'           = ($acks -match '^ack e9 no disponible ahora$').Count -eq 1
    'timer frozen'                  = ($log -match 'freeze start timer=(..)').Count -eq 1
    'no_shoot ok'                   = ($acks -match '^ack e11 ok$').Count -eq 1
    'unknown effect'                = ($acks -match '^ack e12 efecto desconocido$').Count -eq 1
    'spawn ok'                      = ($acks -match '^ack e13 ok$').Count -eq 1
    'more enemies after the spawn'  = ($log -match 'after spawn enemies=\d+ list=ok').Count -eq 1
    'shield refuses troll'          = ($acks -match '^ack e15 escudo activo$').Count -eq 1
    'task list intact at the end'   = ($log -match '^end list=ok mode=2').Count -eq 1
}
$failed = 0
foreach ($k in $expect.Keys) { if (-not $expect[$k]) { "FAIL: $k"; $failed++ } }
foreach ($id in 'e1','e2','e3','e4','e5','e6','e7','e8','e10','e14') {
    if (($acks -match "^ack $id ok$").Count -ne 1) { "FAIL: $id not ok"; $failed++ }
}
$start = ($log | Select-String 'freeze start timer=(..)').Matches.Groups[1].Value
$later = ($log | Select-String 'freeze later timer=(..)').Matches.Groups[1].Value
if ($start -ne $later) { "FAIL: timer moved while frozen ($start -> $later)"; $failed++ }
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
if ($failed -eq 0) { 'cloud90 mame mslug3 e2e passed' } else { "$failed failed"; exit 1 }
