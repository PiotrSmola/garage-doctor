$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$raw = "data/raw"
$spec = "data/spec"
New-Item -ItemType Directory -Path $raw, $spec -Force | Out-Null

$files = @(
    @{ Url = "https://static.nhtsa.gov/odi/ffdd/cmpl/FLAT_CMPL.zip";           Dest = "$raw/FLAT_CMPL.zip" },
    @{ Url = "https://static.nhtsa.gov/odi/ffdd/rcl/FLAT_RCL_POST_2010.zip";  Dest = "$raw/FLAT_RCL_POST_2010.zip" },
    @{ Url = "https://static.nhtsa.gov/odi/ffdd/rcl/FLAT_RCL_PRE_2010.zip";   Dest = "$raw/FLAT_RCL_PRE_2010.zip" },
    @{ Url = "https://static.nhtsa.gov/odi/ffdd/cmpl/CMPL.txt";               Dest = "$spec/CMPL.txt" },
    @{ Url = "https://static.nhtsa.gov/odi/ffdd/rcl/RCL.txt";                 Dest = "$spec/RCL.txt" }
)

foreach ($f in $files) {
    if (Test-Path $f.Dest) {
        Write-Host ("[skip] " + $f.Dest) -ForegroundColor Yellow
        continue
    }
    Write-Host ("[..]   pobieranie " + $f.Url) -ForegroundColor Cyan
    Invoke-WebRequest -Uri $f.Url -OutFile $f.Dest -UseBasicParsing
    $sizeMb = [math]::Round((Get-Item $f.Dest).Length / 1MB, 1)
    Write-Host ("[ok]   " + $f.Dest + "  " + $sizeMb + " MB") -ForegroundColor Green
}

$archives = @("FLAT_CMPL.zip", "FLAT_RCL_POST_2010.zip", "FLAT_RCL_PRE_2010.zip")
foreach ($name in $archives) {
    $zip = Join-Path $raw $name
    if (Test-Path $zip) { Expand-Archive -Path $zip -DestinationPath $raw -Force }
}
Write-Host "[ok]   rozpakowano" -ForegroundColor Green

$cmpl = "$raw/FLAT_CMPL.txt"
if (-not (Test-Path $cmpl)) { Write-Host "[FAIL] brak FLAT_CMPL.txt" -ForegroundColor Red; exit 1 }

$lines = 0
$reader = [System.IO.File]::OpenText((Resolve-Path $cmpl).Path)
try { while ($null -ne $reader.ReadLine()) { $lines++ } } finally { $reader.Close() }

$cmplMb = [math]::Round((Get-Item $cmpl).Length / 1MB, 1)

$rclPost = "$raw/FLAT_RCL_POST_2010.txt"
$rclPre  = "$raw/FLAT_RCL_PRE_2010.txt"
$postLines = 0
$preLines = 0
if (Test-Path $rclPost) {
    $r = [System.IO.File]::OpenText((Resolve-Path $rclPost).Path)
    try { while ($null -ne $r.ReadLine()) { $postLines++ } } finally { $r.Close() }
}
if (Test-Path $rclPre) {
    $r = [System.IO.File]::OpenText((Resolve-Path $rclPre).Path)
    try { while ($null -ne $r.ReadLine()) { $preLines++ } } finally { $r.Close() }
}

Write-Host ""
Write-Host ("FLAT_CMPL.txt           : " + $lines + "  (ref. 2235299)") -ForegroundColor Cyan
Write-Host ("FLAT_RCL_POST_2010.txt  : " + $postLines + "  (ref. 244499)") -ForegroundColor Cyan
Write-Host ("FLAT_RCL_PRE_2010.txt   : " + $preLines + "  (ref. 81715)") -ForegroundColor Cyan
Write-Host ""

$manifest = @(
    "# Data manifest",
    "",
    "Dane NIE sa przechowywane w tym repozytorium. Ten plik dokumentuje zbior,",
    "na ktorym projekt byl budowany, zeby wyniki byly odtwarzalne.",
    "",
    "Zrodlo:   NHTSA Office of Defects Investigation",
    "          https://static.nhtsa.gov/odi/ffdd/",
    "Licencja: us-pd (domena publiczna, bez warunkow uzycia)",
    "",
    ("Pobrano: " + (Get-Date -Format "yyyy-MM-dd")),
    "",
    "## FLAT_CMPL.txt - zgloszenia usterek",
    "",
    ("Rozmiar:       " + $cmplMb + " MB"),
    ("Wierszy:       " + $lines + "   (referencja: 2235299)"),
    "Pol w wierszu: 51 (TAB-delimited, bez naglowka)",
    "Konce linii:   LF",
    "",
    "## FLAT_RCL_POST_2010.txt - akcje serwisowe od 2010",
    "",
    ("Wierszy:       " + $postLines + "   (referencja: 244499)"),
    "Pol w wierszu: 29 (TAB-delimited, bez naglowka)",
    "Konce linii:   CRLF",
    "",
    "## FLAT_RCL_PRE_2010.txt - akcje serwisowe 1967-2009",
    "",
    ("Wierszy:       " + $preLines + "   (referencja: 81715, w tym 5 pustych)"),
    "",
    "Pliki sa aktualizowane codziennie, wiec liczby beda rosly.",
    "",
    "## Jak odtworzyc",
    "",
    "    make data",
    "",
    "## Dlaczego danych nie ma w repo",
    "",
    "Pole CDESCR zawiera swobodny tekst pisany przez wlascicieli pojazdow.",
    "Mimo ze zbior jest w domenie publicznej, narracje regularnie zawieraja",
    "nazwiska, adresy i numery telefonu wpisane przez zglaszajacych.",
    "Redystrybucja tych rekordow oznaczalaby publikowanie cudzych danych osobowych.",
    "",
    "Testy korzystaja z syntetycznych fixtures w tests/fixtures/ - pisanych recznie."
)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText(
    (Join-Path (Get-Location).Path "data/MANIFEST.md"),
    (($manifest -join "`n") + "`n"),
    $utf8NoBom)

Write-Host "[ok]   data/MANIFEST.md zaktualizowany (ten plik commitujesz)" -ForegroundColor Green
Write-Host "       Dane surowe zostaja lokalnie - data/raw/ jest w .gitignore." -ForegroundColor Yellow
