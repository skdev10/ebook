#!/usr/bin/env bash
# Download Google Fonts TTF files for PDFsharp embedded export (run once on dev or server).
set -euo pipefail
DEST="${1:-$(dirname "$0")/../wwwroot/fonts/pdf}"
mkdir -p "$DEST"
BASE="https://github.com/google/fonts/raw/main"

download() {
  local path="$1"
  local out="$2"
  echo "-> $out"
  curl -fsSL "$BASE/$path" -o "$DEST/$out"
}

download "ofl/merriweather/Merriweather%5Bwght%5D.ttf" "Merriweather-Regular.ttf" || \
  download "ofl/merriweather/Merriweather-Regular.ttf" "Merriweather-Regular.ttf"
download "ofl/merriweather/Merriweather-Bold.ttf" "Merriweather-Bold.ttf" 2>/dev/null || true
download "ofl/playfairdisplay/PlayfairDisplay%5Bwght%5D.ttf" "PlayfairDisplay-Regular.ttf" 2>/dev/null || true
download "ofl/playfairdisplay/static/PlayfairDisplay-Bold.ttf" "PlayfairDisplay-Bold.ttf" 2>/dev/null || true
download "ofl/inter/static/Inter-Regular.ttf" "Inter-Regular.ttf"
download "ofl/inter/static/Inter-Bold.ttf" "Inter-Bold.ttf"
download "ofl/cormorantgaramond/CormorantGaramond-Regular.ttf" "CormorantGaramond-Regular.ttf"
download "ofl/cormorantgaramond/CormorantGaramond-Bold.ttf" "CormorantGaramond-Bold.ttf"
download "ofl/cormorantgaramond/CormorantGaramond-Italic.ttf" "CormorantGaramond-Italic.ttf"
download "ofl/ebgaramond/static/EBGaramond-Regular.ttf" "EBGaramond-Regular.ttf"
download "ofl/ebgaramond/static/EBGaramond-Bold.ttf" "EBGaramond-Bold.ttf"
download "ofl/lora/static/Lora-Regular.ttf" "Lora-Regular.ttf"
download "ofl/lora/static/Lora-Bold.ttf" "Lora-Bold.ttf"

echo "Fonts saved to $DEST"
ls -la "$DEST"
