param(
    [Parameter(Mandatory=$true)][string]$Pcsx2Exe,
    [Parameter(Mandatory=$true)][string]$Iso,
    [Parameter(Mandatory=$true)][string]$Config,
    [Parameter(Mandatory=$true)][ValidateSet('gow1','gow2')][string]$Game
)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name pcsx2-qt -ErrorAction SilentlyContinue) { throw 'Close PCSX2 before the isolated native test' }
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetTempPath()) ('c90-gow-native-' + [Guid]::NewGuid().ToString('N'))
$data = Join-Path $work 'PCSX2'
$iniDir = Join-Path $data 'inis'
$states = Join-Path $data 'sstates'
$cards = Join-Path $data 'memcards'
New-Item -ItemType Directory -Force $iniDir,$states,$cards | Out-Null
$text = Get-Content -LiteralPath $Config -Raw
$values = @{PINESlot='28012';EnablePINE='true';EnableFileLogging='true';MemoryCards=$cards;Savestates=$states;ConfirmShutdown='false'}
foreach($name in $values.Keys) {
    $pattern = '(?m)^' + [Regex]::Escape($name) + '\s*=.*$'
    if (![Regex]::IsMatch($text,$pattern)) { throw "Required test configuration field missing: $name" }
    $value=$values[$name]
    $text=[Regex]::Replace($text,$pattern,[System.Text.RegularExpressions.MatchEvaluator]{param($match) $name+' = '+$value})
}
$text=[Regex]::Replace($text,'(?m)^((?:Slot[12]|Multitap[12]_Slot[234])_Filename)\s*=.*$',[System.Text.RegularExpressions.MatchEvaluator]{param($match) $match.Groups[1].Value+' = NativeSafety_'+$match.Groups[1].Value+'.ps2'})
[IO.File]::WriteAllText((Join-Path $iniDir 'PCSX2.ini'),$text,(New-Object Text.UTF8Encoding($false)))
$log=Join-Path $work 'pcsx2-test.log'
$testExe=Join-Path $work 'NativeSafety.exe'
$sources=@(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName)
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /main:Cloud90.GameLink.GowNativeSafety "/out:$testExe" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @sources (Join-Path $PSScriptRoot 'gow-native-safety.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native safety test compilation failed' }
$process = $null
try {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=(Resolve-Path -LiteralPath $Pcsx2Exe).Path
    $info.WorkingDirectory=Split-Path $info.FileName -Parent
    $info.Arguments='-datapath "'+$work+'" -logfile "'+$log+'" -- "'+(Resolve-Path -LiteralPath $Iso).Path+'"'
    $info.UseShellExecute=$false
    $info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $process=[Diagnostics.Process]::Start($info)
    & $testExe $Game $states $log
    if ($LASTEXITCODE -ne 0) { throw "Native safety test failed; evidence: $work" }
    Write-Output "Native evidence kept at $work"
} finally {
    if ($process -and !$process.HasExited) { $process.Kill();$process.WaitForExit() }
    if ($process) { $process.Dispose() }
}
