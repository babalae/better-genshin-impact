cd /d %~dp0
if exist dist rd /s /q dist
mkdir dist\BetterGI

@echo [prepare compiler]
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -property installationPath`) do set "path=%path%;%%i\MSBuild\Current\Bin;%%i\Common7\IDE"

@echo [prepare version]
cd /d ..\BetterGenshinImpact\Core\Config
set "script=Get-Content 'Global.cs' ^| Select-String -Pattern 'Version.*\"(.*)\"' ^| ForEach-Object { $_.Matches.Groups[1].Value }"

for /f "usebackq delims=" %%i in (`powershell -NoLogo -NoProfile -Command ^"%script%^"`) do set version=%%i

echo currnet version is %version%

if "%b%"=="" (
   set "b=%version%"
)

set "tmpfolder=%~dp0dist\BetterGI"
set "archiveFile=BetterGI_v%b%.7z"
set "setupFile=BetterGI_Setup_v%b%.exe"

echo [build app using vs2022]
cd /d %~dp0
rd /s /q ..\BetterGenshinImpact\bin\x64\Release\net7.0-windows10.0.22621.0\publish\win-x64\
cd ..\
dotnet publish -c Release -p:PublishProfile=FolderProfile

echo [pack app using 7z]
cd /d %~dp0
cd /d ..\BetterGenshinImpact\bin\x64\Release\net7.0-windows10.0.22621.0\publish\win-x64\
xcopy * "%tmpfolder%" /E /C /I /Y
cd /d %~dp0
del /f /q %tmpfolder%\*.lib
del /f /q %tmpfolder%\*ffmpeg*.dll
MicaSetup.Tools\7-Zip\7z a publish.7z %tmpfolder%\ -t7z -mx=5 -mf=BCJ2 -r -y
copy /y publish.7z .\MicaSetup\Resources\Setups\publish.7z
if exist "%zipFile%" ( del /f /q "%zipfile%" )
rename publish.7z %archiveFile%

@echo [build uninst using vs2022]
msbuild MicaSetup\MicaSetup.Uninst.csproj /t:Rebuild /p:Configuration=Release /p:DeployOnBuild=true /p:PublishProfile=FolderProfile /restore

@echo [build setup using vs2022]
copy /y .\MicaSetup\bin\Release\net472\MicaSetup.exe .\MicaSetup\Resources\Setups\Uninst.exe
msbuild MicaSetup\MicaSetup.csproj /t:Build /p:Configuration=Release /p:DeployOnBuild=true /p:PublishProfile=FolderProfile /restore

@echo [finish]
del /f /q MicaSetup.exe
copy /y .\MicaSetup\bin\Release\net472\MicaSetup.exe .\
rename MicaSetup.exe %setupFile%
rd /s /q dist\BetterGI

