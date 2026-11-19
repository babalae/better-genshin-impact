@echo off
title Uninstall BetterGI English Menu Patch
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Apply-EnglishPatch.ps1" -Uninstall
