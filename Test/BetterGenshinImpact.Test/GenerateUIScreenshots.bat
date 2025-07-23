@echo off
echo Generating UI Screenshots for Comparison...
powershell -ExecutionPolicy Bypass -File "%~dp0UIScreenshotGenerator.ps1"
echo.
echo Screenshot generation completed. Press any key to exit.
pause > nul