@REM @echo off
set "target1=..\..\AutoPathing"
set "target2=..\..\pathing"

if exist "%target1%" (
    mklink /j pathing "%target1%"
) else if exist "%target2%" (
    mklink /j pathing "%target2%"
) else (
    echo ERROR: Can't find folder "%target1%" or "%target2%"
    pause
)
