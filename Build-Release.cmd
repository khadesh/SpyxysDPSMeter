@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Installer-And-Move.ps1" %*
if errorlevel 1 pause
