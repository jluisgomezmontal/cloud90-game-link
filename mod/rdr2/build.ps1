param(
    [Parameter(Mandatory)][string]$SdkPath,
    [string]$VcVars
)

$ErrorActionPreference = 'Stop'
$sdk = (Resolve-Path -LiteralPath $SdkPath).Path
$include = Join-Path $sdk 'inc'
$library = Join-Path $sdk 'lib\ScriptHookRDR2.lib'
if (!(Test-Path -LiteralPath (Join-Path $include 'main.h')) -or !(Test-Path -LiteralPath $library)) {
    throw 'Use the extracted Script Hook RDR2 SDK folder with inc\main.h and lib\ScriptHookRDR2.lib.'
}

if (!$VcVars) {
    $finder = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $finder) {
        $VcVars = & $finder -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC\Auxiliary\Build\vcvars64.bat' | Select-Object -First 1
    }
}
if (!$VcVars -or !(Test-Path -LiteralPath $VcVars)) {
    throw 'Install the Visual C++ x64 tools or pass -VcVars with the path to vcvars64.bat.'
}

$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Push-Location -LiteralPath $dist
try {
    $command = 'call "' + $VcVars + '" >nul && cl /nologo /LD /MT /O2 /EHsc /std:c++20 /I"' +
        $include + '" ..\Diagnostics.cpp /link /OUT:Cloud90.RDR2.Diagnostics.asi "' + $library +
        '" shell32.lib ole32.lib uuid.lib'
    & cmd.exe /d /c $command
    if ($LASTEXITCODE -ne 0) { throw 'RDR2 diagnostic build failed.' }
}
finally {
    Pop-Location
}
