# Crea il pacchetto di installazione di Corico per Windows (64 bit), autonomo:
# sul PC di destinazione non serve installare .NET.
# Uso (dalla cartella del repository):  powershell -ExecutionPolicy Bypass -File scripts\pubblica.ps1
param(
    [string]$Destinazione = "publish\Corico"
)

$ErrorActionPreference = "Stop"

Write-Host "Esecuzione dei test automatici..."
dotnet test --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Test non superati: pubblicazione annullata." }

Write-Host "Pubblicazione in $Destinazione ..."
dotnet publish src\Corico.Web\Corico.Web.csproj --configuration Release --runtime win-x64 --self-contained true --output $Destinazione
if ($LASTEXITCODE -ne 0) { throw "Pubblicazione non riuscita." }

# Scheda del rilascio, richiesta dall'aggiornamento guidato (scripts\aggiorna.ps1).
$versione = (Select-Xml -Path "Directory.Build.props" -XPath "//Version").Node.InnerText
$commit = (git rev-parse --short HEAD)
$modifiche = (git status --porcelain)
[ordered]@{
    Versione      = $versione
    Commit        = $commit
    ModificheNonRegistrate = [bool]$modifiche
    Data          = (Get-Date).ToString("yyyy-MM-dd HH:mm")
    TestSuperati  = $true
} | ConvertTo-Json | Set-Content -Path (Join-Path $Destinazione "rilascio.json") -Encoding UTF8

# Script di installazione e aggiornamento accanto alla cartella del programma (avviabili con doppio clic).
Copy-Item -Path "scripts\installa.*", "scripts\aggiorna.*", "scripts\pubblica-tailscale.*" -Destination (Split-Path $Destinazione -Parent)

Write-Host "Pacchetto $versione ($commit) pronto in $Destinazione"
