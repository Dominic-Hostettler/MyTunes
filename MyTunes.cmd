@echo off
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File "%~dp0MyTunes.ps1"
if errorlevel 1 pause
pause
