@ECHO OFF
chcp 65001


REM 正则获取当前版本号
cd /d ..\BetterGenshinImpact\Core\Config
set "script=Get-Content 'Global.cs' ^| Select-String -Pattern 'Version.*\"(.*)\"' ^| ForEach-Object { $_.Matches.Groups[1].Value }"

for /f "usebackq delims=" %%i in (`powershell -NoLogo -NoProfile -Command ^"%script%^"`) do set version=%%i

echo 文件内版本号为： %version%

REM 检查是否接收到参数b
if "%~1"=="" (
    set /p "b=请输入自定义版本号（直接回车使用文件内版本号）："
) else (
    set "b=%~1"
)

if "%b%"=="" (
   set "b=%version%"
)

set "tmpfolder=%~dp0\BetterGI.v%b%"
set "zipFile=%~dp0\BetterGI.v%b%.zip"


echo 输入版本号为：%tmpfolder%
echo 目标压缩包为：%zipFile%

cd /d %~dp0
cd /d ..\BetterGenshinImpact\bin\x64\Release\net7.0-windows10.0.22621.0

xcopy * "%tmpfolder%" /E /C /I /Y

rd /s /q "%tmpfolder%\log"
rd /s /q "%tmpfolder%\runtimes\android"
rd /s /q "%tmpfolder%\runtimes\ios"
rd /s /q "%tmpfolder%\runtimes\linux-arm64"
rd /s /q "%tmpfolder%\runtimes\linux-x64"
rd /s /q "%tmpfolder%\runtimes\osx-arm64"
rd /s /q "%tmpfolder%\runtimes\osx-x64"
rd /s /q "%tmpfolder%\runtimes\win-arm"
rd /s /q "%tmpfolder%\runtimes\win-arm64"
rd /s /q "%tmpfolder%\runtimes\win-x86"
del /s /f /q "%tmpfolder%\runtimes\win-x64\native\opencv_videoio_ffmpeg480_64.dll"
del /s /f /q "%tmpfolder%\User\config.json"

IF EXIST "%zipFile%" ( del /f /s /q "%zipfile%" )

@ECHO ON
powershell -nologo -noprofile -command "Compress-Archive -Path "%tmpfolder%" -DestinationPath "%zipFile%""

rd /s /q %tmpfolder%
pause