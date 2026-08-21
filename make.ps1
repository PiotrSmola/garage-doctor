param([Parameter(Position = 0)][string]$Task = "help")
$ErrorActionPreference = "Stop"

function Sdk { docker compose run --rm --no-deps app @args }

switch ($Task) {
    "up"      { docker compose up -d --wait mongo redis mongo-express }
    "down"    { docker compose down }
    "dev"     { docker compose up app }
    "sh"      { docker compose run --rm app bash }
    "logs"    { docker compose logs -f app }
    "build"   { Sdk dotnet build GarageDoctor.sln }
    "test"    { docker compose run --rm app dotnet test GarageDoctor.sln }
    "restore" { Sdk dotnet restore GarageDoctor.sln }
    "data"    { & "$PSScriptRoot/scripts/fetch-data.ps1" }
    "ingest"  { docker compose run --rm app dotnet run --project src/GarageDoctor.Ingest }
    "mongo"   { docker compose exec mongo mongosh garagedoctor }
    "verify"  { & "$PSScriptRoot/scripts/verify.ps1" }
    "reset"   { docker compose down -v; docker compose up -d --wait mongo redis }
    "clean"   {
        docker compose down -v --rmi local
        docker volume rm ((Split-Path -Leaf (Get-Location).Path) + "_nuget") 2>$null
    }
    default {
        Write-Host "Zadania:" -ForegroundColor Cyan
        Write-Host "  up down dev sh logs build test restore"
        Write-Host "  data ingest mongo verify reset clean"
        Write-Host ""
        Write-Host "Uzycie: make dev   albo   ./make.ps1 dev"
    }
}
