param(
    [ValidateSet('Debug','Release','All')][string]$Configuration = 'All',
    [switch]$RunInteraction
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$logs = Join-Path $repoRoot 'logs'
New-Item -ItemType Directory -Force $logs | Out-Null
$masterLog = Join-Path $logs 'verify-all.log'
Remove-Item -LiteralPath $masterLog -Force -ErrorAction SilentlyContinue

function Log([string]$message) {
    $line = ('[{0:yyyy-MM-dd HH:mm:ss}] {1}' -f (Get-Date), $message)
    Write-Host $line
    Add-Content -LiteralPath $masterLog -Value $line
}

function Require-Path([string]$path,[string]$message) {
    if (!(Test-Path -LiteralPath $path)) { throw "$message : $path" }
}

try {
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
    if (!$occtDev) { throw 'OCCT DEV paketi bulunamadi. SRCCAD_OCCT_DEV ayarlayin.' }

    $runtimeCandidates = @(
        $env:SRCCAD_OCCT_RUNTIME,
        (Join-Path $drive 'SrcCadV11\vcpkg_installed\x64-windows\bin'),
        (Join-Path $occtDev 'bin')
    ) | Where-Object { $_ -and $_.Trim() }

    $occtRuntime = $runtimeCandidates | Where-Object {
        Test-Path -LiteralPath (Join-Path $_ 'TKernel.dll')
    } | Select-Object -First 1
    if (!$occtRuntime) { throw 'OCCT runtime bulunamadi. SRCCAD_OCCT_RUNTIME ayarlayin.' }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    Require-Path $vswhere 'Visual Studio vswhere bulunamadi'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
    if (!$msbuild) { throw 'MSBuild bulunamadi.' }

    Log "MSBuild: $msbuild"
    Log "OCCT DEV: $occtDev"
    Log "OCCT RUNTIME: $occtRuntime"

    $configurations = if ($Configuration -eq 'All') { @('Debug','Release') } else { @($Configuration) }

    foreach ($config in $configurations) {
        Log "=== $config x64 solution build ==="
        Push-Location $repoRoot
        try {
            & $msbuild SrcCad.sln /restore /m /t:Build                 "/p:Configuration=$config" /p:Platform=x64                 "/p:OcctRoot=$occtDev" "/p:OcctRuntimeDir=$occtRuntime"                 /p:RestoreIgnoreFailedSources=true /v:m 2>&1 |
                Tee-Object -FilePath $masterLog -Append
            if ($LASTEXITCODE -ne 0) { throw "$config solution build failed. ExitCode=$LASTEXITCODE" }
        }
        finally { Pop-Location }

        $managedExe = Join-Path $repoRoot "src\SrcCad.Tests\bin\x64\$config\net8.0\SrcCad.Tests.exe"
        Require-Path $managedExe "$config managed test executable missing"
        Log "=== $config managed regression ==="
        & $managedExe 2>&1 | Tee-Object -FilePath $masterLog -Append
        if ($LASTEXITCODE -ne 0) { throw "$config managed regression failed. ExitCode=$LASTEXITCODE" }

        $nativeBridge = Join-Path $repoRoot "x64\$config\SrcCad.Kernel.Occt.dll"
        $ijwHost = Join-Path $repoRoot "x64\$config\ijwhost.dll"
        Require-Path $nativeBridge "$config C++/CLI bridge missing"
        Require-Path $ijwHost "$config ijwhost missing"

        $manifest = Join-Path $logs ("native-test-" + $config.ToLowerInvariant() + ".manifest")
        Remove-Item -LiteralPath $manifest -Force -ErrorAction SilentlyContinue

        Log "=== $config native acceptance build ==="
        Push-Location $repoRoot
        try {
            & $msbuild src\SrcCad.Tests\SrcCad.Tests.csproj /restore /m /t:Build                 "/p:Configuration=$config" /p:Platform=x64                 /p:EnableNativeAcceptance=true                 "/p:NativeBridgePath=$nativeBridge"                 "/p:NativeIjwHostPath=$ijwHost"                 "/p:NativeOcctRuntimeDir=$occtRuntime"                 "/p:NativeTestDeploymentManifest=$manifest"                 /p:RestoreIgnoreFailedSources=true /v:m 2>&1 |
                Tee-Object -FilePath $masterLog -Append
            if ($LASTEXITCODE -ne 0) { throw "$config native acceptance build failed. ExitCode=$LASTEXITCODE" }
        }
        finally { Pop-Location }

        Require-Path $manifest "$config native test manifest missing"
        $nativeExe = (Get-Content -LiteralPath $manifest | Select-Object -Last 1).Trim()
        Require-Path $nativeExe "$config native test executable missing"

        Log "=== $config native OCCT acceptance ==="
        Push-Location (Split-Path $nativeExe -Parent)
        try {
            & $nativeExe 2>&1 | Tee-Object -FilePath $masterLog -Append
            if ($LASTEXITCODE -ne 0) { throw "$config native OCCT acceptance failed. ExitCode=$LASTEXITCODE" }
        }
        finally { Pop-Location }

        Log "${config}: BUILD + MANAGED + NATIVE PASS"
    }

    if ($RunInteraction) {
        $acceptance = Join-Path $PSScriptRoot 'Run-SolidAcceptance.ps1'
        Require-Path $acceptance 'Windows interaction acceptance script missing'
        $occtRoot = Split-Path $occtRuntime -Parent
        Log '=== Release Windows UI interaction acceptance ==='
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $acceptance -OcctRoot $occtRoot -Configuration Release -RunInteraction 2>&1 |
            Tee-Object -FilePath $masterLog -Append
        if ($LASTEXITCODE -ne 0) { throw "Windows UI interaction acceptance failed. ExitCode=$LASTEXITCODE" }

        $uiReport = Join-Path $logs 'windows-interaction.json'
        Require-Path $uiReport 'Windows interaction report missing'
        $report = Get-Content -LiteralPath $uiReport -Raw | ConvertFrom-Json
        if (!$report.Completed -or $report.Failed -ne 0) {
            throw 'Windows interaction regression did not complete cleanly.'
        }
        Log ("Windows interaction PASS: " + $report.Passed)
    }

    Log 'VERIFY ALL PASS'
    exit 0
}
catch {
    Log ("VERIFY ALL FAIL: " + $_.Exception.Message)
    exit 1
}
