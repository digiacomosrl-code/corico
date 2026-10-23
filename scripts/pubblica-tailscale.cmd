@echo off
rem Pubblica Corico agli altri uffici tramite Tailscale Serve. Con "disattiva" toglie la pubblicazione.
if /i "%1"=="disattiva" (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pubblica-tailscale.ps1" -Disattiva
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pubblica-tailscale.ps1"
)
pause
