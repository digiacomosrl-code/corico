@echo off
rem Aggiornamento guidato con doppio clic (vedi aggiorna.ps1).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0aggiorna.ps1" -Pacchetto "%~dp0Corico" %*
set ESITO=%ERRORLEVEL%
rem Se tutto e' andato bene la finestra si chiude da sola dopo qualche secondo; in caso di errore resta aperta.
if %ESITO%==0 (timeout /t 15) else (pause)
exit /b %ESITO%
