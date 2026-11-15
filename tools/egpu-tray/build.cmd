@echo off
rem Build dist\egpu-tray.exe with the C# compiler built into .NET Framework 4.x.
setlocal
cd /d "%~dp0"
set "OUT_DIR=..\..\dist"
if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /win32manifest:app.manifest /resource:..\egpu-connect\egpu-connect.ps1,egpu-connect.ps1 /reference:System.Management.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /out:"%OUT_DIR%\egpu-tray.exe" src\*.cs
exit /b %errorlevel%
