@echo off
setlocal
set F=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
if not exist MyTunes.exe goto build
for %%i in (MyTunes.cs) do set SRC=%%~ti
for %%i in (MyTunes.exe) do set EXE=%%~ti
rem Neu bauen, wenn Quelle juenger als exe
for /f %%i in ('powershell -NoProfile -Command "(New-Item MyTunes.cs).LastWriteTimeUtc -gt (New-Item MyTunes.exe).LastWriteTimeUtc"') do if "%%i"=="True" goto build
goto run
:build
"%F%\csc.exe" -nologo -target:winexe -out:MyTunes.exe ^
 -r:"%F%\WPF\PresentationFramework.dll" -r:"%F%\WPF\PresentationCore.dll" -r:"%F%\WPF\WindowsBase.dll" ^
 -r:"%F%\System.Xaml.dll" -r:"%F%\Microsoft.CSharp.dll" -r:"%F%\Microsoft.VisualBasic.dll" -r:"%F%\System.dll" MyTunes.cs
if errorlevel 1 pause & exit /b 1
:run
start "" MyTunes.exe
