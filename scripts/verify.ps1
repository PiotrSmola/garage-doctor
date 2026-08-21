$ErrorActionPreference = "Continue"
$ProgressPreference = "SilentlyContinue"
$script:failed = 0

function Test-Item {
    param([string]$Label, [scriptblock]$Check)
    try {
        & $Check *>$null
        if ($LASTEXITCODE -eq 0 -or $null -eq $LASTEXITCODE) {
            Write-Host "  [ok]   $Label" -ForegroundColor Green
            return
        }
    } catch { }
    Write-Host "  [FAIL] $Label" -ForegroundColor Red
    $script:failed++
}

function Test-Http {
    param([string]$Url)
    try {
        Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 6 | Out-Null
        $global:LASTEXITCODE = 0
    } catch {
        $global:LASTEXITCODE = 1
    }
}

Write-Host "Weryfikacja srodowiska:"

Test-Item "mongo dziala"           { docker compose ps --status running mongo }
Test-Item "mongo odpowiada"        { docker compose exec -T mongo mongosh --quiet --eval "db.adminCommand({ping:1}).ok" }
Test-Item "redis odpowiada"        { docker compose exec -T redis redis-cli ping }
Test-Item "mongo-express na 8091"  { Test-Http "http://127.0.0.1:8091" }
Test-Item "dotnet w kontenerze"    { docker compose run --rm --no-deps app dotnet --version }
Test-Item "solucja sie buduje"     { docker compose run --rm --no-deps app dotnet build GarageDoctor.sln --nologo -v q }
Test-Item "kontener ma internet"   { docker compose run --rm --no-deps app curl -fsS --max-time 15 -o /dev/null -I https://static.nhtsa.gov/odi/ffdd/cmpl/FLAT_CMPL.zip }

docker compose ps --status running app *>$null
if ($LASTEXITCODE -eq 0) {
    Test-Item "web na :5080"       { Test-Http "http://127.0.0.1:5080" }
} else {
    Write-Host "  [--]   web nie dziala (uruchom: make dev)" -ForegroundColor Yellow
}

Write-Host ""
if ($script:failed -eq 0) {
    Write-Host "Wszystko gotowe." -ForegroundColor Green
} else {
    Write-Host ("Nieudane sprawdzenia: " + $script:failed) -ForegroundColor Red
    exit 1
}
