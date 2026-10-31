<#
.SYNOPSIS
Verifies that a targeted pocket-arc replay preserved every reachable seed and exact probe evidence.

.DESCRIPTION
Compares a full-sweep report with its target-reach report for the same frames.
It derives the seed-reach bound from each full-sweep row's actual maximum
seed-to-fit offset plus the target report's configured fit-to-label acceptance
radius. It checks frame pixel hashes, seed ranks, execution/skip status, and
identical geometry/classifier evidence for every retained seed.

This is an offline report comparison. It does not run ADB, camera capture, Cast,
or detector analysis.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $FullSweepRunDirectory,
    [Parameter(Mandatory = $true)] [ValidateNotNullOrEmpty()] [string] $TargetedRunDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

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
        $value = Get-Value -InputObject $current -Name $part
        if (-not $value.Found) { return [pscustomobject]@{ Found = $false; Value = $null } }
        $current = $value.Value
    }
    return [pscustomobject]@{ Found = $true; Value = $current }
}

function Read-RunReport {
    param([Parameter(Mandatory = $true)] [string] $Directory)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Run directory not found: '$Directory'." }
    $resolved = (Resolve-Path -LiteralPath $Directory).Path
    $files = @(Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json')
    if ($files.Count -ne 1) { throw "Expected one replay report in '$resolved'; found $($files.Count)." }
    $report = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    if ($null -eq $report.samples) { throw "Report has no samples array: '$($files[0].FullName)'." }
    return [pscustomobject]@{ Directory = $resolved; Path = $files[0].FullName; Report = $report }
}

function New-FrameIndex {
    param([Parameter(Mandatory = $true)] [object[]] $Samples, [Parameter(Mandatory = $true)] [string] $RunName)
    $index = @{}
    foreach ($sample in $Samples) {
        $frame = Get-Value -InputObject $sample -Name 'frameId'
        if (-not $frame.Found -or $null -eq $frame.Value) { throw "$RunName report has a sample without frameId." }
        $key = [string]$frame.Value
        if ($index.ContainsKey($key)) { throw "$RunName report has duplicate frameId $key." }
        $index[$key] = $sample
    }
    return $index
}

function New-ProbeIndex {
    param([AllowNull()] [object] $Sample, [Parameter(Mandatory = $true)] [string] $RunName, [Parameter(Mandatory = $true)] [string] $FrameId)
    $probes = Get-PathValue -InputObject $Sample -Path 'width1000.darkMaskTrace.rankedSparseBlackPocketArcProbes'
    if (-not $probes.Found -or $null -eq $probes.Value) { throw "$RunName frame $FrameId has no ranked seed array." }
    $index = @{}
    foreach ($probe in @($probes.Value)) {
        $rank = Get-Value -InputObject $probe -Name 'candidateRank'
        if (-not $rank.Found -or $null -eq $rank.Value) { throw "$RunName frame $FrameId contains a seed without candidateRank." }
        $key = [string]$rank.Value
        if ($index.ContainsKey($key)) { throw "$RunName frame $FrameId has duplicate candidate rank $key." }
        $index[$key] = $probe
    }
    return $index
}

function Get-ProbeEvidenceJson {
    param([Parameter(Mandatory = $true)] [object] $Probe)
    $fields = [ordered]@{}
    foreach ($name in @(
        'arcFitAvailable', 'arcFitCentre', 'arcFitRadius', 'arcFitDistanceFromLabel',
        'candidateToSeedOffset', 'candidateToSeedOffsetFraction', 'maximumCandidateToSeedOffset',
        'maximumCandidateToSeedOffsetFraction', 'visibleSectorCount', 'visibleSectorIndices',
        'supportedVisibleSectorCount', 'supportedVisibleSectorIndices', 'unexplainedVisibleSectorCount',
        'unexplainedVisibleSectorIndices', 'globalSupportedSectorCount', 'globalSupportFraction',
        'longestContiguousSupportedRun', 'supportedArcSpanDegrees', 'minimumVisibleSectors',
        'minimumSupportedVisibleSectors', 'minimumContiguousSupportedRun',
        'minimumSupportedArcSpanDegrees', 'diagnosticArcChecksPass', 'score', 'classifier')) {
        $value = Get-Value -InputObject $Probe -Name $name
        $fields[$name] = if ($value.Found) { $value.Value } else { $null }
    }
    return ConvertTo-Json -InputObject ([pscustomobject]$fields) -Depth 40 -Compress
}

$full = Read-RunReport -Directory $FullSweepRunDirectory
$targeted = Read-RunReport -Directory $TargetedRunDirectory
$acceptance = Get-Value -InputObject $targeted.Report -Name 'pocketArcTargetFitAcceptanceRadiusInBallRadii'
if (-not $acceptance.Found -or $null -eq $acceptance.Value -or [double]$acceptance.Value -le 0) {
    throw 'Targeted report has no positive pocketArcTargetFitAcceptanceRadiusInBallRadii.'
}
$acceptanceRadii = [double]$acceptance.Value
$fullConfig = Get-Value -InputObject $full.Report -Name 'replayAnalyzerConfigurationIdentity'
$targetConfig = Get-Value -InputObject $targeted.Report -Name 'replayAnalyzerConfigurationIdentity'
if (-not $fullConfig.Found -or -not $targetConfig.Found -or [string]$fullConfig.Value -cne [string]$targetConfig.Value) {
    throw 'Full and targeted reports do not have the same replay analyzer configuration identity.'
}

$fullFrames = New-FrameIndex -Samples @($full.Report.samples) -RunName 'Full-sweep'
$targetFrames = New-FrameIndex -Samples @($targeted.Report.samples) -RunName 'Targeted'
$fullIds = @($fullFrames.Keys | Sort-Object { [long]$_ })
$targetIds = @($targetFrames.Keys | Sort-Object { [long]$_ })
if ((ConvertTo-Json -InputObject $fullIds -Compress) -cne (ConvertTo-Json -InputObject $targetIds -Compress)) {
    throw 'Full and targeted reports do not contain exactly the same frame IDs.'
}

$seedCount = 0; $executedCount = 0; $skippedCount = 0; $fitWithinAcceptanceCount = 0; $retainedFitCount = 0; $evidenceMatchCount = 0
foreach ($frameId in $fullIds) {
    $fullSample = $fullFrames[$frameId]
    $targetSample = $targetFrames[$frameId]
    $fullHash = Get-Value -InputObject $fullSample -Name 'pixelSha256'
    $targetHash = Get-Value -InputObject $targetSample -Name 'pixelSha256'
    if (-not $fullHash.Found -or -not $targetHash.Found -or [string]$fullHash.Value -cne [string]$targetHash.Value) {
        throw "Frame $frameId pixel hashes differ; this is not an exact-input comparison."
    }
    $radius = Get-PathValue -InputObject $fullSample -Path 'width1000.darkMaskTrace.labelledBallRegion.expectedRadius'
    if (-not $radius.Found -or $null -eq $radius.Value -or [double]$radius.Value -le 0) { throw "Frame $frameId has no positive target ball radius." }
    $fullProbes = New-ProbeIndex -Sample $fullSample -RunName 'Full-sweep' -FrameId $frameId
    $targetProbes = New-ProbeIndex -Sample $targetSample -RunName 'Targeted' -FrameId $frameId
    $ranks = @($fullProbes.Keys | Sort-Object { [int]$_ })
    $targetRanks = @($targetProbes.Keys | Sort-Object { [int]$_ })
    if ((ConvertTo-Json -InputObject $ranks -Compress) -cne (ConvertTo-Json -InputObject $targetRanks -Compress)) {
        throw "Frame $frameId targeted seed ranks differ from the full-sweep list."
    }

    foreach ($rank in $ranks) {
        $seedCount++
        $referenceProbe = $fullProbes[$rank]
        $targetProbe = $targetProbes[$rank]
        $seedDistance = Get-Value -InputObject $referenceProbe -Name 'distanceFromLabel'
        $maximumOffset = Get-Value -InputObject $referenceProbe -Name 'maximumCandidateToSeedOffsetFraction'
        if (-not $seedDistance.Found -or $null -eq $seedDistance.Value -or
            -not $maximumOffset.Found -or $null -eq $maximumOffset.Value -or
            [double]$maximumOffset.Value -lt 0) {
            throw "Frame $frameId rank $rank lacks the source distance or actual probe offset fraction."
        }
        $maxReachRadii = $acceptanceRadii + [double]$maximumOffset.Value
        $shouldExecute = [double]$seedDistance.Value -le [double]$radius.Value * $maxReachRadii + 1e-9
        $execution = Get-Value -InputObject $targetProbe -Name 'probeExecuted'
        if (-not $execution.Found -or [bool]$execution.Value -ne $shouldExecute) {
            throw "Frame $frameId rank $rank has an execution decision inconsistent with reach $maxReachRadii radii."
        }
        if ($shouldExecute) {
            $executedCount++
            if ((Get-ProbeEvidenceJson $referenceProbe) -cne (Get-ProbeEvidenceJson $targetProbe)) {
                throw "Frame $frameId rank $rank retained geometry or classifier evidence changed."
            }
            $evidenceMatchCount++
            $fitAvailable = Get-Value -InputObject $referenceProbe -Name 'arcFitAvailable'
            $fitDistance = Get-Value -InputObject $referenceProbe -Name 'arcFitDistanceFromLabel'
            if (
                ($fitAvailable.Value -eq $true) -and
                ($null -ne $fitDistance.Value) -and
                ([double]$fitDistance.Value -le [double]$radius.Value * $acceptanceRadii + 1e-9)
            ) {
                $fitWithinAcceptanceCount++
                $retainedFitCount++
            }
        } else {
            $skippedCount++
            $skipReason = Get-Value -InputObject $targetProbe -Name 'probeSkipReason'
            if (-not $skipReason.Found -or $skipReason.Value -ne 'seed-outside-target-fit-reach') {
                throw "Frame $frameId rank $rank was skipped without the expected reason."
            }
            $classifierAvailable = Get-PathValue -InputObject $targetProbe -Path 'classifier.available'
            $arcAvailable = Get-Value -InputObject $targetProbe -Name 'arcFitAvailable'
            if ($classifierAvailable.Value -ne $false -or $null -ne $arcAvailable.Value) {
                throw "Frame $frameId rank $rank reports classifier/fit evidence for a skipped computation."
            }
        }
    }
}

[pscustomobject][ordered]@{
    fullSweepRunDirectory = $full.Directory
    targetedRunDirectory = $targeted.Directory
    targetFitAcceptanceRadiusInBallRadii = $acceptanceRadii
    exactInputFrames = $fullIds.Count
    rankedCandidateSeeds = $seedCount
    executedCandidateProbes = $executedCount
    skippedOutsideReach = $skippedCount
    fullFitsWithinTargetAcceptanceRetained = $fitWithinAcceptanceCount
    retainedGeometryAndClassifierEvidenceMatches = $evidenceMatchCount
    passed = $true
}
