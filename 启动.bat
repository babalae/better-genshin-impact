@echo off
title BetterGI 一键启动

echo ================================
echo   BetterGI - 更好的原神
echo ================================
echo.

echo [1/2] 编译中...
dotnet build BetterGenshinImpact\BetterGenshinImpact.csproj -c Debug -v q
if %errorlevel% neq 0 (
    echo.
    echo [X] 编译失败，请检查错误信息
    pause
    exit /b 1
)
echo [OK] 编译成功

echo [2/2] 启动中...
start "" "BetterGenshinImpact\bin\Debug\net8.0-windows10.0.22621.0\BetterGI.exe"
explorer /select,"BetterGenshinImpact\bin\Debug\net8.0-windows10.0.22621.0\BetterGI.exe"

echo [OK] BetterGI 已启动，已打开文件目录
timeout /t 2 >nul
exit
