param([string]$OcctRoot = "", [string]$Configuration = "Debug")
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$OcctRoot) { $OcctRoot = Join-Path (Split-Path $repoRoot -Parent) 'vcpkg_installed\x64-windows' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path $vswhere)) { throw 'Visual Studio / vswhere bulunamadi.' }
$msbuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (!$msbuildPath) { throw 'MSBuild bulunamadi.' }
$logs = Join-Path $repoRoot 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
Push-Location $repoRoot
try {
    & $msbuildPath SrcCad.sln /restore /m /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:OcctRoot=$OcctRoot" /v:m 2>&1 | Tee-Object "$logs\solid-build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Windows/WPF/OCCT derlemesi basarisiz.' }
    $appDir = Join-Path $repoRoot "src\SrcCad.App\bin\x64\$Configuration\net8.0-windows"
    $bridge = Join-Path $appDir 'SrcCad.Kernel.Occt.dll'
    if (!(Test-Path $bridge)) { throw "Kopru bulunamadi: $bridge" }
    & $msbuildPath src\SrcCad.Tests\SrcCad.Tests.csproj /restore /t:Rebuild "/p:Configuration=$Configuration" /p:Platform=x64 /p:EnableNativeAcceptance=true "/p:NativeBridgePath=$bridge" /v:m 2>&1 | Tee-Object "$logs\solid-test-build.log"
    if ($LASTEXITCODE -ne 0) { throw 'Native kabul testi derlenemedi.' }
    $testDir = Join-Path $repoRoot "src\SrcCad.Tests\bin\x64\$Configuration\net8.0"
    Copy-Item "$OcctRoot\bin\*.dll" $testDir -Force
    & "$testDir\SrcCad.Tests.exe" 2>&1 | Tee-Object "$logs\solid-acceptance.log"
    if ($LASTEXITCODE -ne 0) { throw 'Kabul testleri basarisiz.' }
    Write-Host '72 otomatik kontrol gecti. SM-073..120 arayuz/feature kabul kontrolleri ayrica uygulanmalidir.'
} finally { Pop-Location }
