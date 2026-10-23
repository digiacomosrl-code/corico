# Rende Corico raggiungibile dagli altri uffici attraverso Tailscale Serve (sez. 3.1 del prompt strategico).
# Corico continua ad ascoltare solo su 127.0.0.1: Tailscale lo pubblica in HTTPS sul nome del PC
# nella propria rete privata (https://<nome-pc>.<rete>.ts.net), visibile solo ai dispositivi approvati.
# Nessuna porta aperta sul router o sul firewall, nessuna pubblicazione su internet (Funnel non viene usato).
# Uso:  powershell -ExecutionPolicy Bypass -File pubblica-tailscale.ps1            (attiva)
#       powershell -ExecutionPolicy Bypass -File pubblica-tailscale.ps1 -Disattiva (disattiva)
param(
    [int]$Porta = 0,
    [switch]$Disattiva
)

$ErrorActionPreference = "Stop"

$tailscale = Join-Path $env:ProgramFiles "Tailscale\tailscale.exe"
if (-not (Test-Path $tailscale)) { $tailscale = "tailscale" }

if ($Disattiva) {
    & $tailscale serve --https=443 off
    Write-Host "Pubblicazione disattivata: Corico resta utilizzabile solo da questo PC."
    exit 0
}

# Porta di Corico: quella configurata in appsettings.json, altrimenti 5080.
if ($Porta -eq 0) {
    $Porta = 5080
    $impostazioni = Join-Path "$env:LOCALAPPDATA\Programs\Corico" "appsettings.json"
    if (Test-Path $impostazioni) {
        $config = Get-Content $impostazioni -Raw | ConvertFrom-Json
        if ($config.Corico.Porta) { $Porta = [int]$config.Corico.Porta }
    }
}

& $tailscale serve --bg --https=443 "http://127.0.0.1:$Porta"
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Tailscale non ha attivato la pubblicazione. Se sopra compare un indirizzo login.tailscale.com,"
    Write-Host "aprilo per abilitare i certificati HTTPS della rete, poi esegui di nuovo questo script."
    exit 1
}

Write-Host ""
& $tailscale serve status
Write-Host ""
Write-Host "Fatto. I colleghi collegati a Tailscale aprono Corico all'indirizzo https://... indicato sopra."
Write-Host "La pubblicazione resta attiva anche dopo il riavvio del PC; per toglierla: pubblica-tailscale.cmd disattiva"
