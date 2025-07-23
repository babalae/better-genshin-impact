@echo off
echo Running Detailed Data Binding and Localization Tests...
powershell -ExecutionPolicy Bypass -File "%~dp0DetailedDataBindingTests.ps1"
echo.
echo Tests completed. Press any key to exit.
pause > nul