@echo off
echo Checking for hardcoded strings in XAML files...
powershell -ExecutionPolicy Bypass -File "%~dp0HardcodedStringChecker.ps1"
echo.
echo Check completed. Press any key to exit.
pause > nul