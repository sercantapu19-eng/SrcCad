[CmdletBinding()]
param(
    [string]$OcctTag = "V8_0_0",
    [int]$Jobs = [Environment]::ProcessorCount,
    [string]$AndroidSdk = $(if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } elseif ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { Join-Path $env:LOCALAPPDATA "Android\Sdk" })
)

$ErrorActionPreference = "Stop"
$mobileRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$thirdParty = Join-Path $mobileRoot "third_party"
$sourceDir = Join-Path $thirdParty "occt-src"
$buildDir = Join-Path $thirdParty "occt-build\android-arm64"
$installDir = Join-Path $thirdParty "occt-android\arm64-v8a"
$ndkVersion = "28.2.13676358"
$ndkDir = Join-Path $AndroidSdk "ndk\$ndkVersion"
$toolchain = Join-Path $ndkDir "build\cmake\android.toolchain.cmake"

function Require-Command([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) { throw "Required command '$Name' was not found in PATH." }
    return $command.Source
}

$git = Require-Command "git"
$cmakeCommand = Get-Command "cmake" -ErrorAction SilentlyContinue
if ($cmakeCommand) {
    $cmake = $cmakeCommand.Source
} else {
    $cmakeCandidate = Get-ChildItem (Join-Path $AndroidSdk "cmake") -Filter "cmake.exe" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $cmakeCandidate) { throw "CMake was not found. Install CMake from Android Studio SDK Manager or add cmake.exe to PATH." }
    $cmake = $cmakeCandidate.FullName
}

$ninja = Get-Command "ninja" -ErrorAction SilentlyContinue
if (-not $ninja) {
    $ninjaCandidate = Get-ChildItem (Join-Path $AndroidSdk "cmake") -Filter "ninja.exe" -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $ninjaCandidate) { throw "Ninja was not found. Install CMake from Android Studio SDK Manager or add ninja.exe to PATH." }
    $ninjaPath = $ninjaCandidate.FullName
} else {
    $ninjaPath = $ninja.Source
}

if (-not (Test-Path $toolchain)) {
    throw "Android NDK $ndkVersion was not found at '$ndkDir'. Install it in Android Studio > SDK Manager > SDK Tools > NDK (Side by side)."
}

New-Item -ItemType Directory -Force -Path $thirdParty | Out-Null
if (-not (Test-Path (Join-Path $sourceDir "CMakeLists.txt"))) {
    if (Test-Path $sourceDir) { Remove-Item -Recurse -Force $sourceDir }
    & $git clone --depth 1 --branch $OcctTag https://github.com/Open-Cascade-SAS/OCCT.git $sourceDir
    if ($LASTEXITCODE -ne 0) { throw "Could not clone OCCT tag $OcctTag." }
}

$cmakeArgs = @(
    "-S", $sourceDir,
    "-B", $buildDir,
    "-G", "Ninja",
    "-DCMAKE_MAKE_PROGRAM=$ninjaPath",
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain",
    "-DANDROID_ABI=arm64-v8a",
    "-DANDROID_PLATFORM=android-26",
    "-DANDROID_STL=c++_shared",
    "-DCMAKE_BUILD_TYPE=Release",
    "-DCMAKE_INSTALL_PREFIX=$installDir",
    "-DINSTALL_DIR_LAYOUT=Unix",
    "-DBUILD_LIBRARY_TYPE=Shared",
    "-DBUILD_CPP_STANDARD=C++17",
    "-DBUILD_ADDITIONAL_TOOLKITS=TKDESTEP;TKMesh;TKTopAlgo",
    "-DBUILD_MODULE_FoundationClasses=OFF",
    "-DBUILD_MODULE_ModelingData=OFF",
    "-DBUILD_MODULE_ModelingAlgorithms=OFF",
    "-DBUILD_MODULE_Visualization=OFF",
    "-DBUILD_MODULE_ApplicationFramework=OFF",
    "-DBUILD_MODULE_DataExchange=OFF",
    "-DBUILD_MODULE_Draw=OFF",
    "-DBUILD_USE_VCPKG=OFF",
    "-DBUILD_RELEASE_DISABLE_EXCEPTIONS=OFF",
    "-DUSE_OPENGL=OFF",
    "-DUSE_GLES2=OFF",
    "-DUSE_FREETYPE=OFF",
    "-DUSE_FREEIMAGE=OFF",
    "-DUSE_TBB=OFF",
    "-DUSE_VTK=OFF",
    "-DUSE_DRACO=OFF",
    "-DUSE_RAPIDJSON=OFF"
)

Write-Host "Configuring OCCT $OcctTag for Android arm64-v8a..."
& $cmake @cmakeArgs
if ($LASTEXITCODE -ne 0) { throw "OCCT CMake configuration failed." }

Write-Host "Building OCCT..."
& $cmake --build $buildDir --parallel $Jobs
if ($LASTEXITCODE -ne 0) { throw "OCCT build failed." }

Write-Host "Installing OCCT to $installDir ..."
& $cmake --install $buildDir
if ($LASTEXITCODE -ne 0) { throw "OCCT install failed." }

$stepHeader = Get-ChildItem $installDir -Filter "STEPControl_Reader.hxx" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
$stepLibrary = Get-ChildItem $installDir -Filter "libTKDESTEP.so" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
$topologyLibrary = Get-ChildItem $installDir -Filter "libTKTopAlgo.so" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $stepHeader -or -not $stepLibrary -or -not $topologyLibrary) {
    throw "OCCT install validation failed: STEPControl_Reader.hxx, libTKDESTEP.so or libTKTopAlgo.so is missing. Inspect the build output/toolkit list."
}

Write-Host ""
Write-Host "OCCT Android build completed."
Write-Host "Set environment variable SRCCAD_OCCT_ROOT to: $installDir"
Write-Host "The install folder is ignored by Git and must not be committed."
