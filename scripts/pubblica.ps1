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

Write-Host "Pacchetto pronto in $Destinazione"
