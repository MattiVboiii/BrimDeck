@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Update-Local.ps1" %*
if errorlevel 1 pause
