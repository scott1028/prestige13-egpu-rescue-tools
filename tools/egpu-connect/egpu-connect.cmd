@echo off
rem Launch egpu-connect.ps1; it requests admin rights by itself.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0egpu-connect.ps1" %*
