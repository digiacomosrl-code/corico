#!/usr/bin/env bash
# Crea il rilascio di Corico per Windows (64 bit) da Linux, con lo stesso contenuto di scripts\pubblica.ps1:
# test automatici, pacchetto autonomo, cartella ocr (Tesseract), scheda rilascio.json e script di installazione.
# Il risultato standard è UN SOLO FILE, Corico-<versione>-aggiorna.exe (scripts/aggiornamento.nsi):
# con un doppio clic aggiorna Corico con la procedura guidata di aggiorna.ps1, oppure lo installa se manca.
#
# Uso (dalla cartella del repository):
#   scripts/pubblica.sh [--ocr-da <Corico-x.y.z-aggiorna.exe | Corico-x.y.z-win-x64.zip>] [--uscita <cartella>] [--zip]
#     --ocr-da   prende la cartella Corico/ocr da un rilascio precedente se strumenti/ocr non esiste
#     --uscita   cartella in cui scrivere il file finale (predefinita: publish)
#     --zip      crea anche lo zip tradizionale Corico-<versione>-win-x64.zip
# Richiede: dotnet (SDK 10), makensis (pacchetto nsis), 7z (solo con --ocr-da), zip (solo con --zip).
set -euo pipefail

ocr_da=""
uscita="publish"
crea_zip=false
while [ $# -gt 0 ]; do
    case "$1" in
        --ocr-da) ocr_da="$2"; shift 2 ;;
        --uscita) uscita="$2"; shift 2 ;;
        --zip) crea_zip=true; shift ;;
        *) echo "Parametro sconosciuto: $1" >&2; exit 2 ;;
    esac
done

radice="$(cd "$(dirname "$0")/.." && pwd)"
cd "$radice"
versione="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
pacchetto="publish/pacchetto"
mkdir -p "$uscita"
uscita="$(cd "$uscita" && pwd)"

echo "Esecuzione dei test automatici..."
dotnet test --configuration Release

echo "Pubblicazione di Corico $versione ..."
rm -rf "$pacchetto"
dotnet publish src/Corico.Web/Corico.Web.csproj --configuration Release --runtime win-x64 --self-contained true \
    --output "$pacchetto/Corico"

# Lettura delle scansioni (OCR): Tesseract per Windows nella cartella "ocr" accanto a Corico.
if [ -f strumenti/ocr/tesseract.exe ]; then
    cp -r strumenti/ocr "$pacchetto/Corico/ocr"
elif [ -n "$ocr_da" ]; then
    temporanea="$(mktemp -d)"
    7z x -y -bd -o"$temporanea" "$ocr_da" >/dev/null
    origine="$(find "$temporanea" -type f -name tesseract.exe -path '*Corico/ocr/*' | head -1)"
    [ -n "$origine" ] || { echo "Nessuna cartella Corico/ocr in $ocr_da" >&2; exit 1; }
    cp -r "$(dirname "$origine")" "$pacchetto/Corico/ocr"
    rm -rf "$temporanea"
else
    echo "Tesseract non trovato: metterlo in strumenti/ocr oppure usare --ocr-da <rilascio precedente>." >&2
    exit 1
fi

# Scheda del rilascio, richiesta dall'aggiornamento guidato (aggiorna.ps1).
modifiche=false
[ -z "$(git status --porcelain)" ] || modifiche=true
cat > "$pacchetto/Corico/rilascio.json" <<EOF
{
  "Versione": "$versione",
  "Commit": "$(git rev-parse --short HEAD)",
  "ModificheNonRegistrate": $modifiche,
  "Data": "$(date -u '+%Y-%m-%dT%H:%M:%SZ')",
  "TestSuperati": true
}
EOF

# Script di installazione e aggiornamento accanto alla cartella del programma.
cp scripts/installa.* scripts/aggiorna.cmd scripts/aggiorna.ps1 scripts/pubblica-tailscale.* "$pacchetto/"

echo "Creazione del file unico di aggiornamento..."
makensis -V2 -DVERSIONE="$versione" -DORIGINE="$radice/$pacchetto" \
    -DUSCITA="$uscita/Corico-$versione-aggiorna.exe" scripts/aggiornamento.nsi

if $crea_zip; then
    rm -f "$uscita/Corico-$versione-win-x64.zip"
    (cd "$pacchetto" && zip -qr "$uscita/Corico-$versione-win-x64.zip" .)
fi

echo "Rilascio $versione pronto: $uscita/Corico-$versione-aggiorna.exe"
