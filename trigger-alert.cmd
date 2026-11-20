@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Trigger-Alert.ps1" %*
if errorlevel 1 pause
