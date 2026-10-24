@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-DeploymentShare.ps1"
set "result=%ERRORLEVEL%"
echo.
if not "%result%"=="0" echo Setup failed. Review the message above.
pause
exit /b %result%

