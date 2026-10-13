@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo SrcCad tam Windows dogrulamasi basliyor...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Verify-All.ps1" -Configuration All
if errorlevel 1 (
  echo.
  echo [HATA] Tam dogrulama basarisiz.
  echo Log: logs\verify-all.log
  pause
  exit /b 1
)

echo.
echo [PASS] Debug + Release build, managed regresyon ve native OCCT kabul testleri gecti.
pause
exit /b 0
