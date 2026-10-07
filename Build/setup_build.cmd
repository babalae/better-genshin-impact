cd /d %~dp0
if exist dist rd /s /q dist
mkdir dist\BetterGI

@echo [prepare compiler]
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath`) do if not "%%i"=="" set "path=%path%;%%i\MSBuild\Current\Bin;%%i\Common7\IDE"

@echo [prepare version]
cd /d ..\BetterGenshinImpact
set "script=Select-String -Path 'BetterGenshinImpact.csproj' -Pattern 'Version\>(.*)\<\/Version' | ForEach-Object { $_.Matches.Groups[1].Value }"
for /f "usebackq delims=" %%i in (`powershell -NoLogo -NoProfile -Command "%script%"`) do set version=%%i
echo current version is %version%
if "%b%"=="" ( set "b=%version%" )

set "tmpfolder=%~dp0dist\BetterGI"
set "archiveFile=BetterGI_v%b%.7z"
set "setupFile=BetterGI_Setup_v%b%.exe"

echo [build app using vs2022]
cd /d %~dp0
if exist "..\BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64" rd /s /q "..\BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64"
dotnet publish "%~dp0..\BetterGenshinImpact\BetterGenshinImpact.csproj" -c Release -p:PublishProfile="%~dp0..\BetterGenshinImpact\Properties\PublishProfiles\FolderProfile.pubxml"
if errorlevel 1 (
    echo [ERROR] dotnet publish failed. Packaging was skipped.
    exit /b 1
)

if not exist "..\BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64\BetterGI.exe" (
    echo [ERROR] Published executable was not found. Packaging was skipped.
    exit /b 1
)

echo [pack app using 7z]
cd /d %~dp0
cd /d ..\BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64\
if errorlevel 1 exit /b 1
xcopy * "%tmpfolder%" /E /C /I /Y
if errorlevel 1 exit /b 1
cd /d %~dp0
del /f /q %tmpfolder%\*.lib
del /f /q %tmpfolder%\*ffmpeg*.dll

:: 添加一些配置文件开始（大文件不适合放在Github）
if exist "E:\HuiTask\BetterGIBuild\BetterGI" (
    xcopy "E:\HuiTask\BetterGIBuild\BetterGI\*" "%tmpfolder%" /E /C /I /Y
)
:: 添加一些配置文件结束

if exist "publish.7z" del /f /q "publish.7z"
MicaSetup.Tools\7-Zip\7z a publish.7z %tmpfolder%\* -t7z -mx=5 -mf=BCJ2 -r -y
if errorlevel 1 exit /b 1
move /y "publish.7z" "%archiveFile%"
if errorlevel 1 exit /b 1

rd /s /q dist\BetterGI

@pause
