<#
.SYNOPSIS
Compares regular sparse-black candidate fits with pocket-aware arc fits on the same ranked seeds.

.DESCRIPTION
Reads existing O08 replay reports only. It fail-closes on frame/hash/seed/configuration
mismatches, compares fit centres and the distinct support contracts, reports near-duplicate
pocket-arc hypotheses, and can check a target-pruned replay against its full-sweep source.
The tool is diagnostic-only: it does not execute detector analysis or affect production output.

.EXAMPLE
./scripts/Compare-PocketArcFitModels.ps1 `
  -RegularRunDirectory 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/seed75lim32/seed75lim32pos-132044' `
  -PocketArcRunDirectory 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/seed75fullarc/seed75fullarcpos-133220' `
  -TargetedRunDirectory 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/seed75target/seed75targetpos-132941' `
  -CaseLabel 'black-present' `
  -SummaryJsonPath 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/fit-model-comparison/black-present.json' `
  -RowsCsvPath 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/fit-model-comparison/black-present.csv'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $RegularRunDirectory,
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $PocketArcRunDirectory,
    [string] $TargetedRunDirectory,
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $CaseLabel,
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $SummaryJsonPath,
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $RowsCsvPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-Value {
    param([AllowNull()] [object] $InputObject, [Parameter(Mandatory = $true)] [string] $Name)
    if ($null -eq $InputObject) { return [pscustomobject]@{ Found = $false; Value = $null } }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return [pscustomobject]@{ Found = $false; Value = $null } }
    return [pscustomobject]@{ Found = $true; Value = $property.Value }
}

function Get-PathValue {
    param([AllowNull()] [object] $InputObject, [Parameter(Mandatory = $true)] [string] $Path)
    $current = $InputObject
    foreach ($part in $Path.Split('.')) {
        $value = Get-Value -InputObject $current -Name $part
        if (-not $value.Found) { return [pscustomobject]@{ Found = $false; Value = $null } }
        $current = $value.Value
    }
    return [pscustomobject]@{ Found = $true; Value = $current }
}

function Read-Run {
    param([Parameter(Mandatory = $true)] [string] $Directory, [Parameter(Mandatory = $true)] [string] $Label)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "$Label run directory not found: '$Directory'." }
    $resolved = (Resolve-Path -LiteralPath $Directory).Path
    $files = @(Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json')
    if ($files.Count -ne 1) { throw "$Label run must contain exactly one o08-replay-proof*.json; found $($files.Count) in '$resolved'." }
    $report = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    if ($report.completed -ne $true -or $null -eq $report.samples) { throw "$Label report is incomplete or has no samples: '$($files[0].FullName)'." }
    return [pscustomobject]@{ Directory = $resolved; ReportPath = $files[0].FullName; Report = $report }
}

function Get-CanonicalConfig {
    param([Parameter(Mandatory = $true)] [object] $Report, [Parameter(Mandatory = $true)] [string] $Label)
    $identity = Get-Value -InputObject $Report -Name 'replayAnalyzerConfigurationIdentity'
    if (-not $identity.Found -or [string]::IsNullOrWhiteSpace([string]$identity.Value)) { throw "$Label has no replay analyzer configuration identity." }
    $config = ConvertFrom-Json -InputObject ([string]$identity.Value)
    if ($null -eq $config.Settings -or $null -eq $config.Settings.Width) { throw "$Label has no analyzer settings width." }
    # Reports may use different primary widths while carrying the same width-1000 replay rows.
    # Normalize only Width; any other setting or algorithm difference is a hard mismatch.
    $config.Settings.Width = 1000
    return ConvertTo-Json -InputObject $config -Depth 30 -Compress
}

function New-FrameIndex {
    param([Parameter(Mandatory = $true)] [object[]] $Samples, [Parameter(Mandatory = $true)] [string] $Label)
    $index = @{}
    foreach ($sample in $Samples) {
        if ($null -eq $sample.frameId -or [string]::IsNullOrWhiteSpace([string]$sample.pixelSha256)) { throw "$Label has a frame without ID or source pixel hash." }
        $key = [string]$sample.frameId
        if ($index.ContainsKey($key)) { throw "$Label contains duplicate frame ID $key." }
        $index[$key] = $sample
    }
    return $index
}

function New-ProbeIndex {
    param([Parameter(Mandatory = $true)] [object] $Sample, [Parameter(Mandatory = $true)] [string] $Label, [Parameter(Mandatory = $true)] [string] $FrameId)
    $result = Get-PathValue -InputObject $Sample -Path 'width1000.darkMaskTrace.rankedSparseBlackPocketArcProbes'
    if (-not $result.Found -or $null -eq $result.Value) { throw "$Label frame $FrameId has no ranked-seed trace." }
    $index = @{}
    foreach ($probe in @($result.Value)) {
        if ($null -eq $probe.candidateRank) { throw "$Label frame $FrameId has a seed without candidateRank." }
        $key = [string]$probe.candidateRank
        if ($index.ContainsKey($key)) { throw "$Label frame $FrameId has duplicate seed rank $key." }
        $index[$key] = $probe
    }
    return $index
}

function Test-SameSeed {
    param([Parameter(Mandatory = $true)] [object] $Left, [Parameter(Mandatory = $true)] [object] $Right, [Parameter(Mandatory = $true)] [string] $Context)
    foreach ($axis in @('x', 'y')) {
        if ([Math]::Abs([double]$Left.seedCentre.$axis - [double]$Right.seedCentre.$axis) -gt 0.000001) { throw "$Context seed $axis differs." }
    }
    if ([Math]::Abs([double]$Left.seedRadius - [double]$Right.seedRadius) -gt 0.000001) { throw "$Context seed radius differs." }
}

function Get-ComparableArcEvidenceJson {
    param([Parameter(Mandatory = $true)] [object] $Probe)
    $ordered = [ordered]@{}
    foreach ($name in @(
        'arcFitAvailable', 'arcFitCentre', 'arcFitRadius', 'candidateToSeedOffset',
        'candidateToSeedOffsetFraction', 'visibleSectorCount', 'visibleSectorIndices',
        'supportedVisibleSectorCount', 'supportedVisibleSectorIndices',
        'unexplainedVisibleSectorCount', 'unexplainedVisibleSectorIndices',
        'globalSupportedSectorCount', 'globalSupportFraction', 'longestContiguousSupportedRun',
        'supportedArcSpanDegrees', 'diagnosticArcChecksPass', 'classifier')) {
        $value = Get-Value -InputObject $Probe -Name $name
        $ordered[$name] = if ($value.Found) { $value.Value } else { $null }
    }
    return ConvertTo-Json -InputObject ([pscustomobject]$ordered) -Depth 30 -Compress
}

function Write-TextFile {
    param([Parameter(Mandatory = $true)] [string] $Path, [Parameter(Mandatory = $true)] [string] $Content)
    $full = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($full)
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    [IO.File]::WriteAllText($full, $Content, [Text.UTF8Encoding]::new($false))
}

function Get-Median {
    param([AllowEmptyCollection()] [double[]] $Values)
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

if ([string]::IsNullOrWhiteSpace($CaseLabel)) { throw 'CaseLabel must be explicit.' }
if ([IO.Path]::GetFullPath($SummaryJsonPath) -ieq [IO.Path]::GetFullPath($RowsCsvPath)) { throw 'SummaryJsonPath and RowsCsvPath must be different.' }

$regular = Read-Run -Directory $RegularRunDirectory -Label 'Regular'
$pocketArc = Read-Run -Directory $PocketArcRunDirectory -Label 'Pocket-arc'
$targeted = if (-not [string]::IsNullOrWhiteSpace($TargetedRunDirectory)) { Read-Run -Directory $TargetedRunDirectory -Label 'Targeted' } else { $null }
$regularConfig = Get-CanonicalConfig -Report $regular.Report -Label 'Regular'
$arcConfig = Get-CanonicalConfig -Report $pocketArc.Report -Label 'Pocket-arc'
if ($regularConfig -cne $arcConfig) { throw 'Regular and pocket-arc runs have different analyzer algorithms/settings beyond primary width.' }
if ($null -ne $targeted -and (Get-CanonicalConfig -Report $targeted.Report -Label 'Targeted') -cne $arcConfig) { throw 'Targeted run has different analyzer algorithm/settings beyond primary width.' }

$regularFrames = New-FrameIndex -Samples @($regular.Report.samples) -Label 'Regular'
$arcFrames = New-FrameIndex -Samples @($pocketArc.Report.samples) -Label 'Pocket-arc'
$targetFrames = if ($null -ne $targeted) { New-FrameIndex -Samples @($targeted.Report.samples) -Label 'Targeted' } else { $null }
$frameIds = @($regularFrames.Keys | Sort-Object { [long]$_ })
$arcFrameIds = @($arcFrames.Keys | Sort-Object { [long]$_ })
if (($frameIds -join ',') -cne ($arcFrameIds -join ',')) { throw 'Regular and pocket-arc reports do not contain the same frame IDs.' }
if ($null -ne $targeted) {
    $targetFrameIds = @($targetFrames.Keys | Sort-Object { [long]$_ })
    if (($frameIds -join ',') -cne ($targetFrameIds -join ',')) { throw 'Targeted report does not contain the same frame IDs.' }
}

$rows = [System.Collections.Generic.List[object]]::new()
$frameSummaries = [System.Collections.Generic.List[object]]::new()
$duplicateHypotheses = [System.Collections.Generic.List[object]]::new()
$targetedExecuted = 0; $targetedSkipped = 0; $targetedParityMatches = 0
foreach ($frameId in $frameIds) {
    $regularSample = $regularFrames[$frameId]
    $arcSample = $arcFrames[$frameId]
    if ([string]$regularSample.pixelSha256 -cne [string]$arcSample.pixelSha256) { throw "Frame $frameId regular/pocket-arc source hashes differ." }
    if ([long]$regularSample.sourceTimeUs -ne [long]$arcSample.sourceTimeUs) { throw "Frame $frameId regular/pocket-arc source timestamps differ." }
    if ([int]$regularSample.width1000.analysis.configuredWidth -ne 1000 -or [int]$regularSample.width1000.analysis.geometryWidth -ne 1000 -or
        [int]$arcSample.width1000.analysis.configuredWidth -ne 1000 -or [int]$arcSample.width1000.analysis.geometryWidth -ne 1000) { throw "Frame $frameId is missing exact width-1000 analyzer evidence." }

    $regularSeeds = New-ProbeIndex -Sample $regularSample -Label 'Regular' -FrameId $frameId
    $arcSeeds = New-ProbeIndex -Sample $arcSample -Label 'Pocket-arc' -FrameId $frameId
    $ranks = @($regularSeeds.Keys | Sort-Object { [int]$_ })
    $arcRanks = @($arcSeeds.Keys | Sort-Object { [int]$_ })
    if (($ranks -join ',') -cne ($arcRanks -join ',')) { throw "Frame $frameId candidate seed ranks differ." }

    $targetSeeds = $null
    if ($null -ne $targeted) {
        $targetSample = $targetFrames[$frameId]
        if ([string]$regularSample.pixelSha256 -cne [string]$targetSample.pixelSha256 -or [long]$regularSample.sourceTimeUs -ne [long]$targetSample.sourceTimeUs) { throw "Frame $frameId targeted source does not exactly match." }
        $targetSeeds = New-ProbeIndex -Sample $targetSample -Label 'Targeted' -FrameId $frameId
        $targetRanks = @($targetSeeds.Keys | Sort-Object { [int]$_ })
        if (($ranks -join ',') -cne ($targetRanks -join ',')) { throw "Frame $frameId targeted candidate ranks differ." }
    }

    $auditCandidates = @($regularSample.width1000.analysis.candidateAudits | Where-Object { $_.stage -eq 'sparse-black-add' -and $_.pass -eq 'initial' })
    $auditIndex = @{}
    foreach ($audit in $auditCandidates) {
        $key = [string]$audit.index
        if ($auditIndex.ContainsKey($key)) { throw "Frame $frameId has duplicate regular sparse-black audit index $key." }
        $auditIndex[$key] = $audit
    }
    $auditRanks = @($auditIndex.Keys | Sort-Object { [int]$_ })
    if (($auditRanks -join ',') -cne ($ranks -join ',')) { throw "Frame $frameId regular initial sparse-black audits do not cover the ranked seed list." }

    $expectedRadiusResult = Get-PathValue -InputObject $arcSample -Path 'width1000.darkMaskTrace.labelledBallRegion.expectedRadius'
    $expectedRadius = if ($expectedRadiusResult.Found -and $null -ne $expectedRadiusResult.Value -and [double]$expectedRadiusResult.Value -gt 0) { [double]$expectedRadiusResult.Value } else { $null }
    $frameRows = [System.Collections.Generic.List[object]]::new()
    foreach ($rank in $ranks) {
        $regularSeed = $regularSeeds[$rank]
        $arcSeed = $arcSeeds[$rank]
        Test-SameSeed -Left $regularSeed -Right $arcSeed -Context "Frame $frameId rank $rank regular/arc"
        $regularAudit = $auditIndex[$rank]
        foreach ($axis in @('x','y')) {
            if ([Math]::Abs([double]$regularAudit.proposalCentre.$axis - [double]$regularSeed.seedCentre.$axis) -gt 0.000001) { throw "Frame $frameId rank $rank audit proposal differs from seed $axis." }
        }

        $targetProbe = if ($null -ne $targetSeeds) { $targetSeeds[$rank] } else { $null }
        if ($null -ne $targetProbe) {
            Test-SameSeed -Left $arcSeed -Right $targetProbe -Context "Frame $frameId rank $rank arc/targeted"
            if ($targetProbe.probeExecuted -eq $true) {
                $targetedExecuted++
                if ((Get-ComparableArcEvidenceJson $arcSeed) -cne (Get-ComparableArcEvidenceJson $targetProbe)) { throw "Frame $frameId rank $rank targeted executed evidence differs from full arc." }
                $targetedParityMatches++
            } elseif ($targetProbe.probeExecuted -eq $false) {
                $targetedSkipped++
                if ($targetProbe.probeSkipReason -ne 'seed-outside-target-fit-reach') { throw "Frame $frameId rank $rank targeted seed has an unexpected skip reason." }
            } else { throw "Frame $frameId rank $rank targeted seed has no explicit execution status." }
        }

        $normalCentre = Get-Value -InputObject $regularAudit -Name 'fittedCentre'
        $normalFitRadius = Get-Value -InputObject $regularAudit -Name 'fittedRadius'
        $normalSupport = Get-Value -InputObject $regularAudit -Name 'boundarySupport'
        $arcCentre = Get-Value -InputObject $arcSeed -Name 'arcFitCentre'
        $arcFitAvailable = $arcSeed.arcFitAvailable -eq $true -and $arcCentre.Found -and $null -ne $arcCentre.Value
        $normalFitAvailable = $normalCentre.Found -and $null -ne $normalCentre.Value
        $driftPx = $null; $driftRadii = $null
        if ($arcFitAvailable -and $normalFitAvailable) {
            $driftX = [double]$arcCentre.Value.x - [double]$normalCentre.Value.x
            $driftY = [double]$arcCentre.Value.y - [double]$normalCentre.Value.y
            $driftPx = [Math]::Sqrt(($driftX * $driftX) + ($driftY * $driftY))
            if ($null -ne $expectedRadius) { $driftRadii = $driftPx / $expectedRadius }
        }
        $supportFraction = if ($normalSupport.Found -and $null -ne $normalSupport.Value) { [double]$normalSupport.Value } else { $null }
        $supportSectors32 = if ($null -ne $supportFraction -and $supportFraction -ge 0 -and $supportFraction -le 1) { [int][Math]::Round($supportFraction * 32) } else { $null }
        $arcClass = Get-PathValue -InputObject $arcSeed -Path 'classifier.evidence.kind'
        $targetStatus = if ($null -eq $targetProbe) { 'not-supplied' } elseif ($targetProbe.probeExecuted) { 'executed-label-conditioned' } else { 'skipped-label-conditioned' }
        $row = [pscustomobject][ordered]@{
            CaseLabel = $CaseLabel; FrameId = [long]$frameId; SourceTimeUs = [long]$regularSample.sourceTimeUs; PixelSha256 = [string]$regularSample.pixelSha256
            CandidateRank = [int]$rank; SeedX = [double]$regularSeed.seedCentre.x; SeedY = [double]$regularSeed.seedCentre.y; SeedRadiusPx = [double]$regularSeed.seedRadius
            ExpectedBallRadiusPxForScalingOnly = $expectedRadius
            RegularFitAvailable = $normalFitAvailable; RegularFitX = if ($normalFitAvailable) { [double]$normalCentre.Value.x } else { $null }; RegularFitY = if ($normalFitAvailable) { [double]$normalCentre.Value.y } else { $null }
            RegularFitRadiusPx = if ($normalFitRadius.Found -and $null -ne $normalFitRadius.Value) { [double]$normalFitRadius.Value } else { $null }
            RegularBoundarySupportFraction = $supportFraction; RegularSupportApproxSectorsOf32 = $supportSectors32
            RegularDecision = [string]$regularAudit.decision; RegularClassifiedKind = if ($null -ne $regularAudit.classifiedKind) { [string]$regularAudit.classifiedKind } else { $null }
            PocketArcFitAvailable = $arcFitAvailable; PocketArcFitX = if ($arcFitAvailable) { [double]$arcCentre.Value.x } else { $null }; PocketArcFitY = if ($arcFitAvailable) { [double]$arcCentre.Value.y } else { $null }
            PocketArcFitRadiusPx = if ($arcSeed.arcFitRadius -ne $null) { [double]$arcSeed.arcFitRadius } else { $null }; PocketArcClassifiedKind = if ($arcClass.Found) { [string]$arcClass.Value } else { $null }
            PocketArcDiagnosticChecksPass = [bool]$arcSeed.diagnosticArcChecksPass; PocketArcVisibleSectorCount = $arcSeed.visibleSectorCount
            PocketArcSupportedVisibleSectorCount = $arcSeed.supportedVisibleSectorCount; PocketArcGlobalSupportedSectorCount = $arcSeed.globalSupportedSectorCount
            PocketArcSupportedVisibleSectorIndices = (@($arcSeed.supportedVisibleSectorIndices) -join ';')
            CenterDriftPx = $driftPx; CenterDriftBallRadii = $driftRadii
            SupportSectorSetOverlapAvailable = $false; SupportSectorSetOverlapCount = $null
            SupportComparisonLimit = 'Regular candidate audit stores scalar boundarySupport only; pocket-aware audit stores sector indices with a different visibility mask.'
            TargetedProbeStatus = $targetStatus
        }
        $rows.Add($row); $frameRows.Add($row)
    }

    # Duplicate hypotheses are computed over all eligible pocket-arc fits, without label proximity.
    # The physical radius only sets a descriptive sub-half-radius proximity threshold.
    $eligible = @($arcSeeds.Values | Where-Object { $_.arcFitAvailable -eq $true -and $_.diagnosticArcChecksPass -eq $true -and $_.classifier.evidence.kind -eq 'black' })
    $duplicatePairs = 0
    if ($null -ne $expectedRadius) {
        for ($i = 0; $i -lt $eligible.Count; $i++) {
            for ($j = $i + 1; $j -lt $eligible.Count; $j++) {
                $dx = [double]$eligible[$i].arcFitCentre.x - [double]$eligible[$j].arcFitCentre.x
                $dy = [double]$eligible[$i].arcFitCentre.y - [double]$eligible[$j].arcFitCentre.y
                $distance = [Math]::Sqrt(($dx * $dx) + ($dy * $dy))
                if ($distance -lt (0.5 * $expectedRadius)) {
                    $duplicatePairs++
                    $duplicateHypotheses.Add([pscustomobject][ordered]@{
                        CaseLabel = $CaseLabel; FrameId = [long]$frameId; PixelSha256 = [string]$regularSample.pixelSha256
                        CandidateRankA = [int]$eligible[$i].candidateRank; CandidateRankB = [int]$eligible[$j].candidateRank
                        CentreAX = [double]$eligible[$i].arcFitCentre.x; CentreAY = [double]$eligible[$i].arcFitCentre.y
                        CentreBX = [double]$eligible[$j].arcFitCentre.x; CentreBY = [double]$eligible[$j].arcFitCentre.y
                        CentreDistancePx = $distance; CentreDistanceBallRadii = $distance / $expectedRadius
                        ThresholdBallRadii = 0.5; DiagnosticOnly = $true
                    })
                }
            }
        }
    }
    $both = @($frameRows | Where-Object { $_.RegularFitAvailable -and $_.PocketArcFitAvailable })
    $drifts = @($both | ForEach-Object { $_.CenterDriftPx } | Where-Object { $null -ne $_ } | Sort-Object)
    $driftsR = @($both | ForEach-Object { $_.CenterDriftBallRadii } | Where-Object { $null -ne $_ } | Sort-Object)
    $medianPx = Get-Median -Values ([double[]]$drifts)
    $meanPx = if ($drifts.Count) { [double](($drifts | Measure-Object -Average).Average) } else { $null }
    $medianR = Get-Median -Values ([double[]]$driftsR)
    $arcPassCount = @($arcSeeds.Values | Where-Object { $_.diagnosticArcChecksPass -eq $true }).Count
    $normalGateCount = @($frameRows | Where-Object { $null -ne $_.RegularBoundarySupportFraction -and $_.RegularBoundarySupportFraction -ge (21.0 / 32.0) }).Count
    $frameSummaries.Add([pscustomobject][ordered]@{
        CaseLabel = $CaseLabel; FrameId = [long]$frameId; SourceTimeUs = [long]$regularSample.sourceTimeUs; PixelSha256 = [string]$regularSample.pixelSha256
        RankedSeedCount = $ranks.Count; RegularRefinerFitCount = @($frameRows | Where-Object RegularFitAvailable).Count
        RegularSupportAtLeast21Of32Count = $normalGateCount; PocketArcFitCount = @($frameRows | Where-Object PocketArcFitAvailable).Count
        PocketArcDiagnosticPassCount = $arcPassCount; BothFitCount = $both.Count; MeanCentreDriftPx = $meanPx; MedianCentreDriftPx = $medianPx
        MedianCentreDriftBallRadii = $medianR; NearDuplicateBlackHypothesisPairCount = $duplicatePairs
        SectorSetOverlapAvailable = $false; TargetedExecutedCount = @($frameRows | Where-Object TargetedProbeStatus -eq 'executed-label-conditioned').Count
        TargetedSkippedCount = @($frameRows | Where-Object TargetedProbeStatus -eq 'skipped-label-conditioned').Count
    })
}

$allDrifts = @($rows | Where-Object { $_.RegularFitAvailable -and $_.PocketArcFitAvailable -and $null -ne $_.CenterDriftPx } | ForEach-Object { [double]$_.CenterDriftPx } | Sort-Object)
$allDriftsR = @($rows | Where-Object { $_.RegularFitAvailable -and $_.PocketArcFitAvailable -and $null -ne $_.CenterDriftBallRadii } | ForEach-Object { [double]$_.CenterDriftBallRadii } | Sort-Object)
$regularFitRows = @($rows | Where-Object RegularFitAvailable)
$regularGateRows = @($rows | Where-Object { $null -ne $_.RegularBoundarySupportFraction -and $_.RegularBoundarySupportFraction -ge (21.0 / 32.0) })
$arcFitRows = @($rows | Where-Object PocketArcFitAvailable)
$arcPassRows = @($rows | Where-Object PocketArcDiagnosticChecksPass)
$arcBlackRows = @($rows | Where-Object PocketArcClassifiedKind -eq 'black')
$summary = [pscustomobject][ordered]@{
    schemaVersion = 1; tool = 'Compare-PocketArcFitModels'; diagnosticOnly = $true; caseLabel = $CaseLabel
    regularRunDirectory = $regular.Directory; regularReportPath = $regular.ReportPath
    pocketArcRunDirectory = $pocketArc.Directory; pocketArcReportPath = $pocketArc.ReportPath
    targetedRunDirectory = if ($null -ne $targeted) { $targeted.Directory } else { $null }
    width1000AnalyzerConfigurationMatchesAfterWidthNormalization = $true
    exactInputFrames = $frameIds.Count; totalRankedSeeds = $rows.Count; exactInputSourceHashesAndTimestampsMatch = $true; seedRanksAndCoordinatesMatch = $true
    regularRefinerFitCount = $regularFitRows.Count; regularSupportAtLeast21Of32Count = $regularGateRows.Count
    pocketArcFitCount = $arcFitRows.Count; pocketArcDiagnosticPassCount = $arcPassRows.Count; pocketArcBlackClassificationCount = $arcBlackRows.Count
    productionSupportGateSectorsOf32 = 21; productionSupportScalarOnly = $true
    pocketArcVisibilitySupportIsReportedSeparately = $true; supportSectorSetOverlapAvailable = $false
    supportSectorSetOverlapUnavailableReason = 'Regular audit contains only scalar boundarySupport; pocket-aware support uses sector IDs after its visibility mask.'
    centerDriftPairs = $allDrifts.Count; meanCenterDriftPx = if ($allDrifts.Count) { ($allDrifts | Measure-Object -Average).Average } else { $null }
    medianCenterDriftPx = Get-Median -Values ([double[]]$allDrifts)
    medianCenterDriftBallRadii = Get-Median -Values ([double[]]$allDriftsR)
    targetedExecutedSeedCount = $targetedExecuted; targetedSkippedSeedCount = $targetedSkipped
    targetedExecutedEvidenceMatchesFullArc = $targetedParityMatches
    targetedInterpretation = if ($null -ne $targeted) { 'Label-conditioned reach-pruning parity only; not a candidate precision result.' } else { 'Not supplied.' }
    duplicateProximityThresholdBallRadii = 0.5; duplicateHypothesisPairCount = $duplicateHypotheses.Count; duplicateHypothesesAreDiagnosticOnly = $true
    frames = @($frameSummaries); duplicateHypothesisPairs = @($duplicateHypotheses)
    limitations = @(
        'Pocket-aware arc fits are evaluation-only diagnostics and do not feed detector candidates, vision results, or GameState.',
        'Regular boundary support and pocket-aware visible-sector support are different metrics; sector-index overlap is unavailable in the regular audit.',
        'A passing arc geometry/classifier result does not establish target-free precision; many remote seeds can pass.'
    )
}

Write-TextFile -Path $SummaryJsonPath -Content (ConvertTo-Json -InputObject $summary -Depth 40)
$rowArray = $rows.ToArray()
$csv = @($rowArray | ConvertTo-Csv -NoTypeInformation)
Write-TextFile -Path $RowsCsvPath -Content ($csv -join [Environment]::NewLine)
Write-Output ([pscustomobject]@{ Passed = $true; SummaryJsonPath = [IO.Path]::GetFullPath($SummaryJsonPath); RowsCsvPath = [IO.Path]::GetFullPath($RowsCsvPath); Frames = $frameIds.Count; SeedRows = $rows.Count; CenterDriftPairs = $allDrifts.Count; DuplicateHypothesisPairs = $duplicateHypotheses.Count; TargetedExecuted = $targetedExecuted; TargetedSkipped = $targetedSkipped } | ConvertTo-Json -Compress)
