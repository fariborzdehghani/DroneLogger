@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-For-Test.ps1"
set "result=%ERRORLEVEL%"
echo.
if not "%result%"=="0" echo Publish failed. Review the message above.
pause
exit /b %result%

