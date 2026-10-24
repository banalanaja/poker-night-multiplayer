param([Parameter(ValueFromRemainingArguments = $true)][string[]]$CommandArgs)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$fileSystem = New-Object -ComObject Scripting.FileSystemObject
$localTools = $fileSystem.GetFolder((Join-Path $projectRoot '.local\tools')).ShortPath
$nodePath = Join-Path $localTools 'node-v22.23.4-win-x64\node.exe'
$reaPath = Join-Path $localTools 'rea\node_modules\rea-agents\scripts\rea.mjs'
$env:REA_ANALYSIS_PROVIDER = 'ghidra'
$env:GHIDRA_INSTALL_DIR = $fileSystem.GetFolder((Join-Path $localTools 'ghidra_12.1.4_PUBLIC')).ShortPath
if (-not $env:JAVA_HOME) { $env:JAVA_HOME = 'C:\Program Files\Eclipse Adoptium\jdk-21.0.6.7-hotspot' }
if (-not (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME 'bin\java.exe'))) { throw 'Defina JAVA_HOME para um JDK 21 x64.' }
$env:PATH = (Split-Path -Parent $nodePath) + ';' + (Join-Path $env:JAVA_HOME 'bin') + ';' + $env:PATH
$env:TEMP = Join-Path $localTools 'runtime'
$env:TMP = $env:TEMP
& $nodePath --preserve-symlinks --preserve-symlinks-main $reaPath @CommandArgs
exit $LASTEXITCODE
