param(
    [ValidateSet('Release','Debug')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$logs = Join-Path $repoRoot 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$log = Join-Path $logs 'run-srcad.log'

function Write-RunLog([string]$message) {
    $line = ('[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $message)
    Write-Host $line
    Add-Content -LiteralPath $log -Value $line
}

try {
    Write-RunLog "SrcCad $Configuration baslatma islemi."

    Get-Process SrcCad.App -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    $drive = [IO.Path]::GetPathRoot($repoRoot)

    $occtDevCandidates = @(
        $env:SRCCAD_OCCT_DEV,
        (Join-Path $drive 'SrcCadV11\vcpkg_installed\vcpkg\pkgs\opencascade_x64-windows'),
        (Join-Path (Split-Path $repoRoot -Parent) 'vcpkg_installed\vcpkg\pkgs\opencascade_x64-windows')
    ) | Where-Object { $_ -and $_.Trim() }

    $occtDev = $occtDevCandidates | Where-Object {
        (Test-Path -LiteralPath (Join-Path $_ 'include\opencascade\Standard_Version.hxx')) -and
        (Test-Path -LiteralPath (Join-Path $_ 'lib\TKernel.lib'))
    } | Select-Object -First 1

    if (!$occtDev) {
        throw 'OCCT header/lib paketi bulunamadi. SRCCAD_OCCT_DEV ortam degiskenini ayarlayin.'
    }

    $runtimeCandidates = @(
        $env:SRCCAD_OCCT_RUNTIME,
        (Join-Path $drive 'SrcCadV11\vcpkg_installed\x64-windows\bin'),
        (Join-Path $occtDev 'bin')
    ) | Where-Object { $_ -and $_.Trim() }

    $occtRuntime = $runtimeCandidates | Where-Object {
        Test-Path -LiteralPath (Join-Path $_ 'TKernel.dll')
    } | Select-Object -First 1

    if (!$occtRuntime) {
        throw 'OCCT runtime DLL klasoru bulunamadi. SRCCAD_OCCT_RUNTIME ortam degiskenini ayarlayin.'
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (!(Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio vswhere.exe bulunamadi.' }

    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
    if (!$msbuild) { throw 'MSBuild bulunamadi.' }

    Write-RunLog "OCCT DEV: $occtDev"
    Write-RunLog "OCCT RUNTIME: $occtRuntime"
    Write-RunLog "MSBuild: $msbuild"

    Push-Location $repoRoot
    try {
        & $msbuild SrcCad.sln /restore /m /t:Build             "/p:Configuration=$Configuration"             /p:Platform=x64             "/p:OcctRoot=$occtDev"             "/p:OcctRuntimeDir=$occtRuntime"             /p:RestoreIgnoreFailedSources=true             /v:m 2>&1 | Tee-Object -FilePath $log -Append
        if ($LASTEXITCODE -ne 0) { throw "MSBuild basarisiz. ExitCode=$LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }

    $appDir = Join-Path $repoRoot "src\SrcCad.App\bin\x64\$Configuration\net8.0-windows"
    $appExe = Join-Path $appDir 'SrcCad.App.exe'
    $nativeDir = Join-Path $repoRoot "x64\$Configuration"

    if (!(Test-Path -LiteralPath $appExe)) { throw "SrcCad.App.exe bulunamadi: $appExe" }

    # Runtime: vcpkg ana runtime seti + paket DLL'leri.
    Copy-Item -Path (Join-Path $occtRuntime '*.dll') -Destination $appDir -Force
    $packageBin = Join-Path $occtDev 'bin'
    if (Test-Path -LiteralPath $packageBin) {
        Copy-Item -Path (Join-Path $packageBin '*.dll') -Destination $appDir -Force
    }

    # C++/CLI bridge ve ijwhost uygulama yaninda kesin olarak dursun.
    foreach ($name in @('SrcCad.Kernel.Occt.dll','ijwhost.dll')) {
        $source = Join-Path $nativeDir $name
        if (Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination $appDir -Force
        }
    }

    # Release CRT'yi lokal koyarak farkli Windows makinelerinde acilisi daha deterministik yap.
    if ($Configuration -eq 'Release') {
        $vsInstall = & $vswhere -latest -products '*' -property installationPath
        if ($vsInstall) {
            $redistRoot = Join-Path $vsInstall 'VC\Redist\MSVC'
            if (Test-Path -LiteralPath $redistRoot) {
                $crt = Get-ChildItem -LiteralPath $redistRoot -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending |
                    ForEach-Object {
                        Get-ChildItem -LiteralPath $_.FullName -Directory -Recurse -ErrorAction SilentlyContinue |
                            Where-Object { $_.FullName -match '\\x64\\Microsoft\.VC\d+\.CRT$' }
                    } | Select-Object -First 1
                if ($crt) {
                    Copy-Item -Path (Join-Path $crt.FullName '*.dll') -Destination $appDir -Force -ErrorAction SilentlyContinue
                }
            }
        }
    }

    Write-RunLog "SrcCad aciliyor: $appExe"
    Start-Process -FilePath $appExe -WorkingDirectory $appDir
    exit 0
}
catch {
    Write-RunLog ("HATA: " + $_.Exception.Message)
    throw
}
