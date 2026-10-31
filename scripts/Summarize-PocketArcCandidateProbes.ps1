<#
.SYNOPSIS
Flattens ranked sparse-black pocket-arc diagnostics into CSV or JSON.

.DESCRIPTION
Reads only the explicitly supplied replay run directories and writes one row
per ranked candidate seed, preserving run and frame provenance. Frames with no
ranked-probe field are emitted as unavailable rows, so older reports cannot be
mistaken for negative results. A run-level summary counts fits, diagnostic arc
passes, and black classifications without treating any diagnostic as a
detector or table-state result.

Each invocation has one explicit CaseLabel. Keep separate lighting conditions,
capture sessions, and positive/negative controls in separate invocations; the
script does not discover or merge evidence recursively.

.PARAMETER RunDirectory
One or more exact replay run directories. Each must contain exactly one
o08-replay-proof*.json file.

.PARAMETER CaseLabel
Required label copied to every row (for example pocket-black-daylight or
empty-pocket-control). It is caller-supplied and is not inferred from paths.

.PARAMETER Format
Csv (default) or Json.

.PARAMETER OutputPath
Optional path for the flattened candidate rows. Without it, rows are written
to the pipeline.

.PARAMETER SummaryOutputPath
Optional path for per-run counts. Only used in Csv mode; Json output includes
both the row and run arrays in one document.

.EXAMPLE
./scripts/Summarize-PocketArcCandidateProbes.ps1 `
  -CaseLabel 'black-pocket-daylight' `
  -RunDirectory 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/cap32seedarcpos-120000' `
  -OutputPath 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/black-pocket-daylight-probes.csv' `
  -SummaryOutputPath 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/black-pocket-daylight-runs.csv'

.EXAMPLE
./scripts/Summarize-PocketArcCandidateProbes.ps1 `
  -CaseLabel 'empty-pocket-control' `
  -RunDirectory 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/cap32seedarcneg-120010' `
  -Format Json -OutputPath 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/empty-pocket-probes.json'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]] $RunDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $CaseLabel,

    [ValidateSet('Csv', 'Json')]
    [string] $Format = 'Csv',

    [string] $OutputPath,

    [string] $SummaryOutputPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($CaseLabel)) { throw 'CaseLabel must be a meaningful, explicit label.' }
if ($Format -eq 'Json' -and $SummaryOutputPath) {
    throw 'Json output already contains both candidate rows and run summaries; omit SummaryOutputPath.'
}
if ($OutputPath -and $SummaryOutputPath -and
    [string]::Equals([IO.Path]::GetFullPath($OutputPath), [IO.Path]::GetFullPath($SummaryOutputPath), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath and SummaryOutputPath must refer to different files.'
}

function Get-Value {
    param([AllowNull()] [object] $InputObject, [Parameter(Mandatory = $true)] [string] $Name)
    if ($null -eq $InputObject) { return [pscustomobject]@{ Found = $false; Value = $null } }
    if ($InputObject -is [System.Collections.IDictionary]) {
        foreach ($key in $InputObject.Keys) {
            if ([string]::Equals([string]$key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                return [pscustomobject]@{ Found = $true; Value = $InputObject[$key] }
            }
        }
        return [pscustomobject]@{ Found = $false; Value = $null }
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return [pscustomobject]@{ Found = $false; Value = $null } }
    return [pscustomobject]@{ Found = $true; Value = $property.Value }
}

function Get-PathValue {
    param([AllowNull()] [object] $InputObject, [Parameter(Mandatory = $true)] [string] $Path)
    $current = $InputObject
    foreach ($part in $Path.Split('.')) {
        $result = Get-Value -InputObject $current -Name $part
        if (-not $result.Found) { return [pscustomobject]@{ Found = $false; Value = $null } }
        $current = $result.Value
    }
    return [pscustomobject]@{ Found = $true; Value = $current }
}

function Get-FieldValue {
    param([AllowNull()] [object] $InputObject, [Parameter(Mandatory = $true)] [string] $Path)
    $result = Get-PathValue -InputObject $InputObject -Path $Path
    if ($result.Found -and $null -ne $result.Value) { return $result.Value }
    return $null
}

function Get-Count {
    param([AllowNull()] [object] $Analysis, [Parameter(Mandatory = $true)] [string] $Kind)
    $value = Get-PathValue -InputObject $Analysis -Path "counts.$Kind"
    if ($value.Found) { return $value.Value }
    return $null
}

function Resolve-Run {
    param([Parameter(Mandatory = $true)] [string] $Directory)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Run directory not found: '$Directory'." }
    $resolved = (Resolve-Path -LiteralPath $Directory).Path
    $reports = @(Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json')
    if ($reports.Count -ne 1) {
        throw "Expected exactly one o08-replay-proof*.json report in '$resolved'; found $($reports.Count)."
    }
    $report = Get-Content -LiteralPath $reports[0].FullName -Raw | ConvertFrom-Json
    if ($null -eq $report.samples) { throw "Replay report has no samples array: '$($reports[0].FullName)'." }
    $manifestPath = Join-Path $resolved 'manifest.json'
    $manifest = $null
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    $runId = Get-Value -InputObject $manifest -Name 'run'
    if (-not $runId.Found -or [string]::IsNullOrWhiteSpace([string]$runId.Value)) { $runId = [pscustomobject]@{ Value = Split-Path -Leaf $resolved } }
    return [pscustomobject]@{ Directory = $resolved; ReportPath = $reports[0].FullName; Report = $report; RunId = [string]$runId.Value }
}

function Write-OutputFile {
    param([Parameter(Mandatory = $true)] [string] $Path, [Parameter(Mandatory = $true)] [string] $Content)
    $full = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($full)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($full, $Content, [Text.UTF8Encoding]::new($false))
}

$runs = [System.Collections.Generic.List[object]]::new()
$seenDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($directory in $RunDirectory) {
    $run = Resolve-Run -Directory $directory
    if (-not $seenDirectories.Add($run.Directory)) { throw "Run directory was supplied more than once: '$($run.Directory)'." }
    $runs.Add($run)
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($run in $runs) {
    foreach ($sample in @($run.Report.samples)) {
        $frameId = Get-Value -InputObject $sample -Name 'frameId'
        $sourceTimeUs = Get-Value -InputObject $sample -Name 'sourceTimeUs'
        $pixelHash = Get-Value -InputObject $sample -Name 'pixelSha256'
        $analysis = Get-PathValue -InputObject $sample -Path 'width1000.analysis'
        $trace = Get-PathValue -InputObject $sample -Path 'width1000.darkMaskTrace'
        $label = Get-PathValue -InputObject $trace.Value -Path 'manualBlackLabel.sourcePixel'
        $expectedRadius = Get-PathValue -InputObject $trace.Value -Path 'labelledBallRegion.expectedRadius'
        $probesProperty = Get-Value -InputObject $trace.Value -Name 'rankedSparseBlackPocketArcProbes'
        $probeArray = @()
        if ($probesProperty.Found -and $null -ne $probesProperty.Value) { $probeArray = @($probesProperty.Value) }

        if ($probeArray.Count -eq 0) {
            $probeStatus = if (-not $probesProperty.Found) { 'ranked-probe-field-unavailable' } else { 'no-ranked-candidate-seeds' }
            $rows.Add([pscustomobject][ordered]@{
                CaseLabel = $CaseLabel; RunId = $run.RunId; RunDirectory = $run.Directory; ReportPath = $run.ReportPath
                FrameId = if ($frameId.Found) { $frameId.Value } else { $null }
                SourceTimeUs = if ($sourceTimeUs.Found) { $sourceTimeUs.Value } else { $null }
                PixelSha256 = if ($pixelHash.Found) { $pixelHash.Value } else { $null }
                DetectorCircleCount1000 = if ($analysis.Found) { Get-FieldValue $analysis.Value 'circleCount' } else { $null }
                DetectorCueCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'cue' } else { $null }
                DetectorRedCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'red' } else { $null }
                DetectorYellowCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'yellow' } else { $null }
                DetectorBlackCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'black' } else { $null }
                DetectorUncertain1000 = if ($analysis.Found) { Get-FieldValue $analysis.Value 'uncertain' } else { $null }
                DetectorObstructed1000 = if ($analysis.Found) { Get-FieldValue $analysis.Value 'obstructed' } else { $null }
                LabelX = if ($label.Found) { Get-FieldValue $label.Value 'x' } else { $null }
                LabelY = if ($label.Found) { Get-FieldValue $label.Value 'y' } else { $null }
                ExpectedRadius = if ($expectedRadius.Found) { $expectedRadius.Value } else { $null }
                ProbeStatus = $probeStatus
                ProbeExecuted = $null; ProbeSkipReason = $null
                TargetFitAcceptanceRadiusInBallRadii = $null; MaximumSeedToFitOffsetFraction = $null
                MaximumSeedDistanceFromLabelRadii = $null
                CandidateRank = $null; CandidateDecision = $null; ProductionAuditKind = $null
                SeedX = $null; SeedY = $null; SeedRadius = $null; SeedDistanceFromLabel = $null
                ArcFitAvailable = $null; FitX = $null; FitY = $null; FitRadius = $null; FitDistanceFromLabel = $null
                CandidateToSeedOffset = $null; CandidateToSeedOffsetFraction = $null
                VisibleSectorCount = $null; SupportedVisibleSectorCount = $null; GlobalSupportedSectorCount = $null
                GlobalSupportFraction = $null; LongestSupportedRun = $null; SupportedArcSpanDegrees = $null
                DiagnosticArcChecksPass = $null; ClassifierAvailable = $null; ClassifierKind = $null
                ClassifierPattern = $null; ClassifierQuality = $null; ClassifierQualityIsProbability = $null
                UsableBodySampleCount = $null; UsableRimSampleCount = $null
            })
            continue
        }

        foreach ($probe in $probeArray) {
            $seed = Get-Value -InputObject $probe -Name 'seedCentre'
            $fit = Get-Value -InputObject $probe -Name 'arcFitCentre'
            $classifier = Get-Value -InputObject $probe -Name 'classifier'
            $probeExecuted = Get-Value -InputObject $probe -Name 'probeExecuted'
            $probeWasSkipped = $probeExecuted.Found -and $probeExecuted.Value -eq $false
            $classifierEvidence = Get-Value -InputObject $classifier.Value -Name 'evidence'
            # O08 stores the classified kind and sampling evidence under
            # classifier.evidence. Retain compatibility with early reports
            # that wrote those fields directly below classifier.
            $classifierFields = if ($classifierEvidence.Found) { $classifierEvidence.Value } else { $classifier.Value }
            $rows.Add([pscustomobject][ordered]@{
                CaseLabel = $CaseLabel; RunId = $run.RunId; RunDirectory = $run.Directory; ReportPath = $run.ReportPath
                FrameId = if ($frameId.Found) { $frameId.Value } else { $null }
                SourceTimeUs = if ($sourceTimeUs.Found) { $sourceTimeUs.Value } else { $null }
                PixelSha256 = if ($pixelHash.Found) { $pixelHash.Value } else { $null }
                DetectorCircleCount1000 = if ($analysis.Found) { Get-FieldValue $analysis.Value 'circleCount' } else { $null }
                DetectorCueCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'cue' } else { $null }
                DetectorRedCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'red' } else { $null }
                DetectorYellowCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'yellow' } else { $null }
                DetectorBlackCount1000 = if ($analysis.Found) { Get-Count $analysis.Value 'black' } else { $null }
                DetectorUncertain1000 = if ($analysis.Found) { Get-FieldValue $analysis.Value 'uncertain' } else { $null }
                DetectorObstructed1000 = if ($analysis.Found) { Get-FieldValue $analysis.Value 'obstructed' } else { $null }
                LabelX = if ($label.Found) { Get-FieldValue $label.Value 'x' } else { $null }
                LabelY = if ($label.Found) { Get-FieldValue $label.Value 'y' } else { $null }
                ExpectedRadius = if ($expectedRadius.Found) { $expectedRadius.Value } else { $null }
                ProbeStatus = if ($probeWasSkipped) { 'target-reach-skipped' } else { 'ranked-candidate-probe' }
                ProbeExecuted = if ($probeExecuted.Found) { $probeExecuted.Value } else { $true }
                ProbeSkipReason = Get-FieldValue $probe 'probeSkipReason'
                TargetFitAcceptanceRadiusInBallRadii = Get-FieldValue $probe 'targetFitAcceptanceRadiusInBallRadii'
                MaximumSeedToFitOffsetFraction = Get-FieldValue $probe 'maximumSeedToFitOffsetFraction'
                MaximumSeedDistanceFromLabelRadii = Get-FieldValue $probe 'maximumSeedDistanceFromLabelRadii'
                CandidateRank = Get-FieldValue $probe 'candidateRank'
                CandidateDecision = Get-FieldValue $probe 'decision'
                ProductionAuditKind = Get-FieldValue $probe 'classifiedKindFromProductionAudit'
                SeedX = if ($seed.Found) { Get-FieldValue $seed.Value 'x' } else { $null }
                SeedY = if ($seed.Found) { Get-FieldValue $seed.Value 'y' } else { $null }
                SeedRadius = if ($seed.Found) { Get-FieldValue $seed.Value 'radius' } else { $null }
                SeedDistanceFromLabel = Get-FieldValue $probe 'distanceFromLabel'
                ArcFitAvailable = Get-FieldValue $probe 'arcFitAvailable'
                FitX = if ($fit.Found) { Get-FieldValue $fit.Value 'x' } else { $null }
                FitY = if ($fit.Found) { Get-FieldValue $fit.Value 'y' } else { $null }
                FitRadius = if ($fit.Found) { Get-FieldValue $fit.Value 'radius' } else { $null }
                FitDistanceFromLabel = Get-FieldValue $probe 'arcFitDistanceFromLabel'
                CandidateToSeedOffset = Get-FieldValue $probe 'candidateToSeedOffset'
                CandidateToSeedOffsetFraction = Get-FieldValue $probe 'candidateToSeedOffsetFraction'
                VisibleSectorCount = Get-FieldValue $probe 'visibleSectorCount'
                SupportedVisibleSectorCount = Get-FieldValue $probe 'supportedVisibleSectorCount'
                GlobalSupportedSectorCount = Get-FieldValue $probe 'globalSupportedSectorCount'
                GlobalSupportFraction = Get-FieldValue $probe 'globalSupportFraction'
                LongestSupportedRun = Get-FieldValue $probe 'longestContiguousSupportedRun'
                SupportedArcSpanDegrees = Get-FieldValue $probe 'supportedArcSpanDegrees'
                DiagnosticArcChecksPass = Get-FieldValue $probe 'diagnosticArcChecksPass'
                ClassifierAvailable = Get-FieldValue $classifier.Value 'available'
                ClassifierKind = Get-FieldValue $classifierFields 'kind'
                ClassifierPattern = Get-FieldValue $classifierFields 'pattern'
                ClassifierQuality = Get-FieldValue $classifierFields 'quality'
                ClassifierQualityIsProbability = Get-FieldValue $classifierFields 'qualityIsProbability'
                UsableBodySampleCount = Get-FieldValue $classifierFields 'usableBodySampleCount'
                UsableRimSampleCount = Get-FieldValue $classifierFields 'usableRimSampleCount'
            })
        }
    }
}

$probeRows = @($rows.ToArray())
$runSummary = foreach ($run in $runs) {
    $runRows = @($probeRows | Where-Object { $_.RunDirectory -eq $run.Directory })
    $candidateRows = @($runRows | Where-Object { $_.ProbeStatus -in @('ranked-candidate-probe', 'target-reach-skipped') })
    $actualProbes = @($candidateRows | Where-Object { $_.ProbeStatus -eq 'ranked-candidate-probe' })
    $skippedProbes = @($candidateRows | Where-Object { $_.ProbeStatus -eq 'target-reach-skipped' })
    [pscustomobject][ordered]@{
        CaseLabel = $CaseLabel
        RunId = $run.RunId
        RunDirectory = $run.Directory
        ReportPath = $run.ReportPath
        FrameCount = @($run.Report.samples).Count
        FramesWithRankedProbeField = @($runRows | Where-Object { $_.ProbeStatus -ne 'ranked-probe-field-unavailable' } | Select-Object -ExpandProperty FrameId -Unique).Count
        FramesWithoutRankedProbeField = @($runRows | Where-Object ProbeStatus -eq 'ranked-probe-field-unavailable').Count
        RankedCandidateProbeCount = $candidateRows.Count
        ExecutedCandidateProbeCount = $actualProbes.Count
        TargetReachSkippedSeedCount = $skippedProbes.Count
        ArcFitCount = @($actualProbes | Where-Object ArcFitAvailable -eq $true).Count
        DiagnosticArcPassCount = @($actualProbes | Where-Object DiagnosticArcChecksPass -eq $true).Count
        ClassifierBlackCount = @($actualProbes | Where-Object { $_.ClassifierAvailable -eq $true -and [string]::Equals([string]$_.ClassifierKind, 'black', [System.StringComparison]::OrdinalIgnoreCase) }).Count
        DiagnosticOnly = $true
    }
}

if ($Format -eq 'Json') {
    $content = ConvertTo-Json -InputObject ([pscustomobject]@{ schemaVersion = 1; caseLabel = $CaseLabel; diagnosticOnly = $true; runs = @($runSummary); probes = $probeRows }) -Depth 20
    if ($OutputPath) { Write-OutputFile -Path $OutputPath -Content $content } else { Write-Output $content }
} else {
    $content = (@($probeRows) | ConvertTo-Csv -NoTypeInformation) -join [Environment]::NewLine
    if ($OutputPath) { Write-OutputFile -Path $OutputPath -Content $content } else { Write-Output $content }
    if ($SummaryOutputPath) {
        $summaryContent = (@($runSummary) | ConvertTo-Csv -NoTypeInformation) -join [Environment]::NewLine
        Write-OutputFile -Path $SummaryOutputPath -Content $summaryContent
    }
}

foreach ($summary in $runSummary) {
    Write-Host ("{0}: {1}; frames={2}; ranked-seeds={3}; executed={4}; reach-skipped={5}; fits={6}; arc-passes={7}; classifier-black={8}; diagnostic only" -f
        $summary.RunId, $CaseLabel, $summary.FrameCount, $summary.RankedCandidateProbeCount,
        $summary.ExecutedCandidateProbeCount, $summary.TargetReachSkippedSeedCount,
        $summary.ArcFitCount, $summary.DiagnosticArcPassCount, $summary.ClassifierBlackCount)
}
