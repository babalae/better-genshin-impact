cd /d %~dp0
if exist dist rd /s /q dist
mkdir dist\BetterGI

@echo [prepare compiler]
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath`) do set "path=%path%;%%i\MSBuild\Current\Bin;%%i\Common7\IDE"

@echo [prepare version]
cd /d ..\BetterGenshinImpact
set "script=Get-Content 'BetterGenshinImpact.csproj' | Select-String -Pattern 'AssemblyVersion\>(.*)\<\/AssemblyVersion' | ForEach-Object { $_.Matches.Groups[1].Value }"
for /f "usebackq delims=" %%i in (`powershell -NoLogo -NoProfile -Command "%script%"`) do set version=%%i
echo current version is %version%
if "%b%"=="" ( set "b=%version%" )

set "tmpfolder=%~dp0dist\BetterGI"
set "archiveFile=BetterGI_v%b%.7z"
set "setupFile=BetterGI_Setup_v%b%.exe"

echo [build app using vs2022]
cd /d %~dp0
rd /s /q ..\BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64\
cd ..\
dotnet publish -c Release -p:PublishProfile=FolderProfile

echo [pack app using 7z]
cd /d %~dp0
cd /d ..\BetterGenshinImpact\bin\x64\Release\net8.0-windows10.0.22621.0\publish\win-x64\
xcopy * "%tmpfolder%" /E /C /I /Y
cd /d %~dp0
del /f /q %tmpfolder%\*.lib
del /f /q %tmpfolder%\*ffmpeg*.dll
MicaSetup.Tools\7-Zip\7z a publish.7z %tmpfolder%\* -t7z -mx=5 -mf=BCJ2 -r -y
if exist "%zipFile%" ( del /f /q "%zipfile%" )
rename publish.7z %archiveFile%

rd /s /q dist\BetterGI

