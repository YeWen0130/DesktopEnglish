@echo off
chcp 65001 >nul
title Uninstall DesktopEnglish
rem Standalone uninstaller entry. All logic lives in uninstall.ps1; this file only launches it.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
echo.
if /i "%~1"=="/y" exit /b 0
pause