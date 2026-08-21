#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$SolutionName = 'GarageDoctor'

function Write-Step { param([string]$Message) Write-Host "`n> $Message" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Message) Write-Host "  [ok]   $Message" -ForegroundColor Green }
function Write-Skip { param([string]$Message) Write-Host "  [skip] $Message" -ForegroundColor Yellow }
function Write-Warn { param([string]$Message) Write-Host "  [warn] $Message" -ForegroundColor Yellow }
function Write-Fail { param([string]$Message) Write-Host "`n[FAIL] $Message" -ForegroundColor Red; exit 1 }

function Write-ProjectFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowEmptyCollection()][string[]]$Lines
    )
    if (Test-Path $Path) { Write-Skip "$Path (juz istnieje)"; return }
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $eol = "`n"
    if ($Path -match '\.(ps1|bat|cmd)$') { $eol = "`r`n" }
    $text = ($Lines -join $eol) + $eol
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path (Get-Location).Path $Path), $text, $utf8NoBom)
    Write-Ok $Path
}

function Invoke-Sdk {
    param(
        [Parameter(Mandatory = $true)][string[]]$CommandArgs,
        [switch]$Tolerant
    )
    $mount = (Get-Location).Path
    $volume = (Split-Path -Leaf $mount) + '_nuget'
    & docker run --rm -v "${mount}:/app" -w /app -v "${volume}:/root/.nuget/packages" mcr.microsoft.com/dotnet/sdk:9.0 @CommandArgs
    if ($LASTEXITCODE -ne 0 -and -not $Tolerant) { throw "dotnet zwrocil kod $LASTEXITCODE" }
}

Write-Step 'Sprawdzanie wymagan hosta'

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Write-Fail 'Brak Dockera. Zainstaluj Docker Desktop.' }
& docker compose version | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Fail 'Brak "docker compose" (plugin v2).' }
& docker info 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Fail 'Docker daemon nie odpowiada. Uruchom Docker Desktop.' }
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Write-Fail 'Brak gita.' }

Write-Ok ('docker ' + (& docker version --format '{{.Server.Version}}'))
Write-Ok ('compose ' + (& docker compose version --short))

$hostOs = & docker info --format '{{.OperatingSystem}}' 2>$null
if ($hostOs -match 'WSL|Docker Desktop') { Write-Ok "backend: $hostOs" }
else { Write-Warn 'Nie wykryto WSL2. Settings -> General -> Use WSL 2 based engine' }

Write-Step 'Inicjalizacja repozytorium'

if (Test-Path '.git') {
    Write-Skip '.git juz istnieje'
} else {
    & git init -q
    Write-Ok 'git init'
}
& git config core.autocrlf false
Write-Ok 'core.autocrlf = false'

Write-Step 'Struktura katalogow'

$dirs = @('src', 'tests', 'tests/fixtures', 'data/raw', 'data/spec', 'docs', 'scripts', '.devcontainer')
foreach ($d in $dirs) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
Write-Ok 'katalogi utworzone'

Write-Step 'Pliki konfiguracyjne'

Write-ProjectFile '.gitattributes' @(
    '* text=auto eol=lf',
    '*.ps1 text eol=crlf',
    '*.bat text eol=crlf',
    '*.cmd text eol=crlf',
    '*.cs text eol=lf',
    '*.zip binary'
)

Write-ProjectFile '.gitignore' @(
    'bin/',
    'obj/',
    '.vs/',
    '*.user',
    '.env',
    'data/raw/',
    'TestResults/'
)

Write-ProjectFile '.dockerignore' @(
    'bin',
    'obj',
    '.git',
    '.vs',
    '.env',
    'data/raw'
)

Write-ProjectFile 'Dockerfile' @(
    'FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dev',
    'WORKDIR /app',
    'ENV DOTNET_CLI_TELEMETRY_OPTOUT=1',
    'ENV DOTNET_USE_POLLING_FILE_WATCHER=1',
    'ENV ASPNETCORE_URLS=http://+:8080',
    'RUN apt-get update \',
    ' && apt-get install -y --no-install-recommends curl unzip \',
    ' && rm -rf /var/lib/apt/lists/*',
    'EXPOSE 8080',
    'CMD ["dotnet", "watch", "run", "--project", "src/GarageDoctor.Web", "--no-launch-profile"]',
    '',
    'FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build',
    'WORKDIR /src',
    'COPY . .',
    'RUN dotnet restore GarageDoctor.sln \',
    ' && dotnet publish src/GarageDoctor.Web -c Release -o /publish --no-restore',
    '',
    'FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime',
    'WORKDIR /app',
    'ENV ASPNETCORE_URLS=http://+:8080',
    'COPY --from=build /publish .',
    'EXPOSE 8080',
    'ENTRYPOINT ["dotnet", "GarageDoctor.Web.dll"]'
)

Write-ProjectFile 'compose.yml' @(
    'services:',
    '  mongo:',
    '    image: mongo:8',
    '    restart: unless-stopped',
    '    environment:',
    '      MONGO_INITDB_DATABASE: garagedoctor',
    '    ports:',
    '      - "127.0.0.1:27018:27017"',
    '    volumes:',
    '      - mongodata:/data/db',
    '    healthcheck:',
    '      test: ["CMD", "mongosh", "--quiet", "--eval", "db.adminCommand({ping:1}).ok"]',
    '      interval: 10s',
    '      timeout: 5s',
    '      retries: 12',
    '',
    '  mongo-express:',
    '    image: mongo-express:1',
    '    restart: unless-stopped',
    '    ports:',
    '      - "127.0.0.1:8091:8081"',
    '    environment:',
    '      ME_CONFIG_MONGODB_URL: mongodb://mongo:27017',
    '      ME_CONFIG_BASICAUTH: "false"',
    '    depends_on:',
    '      mongo:',
    '        condition: service_healthy',
    '',
    '  redis:',
    '    image: redis:7-alpine',
    '    restart: unless-stopped',
    '    ports:',
    '      - "127.0.0.1:6380:6379"',
    '    healthcheck:',
    '      test: ["CMD", "redis-cli", "ping"]',
    '      interval: 10s',
    '      timeout: 3s',
    '      retries: 5',
    '',
    '  app:',
    '    build:',
    '      context: .',
    '      target: dev',
    '    environment:',
    '      ASPNETCORE_ENVIRONMENT: Development',
    '      ConnectionStrings__Mongo: mongodb://mongo:27017',
    '      ConnectionStrings__Redis: redis:6379',
    '      MongoDatabase: garagedoctor',
    '    ports:',
    '      - "127.0.0.1:5080:8080"',
    '    volumes:',
    '      - .:/app',
    '      - nuget:/root/.nuget/packages',
    '    depends_on:',
    '      mongo:',
    '        condition: service_healthy',
    '      redis:',
    '        condition: service_healthy',
    '',
    'volumes:',
    '  mongodata:',
    '  nuget:'
)

Write-ProjectFile 'Directory.Build.props' @(
    '<Project>',
    '  <PropertyGroup>',
    '    <LangVersion>latest</LangVersion>',
    '    <Nullable>enable</Nullable>',
    '    <ImplicitUsings>enable</ImplicitUsings>',
    '    <InvariantGlobalization>true</InvariantGlobalization>',
    '  </PropertyGroup>',
    '</Project>'
)

Write-ProjectFile 'make.ps1' @(
    'param([Parameter(Position = 0)][string]$Task = "help")',
    '$ErrorActionPreference = "Stop"',
    '',
    'function Sdk { docker compose run --rm --no-deps app @args }',
    '',
    'switch ($Task) {',
    '    "up"      { docker compose up -d --wait mongo redis mongo-express }',
    '    "down"    { docker compose down }',
    '    "dev"     { docker compose up app }',
    '    "sh"      { docker compose run --rm app bash }',
    '    "logs"    { docker compose logs -f app }',
    '    "build"   { Sdk dotnet build GarageDoctor.sln }',
    '    "test"    { docker compose run --rm app dotnet test GarageDoctor.sln }',
    '    "restore" { Sdk dotnet restore GarageDoctor.sln }',
    '    "data"    { & "$PSScriptRoot/scripts/fetch-data.ps1" }',
    '    "ingest"  { docker compose run --rm app dotnet run --project src/GarageDoctor.Ingest }',
    '    "mongo"   { docker compose exec mongo mongosh garagedoctor }',
    '    "verify"  { & "$PSScriptRoot/scripts/verify.ps1" }',
    '    "reset"   { docker compose down -v; docker compose up -d --wait mongo redis }',
    '    "clean"   {',
    '        docker compose down -v --rmi local',
    '        docker volume rm ((Split-Path -Leaf (Get-Location).Path) + "_nuget") 2>$null',
    '    }',
    '    default {',
    '        Write-Host "Zadania:" -ForegroundColor Cyan',
    '        Write-Host "  up down dev sh logs build test restore"',
    '        Write-Host "  data ingest mongo verify reset clean"',
    '        Write-Host ""',
    '        Write-Host "Uzycie: make dev   albo   ./make.ps1 dev"',
    '    }',
    '}'
)

Write-ProjectFile 'make.bat' @(
    '@echo off',
    'setlocal',
    'cd /d "%~dp0"',
    'if "%~1"=="" (',
    '    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make.ps1" help',
    ') else (',
    '    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make.ps1" %*',
    ')'
)

Write-ProjectFile 'scripts/fetch-data.ps1' @(
    '$ErrorActionPreference = "Stop"',
    '$ProgressPreference = "SilentlyContinue"',
    '',
    '$raw = "data/raw"',
    '$spec = "data/spec"',
    'New-Item -ItemType Directory -Path $raw, $spec -Force | Out-Null',
    '',
    '$files = @(',
    '    @{ Url = "https://static.nhtsa.gov/odi/ffdd/cmpl/FLAT_CMPL.zip";           Dest = "$raw/FLAT_CMPL.zip" },',
    '    @{ Url = "https://static.nhtsa.gov/odi/ffdd/rcl/FLAT_RCL_POST_2010.zip";  Dest = "$raw/FLAT_RCL_POST_2010.zip" },',
    '    @{ Url = "https://static.nhtsa.gov/odi/ffdd/rcl/FLAT_RCL_PRE_2010.zip";   Dest = "$raw/FLAT_RCL_PRE_2010.zip" },',
    '    @{ Url = "https://static.nhtsa.gov/odi/ffdd/cmpl/CMPL.txt";               Dest = "$spec/CMPL.txt" },',
    '    @{ Url = "https://static.nhtsa.gov/odi/ffdd/rcl/RCL.txt";                 Dest = "$spec/RCL.txt" }',
    ')',
    '',
    'foreach ($f in $files) {',
    '    if (Test-Path $f.Dest) {',
    '        Write-Host ("[skip] " + $f.Dest) -ForegroundColor Yellow',
    '        continue',
    '    }',
    '    Write-Host ("[..]   pobieranie " + $f.Url) -ForegroundColor Cyan',
    '    Invoke-WebRequest -Uri $f.Url -OutFile $f.Dest -UseBasicParsing',
    '    $sizeMb = [math]::Round((Get-Item $f.Dest).Length / 1MB, 1)',
    '    Write-Host ("[ok]   " + $f.Dest + "  " + $sizeMb + " MB") -ForegroundColor Green',
    '}',
    '',
    '$archives = @("FLAT_CMPL.zip", "FLAT_RCL_POST_2010.zip", "FLAT_RCL_PRE_2010.zip")',
    'foreach ($name in $archives) {',
    '    $zip = Join-Path $raw $name',
    '    if (Test-Path $zip) { Expand-Archive -Path $zip -DestinationPath $raw -Force }',
    '}',
    'Write-Host "[ok]   rozpakowano" -ForegroundColor Green',
    '',
    '$cmpl = "$raw/FLAT_CMPL.txt"',
    'if (-not (Test-Path $cmpl)) { Write-Host "[FAIL] brak FLAT_CMPL.txt" -ForegroundColor Red; exit 1 }',
    '',
    '$lines = 0',
    '$reader = [System.IO.File]::OpenText((Resolve-Path $cmpl).Path)',
    'try { while ($null -ne $reader.ReadLine()) { $lines++ } } finally { $reader.Close() }',
    '',
    '$cmplMb = [math]::Round((Get-Item $cmpl).Length / 1MB, 1)',
    '',
    '$rclPost = "$raw/FLAT_RCL_POST_2010.txt"',
    '$rclPre  = "$raw/FLAT_RCL_PRE_2010.txt"',
    '$postLines = 0',
    '$preLines = 0',
    'if (Test-Path $rclPost) {',
    '    $r = [System.IO.File]::OpenText((Resolve-Path $rclPost).Path)',
    '    try { while ($null -ne $r.ReadLine()) { $postLines++ } } finally { $r.Close() }',
    '}',
    'if (Test-Path $rclPre) {',
    '    $r = [System.IO.File]::OpenText((Resolve-Path $rclPre).Path)',
    '    try { while ($null -ne $r.ReadLine()) { $preLines++ } } finally { $r.Close() }',
    '}',
    '',
    'Write-Host ""',
    'Write-Host ("FLAT_CMPL.txt           : " + $lines + "  (ref. 2235299)") -ForegroundColor Cyan',
    'Write-Host ("FLAT_RCL_POST_2010.txt  : " + $postLines + "  (ref. 244499)") -ForegroundColor Cyan',
    'Write-Host ("FLAT_RCL_PRE_2010.txt   : " + $preLines + "  (ref. 81715)") -ForegroundColor Cyan',
    'Write-Host ""',
    '',
    '$manifest = @(',
    '    "# Data manifest",',
    '    "",',
    '    "Dane NIE sa przechowywane w tym repozytorium. Ten plik dokumentuje zbior,",',
    '    "na ktorym projekt byl budowany, zeby wyniki byly odtwarzalne.",',
    '    "",',
    '    "Zrodlo:   NHTSA Office of Defects Investigation",',
    '    "          https://static.nhtsa.gov/odi/ffdd/",',
    '    "Licencja: us-pd (domena publiczna, bez warunkow uzycia)",',
    '    "",',
    '    ("Pobrano: " + (Get-Date -Format "yyyy-MM-dd")),',
    '    "",',
    '    "## FLAT_CMPL.txt - zgloszenia usterek",',
    '    "",',
    '    ("Rozmiar:       " + $cmplMb + " MB"),',
    '    ("Wierszy:       " + $lines + "   (referencja: 2235299)"),',
    '    "Pol w wierszu: 51 (TAB-delimited, bez naglowka)",',
    '    "Konce linii:   LF",',
    '    "",',
    '    "## FLAT_RCL_POST_2010.txt - akcje serwisowe od 2010",',
    '    "",',
    '    ("Wierszy:       " + $postLines + "   (referencja: 244499)"),',
    '    "Pol w wierszu: 29 (TAB-delimited, bez naglowka)",',
    '    "Konce linii:   CRLF",',
    '    "",',
    '    "## FLAT_RCL_PRE_2010.txt - akcje serwisowe 1967-2009",',
    '    "",',
    '    ("Wierszy:       " + $preLines + "   (referencja: 81715, w tym 5 pustych)"),',
    '    "",',
    '    "Pliki sa aktualizowane codziennie, wiec liczby beda rosly.",',
    '    "",',
    '    "## Jak odtworzyc",',
    '    "",',
    '    "    make data",',
    '    "",',
    '    "## Dlaczego danych nie ma w repo",',
    '    "",',
    '    "Pole CDESCR zawiera swobodny tekst pisany przez wlascicieli pojazdow.",',
    '    "Mimo ze zbior jest w domenie publicznej, narracje regularnie zawieraja",',
    '    "nazwiska, adresy i numery telefonu wpisane przez zglaszajacych.",',
    '    "Redystrybucja tych rekordow oznaczalaby publikowanie cudzych danych osobowych.",',
    '    "",',
    '    "Testy korzystaja z syntetycznych fixtures w tests/fixtures/ - pisanych recznie."',
    ')',
    '',
    '$utf8NoBom = New-Object System.Text.UTF8Encoding($false)',
    '[System.IO.File]::WriteAllText(',
    '    (Join-Path (Get-Location).Path "data/MANIFEST.md"),',
    '    (($manifest -join "`n") + "`n"),',
    '    $utf8NoBom)',
    '',
    'Write-Host "[ok]   data/MANIFEST.md zaktualizowany (ten plik commitujesz)" -ForegroundColor Green',
    'Write-Host "       Dane surowe zostaja lokalnie - data/raw/ jest w .gitignore." -ForegroundColor Yellow'
)

Write-ProjectFile 'scripts/verify.ps1' @(
    '$ErrorActionPreference = "Continue"',
    '$ProgressPreference = "SilentlyContinue"',
    '$script:failed = 0',
    '',
    'function Test-Item {',
    '    param([string]$Label, [scriptblock]$Check)',
    '    try {',
    '        & $Check *>$null',
    '        if ($LASTEXITCODE -eq 0 -or $null -eq $LASTEXITCODE) {',
    '            Write-Host "  [ok]   $Label" -ForegroundColor Green',
    '            return',
    '        }',
    '    } catch { }',
    '    Write-Host "  [FAIL] $Label" -ForegroundColor Red',
    '    $script:failed++',
    '}',
    '',
    'function Test-Http {',
    '    param([string]$Url)',
    '    try {',
    '        Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 6 | Out-Null',
    '        $global:LASTEXITCODE = 0',
    '    } catch {',
    '        $global:LASTEXITCODE = 1',
    '    }',
    '}',
    '',
    'Write-Host "Weryfikacja srodowiska:"',
    '',
    'Test-Item "mongo dziala"           { docker compose ps --status running mongo }',
    'Test-Item "mongo odpowiada"        { docker compose exec -T mongo mongosh --quiet --eval "db.adminCommand({ping:1}).ok" }',
    'Test-Item "redis odpowiada"        { docker compose exec -T redis redis-cli ping }',
    'Test-Item "mongo-express na 8091"  { Test-Http "http://127.0.0.1:8091" }',
    'Test-Item "dotnet w kontenerze"    { docker compose run --rm --no-deps app dotnet --version }',
    'Test-Item "solucja sie buduje"     { docker compose run --rm --no-deps app dotnet build GarageDoctor.sln --nologo -v q }',
    'Test-Item "kontener ma internet"   { docker compose run --rm --no-deps app curl -fsS --max-time 15 -o /dev/null -I https://static.nhtsa.gov/odi/ffdd/cmpl/FLAT_CMPL.zip }',
    '',
    '$running = @(docker compose ps --services --filter status=running 2>$null)',
    'if ($running -contains "app") {',
    '    Test-Item "web na :5080"       { Test-Http "http://127.0.0.1:5080" }',
    '} else {',
    '    Write-Host "  [--]   web nie uruchomiony - to normalne po bootstrapie" -ForegroundColor Yellow',
    '    Write-Host "         uruchom: ./make.ps1 dev" -ForegroundColor Yellow',
    '}',
    '',
    'Write-Host ""',
    'if ($script:failed -eq 0) {',
    '    Write-Host "Wszystko gotowe." -ForegroundColor Green',
    '} else {',
    '    Write-Host ("Nieudane sprawdzenia: " + $script:failed) -ForegroundColor Red',
    '    exit 1',
    '}'
)

Write-ProjectFile '.devcontainer/devcontainer.json' @(
    '{',
    '  "name": "Garage Doctor",',
    '  "dockerComposeFile": "../compose.yml",',
    '  "service": "app",',
    '  "workspaceFolder": "/app",',
    '  "shutdownAction": "stopCompose",',
    '  "overrideCommand": true,',
    '  "forwardPorts": [5080, 8091, 27018],',
    '  "customizations": {',
    '    "vscode": {',
    '      "extensions": [',
    '        "ms-dotnettools.csdevkit",',
    '        "ms-dotnettools.csharp",',
    '        "mongodb.mongodb-vscode",',
    '        "ms-azuretools.vscode-docker"',
    '      ]',
    '    }',
    '  }',
    '}'
)

Write-Step 'Budowanie obrazu SDK'

& docker compose build app
if ($LASTEXITCODE -ne 0) { Write-Fail 'Budowanie obrazu nie powiodlo sie.' }
Write-Ok 'obraz zbudowany'

Write-Step 'Tworzenie solucji i projektow'

if (Test-Path ($SolutionName + '.sln')) {
    Write-Skip 'solucja juz istnieje'
} else {
    Invoke-Sdk @('dotnet', 'new', 'sln', '-n', $SolutionName)
    Write-Ok 'solucja'
}

$projects = @(
    @{ Template = 'classlib'; Name = 'GarageDoctor.Domain';           Path = 'src/GarageDoctor.Domain' },
    @{ Template = 'classlib'; Name = 'GarageDoctor.Infrastructure';   Path = 'src/GarageDoctor.Infrastructure' },
    @{ Template = 'console';  Name = 'GarageDoctor.Ingest';           Path = 'src/GarageDoctor.Ingest' },
    @{ Template = 'mvc';      Name = 'GarageDoctor.Web';              Path = 'src/GarageDoctor.Web' },
    @{ Template = 'xunit';    Name = 'GarageDoctor.UnitTests';        Path = 'tests/GarageDoctor.UnitTests' },
    @{ Template = 'xunit';    Name = 'GarageDoctor.IntegrationTests'; Path = 'tests/GarageDoctor.IntegrationTests' }
)

foreach ($proj in $projects) {
    $csproj = Join-Path $proj.Path ($proj.Name + '.csproj')
    if (Test-Path $csproj) {
        Write-Skip $proj.Name
        continue
    }
    Invoke-Sdk @('dotnet', 'new', $proj.Template, '-n', $proj.Name, '-o', $proj.Path, '-f', 'net9.0')
    Write-Ok $proj.Name
}

Write-Step 'Dodawanie projektow do solucji'

foreach ($proj in $projects) {
    Invoke-Sdk @('dotnet', 'sln', ($SolutionName + '.sln'), 'add', $proj.Path) -Tolerant
}
Write-Ok 'projekty w solucji'

Write-Step 'Referencje miedzy projektami'

$references = @(
    @{ From = 'src/GarageDoctor.Infrastructure';   To = @('src/GarageDoctor.Domain') },
    @{ From = 'src/GarageDoctor.Ingest';           To = @('src/GarageDoctor.Domain', 'src/GarageDoctor.Infrastructure') },
    @{ From = 'src/GarageDoctor.Web';              To = @('src/GarageDoctor.Domain', 'src/GarageDoctor.Infrastructure') },
    @{ From = 'tests/GarageDoctor.UnitTests';      To = @('src/GarageDoctor.Domain') },
    @{ From = 'tests/GarageDoctor.IntegrationTests'; To = @('src/GarageDoctor.Domain', 'src/GarageDoctor.Infrastructure') }
)

foreach ($ref in $references) {
    foreach ($target in $ref.To) {
        Invoke-Sdk @('dotnet', 'add', $ref.From, 'reference', $target) -Tolerant
    }
}
Write-Ok 'referencje ustawione'

Write-Step 'Pakiety NuGet'

$packages = @(
    @{ Project = 'src/GarageDoctor.Infrastructure'; Name = 'MongoDB.Driver' },
    @{ Project = 'src/GarageDoctor.Infrastructure'; Name = 'Microsoft.Extensions.Options' },
    @{ Project = 'src/GarageDoctor.Infrastructure'; Name = 'Microsoft.Extensions.Logging.Abstractions' },
    @{ Project = 'src/GarageDoctor.Ingest';         Name = 'Microsoft.Extensions.Hosting' },
    @{ Project = 'src/GarageDoctor.Ingest';         Name = 'Serilog.Extensions.Hosting' },
    @{ Project = 'src/GarageDoctor.Ingest';         Name = 'Serilog.Sinks.Console' },
    @{ Project = 'src/GarageDoctor.Web';            Name = 'Microsoft.Extensions.Caching.StackExchangeRedis' },
    @{ Project = 'src/GarageDoctor.Web';            Name = 'Microsoft.Extensions.Identity.Core' },
    @{ Project = 'src/GarageDoctor.Web';            Name = 'Serilog.AspNetCore' },
    @{ Project = 'tests/GarageDoctor.IntegrationTests'; Name = 'Testcontainers.MongoDb' },
    @{ Project = 'tests/GarageDoctor.IntegrationTests'; Name = 'MongoDB.Driver' }
)

foreach ($pkg in $packages) {
    $csprojPath = Get-ChildItem -Path $pkg.Project -Filter '*.csproj' | Select-Object -First 1
    if ($csprojPath -and ((Get-Content $csprojPath.FullName -Raw) -match [regex]::Escape($pkg.Name))) {
        Write-Skip ($pkg.Name + ' -> ' + $pkg.Project)
        continue
    }
    Invoke-Sdk @('dotnet', 'add', $pkg.Project, 'package', $pkg.Name)
    Write-Ok ($pkg.Name + ' -> ' + $pkg.Project)
}

Write-Step 'Kompilacja kontrolna'

Invoke-Sdk @('dotnet', 'build', ($SolutionName + '.sln'), '--nologo')
Write-Ok 'solucja sie buduje'

Write-Step 'Uruchamianie infrastruktury'

& docker compose up -d --wait mongo redis mongo-express
if ($LASTEXITCODE -ne 0) { Write-Fail 'Nie udalo sie podniesc infrastruktury.' }
Write-Ok 'mongo na 127.0.0.1:27018 (tylko localhost)'
Write-Ok 'mongo-express na http://127.0.0.1:8091 (tylko localhost)'
Write-Ok 'redis na 127.0.0.1:6380 (tylko localhost)'

Write-Step 'Weryfikacja'

& (Join-Path (Get-Location).Path 'scripts/verify.ps1')

Write-Host ''
Write-Host '--------------------------------------------------------------' -ForegroundColor Cyan
Write-Host '  Gotowe.' -ForegroundColor Cyan
Write-Host ''
Write-Host '  W PowerShellu:              W cmd.exe:'
Write-Host '  ./make.ps1 dev              make dev      -> http://127.0.0.1:5080'
Write-Host '  ./make.ps1 data             make data     pobierz dane NHTSA (353 MB)'
Write-Host '  ./make.ps1 test             make test     testy'
Write-Host '  ./make.ps1 mongo            make mongo    powloka mongosh'
Write-Host '  ./make.ps1 verify           make verify   ponowna weryfikacja'
Write-Host '  ./make.ps1 down             make down     zatrzymaj wszystko'
Write-Host ''
Write-Host '  mongo-express: http://127.0.0.1:8091'
Write-Host ''
Write-Host '  Nastepny krok:'
Write-Host '    1) ./make.ps1 data'
Write-Host '    2) skopiuj PROMPT_GARAGE_DATA.md do docs/'
Write-Host '    3) claude'
Write-Host '--------------------------------------------------------------' -ForegroundColor Cyan
Write-Host ''
