$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeAcceptanceDeployment.ps1')
$root = Join-Path ([IO.Path]::GetTempPath()) ('srccad deployment fixture ' + [Guid]::NewGuid())
$source = Join-Path $root 'source'
$target = Join-Path $root 'target with spaces'
New-Item -ItemType Directory -Path $source,$target -Force | Out-Null
$script:passed = 0
function Fixture([string]$path, [UInt16]$machine=0x8664) {
    # Minimal PE header fixture for file/path validation ONLY. Never executed or loaded.
    $bytes = New-Object byte[] 256
    [BitConverter]::GetBytes([UInt16]0x5A4D).CopyTo($bytes,0)
    [BitConverter]::GetBytes([Int32]128).CopyTo($bytes,60)
    [BitConverter]::GetBytes([UInt32]0x4550).CopyTo($bytes,128)
    [BitConverter]::GetBytes($machine).CopyTo($bytes,132)
    [IO.File]::WriteAllBytes($path,$bytes)
}
function Check([string]$name,[scriptblock]$action,[string]$errorPattern='') {
    $caught = $null
    try { & $action } catch { $caught = $_.Exception.Message }
    if ($errorPattern) {
        if (!$caught -or $caught -notlike $errorPattern) { throw "FAIL $name expected=$errorPattern actual=$caught" }
    } elseif ($caught) { throw "FAIL $name : $caught" }
    $script:passed++
    Write-Output "DEPLOYMENT PASS $name"
}
try {
    $names = @('SrcCad.Kernel.Occt','ijwhost','TKernel','TKMath','TKG2d','TKG3d','TKGeomBase','TKGeomAlgo','TKBRep','TKTopAlgo','TKPrim','TKService','TKV3d','TKOpenGl')
    foreach ($name in $names) { Fixture (Join-Path $source "$name.dll") }
    Copy-Item "$source/*.dll" $target
    $bridge = Join-Path $source 'SrcCad.Kernel.Occt.dll'
    $hostPath = Join-Path $source 'ijwhost.dll'
    $log = Join-Path $root 'deployment.log'
    $validate = { Assert-NativeAcceptanceDeployment -TestDirectory $target -BridgePath $bridge -IjwHostPath $hostPath -OcctBin $source -LogPath $log }
    Check 'complete x64 deployment with spaces' $validate
    Check 'manifest lists all runtime files' { if (@(Get-Content $log).Count -ne 14) { throw 'Incomplete manifest' } }
    Remove-Item (Join-Path $target 'SrcCad.Kernel.Occt.dll')
    Check 'missing bridge rejected' $validate '*Native DLL missing*SrcCad.Kernel.Occt.dll*'
    Copy-Item $bridge $target
    Remove-Item (Join-Path $target 'ijwhost.dll')
    Check 'missing ijwhost rejected' $validate '*Native DLL missing*ijwhost.dll*'
    Copy-Item $hostPath $target
    Fixture (Join-Path $target 'TKMath.dll') 0x14c
    Check 'x86 dependency rejected' $validate '*not Windows x64*TKMath.dll*'
    Copy-Item (Join-Path $source 'TKMath.dll') $target -Force
    $stale = Join-Path $target 'TKernel.dll'
    $bytes = [IO.File]::ReadAllBytes($stale); $bytes[200]=1; [IO.File]::WriteAllBytes($stale,$bytes)
    Check 'stale dependency rejected' $validate '*stale or from a different build*TKernel.dll*'
    Copy-Item (Join-Path $source 'TKernel.dll') $target -Force
    [IO.File]::WriteAllText((Join-Path $target 'ijwhost.dll'),'invalid')
    Check 'corrupt host rejected' $validate '*Not a PE file*ijwhost.dll*'
    Copy-Item $hostPath $target -Force
    Remove-Item (Join-Path $source 'TKOpenGl.dll')
    Check 'missing source dependency rejected' $validate '*Native DLL missing*TKOpenGl.dll*'
    Write-Output "DEPLOYMENT INFRASTRUCTURE: $script:passed/8 PASS (filesystem fixtures; NOT native B-Rep tests)"
} finally { Remove-Item -LiteralPath $root -Recurse -Force }
