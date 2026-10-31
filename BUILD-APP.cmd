@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\Build-Windows.ps1" %*
if errorlevel 1 (
  echo.
  echo BUILD THAT BAI. Xem ma loi chi tiet phia tren.
  pause
  exit /b 1
)
echo.
echo Da xuat ra .exe trong Publish\win-x64
pause
