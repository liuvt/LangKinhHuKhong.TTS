@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
title LangKinhHuKhong.TTS v1.1.0 - Build and Run
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scripts\Build-Windows.ps1" -Runtime win-x64
if errorlevel 1 (
    echo.
    echo Build that bai. Xem thong bao loi phia tren.
    pause
    exit /b 1
)
if not exist "%~dp0Publish\win-x64\LangKinhHuKhong.TTS.exe" (
    echo Khong tim thay EXE sau khi build.
    pause
    exit /b 1
)
echo Dang khoi chay LangKinhHuKhong.TTS v1.1.0...
start "LangKinhHuKhong.TTS" "%~dp0Publish\win-x64\LangKinhHuKhong.TTS.exe"
exit /b 0
