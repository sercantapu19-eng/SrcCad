@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Building Open CASCADE for Android arm64-v8a...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build_occt_android.ps1"
if errorlevel 1 (
  echo.
  echo [ERROR] OCCT Android build failed. Read the CMake output above.
  exit /b 1
)

set "SRCCAD_OCCT_ROOT=%~dp0third_party\occt-android\arm64-v8a"

echo.
echo [2/2] Building SrcCad Mobile debug APK...
call "%~dp0gradlew.bat" :app:assembleDebug
if errorlevel 1 (
  echo.
  echo [ERROR] Android APK build failed.
  exit /b 2
)

echo.
echo [SUCCESS] Debug APK created:
echo %~dp0app\build\outputs\apk\debug\app-debug.apk
exit /b 0
