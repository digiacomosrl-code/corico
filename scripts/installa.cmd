@echo off
rem Installa Corico con doppio clic: cartella dati D:\CORICO, oppure C:\CORICO se il disco D: non esiste.
set DATI=D:\CORICO
if not exist D:\ set DATI=C:\CORICO
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0installa.ps1" -Pacchetto "%~dp0Corico" -CartellaDati "%DATI%"
pause
