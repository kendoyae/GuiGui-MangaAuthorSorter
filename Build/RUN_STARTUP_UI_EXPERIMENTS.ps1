param([int]$Rounds = 3)
$ErrorActionPreference = 'Stop'
if ($Rounds -lt 1 -or $Rounds -gt 9) { throw 'Rounds must be between 1 and 9.' }
$taskWorkspace = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskWorkspace
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskIsolated = Join-Path $env:TEMP ('GuiGui-StartupUi-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskIsolated | Out-Null
$taskBinary = Join-Path $taskIsolated 'StartupUiExperimentTests.exe'
& $taskCompiler /nologo /target:exe /platform:anycpu /optimize+ /main:MangaAuthorSorter.Tests.StartupUiExperimentTests "/out:$taskBinary" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll '@Build\CompilerSources.rsp' 'Tests\StartupUiExperimentTests.cs'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem -LiteralPath (Join-Path $taskWorkspace 'bin\Release') -Filter 'Everything*.dll' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskIsolated }
$taskResults = Join-Path $taskWorkspace ('Docs\StartupUiAcceptance\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
& $taskBinary Seed
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$taskOrders = @('ABC','BCA','CAB')
for ($taskRound = 0; $taskRound -lt $Rounds; $taskRound++) {
    foreach ($taskMode in $taskOrders[$taskRound % 3].ToCharArray()) {
        $taskFile = Join-Path $taskResults ('round-' + ($taskRound + 1) + '-' + $taskMode + '.json')
        & $taskBinary ([string]$taskMode) $taskFile
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
Write-Output ('[PASS] Independent-process A/B/C results: ' + $taskResults)
