<#
.SYNOPSIS
  Copies SkyEye's git-ignored evidence folder (work/) to a backup location and verifies it.

.DESCRIPTION
  Mirrors-without-deleting: files are added or updated at the destination, nothing there
  is removed. After copying, every file under work/ is compared by size, and every media
  file listed in work/evidence-audit/inventory.csv is compared by SHA-1. A manifest of the
  copy is written to <Destination>/backup-manifest.csv and a summary to backup-report.txt.

.EXAMPLE
  ./scripts/Backup-WorkEvidence.ps1 -Destination D:\SkyEye
#>
param(
    [Parameter(Mandatory)] [string] $Destination,
    [switch] $SkipHash
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'work'
$target = Join-Path $Destination 'work'
if (-not (Test-Path $source)) { throw "No work/ folder at $source" }
New-Item -ItemType Directory -Force -Path $target | Out-Null

$log = Join-Path $Destination 'robocopy.log'
Write-Host "Copying $source -> $target (log: $log)"
robocopy $source $target /E /COPY:DAT /DCOPY:T /R:1 /W:1 /NP /NFL /NDL /MT:8 /LOG:$log | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE; see $log" }

Write-Host 'Comparing sizes...'
$missing = 0; $sizeMismatch = 0; $count = 0; $bytes = 0
$rows = foreach ($f in Get-ChildItem -LiteralPath $source -Recurse -File) {
    $rel = $f.FullName.Substring($source.Length + 1)
    $dst = Join-Path $target $rel
    $count++; $bytes += $f.Length
    if (-not (Test-Path -LiteralPath $dst)) { $missing++; $state = 'missing' }
    elseif ((Get-Item -LiteralPath $dst).Length -ne $f.Length) { $sizeMismatch++; $state = 'size-mismatch' }
    else { $state = 'ok' }
    [pscustomobject]@{ path = "work/$($rel -replace '\\','/')"; bytes = $f.Length; state = $state }
}
$rows | Export-Csv -NoTypeInformation -Path (Join-Path $Destination 'backup-manifest.csv')

$hashChecked = 0; $hashBad = 0
if (-not $SkipHash) {
    $inventory = Join-Path $source 'evidence-audit/inventory.csv'
    if (Test-Path $inventory) {
        Write-Host 'Verifying SHA-1 of inventoried media files (takes a few minutes)...'
        foreach ($r in Import-Csv $inventory) {
            if (-not $r.path.StartsWith('work/') -or -not $r.sha1) { continue }
            $dst = Join-Path $Destination ($r.path -replace '/','\')
            if (-not (Test-Path -LiteralPath $dst)) { $hashBad++; continue }
            $hashChecked++
            if ((Get-FileHash -Algorithm SHA1 -LiteralPath $dst).Hash.ToLower() -ne $r.sha1) { $hashBad++ }
        }
    }
}

$report = @(
    "Backup of $source to $target, $(Get-Date -Format s)",
    "Files: $count  Bytes: $bytes",
    "Missing at destination: $missing  Size mismatches: $sizeMismatch",
    "SHA-1 checked: $hashChecked  SHA-1 failures: $hashBad",
    $(if ($missing + $sizeMismatch + $hashBad -eq 0) { 'RESULT: PASS' } else { 'RESULT: FAIL' })
)
$report | Set-Content (Join-Path $Destination 'backup-report.txt')
$report | ForEach-Object { Write-Host $_ }
