@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if not exist logs mkdir logs

set "MSBUILD=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
if not exist "%MSBUILD%" (
  set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
  if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
  )
)

if not exist "%MSBUILD%" (
  echo [HATA] MSBuild bulunamadi. Visual Studio C++/.NET kurulumunu kontrol edin.
  pause
  exit /b 2
)

echo SrcCad V1.4 Usable Part Alpha - restore + derleme basliyor...
"%MSBUILD%" SrcCad.sln /restore /m /t:Build /p:Configuration=Debug /p:Platform=x64 /p:RestoreIgnoreFailedSources=true /v:m /fl /flp:logfile=logs\build.log;verbosity=normal
if errorlevel 1 (
  echo.
  echo [HATA] Derleme basarisiz. logs\build.log olusturuldu.
  echo COLLECT_DIAGNOSTICS.bat dosyasini calistirip olusan ZIP'i gonderebilirsiniz.
  pause
  exit /b 1
)

set "TEST=%~dp0src\SrcCad.Tests\bin\x64\Debug\net8.0-windows\SrcCad.Tests.exe"
if not exist "%TEST%" (
  echo [HATA] SrcCad.Tests.exe bulunamadi.
  pause
  exit /b 4
)

echo SrcCad cekirdek smoke testleri calistiriliyor...
"%TEST%" > logs\smoke-test.log 2>&1
if errorlevel 1 (
  echo.
  echo [HATA] Cekirdek smoke testi basarisiz. logs\smoke-test.log olusturuldu.
  echo COLLECT_DIAGNOSTICS.bat calistirip olusan ZIP'i gonderebilirsiniz.
  pause
  exit /b 5
)

set "APP=%~dp0src\SrcCad.App\bin\x64\Debug\net8.0-windows\SrcCad.App.exe"
if not exist "%APP%" (
  echo [HATA] SrcCad.App.exe bulunamadi.
  pause
  exit /b 3
)

start "SrcCad" "%APP%"
exit /b 0
