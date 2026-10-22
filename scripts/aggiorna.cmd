@echo off
rem Aggiornamento guidato con doppio clic (vedi aggiorna.ps1).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0aggiorna.ps1" -Pacchetto "%~dp0Corico" %*
pause
