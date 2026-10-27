param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$Server = '',
    [switch]$InstallerOnly,
    [string]$BuiltApp = ''
)
$ErrorActionPreference = 'Stop'
if (!$BuiltApp) { $BuiltApp = Join-Path $PSScriptRoot '..\dist\Cloud90GameLink.exe' }
if (!$InstallerOnly -and $Server -notmatch '^http://127\.0\.0\.1:[0-9]+$') { throw 'Use a loopback-only fake server' }
if (Get-Process Cuphead -ErrorAction SilentlyContinue) { throw 'Close Cuphead first' }
$work = Join-Path ([IO.Path]::GetTempPath()) ('c90-cuphead-live-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($BuiltApp))
    if ($assembly.GetManifestResourceNames() -notcontains 'Cloud90.Cuphead.dll') { throw 'Build the full Cuphead package first' }
    $resources = @()
    foreach ($name in $assembly.GetManifestResourceNames()) {
        $path = Join-Path $work $name
        $inputStream = $assembly.GetManifestResourceStream($name)
        $outputStream = [IO.File]::Create($path)
        try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
        $resources += "/resource:$path,$name"
    }
    $root = Split-Path -Parent $PSScriptRoot
    $sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) + (Join-Path $PSScriptRoot 'CupheadLive.cs')
    $exe = Join-Path $work 'CupheadLive.exe'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:Cloud90.GameLink.CupheadLive "/out:$exe" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @resources @sources
    if ($LASTEXITCODE -ne 0) { throw 'Live harness compilation failed' }
    $testMode = if ($InstallerOnly) { '--installer' } else { $Server }
    & $exe $GameDir $testMode
    if ($LASTEXITCODE -ne 0) { throw 'Live harness failed' }
}
finally {
    $cleanup = [IO.Path]::GetFullPath($work)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$cleanup.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or ![IO.Path]::GetFileName($cleanup).StartsWith('c90-cuphead-live-')) { throw 'Unsafe cleanup path' }
    Remove-Item -LiteralPath $cleanup -Recurse -Force
}
