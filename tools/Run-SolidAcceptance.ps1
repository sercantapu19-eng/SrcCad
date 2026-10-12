param([string]$OcctRoot = "", [ValidateSet('Debug','Release')][string]$Configuration = "Debug", [switch]$RunInteraction)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'NativeAcceptanceDeployment.ps1')
if (!$OcctRoot) { $OcctRoot = Join-Path (Split-Path $repoRoot -Parent) 'vcpkg_installed\x64-windows' }
$OcctRoot = (Resolve-Path -LiteralPath $OcctRoot).Path
# The vcxproj links OcctRoot/lib in both configurations, so use its matching release bin.
$occtBin = Join-Path $OcctRoot 'bin'
Assert-X64PeFile -Path (Join-Path $occtBin 'TKernel.dll')
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Visual Studio / vswhere bulunamadi.' }
$msbuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuildPath) { throw 'MSBuild bulunamadi.' }
$logs = Join-Path $repoRoot 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$nativeManifest = Join-Path $logs 'solid-native-paths.txt'
$testManifest = Join-Path $logs 'solid-test-path.txt'
$uiManifest = Join-Path $logs 'solid-ui-path.txt'
# Old manifests cannot masquerade as a successful current build.
Remove-Item -LiteralPath $nativeManifest, $testManifest, $uiManifest -Force -ErrorAction SilentlyContinue
$previousPath = $env:PATH
Push-Location $repoRoot
try {
    & $msbuildPath SrcCad.sln /restore /m /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:OcctRoot=$OcctRoot" "/p:OcctRuntimeDir=$occtBin" "/p:NativeDeploymentManifest=$nativeManifest" "/p:UiAcceptanceManifest=$uiManifest" /v:m 2>&1 | Tee-Object "$logs\solid-build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Windows/WPF/OCCT derlemesi basarisiz.' }
    $nativePaths = @(Get-Content -LiteralPath $nativeManifest | Where-Object { $_.Trim() })
    if ($nativePaths.Count -ne 2) { throw "Invalid native build manifest: $nativeManifest" }
    $bridge = $nativePaths[0].Trim()
    $ijwHost = $nativePaths[1].Trim()
    Assert-X64PeFile -Path $bridge
    Assert-X64PeFile -Path $ijwHost
    & $msbuildPath src\SrcCad.Tests\SrcCad.Tests.csproj /restore /t:Rebuild "/p:Configuration=$Configuration" /p:Platform=x64 /p:EnableNativeAcceptance=true "/p:NativeBridgePath=$bridge" "/p:NativeIjwHostPath=$ijwHost" "/p:NativeOcctRuntimeDir=$occtBin" "/p:NativeTestDeploymentManifest=$testManifest" /v:m 2>&1 | Tee-Object "$logs\solid-test-build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Native kabul testi derlenemedi.' }
    $testAssembly = (Get-Content -LiteralPath $testManifest -Raw).Trim()
    $testDir = Split-Path $testAssembly -Parent
    $testExe = Join-Path $testDir 'SrcCad.Tests.exe'
    if (!(Test-Path -LiteralPath $testExe)) { throw "Test executable missing: $testExe" }
    Assert-NativeAcceptanceDeployment -TestDirectory $testDir -BridgePath $bridge -IjwHostPath $ijwHost -OcctBin $occtBin -LogPath "$logs\solid-deployment.log"
    $env:PATH = "$testDir;$previousPath"
    # Capture stderr without PowerShell 5.1 converting it into a terminating
    # NativeCommandError before the full CLR loader exception can be logged.
    $stdoutLog = Join-Path $logs 'solid-stdout.log'
    $stderrLog = Join-Path $logs 'solid-stderr.log'
    $process = Start-Process -FilePath $testExe -WorkingDirectory $testDir -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
    @(Get-Content -LiteralPath $stdoutLog; Get-Content -LiteralPath $stderrLog) | Tee-Object "$logs\solid-acceptance.log"
    if ($process.ExitCode -ne 0) { throw 'Kabul testleri basarisiz; solid-acceptance.log ve solid-deployment.log dosyalarina bakin.' }
    $nativePasses = @(Select-String -Path "$logs\solid-acceptance.log" -Pattern '^SM-0(6[1-9]|7[0-2]) PASS ')
    if ($nativePasses.Count -ne 12) { throw '12 native testin tamami calismadi; sonuc kabul edilmiyor.' }
    # Extra regressions are not new SM acceptance IDs, but must not be silently skipped.
    foreach ($summary in @(
        '^FEATURE HISTORY: 12/12 PASS ',
        '^INTERACTION REGRESSION: 50 PASS ',
        '^SOURCE SKETCH EDIT: 18 PASS ',
        '^SKETCH FOUNDATION: [1-9][0-9]* PASS ',
        '^PART ROUNDTRIP: [1-9][0-9]* PASS ',
        '^WINDOW SELECTION: 18 PASS ',
        '^DIMENSION EDITING: 80 PASS ',
        '^DELETION HISTORY: 309 PASS ',
        '^FEATURE GRAPH: 42 PASS ',
        '^NATIVE ANGLE EDITING: 12/12 PASS$',
        '^NATIVE DELETION HISTORY: 9/9 PASS$',
        '^NATIVE FEATURE GRAPH: 81/81 PASS$',
        '^TRIM INTERACTION: [1-9][0-9]* PASS ',
        '^PARALLEL DIMENSIONS: [1-9][0-9]* PASS ',
        '^GENERAL CONSTRAINTS: [1-9][0-9]* PASS ',
        '^THREE POINT TOOLS: [1-9][0-9]* PASS ',
        '^PERFORMANCE SMOKE: 2/2 PASS ',
        '^NATIVE THREE POINT: 9/9 PASS$',
        '^NATIVE PARALLEL DIMENSIONS: 9/9 PASS$',
        '^NATIVE REFERENCE ROUNDTRIP: 12/12 PASS$',
        '^NATIVE SOURCE SKETCH REGRESSION: 12/12 PASS '
    )) {
        if (@(Select-String -Path "$logs\solid-acceptance.log" -Pattern $summary).Count -ne 1) {
            throw "Ek regresyon paketi eksik veya tekrarlandi: $summary"
        }
    }
    if ($RunInteraction) {
        $uiExe=(Get-Content -LiteralPath $uiManifest -Raw).Trim()
        if (!(Test-Path -LiteralPath $uiExe)) { throw "Built UI executable missing: $uiExe" }
        $uiDir=Split-Path $uiExe -Parent
        # Deploy the exact bridge/host used by this build before testing UI events.
        foreach ($source in @($bridge,$ijwHost)) {
            $dest=Join-Path $uiDir (Split-Path $source -Leaf)
            if ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($dest)) { Copy-Item -LiteralPath $source -Destination $dest -Force }
        }
        Assert-NativeAcceptanceDeployment -TestDirectory $uiDir -BridgePath $bridge -IjwHostPath $ijwHost -OcctBin $occtBin -LogPath "$logs\solid-ui-deployment.log"
        $env:PATH="$uiDir;$previousPath"
        & (Join-Path $PSScriptRoot 'Run-WindowsInteraction.ps1') -AppPath $uiExe -ReportPath (Join-Path $logs 'windows-interaction.json')
    } else {
        Write-Host 'Windows UI interaction NOT RUN. Use -RunInteraction on an interactive desktop.'
    }
    Write-Host '72 kabul kontrolu ve ek regresyon paketleri gecti. SM-073..120 ve Windows manuel akisi ayrica dogrulanmalidir; Kati Model tamamlandi sayilmaz.'
} finally {
    $env:PATH = $previousPath
    Pop-Location
}
