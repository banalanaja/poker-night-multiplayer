param([string]$Version = '0.2.0-preview.2')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-z0-9.]+)?$') { throw 'Versão inválida.' }
$project=Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME=Join-Path $project '.local\dotnet'
$env:NUGET_PACKAGES=Join-Path $project '.local\nuget'
$output=Join-Path $project ('dist\PokerNightMultiplayer-'+$Version+'-win-x64')
if(Test-Path -LiteralPath $output) { throw 'O diretório de publicação já existe; use uma pasta limpa para evitar arquivos extras.' }
& dotnet publish (Join-Path $project 'src\PokerNight.Desktop') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $output
if($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o aplicativo Windows.' }
Copy-Item -LiteralPath (Join-Path $project 'LICENSE') -Destination $output
foreach($package in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
    $cache=Join-Path $env:NUGET_PACKAGES $package
    $runtimePackage=Get-ChildItem -LiteralPath $cache -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $notice=Join-Path $output ('notices\'+$package)
    New-Item -ItemType Directory -Path $notice -Force | Out-Null
    $licenseFound=$false
    foreach($name in @('LICENSE','LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')) {
        $source=Join-Path $runtimePackage.FullName $name
        if(Test-Path -LiteralPath $source) {
            Copy-Item -LiteralPath $source -Destination $notice
            if($name -like 'LICENSE*') { $licenseFound=$true }
        }
    }
    if(-not $licenseFound) { throw 'Licença do runtime ausente.' }
}
Copy-Item -LiteralPath (Join-Path $project 'docs\JOGAR.md') -Destination (Join-Path $output 'LEIA-ME.md')
# Only our launcher/scripts and the redistributable .NET runtime. No generated game packages or TTG binaries.
$unexpected=Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Extension -in @('.ttarch2','.prop','.save','.bundle') -or $_.Name -like '*OriginalSystem*' -or $_.Name -eq 'TTG Tools.exe' }
if($unexpected) { throw 'Conteúdo não distribuível na pasta de publicação.' }
$zip=$output+'.zip'
Compress-Archive -LiteralPath $output -DestinationPath $zip
Get-FileHash -LiteralPath $zip -Algorithm SHA256 | Select-Object Path,Hash
