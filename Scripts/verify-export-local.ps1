param(
    [string]$BaseUrl = "http://localhost:5282"
)

$ErrorActionPreference = "Stop"
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession

$connStr = "Server=localhost;Port=3306;Database=ebookpublications;User=root;Password=Root@1234;SslMode=Preferred;AllowPublicKeyRetrieval=True;"
$dll = Get-ChildItem "$env:USERPROFILE\.nuget\packages\mysqlconnector\*\lib\net8.0\MySqlConnector.dll" |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $dll) { throw "MySqlConnector.dll not found in NuGet cache." }
Add-Type -Path $dll.FullName

$conn = New-Object MySqlConnector.MySqlConnection($connStr)
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT u.UserEmail, u.Password, b.BookId FROM users u JOIN books b ON b.UserId = u.UserId JOIN chapters c ON c.BookId = b.BookId WHERE c.Content IS NOT NULL AND LENGTH(TRIM(c.Content)) > 10 GROUP BY u.UserId, b.BookId ORDER BY b.BookId DESC LIMIT 1"
$reader = $cmd.ExecuteReader()
if (-not $reader.Read()) { throw "No book with chapter content in database." }
$Email = $reader["UserEmail"].ToString()
$Password = $reader["Password"].ToString()
$bookId = [int]$reader["BookId"]
$reader.Close()
$conn.Close()

Write-Host "Using bookId=$bookId email=$Email"

$loginBody = @{ UserEmail = $Email; Password = $Password; RememberMe = "false" }
Invoke-WebRequest -Uri "$BaseUrl/Account/UserLogin" -Method POST -Body $loginBody -WebSession $session | Out-Null

function Get-DownloadBytes {
    param([string]$Url, [hashtable]$Payload)
    $json = $Payload | ConvertTo-Json -Compress
    $r = Invoke-WebRequest -Uri $Url -Method POST -Body $json -ContentType "application/json" -WebSession $session
    $ms = New-Object System.IO.MemoryStream
    $r.RawContentStream.CopyTo($ms)
    return $ms.ToArray()
}

$payload = @{
    bookId = $bookId
    displayTitle = "Verify Export"
    displayAuthor = "Test Author"
    displayGenre = "Fiction"
    interiorStyle = "Classic"
    textSize = "Large"
    lineSpacing = "1.8"
    bookFormat = "Ebook"
}

Write-Host "Testing PDF download..."
$pdfBytes = Get-DownloadBytes "$BaseUrl/Dashboard/DownloadBookPdf" $payload
$pdfOk = ($pdfBytes.Length -gt 128 -and $pdfBytes[0] -eq 0x25 -and $pdfBytes[1] -eq 0x50)
Write-Host "PDF: bytes=$($pdfBytes.Length) valid=$pdfOk"

Write-Host "Testing EPUB download..."
$epubBytes = Get-DownloadBytes "$BaseUrl/Books/ExportEpub" $payload
$epubOk = ($epubBytes.Length -gt 80 -and $epubBytes[0] -eq 0x50 -and $epubBytes[1] -eq 0x4B)
Write-Host "EPUB: bytes=$($epubBytes.Length) valid=$epubOk"

$outDir = Join-Path $env:TEMP "ebook-verify-$bookId"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
[IO.File]::WriteAllBytes("$outDir\verify.pdf", $pdfBytes)
[IO.File]::WriteAllBytes("$outDir\verify.epub", $epubBytes)

$preview = Invoke-WebRequest -Uri "$BaseUrl/BookDesign/PreviewBookHtml?bookId=$bookId" -WebSession $session
Write-Host "Preview HTML length: $($preview.Content.Length)"

Write-Host "OUTDIR=$outDir"
Write-Host "BOOKID=$bookId"
Write-Host "PDF_OK=$pdfOk"
Write-Host "EPUB_OK=$epubOk"
if (-not ($pdfOk -and $epubOk)) { exit 1 }
