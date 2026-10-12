@echo off
setlocal EnableExtensions
cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Run-SrcCad.ps1"
if errorlevel 1 (
  echo.
  echo [HATA] SrcCad baslatilamadi. logs\run-srcad.log dosyasini ChatGPT'ye gonderin.
  pause
  exit /b 1
)
exit /b 0
