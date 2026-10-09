# Verify that every compiler-response source exists and matches the project inventory.
# The legacy .NET Framework csc.exe may misinterpret forward slashes as option separators.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rspPath = Join-Path $PSScriptRoot 'CompilerSources.rsp'
$projectPath = Join-Path $root 'MangaAuthorSorter.csproj'
if (-not (Test-Path -LiteralPath $rspPath -PathType Leaf)) { throw 'Missing Build\CompilerSources.rsp.' }
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw 'Missing MangaAuthorSorter.csproj.' }

[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$namespaces = New-Object System.Xml.XmlNamespaceManager($project.NameTable)
$namespaces.AddNamespace('p', 'http://schemas.microsoft.com/developer/msbuild/2003')
$projectSources = @{}
foreach ($node in $project.SelectNodes('//p:Compile', $namespaces)) {
    $name = $node.GetAttribute('Include').Replace('/', '\')
    if ($projectSources.ContainsKey($name)) { throw "Duplicate C# project entry: $name" }
    $projectSources[$name] = $true
}
if ($projectSources.Count -eq 0) { throw 'C# project contains no compiled sources.' }

$rspSources = @{}
$lineNumber = 0
foreach ($entry in (Get-Content -LiteralPath $rspPath -Encoding UTF8)) {
    $lineNumber++
    $name = $entry.Trim()
    if ($name.Length -eq 0) { continue }
    if ($name.Contains('/')) {
        throw "CompilerSources.rsp line ${lineNumber}: Use Windows backslashes in source paths: $name"
    }
    if ($name.StartsWith('"') -and $name.EndsWith('"')) {
        $name = $name.Substring(1, $name.Length - 2)
    }
    if ($rspSources.ContainsKey($name)) { throw "Duplicate compiler-response entry: $name" }
    $rspSources[$name] = $true
    $absolute = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
        throw "Compiler source missing: $name (expected $absolute)"
    }
}
$missing = @($projectSources.Keys | Where-Object { -not $rspSources.ContainsKey($_) })
$extra = @($rspSources.Keys | Where-Object { -not $projectSources.ContainsKey($_) })
if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
    throw "CompilerSources.rsp differs from MangaAuthorSorter.csproj. Missing: $($missing -join ', '); Extra: $($extra -join ', ')"
}
Write-Host "[OK] C# source manifest: $($rspSources.Count) files exist, match project entries, and use Windows paths."

# Catch the V1.13.8 advanced downloader contract regression before invoking csc.exe.
$advancedSource = Get-Content -LiteralPath (Join-Path $root 'Services\AdvancedSourceService.cs') -Raw -Encoding UTF8
$advancedForm = Get-Content -LiteralPath (Join-Path $root 'Forms\AdvancedAuthorBuilderForm.cs') -Raw -Encoding UTF8
$downloaderPattern = 'public\s+static\s+Task<string>\s+DownloadAsync\s*\(\s*AdvancedSourceDefinition\s+source\s*,\s*string\s+directory\s*,\s*IProgress<AdvancedSourceProgress>\s+progress\s*,\s*CancellationToken\s+token\s*,\s*AdvancedOperationControl\s+control\s*(?:=\s*null\s*)?\)'
if (-not [regex]::IsMatch($advancedSource, $downloaderPattern)) {
    throw 'AdvancedSourceService.DownloadAsync must accept the advanced operation control parameter.'
}
if ($advancedForm -notmatch 'AdvancedSourceService\.DownloadAsync\s*\(\s*state\.Source\s*,\s*_dataDirectory\s*,\s*progress\s*,\s*ct\s*,\s*_operationControl\s*\)') {
    throw 'Advanced builder download UI and downloader method do not agree on the operation-control argument.'
}
Write-Host '[OK] Advanced source downloader: operation-control parameter and UI call match.'

