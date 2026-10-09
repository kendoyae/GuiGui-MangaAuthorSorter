$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$languageFiles = Get-ChildItem (Join-Path $root 'Languages') -Filter '*.json'
if ($languageFiles.Count -eq 0) { throw 'No language packs found.' }

$sets = @{}
foreach ($file in $languageFiles) {
    $json = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $json.Strings) { throw "Language pack has no Strings object: $($file.Name)" }
    $keys = @($json.Strings.PSObject.Properties.Name)
    $sets[$file.Name] = @{} 
    foreach ($key in $keys) { $sets[$file.Name][$key] = $true }
}

$referenceName = '_template.json'
if (-not $sets.ContainsKey($referenceName)) { $referenceName = $languageFiles[0].Name }
$reference = $sets[$referenceName]
foreach ($name in $sets.Keys) {
    $missing = @($reference.Keys | Where-Object { -not $sets[$name].ContainsKey($_) })
    $extra = @($sets[$name].Keys | Where-Object { -not $reference.ContainsKey($_) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "Language keys differ in $name. Missing: $($missing -join ', '); Extra: $($extra -join ', ')"
    }
}

$called = New-Object 'System.Collections.Generic.HashSet[string]'
Get-ChildItem $root -Recurse -Filter '*.cs' | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
} | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($text, '\bL(?:F)?\("([^"]+)"')) {
        [void]$called.Add($match.Groups[1].Value)
    }
}
$unknown = @($called | Where-Object { -not $reference.ContainsKey($_) })
if ($unknown.Count -gt 0) { throw "Language keys used by code are missing: $($unknown -join ', ')" }
# Keep the built-in C# dictionaries in sync with external JSON packs.
# JSON-only validation cannot detect duplicate keys in Dictionary initializers,
# which throw ArgumentException during LanguageManager construction.
$languageManagerPath = Join-Path $root 'Services\LanguageManager.cs'
$germanManagerPath = Join-Path $root 'Services\GermanLanguagePack.cs'
$managerText = Get-Content -LiteralPath $languageManagerPath -Raw -Encoding UTF8
$zhMarker = 'private static Dictionary<string, string> CreateZhStrings()'
$enMarker = 'private static Dictionary<string, string> CreateEnStrings()'
$zhStart = $managerText.IndexOf($zhMarker, [StringComparison]::Ordinal)
$enStart = $managerText.IndexOf($enMarker, [StringComparison]::Ordinal)
if ($zhStart -lt 0 -or $enStart -le $zhStart) {
    throw 'Cannot locate built-in Chinese and English language dictionaries.'
}

function Test-BuiltInLanguageKeys($languageName, $sourceText, $pattern) {
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    $duplicates = New-Object 'System.Collections.Generic.List[string]'
    foreach ($match in [regex]::Matches($sourceText, $pattern)) {
        $key = $match.Groups[1].Value
        if (-not $seen.Add($key)) { [void]$duplicates.Add($key) }
    }
    if ($seen.Count -eq 0) { throw "No built-in keys found for $languageName" }
    if ($duplicates.Count -gt 0) {
        throw "Duplicate built-in language keys in ${languageName}: $($duplicates -join ', ')"
    }
    $officialName = $languageName + '.json'
    if (-not $sets.ContainsKey($officialName)) { throw "Missing official language pack: $officialName" }
    $missing = @($sets[$officialName].Keys | Where-Object { -not $seen.Contains($_) })
    $extra = @($seen | Where-Object { -not $sets[$officialName].ContainsKey($_) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "Built-in keys differ in ${languageName}. Missing: $($missing -join ', '); Extra: $($extra -join ', ')"
    }
    Write-Host "[OK] Built-in ${languageName}: $($seen.Count) unique keys"
}

$initializerPattern = '(?m)^\s*\{\s*"([A-Za-z0-9_.-]+)"\s*,'
Test-BuiltInLanguageKeys 'zh-CN' $managerText.Substring($zhStart, $enStart - $zhStart) $initializerPattern
Test-BuiltInLanguageKeys 'en-US' $managerText.Substring($enStart) $initializerPattern
$germanText = Get-Content -LiteralPath $germanManagerPath -Raw -Encoding UTF8
$assignmentPattern = '(?m)^\s*d\["([A-Za-z0-9_.-]+)"\]\s*='
Test-BuiltInLanguageKeys 'de-DE' $germanText $assignmentPattern

Write-Host "[OK] Language packs: $($languageFiles.Count), keys: $($reference.Count), referenced: $($called.Count)"
