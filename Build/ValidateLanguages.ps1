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
Write-Host "[OK] Language packs: $($languageFiles.Count), keys: $($reference.Count), referenced: $($called.Count)"
