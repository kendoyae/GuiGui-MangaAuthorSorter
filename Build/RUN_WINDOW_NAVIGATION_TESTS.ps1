$ErrorActionPreference = 'Stop'
$taskWorkspace = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskWorkspace
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$taskBinaryDirectory = Join-Path $taskWorkspace 'bin\Tests'
New-Item -ItemType Directory -Path $taskBinaryDirectory -Force | Out-Null
$taskBinary = Join-Path $taskBinaryDirectory 'WindowNavigationTests.exe'
& $taskCompiler /nologo /target:exe /platform:anycpu /optimize+ /main:MangaAuthorSorter.Tests.WindowNavigationTests "/out:$taskBinary" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll '@Build\CompilerSources.rsp' 'Tests\WindowNavigationTests.cs'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
# MainForm reads its executable directory. Isolate all settings and startup
# caches in a unique test directory instead of touching the user's app data.
$taskIsolatedDirectory = Join-Path $env:TEMP ('GuiGui-WindowNavigation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskIsolatedDirectory | Out-Null
$taskIsolatedBinary = Join-Path $taskIsolatedDirectory 'WindowNavigationTests.exe'
Copy-Item -LiteralPath $taskBinary -Destination $taskIsolatedBinary
& $taskIsolatedBinary
exit $LASTEXITCODE