@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-DroneLogger.ps1"
set "result=%ERRORLEVEL%"
if not "%result%"=="0" (
  echo.
  echo Update failed. Review the message above.
  pause
)
exit /b %result%

