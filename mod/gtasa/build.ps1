param([string]$VcVars = '')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (!$VcVars) {
    $finder = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $finder) {
        $VcVars = & $finder -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'VC\Auxiliary\Build\vcvars32.bat' | Select-Object -First 1
    }
    if (!$VcVars) { $VcVars = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat' }
}
if (!(Test-Path -LiteralPath $VcVars)) { throw 'Install Visual C++ x86 tools or pass -VcVars with vcvars32.bat' }
New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null
$loader = Join-Path $root 'loader\dinput8.dll'
if ((Get-FileHash -LiteralPath $loader -Algorithm SHA256).Hash.ToLowerInvariant() -ne '63b465dbc1bf9c5ab4e33c3ccad099d9edb702f2c51587ac8d4f196497396437') { throw 'Wrong pinned Ultimate ASI Loader' }
Copy-Item -LiteralPath $loader -Destination (Join-Path $root 'dist\dinput8.dll') -Force
$sources = @('Bridge.cpp', 'vendor\minhook\src\buffer.c', 'vendor\minhook\src\hook.c', 'vendor\minhook\src\trampoline.c', 'vendor\minhook\src\hde\hde32.c')
$command = 'call "' + $VcVars + '" >nul && cl /nologo /LD /MT /O2 /EHsc /std:c++17 /D_WIN32_WINNT=0x0601 ' + ($sources -join ' ') + ' /link /OUT:dist\Cloud90.GTASA.asi user32.lib advapi32.lib'
Push-Location $root
try { & cmd.exe /d /c $command; if ($LASTEXITCODE -ne 0) { throw 'GTA ASI build failed' } }
finally { Pop-Location }
