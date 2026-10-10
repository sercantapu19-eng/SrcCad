$ErrorActionPreference = "SilentlyContinue"
$root = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$temp = Join-Path $env:TEMP "SrcCad_Diagnostics_$stamp"
$out = Join-Path $root "SrcCad_Diagnostics_$stamp.zip"
New-Item -ItemType Directory -Force -Path $temp | Out-Null

$info = @()
$info += "SrcCad diagnostics"
$info += "Date: $(Get-Date -Format o)"
$info += "OS: $([Environment]::OSVersion.VersionString)"
$info += "64-bit OS: $([Environment]::Is64BitOperatingSystem)"
$info += "Machine: $env:COMPUTERNAME"
$info += "OCCT header: $(Test-Path (Join-Path (Split-Path -Parent $root) 'vcpkg_installed\x64-windows\include\opencascade\TopoDS_Shape.hxx'))"
$info += "OCCT TKernel.dll: $(Test-Path (Join-Path (Split-Path -Parent $root) 'vcpkg_installed\x64-windows\bin\TKernel.dll'))"
$info | Set-Content (Join-Path $temp "environment.txt") -Encoding UTF8

if (Test-Path (Join-Path $root "logs")) { Copy-Item (Join-Path $root "logs") (Join-Path $temp "logs") -Recurse -Force }
$appLogs = Join-Path $root "src\SrcCad.App\bin\x64\Debug\net8.0-windows\logs"
if (Test-Path $appLogs) { Copy-Item $appLogs (Join-Path $temp "runtime_logs") -Recurse -Force }
Copy-Item (Join-Path $root "src\SrcCad.Kernel.Occt\*.cpp") $temp -Force
Copy-Item (Join-Path $root "src\SrcCad.Kernel.Occt\*.h") $temp -Force
Copy-Item (Join-Path $root "src\SrcCad.Kernel.Occt\*.vcxproj") $temp -Force
Copy-Item (Join-Path $root "src\SrcCad.App\*.csproj") $temp -Force
Copy-Item (Join-Path $root "SrcCad.sln") $temp -Force

if (Test-Path $out) { Remove-Item $out -Force }
Compress-Archive -Path (Join-Path $temp "*") -DestinationPath $out -Force
Remove-Item $temp -Recurse -Force
Write-Host "Olusturuldu: $out" -ForegroundColor Green
