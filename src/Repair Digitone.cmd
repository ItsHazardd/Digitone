@echo off
title Repair Digitone
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Repair-Digitone.ps1" %*
if errorlevel 9009 pause
