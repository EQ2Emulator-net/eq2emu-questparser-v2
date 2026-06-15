@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Create-GitHubRelease.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%CI%"=="true" pause
exit /b %EXIT_CODE%
