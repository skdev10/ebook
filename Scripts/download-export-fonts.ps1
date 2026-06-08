$dest = Join-Path $PSScriptRoot "..\wwwroot\fonts\pdf"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$base = "https://github.com/google/fonts/raw/main"

$files = @{
    "ofl/inter/static/Inter-Regular.ttf" = "Inter-Regular.ttf"
    "ofl/inter/static/Inter-Bold.ttf" = "Inter-Bold.ttf"
    "ofl/merriweather/Merriweather-Regular.ttf" = "Merriweather-Regular.ttf"
    "ofl/playfairdisplay/static/PlayfairDisplay-Bold.ttf" = "PlayfairDisplay-Bold.ttf"
    "ofl/cormorantgaramond/CormorantGaramond-Regular.ttf" = "CormorantGaramond-Regular.ttf"
    "ofl/cormorantgaramond/CormorantGaramond-Bold.ttf" = "CormorantGaramond-Bold.ttf"
    "ofl/cormorantgaramond/CormorantGaramond-Italic.ttf" = "CormorantGaramond-Italic.ttf"
    "ofl/ebgaramond/static/EBGaramond-Regular.ttf" = "EBGaramond-Regular.ttf"
    "ofl/lora/static/Lora-Regular.ttf" = "Lora-Regular.ttf"
}

foreach ($kv in $files.GetEnumerator()) {
    $url = "$base/$($kv.Key)"
    $out = Join-Path $dest $kv.Value
    Write-Host "-> $($kv.Value)"
    Invoke-WebRequest -Uri $url -OutFile $out -UseBasicParsing
}
Write-Host "Done: $dest"
