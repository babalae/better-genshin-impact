@echo off
echo Running Task Settings Module Tests...
powershell -ExecutionPolicy Bypass -File "%~dp0RunTaskSettingsTests.ps1"
echo.
echo Tests completed. Press any key to exit.
pause > nul