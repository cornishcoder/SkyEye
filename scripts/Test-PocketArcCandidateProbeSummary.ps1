<#
.SYNOPSIS
Runs a regression check for flattening the real O08 nested classifier evidence.

.DESCRIPTION
Copies the checked-in O08-shaped report fixture into a temporary run folder,
runs the production summarizer, and asserts the nested evidence fields and
summary counts. It performs no ADB or camera actions.
#>
[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repoRoot 'tests/fixtures/pocket-arc/ranked-sparse-black-probe-report.json'
if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw "Fixture not found: $fixture" }

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('skyeye-pocketarc-summary-regression-' + [guid]::NewGuid().ToString('N'))
$runDirectory = Join-Path $tempRoot 'fixture-run'
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
Copy-Item -LiteralPath $fixture -Destination (Join-Path $runDirectory 'o08-replay-proof-fixture-pocketarc.json')
[IO.File]::WriteAllText((Join-Path $runDirectory 'manifest.json'), '{"run":"fixture-run"}', [Text.UTF8Encoding]::new($false))

$probeCsv = Join-Path $tempRoot 'probes.csv'
$summaryCsv = Join-Path $tempRoot 'summary.csv'
& (Join-Path $PSScriptRoot 'Summarize-PocketArcCandidateProbes.ps1') `
    -CaseLabel 'fixture-black-pocket' `
    -RunDirectory $runDirectory `
    -OutputPath $probeCsv `
    -SummaryOutputPath $summaryCsv | Out-Null

$probeRows = @(Import-Csv -LiteralPath $probeCsv)
$summaryRows = @(Import-Csv -LiteralPath $summaryCsv)
if ($probeRows.Count -ne 2) { throw "Expected two flattened candidate rows, found $($probeRows.Count)." }
$blackRow = $probeRows | Where-Object CandidateRank -eq '3'
$noFitRow = $probeRows | Where-Object CandidateRank -eq '4'
if ($blackRow.ClassifierKind -ne 'black' -or $blackRow.ClassifierPattern -ne 'solid-dark' -or
    $blackRow.ClassifierQuality -ne '0.9' -or $blackRow.ClassifierQualityIsProbability -ne 'False' -or
    $blackRow.UsableBodySampleCount -ne '500' -or $blackRow.UsableRimSampleCount -ne '90') {
    throw 'Nested classifier.evidence values did not survive flattening.'
}
if ($noFitRow.ClassifierAvailable -ne 'False' -or -not [string]::IsNullOrWhiteSpace($noFitRow.ClassifierKind)) {
    throw 'Unavailable nested classifier evidence was incorrectly reported as a classification.'
}
if ($summaryRows.Count -ne 1 -or $summaryRows[0].RankedCandidateProbeCount -ne '2' -or
    $summaryRows[0].ArcFitCount -ne '1' -or $summaryRows[0].ClassifierBlackCount -ne '1') {
    throw 'Run summary did not count the one available black classification correctly.'
}

Write-Output 'Pocket arc candidate summary regression passed.'
