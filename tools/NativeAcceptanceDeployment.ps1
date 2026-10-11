# Shared preflight; also tested with filesystem fixtures. Does not load native code.
function Assert-X64PeFile {
    param([Parameter(Mandatory=$true)][string]$Path)
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Native DLL missing: $Path" }
    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE file: $Path" }
        $stream.Position = 0x3C
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $stream.Length - 6) { throw "Invalid PE header: $Path" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Native DLL is not Windows x64: $Path" }
    } finally { $reader.Dispose() }
}

function Assert-NativeAcceptanceDeployment {
    param([Parameter(Mandatory=$true)][string]$TestDirectory,
          [Parameter(Mandatory=$true)][string]$BridgePath,
          [Parameter(Mandatory=$true)][string]$IjwHostPath,
          [Parameter(Mandatory=$true)][string]$OcctBin,
          [Parameter(Mandatory=$true)][string]$LogPath)
    # Match the production link libraries; all transitive vcpkg DLLs are copied by MSBuild too.
    $required = @('TKernel','TKMath','TKG2d','TKG3d','TKGeomBase','TKGeomAlgo','TKBRep','TKTopAlgo','TKPrim','TKService','TKV3d','TKOpenGl')
    $sources = @($BridgePath, $IjwHostPath)
    foreach ($name in $required) { $sources += (Join-Path $OcctBin "$name.dll") }
    foreach ($source in $sources) {
        Assert-X64PeFile -Path $source
        $destination = Join-Path $TestDirectory (Split-Path $source -Leaf)
        Assert-X64PeFile -Path $destination
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash) {
            throw "Native DLL is stale or from a different build: $destination"
        }
    }
    Get-ChildItem -LiteralPath $TestDirectory -Filter '*.dll' | Sort-Object Name | ForEach-Object {
        '{0} {1} {2}' -f $_.Name, $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    } | Set-Content -LiteralPath $LogPath -Encoding UTF8
}
