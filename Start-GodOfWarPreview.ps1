$ErrorActionPreference = 'Stop'
$preview = Join-Path $PSScriptRoot 'dist\Cloud90GameLink-GowPreview.exe'
if (!(Test-Path -LiteralPath $preview)) { throw 'Build the preview first with build-gow-preview.ps1' }
$env:CLOUD90_GOW_EXPERIMENTAL = '1'
Start-Process -FilePath $preview -WorkingDirectory $PSScriptRoot
