[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$EvidenceRelativePath = 'work/production/evidence/code70-daylight-pocket-capture-20261031',
    [string]$OutputRelativePath = 'docs/evidence/code70-exact-frame-stage-crosswalk-20261031/code70-cue-stage-crosswalk.csv'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath($RepositoryRoot)
$evidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $root $EvidenceRelativePath))
$runDirectory = Join-Path $evidenceRoot 'cue-probe-replays/cueprobe6-110025'
$manifestPath = Join-Path $runDirectory 'manifest.json'
$livePath = Join-Path $evidenceRoot 'live-detect.jsonl'
$sourceDirectory = Join-Path $evidenceRoot 'exact-replay-input'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

if ($manifest.mode -ne 'probe-only' -or [string]::IsNullOrWhiteSpace($manifest.report) -or
    $manifest.sourceFrames.Count -lt 1 -or $manifest.sourceFrames.Count -gt 24) {
    throw 'Expected a bounded probe-only cue-probe manifest with source frames.'
}

$reportPath = [System.IO.Path]::GetFullPath((Join-Path $runDirectory $manifest.report))
$runDirectoryPrefix = $runDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $reportPath.StartsWith($runDirectoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Replay report path escapes its run directory.'
}
$reportBytes = [System.IO.File]::ReadAllBytes($reportPath)
$reportSha256 = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($reportBytes)).ToLowerInvariant()
if (-not $reportSha256.Equals([string]$manifest.reportSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Cue-probe report bytes do not match the replay manifest.'
}
$report = [System.Text.Encoding]::UTF8.GetString($reportBytes) | ConvertFrom-Json
$samples = @($report.samples)
$observations = @($report.fullAnalysis800.observations)
if (-not $report.completed -or $samples.Count -ne $manifest.sourceFrames.Count -or
    $observations.Count -ne $samples.Count -or @($samples | Where-Object { -not $_.detectionRequested }).Count -ne 0) {
    throw 'Cue-probe sample and requested-observation coverage is inconsistent.'
}

$liveRows = @(Get-Content -LiteralPath $livePath | ForEach-Object { $_ | ConvertFrom-Json })
$crosswalk = [System.Collections.Generic.List[object]]::new()
$captureRunId = $null
$sourceScope = $null

for ($i = 0; $i -lt $samples.Count; $i++) {
    $sample = $samples[$i]
    $observation = $observations[$i]
    $manifestFrames = @($manifest.sourceFrames | Where-Object { $_.frameId -eq $sample.frameId })
    if ($manifestFrames.Count -ne 1 -or $manifestFrames[0].sha256 -ne $sample.pixelSha256) {
        throw "Replay manifest does not uniquely bind source frame $($sample.frameId)."
    }

    $receiptPath = Join-Path $sourceDirectory ($sample.frameId.ToString() + '.json')
    $pixelPath = Join-Path $sourceDirectory ($sample.frameId.ToString() + '.png')
    $receipt = Get-Content -Raw -LiteralPath $receiptPath | ConvertFrom-Json
    $indexed = $receipt.indexedFrame
    $sampleIdMatch = [regex]::Match([string]$indexed.sampleId,
        '^code70-(?<captureRunId>[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})-\d+$')
    if (-not $sampleIdMatch.Success) { throw "Source receipt has no recognized Code70 capture ID for frame $($sample.frameId)." }
    $sampleCaptureRunId = $sampleIdMatch.Groups['captureRunId'].Value
    if ($null -eq $captureRunId) { $captureRunId = $sampleCaptureRunId }
    elseif ($captureRunId -ne $sampleCaptureRunId) { throw 'Source receipts cross capture runs.' }
    $scopeSignature = '{0}|{1}|{2}' -f $indexed.scope.timeline, $indexed.scope.generation, $indexed.scope.calibration
    if ($null -eq $sourceScope) { $sourceScope = $scopeSignature }
    elseif ($sourceScope -ne $scopeSignature) { throw 'Source receipts cross timelines, generations, or calibrations.' }
    $pixelSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $pixelPath).Hash.ToLowerInvariant()
    if ($indexed.frameId -ne $sample.frameId -or $indexed.sourceTimeUs -ne $sample.sourceTimeUs -or
        $indexed.pixelSha256 -ne $sample.pixelSha256 -or $pixelSha256 -ne $sample.pixelSha256) {
        throw "Replay sample, source receipt, and PNG bytes disagree for frame $($sample.frameId)."
    }

    $liveMatches = @($liveRows | Where-Object { $_.runId -eq $sampleCaptureRunId -and $_.kind -eq 'frame' -and $_.frame.frameId -eq $sample.frameId -and $_.frame.sourceTimeUs -eq $sample.sourceTimeUs -and $_.frame.frameEvidence.sha256 -eq $sample.pixelSha256 })
    if ($liveMatches.Count -ne 1) {
        throw "Expected one physical log row bound by frame ID, PTS, and PNG hash for frame $($sample.frameId)."
    }

    if ($observation.configuredWidth -ne 800 -or $observation.circleCount -ne $observation.circles.Count) {
        throw "Unexpected width or final-circle count for frame $($sample.frameId)."
    }

    $audits = @($observation.candidateAudits)
    $blackAdd = @($audits | Where-Object { $_.stage -eq 'sparse-black-add' })
    $blackSearch = @($audits | Where-Object { $_.stage -eq 'sparse-black-search' })
    if ($blackSearch.Count -ne 1) { throw "Expected one sparse-black search summary for frame $($sample.frameId)." }
    $searchText = [string]$blackSearch[0].decision
    $seedMatch = [regex]::Match($searchText, 'ball-sized-full-table-seeds=(\d+)')
    $rankedMatch = [regex]::Match($searchText, 'ranked=(\d+)')
    $capMatch = [regex]::Match($searchText, 'fit-cap=(\d+)')
    if (-not $seedMatch.Success -or -not $rankedMatch.Success -or -not $capMatch.Success) {
        throw "Sparse-black seed summary is malformed for frame $($sample.frameId)."
    }

    $frame = $liveMatches[0].frame
    if ($frame.timeline -ne $indexed.scope.timeline -or $frame.mediaGeneration -ne $indexed.scope.generation -or $frame.calibrationId -ne $indexed.scope.calibration) {
        throw "Physical log scope differs from the receipt for frame $($sample.frameId)."
    }
    $cueRecovery = $observation.recognition.cueBallRecovery
    $crosswalk.Add([pscustomobject]@{
        CaptureRunId = $sampleCaptureRunId
        SampleId = $indexed.sampleId
        FrameId = $sample.frameId
        SourceTimeUs = $sample.sourceTimeUs
        PixelSha256 = $sample.pixelSha256
        SourceIdentityVerified = $true
        Timeline = $frame.timeline
        Generation = $frame.mediaGeneration
        CalibrationId = $frame.calibrationId
        CapturedAt = ([DateTimeOffset]$frame.capturedAt).ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
        FrameSource = $frame.frameSource
        FrameAgeMs = $frame.frameAgeMs
        LiveDetectorElapsedMs = $frame.detectorElapsedMs
        LiveFullPassElapsedMs = $frame.fullPassElapsedMs
        LiveBallCount = $frame.ballCount
        LiveRedCount = $frame.reds
        LiveYellowCount = $frame.yellows
        LiveBlackCount = $frame.black
        LiveCueCount = $frame.cue
        LiveUncertain = $frame.uncertain
        LiveLayoutUncertain = $frame.layoutUncertain
        LiveObstructed = $frame.obstructed
        LiveCheckOutcome = $frame.checkOutcome
        LiveSettledBaselineMatchesCurrent = $frame.settledBaselineMatchesCurrent
        ReplayMode = $manifest.mode
        ReplayApkSha256 = $manifest.apkSha256
        ReplayObservationIndex = $i
        ReplayReportSha256 = $reportSha256
        ReplayConfigurationComparison = $report.configurationComparison
        RecordedAnalyzerConfigurationIdentity = $report.recordedAnalyzerConfigurationIdentity
        ReplayAnalyzerConfigurationIdentity = $report.replayAnalyzerConfigurationIdentity
        ReplayConfigurationMatchesPrimary = $report.configurationMatchesPrimaryWidth
        ReplayConfigurationSettingsMatchPrimary = $report.configurationSettingsMatchPrimaryWidth
        ReplayLocalIlluminationNormalization = $report.localIlluminationNormalization
        ReplayPocketExclusionRadius = $report.pocketExclusionRadius
        ReplayMinimumHoughBoundarySupport = $report.minimumHoughBoundarySupport
        ReplayMinimumSparseColourBoundarySupport = $report.minimumSparseColourBoundarySupport
        ReplayPackedColourBoundarySearchExtentFraction = $report.packedColourBoundarySearchExtentFraction
        ReplayProduction800BoundaryExtent = $report.production800BoundaryExtent
        ReplaySparseBlackSearchExtentFraction = $report.sparseBlackSearchExtentFraction
        ReplayMinimumSparseBlackBoundarySupport = $report.minimumSparseBlackBoundarySupport
        ReplaySparseBlackCandidateLimit = $report.sparseBlackCandidateLimit
        ReplayWallMs = $sample.analysis800Metrics.wallMs
        ReplayBallCount = $observation.circleCount
        ReplayRedCount = @($observation.circles | Where-Object { $_.kind -eq 'red' }).Count
        ReplayYellowCount = @($observation.circles | Where-Object { $_.kind -eq 'yellow' }).Count
        ReplayBlackCount = @($observation.circles | Where-Object { $_.kind -eq 'black' }).Count
        ReplayCueCount = @($observation.circles | Where-Object { $_.kind -eq 'cue' }).Count
        ReplayUncertain = $observation.uncertain
        ReplayLayoutUncertain = $observation.layoutUncertain
        ReplayObstructed = $observation.obstructed
        CueRecoveryReason = $cueRecovery.reason
        CueFirstPassCount = $cueRecovery.initialCueBallCount
        CueNeutralBodyCandidates = $cueRecovery.neutralBodyCandidates
        CueRecoveredNeutralBodies = $cueRecovery.neutralBodiesRecovered
        BlackBallSizedSeeds = [int]$seedMatch.Groups[1].Value
        BlackRankedSeeds = [int]$rankedMatch.Groups[1].Value
        BlackFitCap = [int]$capMatch.Groups[1].Value
        BlackSeedAppearanceClassifiedBlack = @($audits | Where-Object {
            $_.stage -eq 'sparse-black-seed-body-probe' -and $_.classifiedKind -eq 'black'
        }).Count
        BlackRejectedNoBoundaryFit = @($blackAdd | Where-Object { $_.decision -eq 'rejected-no-boundary-fit' }).Count
        BlackRejectedInsufficientBoundarySupport = @($blackAdd | Where-Object { $_.decision -eq 'rejected-insufficient-boundary-support' }).Count
        BlackRejectedClassification = @($blackAdd | Where-Object { $_.decision -eq 'rejected-classification' }).Count
        BlackEdgeProbesClassifiedCue = @($audits | Where-Object {
            $_.stage -eq 'sparse-black-dark-edge-probe' -and $_.classifiedKind -eq 'cue'
        }).Count
        HoughCandidatesRejectedAsDuplicate = @($audits | Where-Object {
            $_.stage -eq 'hough' -and $_.decision -eq 'rejected-duplicate-before-fit'
        }).Count
        ColourPeaksRejectedAsDuplicate = @($audits | Where-Object {
            $_.decision -eq 'rejected-duplicate-colour-peak'
        }).Count
        RawCircleCount = $observation.recognition.rawCircleCount
        FinalCircleCount = $observation.recognition.finalCircleCount
    })
}

$outputPath = [System.IO.Path]::GetFullPath((Join-Path $root $OutputRelativePath))
$outputDirectory = [System.IO.Path]::GetDirectoryName($outputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$crosswalk | Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding utf8
Write-Output "Wrote $outputPath"
Write-Output "Replay report SHA-256: $reportSha256"
