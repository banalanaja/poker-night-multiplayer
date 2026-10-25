param(
    [string]$GameDirectory = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$TtgDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.local\tools\ttg-tools-1.16.1'),
    [string]$RuntimeDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.local\runtime'),
    [string]$ModVersion = '0.2.0-preview.2',
    [switch]$Uninstall
)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$game=(Resolve-Path -LiteralPath $GameDirectory).Path
$runtime=[IO.Path]::GetFullPath($RuntimeDirectory)
$profile=Get-Content -LiteralPath (Join-Path $projectRoot 'profiles\steam-25257439.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath (Join-Path $game 'CelebrityPoker.exe') -Algorithm SHA256).Hash -ne $profile.sha256) { throw 'Build incompatível; nenhum arquivo foi instalado.' }
if (Get-Process -Name CelebrityPoker -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path -eq (Join-Path $game 'CelebrityPoker.exe') }) {
    throw 'Feche esta instalação do Poker Night antes de instalar ou remover scripts.'
}
$archiveName='CP_pc_Project_PokerNightMultiplayer.ttarch2'
$descriptorName='_resdesc_50_Project_PokerNightMultiplayer.lua'
$archiveTarget=Join-Path $game ('Archives\'+$archiveName)
$descriptorTarget=Join-Path $game ('Archives\'+$descriptorName)
$manifestPath=Join-Path $runtime 'install-manifest.json'
$existing=$null
if (Test-Path -LiteralPath $manifestPath) { $existing=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json }
if ($existing -and $existing.gameDirectory -ne $game) { throw 'O manifesto pertence a outra instalação.' }
foreach($target in @($archiveTarget,$descriptorTarget)) {
    if (Test-Path -LiteralPath $target) {
        $entry=@($existing.files | Where-Object name -eq ([IO.Path]::GetFileName($target)))
        if ($entry.Count -ne 1 -or (Get-FileHash -LiteralPath $target).Hash -ne $entry[0].sha256) { throw 'Arquivo de destino existente não corresponde ao manifesto; não será sobrescrito.' }
    }
}
if($Uninstall) {
    if(-not $existing) { throw 'Manifesto de instalação não encontrado.' }
    foreach($target in @($archiveTarget,$descriptorTarget)) { if(Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target } }
    Remove-Item -LiteralPath $manifestPath
    'Scripts do mod removidos; arquivos originais preservados.'
    exit 0
}
$tool=(Resolve-Path -LiteralPath (Join-Path $TtgDirectory 'TTG Tools.exe')).Path
$compiler=(Resolve-Path -LiteralPath (Join-Path $TtgDirectory 'LencTools\LuaR Files\luac.exe')).Path
$frameworkCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$work=Join-Path $runtime 'package'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$bridge=Join-Path $runtime 'TtgPackageBridge.exe'
& $frameworkCompiler /nologo ('/out:'+$bridge) (Join-Path $PSScriptRoot 'TtgPackageBridge.cs')
if($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o adaptador externo.' }
$sourceArchive=Join-Path $game 'Archives\CP_pc_Project_compressed.ttarch2'
$sourceHash=(Get-FileHash -LiteralPath $sourceArchive).Hash
& $bridge $tool $game $work '_' '_' '_' extract
if($LASTEXITCODE -ne 0) { throw 'Falha ao extrair o bootstrap original localmente.' }
$scripts=@{'_system.lua'='SystemWrapper.lua';'PNM_BootGuard.lua'='BootGuard.lua';'PNM_Runtime.lua'='Runtime.lua';'PNM_Json.lua'='Json.lua';'PNM_Adapter.lua'='PokerNightMultiplayer.lua'}
foreach($entry in $scripts.GetEnumerator()) {
    & $compiler -s -o (Join-Path $work $entry.Key) (Join-Path $projectRoot ('game\'+$entry.Value))
    if($LASTEXITCODE -ne 0) { throw 'Falha ao compilar scripts Lua.' }
}
$quotedDirectory=ConvertTo-Json $runtime.Replace('\','/') -Compress
$configSource=Join-Path $runtime 'install-config.lua'
[IO.File]::WriteAllText($configSource,('return { directory = '+$quotedDirectory+' }'),[Text.UTF8Encoding]::new($false))
& $compiler -s -o (Join-Path $work 'PNM_InstallConfig.lua') $configSource
if($LASTEXITCODE -ne 0) { throw 'Falha ao compilar a configuração local.' }
$descriptorSource=Join-Path $runtime 'descriptor-source.lua'
$descriptor=@"
local set = {}
set.name = "PokerNightMultiplayer"
set.setName = "PokerNightMultiplayer"
set.descriptionFilenameOverride = ""
set.logicalName = "<Project>"
set.logicalDestination = "<>"
set.priority = -8888
set.localDir = _currentDirectory
set.enableMode = "constant"
set.version = "trunk"
set.descriptionPriority = 1000
set.gameDataName = "PokerNightMultiplayer Game Data"
set.gameDataPriority = 1000
set.gameDataEnableMode = "constant"
set.localDirIncludeBase = true
set.localDirRecurse = false
set.localDirIncludeOnly = nil
set.localDirExclude = { "Packaging/", "_dev/" }
set.gameDataArchives = { _currentDirectory .. "$archiveName" }
RegisterSetDescription(set)
"@
[IO.File]::WriteAllText($descriptorSource,$descriptor,[Text.UTF8Encoding]::new($false))
# Do not leave discoverable resource descriptors/archives in the workspace cache.
$archiveOutput=Join-Path $runtime 'compiled-archive.bin'
$descriptorOutput=Join-Path $runtime 'compiled-descriptor.bin'
$archiveBuild=Join-Path ([IO.Path]::GetTempPath()) ('pnm-package-'+[Guid]::NewGuid().ToString('N')+'.ttarch2')
try {
    # TTG requires the ttarch2 suffix internally; build outside the game resource tree.
    & $bridge $tool $game $work $archiveBuild $descriptorSource $descriptorOutput pack
    if($LASTEXITCODE -ne 0) { throw 'Falha ao empacotar scripts.' }
    Move-Item -LiteralPath $archiveBuild -Destination $archiveOutput -Force
} finally { if(Test-Path -LiteralPath $archiveBuild) { Remove-Item -LiteralPath $archiveBuild } }
if((Get-FileHash -LiteralPath $sourceArchive).Hash -ne $sourceHash) { throw 'O arquivo original mudou durante o empacotamento.' }
$files=@(@{name=$archiveName;sha256=(Get-FileHash -LiteralPath $archiveOutput).Hash},@{name=$descriptorName;sha256=(Get-FileHash -LiteralPath $descriptorOutput).Hash})
$written=@()
$backups=@{}
$oldManifest=if(Test-Path -LiteralPath $manifestPath) { [IO.File]::ReadAllBytes($manifestPath) } else { $null }
foreach($target in @($archiveTarget,$descriptorTarget)) {
    if(Test-Path -LiteralPath $target) { $backups[$target]=[IO.File]::ReadAllBytes($target) }
}
try {
    $written+=$archiveTarget;Copy-Item -LiteralPath $archiveOutput -Destination $archiveTarget
    $written+=$descriptorTarget;Copy-Item -LiteralPath $descriptorOutput -Destination $descriptorTarget
    @{schemaVersion=1;modVersion=$ModVersion;gameDirectory=$game;gameSha256=$profile.sha256;files=$files;originalProjectSha256=$sourceHash} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
} catch {
    # Restore the previous task-owned version after a failed update.
    foreach($target in @($archiveTarget,$descriptorTarget)) {
        if($backups.ContainsKey($target)) { [IO.File]::WriteAllBytes($target,$backups[$target]) }
        elseif($target -in $written -and (Test-Path -LiteralPath $target)) { Remove-Item -LiteralPath $target }
    }
    if($null -ne $oldManifest) { [IO.File]::WriteAllBytes($manifestPath,$oldManifest) }
    elseif(Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
    throw
}
'Scripts instalados. Sem session-config.json, o jogo permanece no modo original.'
