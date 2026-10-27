param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [Parameter(Mandatory=$true)][string]$BepInExDir,
    [Parameter(Mandatory=$true)][string]$JsonDll
)
$ErrorActionPreference = 'Stop'
if ((Get-FileHash -LiteralPath $JsonDll -Algorithm SHA256).Hash.ToLowerInvariant() -ne '0f89b82c6b76816b17286951ba83201c71b6611f3e75e907552247dfc72034f2') { throw 'Requires pinned Newtonsoft.Json 13.0.3 net20' }
$managed = Join-Path $GameDir 'Cuphead_Data\Managed'
$core = Join-Path $BepInExDir 'BepInEx\core'
$out = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $out | Out-Null
$refs = @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.InputModule.dll','UnityEngine.JSONSerializeModule.dll','UnityEngine.Physics2DModule.dll','Rewired_Core.dll') | ForEach-Object { '/r:' + (Join-Path $managed $_) }
$refs += @(( '/r:' + (Join-Path $core 'BepInEx.dll')), ('/r:' + (Join-Path $core '0Harmony.dll')))
$refs += '/r:' + $JsonDll
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /noconfig /target:library /nostdlib /optimize+ "/out:$out\Cloud90.Cuphead.dll" @refs (Join-Path $PSScriptRoot 'Protocol.cs') (Join-Path $PSScriptRoot 'BridgeJson.cs') (Join-Path $PSScriptRoot 'Plugin.cs') (Join-Path $PSScriptRoot 'TrollEffects.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cuphead plugin build failed' }
Get-FileHash -LiteralPath "$out\Cloud90.Cuphead.dll" -Algorithm SHA256
Copy-Item -LiteralPath $JsonDll -Destination "$out\Newtonsoft.Json.dll" -Force
