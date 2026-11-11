$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testExe = Join-Path ([IO.Path]::GetTempPath()) ('cloud90-pine-test-' + [Guid]::NewGuid().ToString('N') + '.exe')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName)
try {
    & $compiler /nologo /target:exe /main:Cloud90.GameLink.PineTest "/out:$testExe" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll @sources (Join-Path $PSScriptRoot 'pine-test.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'PINE tests failed' }
} finally { if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe } }
