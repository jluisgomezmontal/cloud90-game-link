$ErrorActionPreference = 'Stop'
$finder = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vcvars = & $finder -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC\Auxiliary\Build\vcvars64.bat' | Select-Object -First 1
if (!$vcvars) { throw 'Install the Visual C++ x64 build tools.' }

$dist = Join-Path $PSScriptRoot '../../dist/tlou2-native'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Push-Location -LiteralPath $dist
try {
    $source = Join-Path $PSScriptRoot 'Probe.cpp'
    & cmd.exe /d /c ('call "' + $vcvars + '" >nul && cl /nologo /LD /MT /W4 /WX /O2 /EHsc /std:c++20 "' + $source + '" /link /OUT:Cloud90.Tlou2.asi bcrypt.lib')
    if ($LASTEXITCODE -ne 0) { throw 'TLOU2 native probe build failed.' }
} finally {
    Pop-Location
}
