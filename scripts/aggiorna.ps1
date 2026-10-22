# Aggiornamento guidato di Corico (prompt strategico, sez. 5.3).
#  1. verifica che il pacchetto abbia superato i test automatici (scheda rilascio.json creata da pubblica.ps1);
#  2. arresta Corico ed esegue una copia di sicurezza verificata del database;
#  3. chiede l'approvazione esplicita;
#  4. installa la nuova versione e applica le migrazioni del database;
#  5. esegue i controlli successivi (integrità, apertura dati, ricalcolo commesse, versioni ufficiali) e l'avvio;
#  6. in caso di errore ripristina automaticamente programma e dati precedenti.
# La cartella dati non viene mai sostituita: si ripristina solo il database dalla copia di sicurezza.
#
# Uso:  powershell -ExecutionPolicy Bypass -File scripts\aggiorna.ps1 -Pacchetto publish\Corico
# Prova del ritorno alla versione precedente (ambiente di prova):  ... -SimulaErrore
param(
    [Parameter(Mandatory = $true)][string]$Pacchetto,
    [string]$CartellaProgramma = "$env:LOCALAPPDATA\Programs\Corico",
    [switch]$SimulaErrore
)

$ErrorActionPreference = "Stop"
$Pacchetto = (Resolve-Path $Pacchetto).Path
$precedente = "$CartellaProgramma.precedente"
$suWindows = $env:OS -eq "Windows_NT"
$eseguibile = if ($suWindows) { "Corico.Web.exe" } else { "Corico.Web" }   # altri sistemi: solo per le prove

function Leggi-Json($file) { Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json }

function Arresta-Corico {
    Get-Process -Name "Corico.Web" -ErrorAction SilentlyContinue | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(10000)) { Stop-Process -Id $_.Id -Force }
    }
    Start-Sleep -Seconds 1
}

function Avvia-Corico {
    $file = Join-Path $CartellaProgramma $eseguibile
    if ($suWindows) { Start-Process -FilePath $file -WorkingDirectory $CartellaProgramma -WindowStyle Hidden }
    else { Start-Process -FilePath $file -WorkingDirectory $CartellaProgramma -RedirectStandardOutput (Join-Path $cartellaDati "Log/console.txt") }
}

function Attendi-Avvio($porta) {
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:$porta/" -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) { return $true }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

# Esegue un comando "--aggiornamento" di Corico.Web.exe nella cartella indicata e ne restituisce uscita e codice.
function Comando-Aggiornamento($cartella, [string[]]$argomenti) {
    Push-Location $cartella
    # In Windows PowerShell 5.1 le righe su stderr di un programma esterno diventerebbero errori bloccanti.
    $ErrorActionPreference = "Continue"
    try {
        $env:Corico__CartellaDati = $cartellaDati
        $righe = & (Join-Path $cartella $eseguibile) --aggiornamento @argomenti 2>&1 | ForEach-Object { "$_" }
        return @{ Righe = $righe; Codice = $LASTEXITCODE }
    } finally {
        Remove-Item Env:\Corico__CartellaDati -ErrorAction SilentlyContinue
        Pop-Location
    }
}

# ---------------------------------------------------------------- 1. controlli preliminari
$scheda = Join-Path $Pacchetto "rilascio.json"
if (-not (Test-Path $scheda)) { throw "Il pacchetto non contiene rilascio.json: crealo con scripts\pubblica.ps1 (che esegue i test)." }
$rilascio = Leggi-Json $scheda
if (-not $rilascio.TestSuperati) { throw "Il pacchetto non ha superato i test automatici: aggiornamento bloccato." }

$impostazioni = Join-Path $CartellaProgramma "appsettings.json"
if (-not (Test-Path $impostazioni)) { throw "Corico non risulta installato in $CartellaProgramma. Usa scripts\installa.ps1." }
$config = Leggi-Json $impostazioni
$cartellaDati = $config.Corico.CartellaDati
$porta = $config.Corico.Porta
$database = Join-Path $cartellaDati "Dati\corico.db"
$installata = "non indicata"
$schedaInstallata = Join-Path $CartellaProgramma "rilascio.json"
if (Test-Path $schedaInstallata) { $v = Leggi-Json $schedaInstallata; $installata = "$($v.Versione) ($($v.Commit), $($v.Data))" }

Write-Host ""
Write-Host "Versione installata : $installata"
Write-Host "Nuova versione      : $($rilascio.Versione) ($($rilascio.Commit), $($rilascio.Data))"
Write-Host "Cartella dati       : $cartellaDati"
Write-Host ""

# ---------------------------------------------------------------- 2. arresto e copia di sicurezza
Write-Host "Arresto di Corico..."
Arresta-Corico
Write-Host "Copia di sicurezza del database..."
$esito = Comando-Aggiornamento $Pacchetto @("copia-sicurezza")
$esito.Righe | ForEach-Object { Write-Host "  $_" }
$copia = ($esito.Righe | Where-Object { $_ -like "COPIA=*" } | Select-Object -First 1)
if ($esito.Codice -ne 0 -or -not $copia) {
    Avvia-Corico
    throw "Copia di sicurezza non riuscita o non verificata: aggiornamento annullato, Corico riavviato con la versione installata."
}
$copia = $copia.Substring(6)

# ---------------------------------------------------------------- 3. approvazione
$risposta = Read-Host "Installare la nuova versione? Scrivi S per confermare"
if ($risposta -ne "S" -and $risposta -ne "s") {
    Avvia-Corico
    Write-Host "Aggiornamento annullato. Corico riavviato con la versione installata."
    exit 0
}

# ---------------------------------------------------------------- 4. installazione
Write-Host "Installazione..."
if (Test-Path $precedente) { Remove-Item $precedente -Recurse -Force }
Rename-Item -Path $CartellaProgramma -NewName (Split-Path $precedente -Leaf)
New-Item -ItemType Directory -Force -Path $CartellaProgramma | Out-Null
Copy-Item -Path (Join-Path $Pacchetto "*") -Destination $CartellaProgramma -Recurse -Force
Copy-Item -Path (Join-Path $precedente "appsettings.json") -Destination $impostazioni -Force

function Ritorna-Alla-Precedente($motivo) {
    Write-Host ""
    Write-Host "ERRORE: $motivo" -ForegroundColor Red
    Write-Host "Ritorno alla versione precedente..."
    Arresta-Corico
    Remove-Item $CartellaProgramma -Recurse -Force
    Rename-Item -Path $precedente -NewName (Split-Path $CartellaProgramma -Leaf)
    if ($copia) {
        foreach ($f in @("$database-wal", "$database-shm")) { if (Test-Path $f) { Remove-Item $f -Force } }
        Copy-Item -Path $copia -Destination $database -Force
        Write-Host "Database ripristinato dalla copia $copia"
    }
    Avvia-Corico
    if (Attendi-Avvio $porta) { Write-Host "Corico riavviato con la versione precedente: nessun dato perso." }
    else { Write-Host "ATTENZIONE: la versione precedente non risponde. Contattare l'assistenza." -ForegroundColor Red }
    exit 1
}

# ---------------------------------------------------------------- 5. migrazioni e controlli
Write-Host "Migrazioni e controlli..."
$argomenti = @("verifica")
if ($copia) { $argomenti += @("--copia", $copia) }
if ($SimulaErrore) { $argomenti += "--simula-errore" }
try { $esito = Comando-Aggiornamento $CartellaProgramma $argomenti }
catch { Ritorna-Alla-Precedente "controlli non eseguiti: $($_.Exception.Message)" }
$esito.Righe | ForEach-Object { Write-Host "  $_" }
if ($esito.Codice -ne 0) { Ritorna-Alla-Precedente "controlli dopo l'aggiornamento non superati." }

Avvia-Corico
if (-not (Attendi-Avvio $porta)) { Ritorna-Alla-Precedente "la nuova versione non si avvia." }

Write-Host ""
Write-Host "Aggiornamento completato: Corico $($rilascio.Versione) in uso su http://127.0.0.1:$porta/" -ForegroundColor Green
Write-Host "La versione precedente resta in $precedente fino al prossimo aggiornamento."
