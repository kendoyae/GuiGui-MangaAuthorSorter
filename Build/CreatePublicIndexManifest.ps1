param(
    [Parameter(Mandatory=$true)][string]$DatabasePath,
    [Parameter(Mandatory=$true)][string]$DownloadUrl,
    [Parameter(Mandatory=$true)][string]$Revision,
    [string]$OutputPath = '.\public-index-manifest.json'
)
$ErrorActionPreference = 'Stop'
$database = (Resolve-Path -LiteralPath $DatabasePath).Path
if (-not $DownloadUrl.StartsWith('https://')) { throw 'Use an HTTPS download URL.' }
if ($Revision.Length -gt 128 -or [string]::IsNullOrWhiteSpace($Revision)) { throw 'Revision must be nonempty and at most 128 chars.' }
$info = Get-Item -LiteralPath $database
if ($info.Length -lt 8192) { throw 'Database appears empty.' }
$manifest = [ordered]@{
    schemaVersion = 4
    revision = $Revision
    url = $DownloadUrl
    size = [long]$info.Length
    sha256 = (Get-FileHash -LiteralPath $database -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Manifest generated: $OutputPath"
Write-Host "Upload both the exact verified SQLite file and this manifest to their configured HTTPS locations."
