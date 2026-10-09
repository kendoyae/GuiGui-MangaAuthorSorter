param([switch]$UseEverything)
$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskWorkspace
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$taskOutput = Join-Path $taskWorkspace 'bin\Tests'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskBinary = Join-Path $taskOutput 'StartupRecoveryTests.exe'
& $taskCompiler /nologo /target:exe /platform:anycpu /optimize+ /main:MangaAuthorSorter.Tests.StartupRecoveryTests "/out:$taskBinary" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll '@Build\CompilerSources.rsp' 'Tests\StartupRecoveryTests.cs'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$taskIsolatedDirectory = Join-Path $env:TEMP ('GuiGui-StartupRecovery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskIsolatedDirectory | Out-Null
$taskIsolatedBinary = Join-Path $taskIsolatedDirectory 'StartupRecoveryTests.exe'
Copy-Item -LiteralPath $taskBinary -Destination $taskIsolatedBinary
Get-ChildItem -LiteralPath (Join-Path $taskWorkspace 'bin\Release') -Filter 'Everything*.dll' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $taskIsolatedDirectory
}
$env:GUIGUI_TEST_EVERYTHING = if ($UseEverything) { '1' } else { '0' }
& $taskIsolatedBinary
exit $LASTEXITCODE
