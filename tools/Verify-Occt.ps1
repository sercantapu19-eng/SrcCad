$ErrorActionPreference = "Stop"
$solutionRoot = Split-Path -Parent $PSScriptRoot
$vcpkgRoot = Join-Path (Split-Path -Parent $solutionRoot) "vcpkg_installed\x64-windows"
$header = Join-Path $vcpkgRoot "include\opencascade\TopoDS_Shape.hxx"
$lib = Join-Path $vcpkgRoot "lib\TKernel.lib"
$dll = Join-Path $vcpkgRoot "bin\TKernel.dll"

Write-Host "SrcCad V1.1 Foundation - OCCT kontrolu" -ForegroundColor Cyan
Write-Host ("Header : {0}" -f (Test-Path $header))
Write-Host ("Lib    : {0}" -f (Test-Path $lib))
Write-Host ("DLL    : {0}" -f (Test-Path $dll))
Write-Host "Kok: $vcpkgRoot"

if ((Test-Path $header) -and (Test-Path $lib) -and (Test-Path $dll)) {
  Write-Host "OCCT yolu hazir." -ForegroundColor Green
  exit 0
}
Write-Host "OCCT dosyalarindan biri eksik." -ForegroundColor Red
exit 1
