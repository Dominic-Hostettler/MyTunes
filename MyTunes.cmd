@echo off
rem Buildet MyTunes.exe falls noetig und startet es
if not exist "%~dp0MyTunes.exe" (
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -target:winexe -out:"%~dp0MyTunes.exe" -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:Microsoft.CSharp.dll -r:Microsoft.VisualBasic.dll "%~dp0MyTunes.cs"
    if errorlevel 1 pause
)
start "" "%~dp0MyTunes.exe"
