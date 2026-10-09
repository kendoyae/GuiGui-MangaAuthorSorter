param([Parameter(Mandatory=$true)][string]$AppBinary, [Parameter(Mandatory=$true)][string]$Label)
$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path -Parent $PSScriptRoot
$taskResolvedBinary = (Resolve-Path -LiteralPath $AppBinary).Path
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskDirectory = Join-Path $env:TEMP ('GuiGui-StartupComparison-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskDirectory | Out-Null
$taskHarness = Join-Path $taskDirectory 'StartupComparisonBenchmark.exe'
& $taskCompiler /nologo /target:exe /optimize+ "/out:$taskHarness" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll (Join-Path $taskWorkspace 'Tests\StartupComparisonBenchmark.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath $taskResolvedBinary -Destination (Join-Path $taskDirectory 'BenchmarkApp.exe')
Get-ChildItem -LiteralPath (Split-Path -Parent $taskResolvedBinary) -Filter 'Everything*.dll' | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $taskDirectory }
& $taskHarness $Label
exit $LASTEXITCODE
