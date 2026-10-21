# Installa Corico sul PC: copia il programma, imposta l'avvio automatico all'accesso
# dell'utente e crea il collegamento sul desktop.
# Uso:  powershell -ExecutionPolicy Bypass -File scripts\installa.ps1 -Pacchetto publish\Corico -CartellaDati D:\CORICO
param(
    [Parameter(Mandatory = $true)][string]$Pacchetto,
    [string]$CartellaProgramma = "$env:LOCALAPPDATA\Programs\Corico",
    [string]$CartellaDati = "D:\CORICO",
    [int]$Porta = 5080
)

$ErrorActionPreference = "Stop"

# 1. Copia del programma (la cartella dati non viene mai toccata).
New-Item -ItemType Directory -Force -Path $CartellaProgramma | Out-Null
$impostazioniEsistenti = Join-Path $CartellaProgramma "appsettings.json"
$salvate = $null
if (Test-Path $impostazioniEsistenti) { $salvate = Get-Content $impostazioniEsistenti -Raw }
Copy-Item -Path (Join-Path $Pacchetto "*") -Destination $CartellaProgramma -Recurse -Force
if ($salvate) { Set-Content -Path $impostazioniEsistenti -Value $salvate -Encoding UTF8 }

# 2. Configurazione della cartella dati e della porta (solo alla prima installazione).
if (-not $salvate) {
    $config = Get-Content $impostazioniEsistenti -Raw | ConvertFrom-Json
    $config.Corico.CartellaDati = $CartellaDati
    $config.Corico.Porta = $Porta
    $config | ConvertTo-Json -Depth 10 | Set-Content -Path $impostazioniEsistenti -Encoding UTF8
}
New-Item -ItemType Directory -Force -Path $CartellaDati | Out-Null

# 3. Avvio automatico all'accesso dell'utente, senza finestra.
$shell = New-Object -ComObject WScript.Shell
$esecuzioneAutomatica = Join-Path ([Environment]::GetFolderPath("Startup")) "Corico.lnk"
$avvio = $shell.CreateShortcut($esecuzioneAutomatica)
$avvio.TargetPath = Join-Path $CartellaProgramma "Corico.Web.exe"
$avvio.WorkingDirectory = $CartellaProgramma
$avvio.WindowStyle = 7
$avvio.Save()

# 4. Collegamento sul desktop che apre il browser.
$desktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "Corico.url"
Set-Content -Path $desktop -Value "[InternetShortcut]`r`nURL=http://127.0.0.1:$Porta/" -Encoding ASCII

# 5. Avvio immediato.
Start-Process -FilePath (Join-Path $CartellaProgramma "Corico.Web.exe") -WorkingDirectory $CartellaProgramma -WindowStyle Hidden
Write-Host "Corico installato. Apri il collegamento 'Corico' sul desktop oppure http://127.0.0.1:$Porta/"
