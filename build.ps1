param([switch]$Native)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.local\dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.local\nuget'
Push-Location $PSScriptRoot
try {
    & dotnet build src/PokerNight.Launcher -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o aplicativo auxiliar.' }
    & dotnet run --project tests/PokerNight.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes de protocolo.' }
    if ($Native) {
        & cmake -S native -B build/native -A x64
        if ($LASTEXITCODE -ne 0) { throw 'Falha na configuração C++.' }
        & cmake --build build/native --config Release
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a ponte nativa.' }
        & ctest --test-dir build/native -C Release --output-on-failure
        if ($LASTEXITCODE -ne 0) { throw 'Falha nos testes nativos.' }
    }
} finally { Pop-Location }
