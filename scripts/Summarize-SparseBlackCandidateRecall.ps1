<#
.SYNOPSIS
Summarizes sparse-black seed rank and spatial hit rates from saved replay reports.

.DESCRIPTION
Reads explicit replay run directories or evidence roots containing either
`*blacktrace.json` reports or focused `*pocketarc.json` reports. It uses only
each report's `initial` `sparse-black-add` candidate audits for ranked
candidates, and the evaluation-only `darkMaskTrace` point/seed audit for
spatial distances. The report's candidate rank is the one-based form of the
audit `index`.

Black-present and empty-pocket-control labels are read conservatively from the
run manifest's `purpose`. A blacktrace reference point is usable only when the
manifest's `manualBlackLabelSourcePixel` exists and matches the trace's
`manualBlackLabel.sourcePixel`. A focused pocket-arc report uses only
`pocketArcLabelSourcePixel`, and only on an exact source-pixel match. This
prevents trace default/fallback points from being used as labelled points.
Unknown purposes, missing samples/widths/points, and point mismatches remain
explicit and unevaluable. Detector counts, accepted detections, and fallback
probe proposals are never used to assign label classes or create candidates.

The 3R qualification neighborhood matches the `nearbyQualifiedSeeds` audit
window in `PackedBlackMaskTrace`; a seed must also pass the recorded replay
configuration's maximum-radius gate. Top-K metrics mean a spatial candidate
hit within 3R of the labelled interior point, not exact centre recall. Outcomes
are marked unevaluable when the ranked candidate prefix is not fully present in
the candidate audit rows. Configuration mismatch flags are carried through and
do not filter rows.

.PARAMETER InputPath
One or more explicit replay report files, run directories, or evidence roots.
Directories are searched recursively for `*blacktrace.json` and
`*pocketarc.json` reports.

.PARAMETER OutputDirectory
Directory where `sparse-black-candidate-recall.csv` and
`sparse-black-candidate-recall.json` are written.

.PARAMETER TopK
Candidate cutoffs to summarize. Values must be between 1 and 64.

.EXAMPLE
.\Summarize-SparseBlackCandidateRecall.ps1 `
  -InputPath 'work\production\evidence\code69-temporal-edge-check-20261031\device-replays\code69seedP32radius-1101-102455' `
  -OutputDirectory 'work\analysis\sparse-black-seed-recall'

.EXAMPLE
.\Summarize-SparseBlackCandidateRecall.ps1 `
  -InputPath 'work\production\evidence\code69-temporal-edge-check-20261031' `
  -OutputDirectory 'work\analysis\sparse-black-code69' `
  -TopK 1,2,4,8,12,16,32

.EXAMPLE
.\Summarize-SparseBlackCandidateRecall.ps1 `
  -InputPath 'work\production\evidence\code70-daylight-pocket-capture-20261031\pocket-arc-holdout' `
  -OutputDirectory 'work\analysis\pocket-arc-cap-sweep' `
  -TopK 12,32
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]] $InputPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [int[]] $TopK = @(1, 2, 4, 8, 12, 16, 32, 64)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($k in $TopK) {
    if ($k -lt 1 -or $k -gt 64) {
        throw "TopK values must be between 1 and 64; got $k."
    }
}
$TopK = @($TopK | Sort-Object -Unique)

function Get-JsonValue {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory = $true)] [string] $Name
    )
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $null
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Convert-ToNumber {
    param([AllowNull()] [object] $Value)
    if ($null -eq $Value) { return $null }
    $number = 0.0
    if ([double]::TryParse([string]$Value, [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture, [ref]$number)) {
        return $number
    }
    return $null
}

function Get-ExplicitLabelClass {
    param([AllowNull()] [string] $Purpose)
    if ([string]::IsNullOrWhiteSpace($Purpose)) { return 'Unknown' }
    $text = $Purpose.ToLowerInvariant()
    $absent = $text -match '\bblack[-\s]+absent\b|\bempty[-\s]+pockets?\b|\bblack[-\s]+removed\b'
    $present = $text -match '\bblack[-\s]+present\b|\buser.confirmed black cent(er|re)\b|\bblack ball present\b|\b(labelled|labeled) black\b'
    if ($absent -and -not $present) { return 'EmptyPocketControl' }
    if ($present -and -not $absent) { return 'BlackPresent' }
    return 'Unknown'
}

function Get-NearestSeedArea {
    param(
        [AllowNull()] [object[]] $Seeds,
        [double] $X,
        [double] $Y,
        [double] $Radius
    )
    $best = $null
    $bestDistance = [double]::PositiveInfinity
    foreach ($seed in @($Seeds)) {
        $seedX = Convert-ToNumber (Get-JsonValue $seed 'x')
        $seedY = Convert-ToNumber (Get-JsonValue $seed 'y')
        $seedRadius = Convert-ToNumber (Get-JsonValue $seed 'radius')
        $area = Get-JsonValue $seed 'area'
        if ($null -eq $seedX -or $null -eq $seedY -or $null -eq $seedRadius -or $null -eq $area) { continue }
        $distance = [Math]::Sqrt([Math]::Pow($seedX - $X, 2) + [Math]::Pow($seedY - $Y, 2))
        if ($distance -le 0.75 -and [Math]::Abs($seedRadius - $Radius) -le 0.75 -and $distance -lt $bestDistance) {
            $best = [int]$area
            $bestDistance = $distance
        }
    }
    return $best
}

function Find-Manifest {
    param([Parameter(Mandatory = $true)] [string] $ReportDirectory)
    $directory = Get-Item -LiteralPath $ReportDirectory
    while ($null -ne $directory) {
        $candidate = Join-Path $directory.FullName 'manifest.json'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
        $directory = $directory.Parent
    }
    return $null
}

$reportFiles = [System.Collections.Generic.List[string]]::new()
foreach ($providedPath in $InputPath) {
    if (-not (Test-Path -LiteralPath $providedPath)) {
        throw "Input path does not exist: $providedPath"
    }
    $item = Get-Item -LiteralPath $providedPath
    if (-not $item.PSIsContainer) {
        $reportFiles.Add($item.FullName)
        continue
    }
    foreach ($filter in @('*blacktrace.json', '*pocketarc.json')) {
        Get-ChildItem -LiteralPath $item.FullName -File -Recurse -Filter $filter |
            ForEach-Object { $reportFiles.Add($_.FullName) }
    }
}
$uniqueReportFiles = @($reportFiles | Sort-Object -Unique)
if ($uniqueReportFiles.Count -eq 0) {
    throw 'No *blacktrace.json or *pocketarc.json reports were found under the supplied input paths.'
}

$frames = [System.Collections.Generic.List[object]]::new()
$candidateRows = [System.Collections.Generic.List[object]]::new()
$parsedReports = 0
$skippedReports = 0

foreach ($reportPath in $uniqueReportFiles) {
    $reportName = [IO.Path]::GetFileName($reportPath)
    try {
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable
    }
    catch {
        Write-Warning "Skipping invalid JSON report '$reportPath': $($_.Exception.Message)"
        $skippedReports++
        continue
    }
    $isPocketArcReport = ($reportName -match 'pocketarc\.json$') -or ((Get-JsonValue $report 'pocketArcProbeOnly') -eq $true)
    $reportKind = if ($isPocketArcReport) { 'PocketArcProbeOnly' } else { 'BlackTrace' }
    if (-not $isPocketArcReport -and $reportName -notmatch 'blacktrace\.json$') {
        Write-Warning "Skipping unrecognized replay report file: $reportPath"
        $skippedReports++
        continue
    }

    $manifestPath = Find-Manifest ([IO.Path]::GetDirectoryName($reportPath))
    $manifest = $null
    if ($null -ne $manifestPath) {
        try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable }
        catch { Write-Warning "Could not parse manifest '$manifestPath': $($_.Exception.Message)" }
    }

    $samplesValue = Get-JsonValue $report 'samples'
    $samples = if ($null -eq $samplesValue) { @() } else { @($samplesValue) }
    $parsedReports++

    $runId = Get-JsonValue $manifest 'run'
    if ($null -eq $runId) { $runId = Get-JsonValue $manifest 'runId' }
    if ($null -eq $runId) { $runId = Get-JsonValue $report 'run' }
    if ($null -eq $runId) { $runId = [IO.Path]::GetFileName([IO.Path]::GetDirectoryName($reportPath)) }
    $purpose = Get-JsonValue $manifest 'purpose'
    $labelClass = Get-ExplicitLabelClass ([string]$purpose)
    $manifestPointField = if ($isPocketArcReport) { 'pocketArcLabelSourcePixel' } else { 'manualBlackLabelSourcePixel' }
    $manifestPoint = Get-JsonValue $manifest $manifestPointField
    $manifestPointX = Convert-ToNumber (Get-JsonValue $manifestPoint 'x')
    $manifestPointY = Convert-ToNumber (Get-JsonValue $manifestPoint 'y')

    $sampleRecords = [System.Collections.Generic.List[object]]::new()
    foreach ($sample in $samples) {
        $sampleRecords.Add([pscustomobject]@{
            Sample = $sample
            FrameId = Get-JsonValue $sample 'frameId'
            EvidenceStatus = 'SamplePresent'
        })
    }
    $manifestSourceFrames = Get-JsonValue $manifest 'sourceFrames'
    $expectedFrameRecords = @()
    if ($null -ne $manifestSourceFrames) {
        foreach ($sourceFrame in @($manifestSourceFrames)) {
            $expectedId = if ($sourceFrame -is [string] -or $sourceFrame -is [ValueType]) { $sourceFrame } else { Get-JsonValue $sourceFrame 'frameId' }
            if ($null -ne $expectedId) { $expectedFrameRecords += $expectedId }
        }
    }
    if ($expectedFrameRecords.Count -gt 0) {
        $sampleFrameIds = @($sampleRecords | ForEach-Object { [string]$_.FrameId })
        foreach ($expectedId in $expectedFrameRecords) {
            if ($sampleFrameIds -notcontains [string]$expectedId) {
                $sampleRecords.Add([pscustomobject]@{
                    Sample = $null
                    FrameId = $expectedId
                    EvidenceStatus = 'MissingSampleEvidence'
                })
            }
        }
    }
    elseif ($sampleRecords.Count -eq 0) {
        $sampleRecords.Add([pscustomobject]@{
            Sample = $null
            FrameId = Get-JsonValue $report 'frameId'
            EvidenceStatus = 'MissingSampleEvidence'
        })
    }

    foreach ($sampleRecord in $sampleRecords) {
        $sample = $sampleRecord.Sample
        $frameId = $sampleRecord.FrameId
        foreach ($widthName in @('width800', 'width1000')) {
            $widthBlock = Get-JsonValue $sample $widthName
            $trace = Get-JsonValue $widthBlock 'darkMaskTrace'
            $evidenceStatus = if ($sampleRecord.EvidenceStatus -eq 'MissingSampleEvidence') {
                'MissingSampleEvidence'
            } elseif ($null -eq $widthBlock) {
                'MissingWidthBlock'
            } elseif ($null -eq $trace) {
                'MissingDarkMaskTrace'
            } else {
                'TracePresent'
            }

            $analysis = Get-JsonValue $widthBlock 'analysis'
            $analysisWidth = Convert-ToNumber (Get-JsonValue $trace 'analysisWidth')
            if ($null -eq $analysisWidth) {
                $analysisWidth = if ($widthName -eq 'width800') { 800.0 } else { 1000.0 }
            }
            $auditGeometryWidth = Convert-ToNumber (Get-JsonValue $trace 'candidateAuditGeometryWidth')
            if ($null -eq $auditGeometryWidth -or $auditGeometryWidth -le 0) { $auditGeometryWidth = $analysisWidth }
            $auditScale = $analysisWidth / $auditGeometryWidth
            $manualTrace = Get-JsonValue $trace 'manualBlackLabel'
            $tracePoint = Get-JsonValue $manualTrace 'sourcePixel'
            $tracePointX = Convert-ToNumber (Get-JsonValue $tracePoint 'x')
            $tracePointY = Convert-ToNumber (Get-JsonValue $tracePoint 'y')
            $analysisPoint = Get-JsonValue $manualTrace 'analysisPixel'
            $referenceX = Convert-ToNumber (Get-JsonValue $analysisPoint 'x')
            $referenceY = Convert-ToNumber (Get-JsonValue $analysisPoint 'y')
            $pointMatches = if ($isPocketArcReport) {
                ($tracePointX -eq $manifestPointX) -and ($tracePointY -eq $manifestPointY)
            } else {
                ([Math]::Abs($tracePointX - $manifestPointX) -le 0.001) -and ([Math]::Abs($tracePointY - $manifestPointY) -le 0.001)
            }

            $labelStatus = 'MissingManifest'
            $referenceAvailable = $false
            if ($null -ne $manifest) {
                if ($null -eq $manifestPointX -or $null -eq $manifestPointY) {
                    $labelStatus = 'MissingManualReferencePoint'
                }
                elseif ($null -eq $tracePointX -or $null -eq $tracePointY -or $null -eq $referenceX -or $null -eq $referenceY) {
                    $labelStatus = 'MissingTraceReferencePoint'
                }
                elseif (-not $pointMatches) {
                    $labelStatus = 'UncertainPointMismatch'
                }
                else {
                    $referenceAvailable = $true
                    $labelStatus = if ($labelClass -eq 'Unknown') { 'ReferencePointAvailableClassUnknown' } else { 'ExplicitManifestPurpose' }
                }
            }

            $expectedRadius = Convert-ToNumber (Get-JsonValue (Get-JsonValue $trace 'labelledBallRegion') 'expectedRadius')
            $qualificationRadius = if ($null -ne $expectedRadius) { 3.0 * $expectedRadius } else { $null }
            $replayIdentity = Get-JsonValue $report 'replayAnalyzerConfigurationIdentity'
            if ($replayIdentity -is [string]) {
                try { $replayIdentity = $replayIdentity | ConvertFrom-Json -AsHashtable }
                catch { $replayIdentity = $null }
            }
            $replaySettings = Get-JsonValue $replayIdentity 'Settings'
            $maximumRadiusAtWidth1000 = Convert-ToNumber (Get-JsonValue $replaySettings 'MaximumRadius')
            $maximumSeedRadius = if ($null -ne $maximumRadiusAtWidth1000) { $maximumRadiusAtWidth1000 * ($analysisWidth / 1000.0) } else { $null }
            $nearbyQualifiedSeeds = @(Get-JsonValue $trace 'nearbyQualifiedSeeds')
            $qualifyingSeeds = @()
            if ($referenceAvailable -and $null -ne $qualificationRadius -and $null -ne $maximumSeedRadius) {
                foreach ($seed in $nearbyQualifiedSeeds) {
                    $passesMargin = Get-JsonValue $seed 'passesMargin'
                    $passesMinimumRadius = Get-JsonValue $seed 'passesMinimumRadius'
                    $distance = Convert-ToNumber (Get-JsonValue $seed 'distanceFromLabel')
                    $seedRadius = Convert-ToNumber (Get-JsonValue $seed 'radius')
                    if (($passesMargin -eq $true) -and ($passesMinimumRadius -eq $true) -and ($null -ne $distance) -and ($distance -le $qualificationRadius) -and ($null -ne $seedRadius) -and ($seedRadius -le $maximumSeedRadius)) {
                        $qualifyingSeeds += $seed
                    }
                }
            }
            $qualifyingSeedTelemetryAvailable = $referenceAvailable -and $null -ne $qualificationRadius -and $null -ne $maximumSeedRadius
            $hasQualifyingSeed = if (-not $qualifyingSeedTelemetryAvailable) { $null } else { $qualifyingSeeds.Count -gt 0 }

            $audits = @(Get-JsonValue $analysis 'candidateAudits')
            $searchAudit = $audits |
                Where-Object { (Get-JsonValue $_ 'pass') -eq 'initial' -and (Get-JsonValue $_ 'stage') -eq 'sparse-black-search' } |
                Select-Object -First 1
            $searchDecision = [string](Get-JsonValue $searchAudit 'decision')
            $rankedCandidateCount = $null
            if ($searchDecision -match 'ranked=(\d+)') { $rankedCandidateCount = [int]$Matches[1] }
            $sparseAudits = @($audits |
                Where-Object { (Get-JsonValue $_ 'pass') -eq 'initial' -and (Get-JsonValue $_ 'stage') -eq 'sparse-black-add' } |
                Sort-Object { [int](Get-JsonValue $_ 'index') })
            $candidateTelemetryPresent = ($null -ne $searchAudit) -or ($sparseAudits.Count -gt 0)

            $candidateRecords = [System.Collections.Generic.List[object]]::new()
            $rankSet = [System.Collections.Generic.HashSet[int]]::new()
            foreach ($audit in $sparseAudits) {
                $rankIndex = Convert-ToNumber (Get-JsonValue $audit 'index')
                $proposalCentre = Get-JsonValue $audit 'proposalCentre'
                $rawX = Convert-ToNumber (Get-JsonValue $proposalCentre 'x')
                $rawY = Convert-ToNumber (Get-JsonValue $proposalCentre 'y')
                $rawRadius = Convert-ToNumber (Get-JsonValue $audit 'proposalRadius')
                if ($null -eq $rankIndex -or $null -eq $rawX -or $null -eq $rawY -or $null -eq $rawRadius) { continue }
                $rankIndex = [int]$rankIndex
                [void]$rankSet.Add($rankIndex)
                $candidateX = $rawX * $auditScale
                $candidateY = $rawY * $auditScale
                $candidateRadius = $rawRadius * $auditScale
                $distanceToReference = if ($referenceAvailable) {
                    [Math]::Sqrt([Math]::Pow($candidateX - $referenceX, 2) + [Math]::Pow($candidateY - $referenceY, 2))
                } else { $null }
                $candidateQualifies = if ($null -eq $distanceToReference -or $null -eq $qualificationRadius) { $null } else { $distanceToReference -le $qualificationRadius }
                $seedArea = Get-NearestSeedArea -Seeds @(Get-JsonValue $trace 'productionSeeds') -X $candidateX -Y $candidateY -Radius $candidateRadius
                $candidateRecords.Add([pscustomobject][ordered]@{
                    RunId = [string]$runId
                    ReportKind = $reportKind
                    FrameId = $frameId
                    EvidenceStatus = $evidenceStatus
                    AnalysisWidth = [int]$analysisWidth
                    LabelClass = $labelClass
                    LabelStatus = $labelStatus
                    ReferencePointRole = if ($labelClass -eq 'BlackPresent') { 'BlackLabelPoint' } elseif ($labelClass -eq 'EmptyPocketControl') { 'EmptyPocketSpatialAnchor' } else { 'UnclassifiedReference' }
                    ReferencePointSourceField = $manifestPointField
                    ReferencePointSourceX = $manifestPointX
                    ReferencePointSourceY = $manifestPointY
                    ReferencePointAnalysisX = $referenceX
                    ReferencePointAnalysisY = $referenceY
                    CandidateRank = $rankIndex + 1
                    CandidatePass = Get-JsonValue $audit 'pass'
                    CandidateStage = Get-JsonValue $audit 'stage'
                    CandidateKind = Get-JsonValue $audit 'kind'
                    CandidateRadiusPx = [Math]::Round($candidateRadius, 4)
                    CandidateAreaPixels = $seedArea
                    CandidateAnalysisX = [Math]::Round($candidateX, 4)
                    CandidateAnalysisY = [Math]::Round($candidateY, 4)
                    DistanceToReferencePx = if ($null -ne $distanceToReference) { [Math]::Round($distanceToReference, 4) } else { $null }
                    WithinThreeBallRadii = $candidateQualifies
                    FittedBoundarySupport = Get-JsonValue $audit 'boundarySupport'
                    FittedRadiusPx = if ($null -ne (Convert-ToNumber (Get-JsonValue $audit 'fittedRadius'))) { [Math]::Round((Convert-ToNumber (Get-JsonValue $audit 'fittedRadius')) * $auditScale, 4) } else { $null }
                    FittedCentreAnalysisX = if ($null -ne (Convert-ToNumber (Get-JsonValue (Get-JsonValue $audit 'fittedCentre') 'x'))) { [Math]::Round((Convert-ToNumber (Get-JsonValue (Get-JsonValue $audit 'fittedCentre') 'x')) * $auditScale, 4) } else { $null }
                    FittedCentreAnalysisY = if ($null -ne (Convert-ToNumber (Get-JsonValue (Get-JsonValue $audit 'fittedCentre') 'y'))) { [Math]::Round((Convert-ToNumber (Get-JsonValue (Get-JsonValue $audit 'fittedCentre') 'y')) * $auditScale, 4) } else { $null }
                    ClassifiedKind = Get-JsonValue $audit 'classifiedKind'
                    Decision = Get-JsonValue $audit 'decision'
                    ConfigurationMatchesPrimaryWidth = Get-JsonValue $report 'configurationMatchesPrimaryWidth'
                    ConfigurationSettingsMatchPrimaryWidth = Get-JsonValue $report 'configurationSettingsMatchPrimaryWidth'
                    ConfigurationAlgorithmChangedSinceCapture = Get-JsonValue $report 'configurationAlgorithmChangedSinceCapture'
                    ConfigurationComparison = Get-JsonValue $report 'configurationComparison'
                    FrameConfigurationMatchesPrimary = Get-JsonValue $sample 'configurationMatchesPrimary'
                })
            }

            $prefixCount = 0
            while ($rankSet.Contains($prefixCount)) { $prefixCount++ }
            $auditComplete = $false
            if ($null -ne $rankedCandidateCount) {
                $auditComplete = ($prefixCount -eq $rankedCandidateCount) -and ($rankSet.Count -eq $rankedCandidateCount)
            }

            $topKResults = [System.Collections.Generic.List[object]]::new()
            foreach ($k in $TopK) {
                $coveredCandidateCount = if ($null -ne $rankedCandidateCount) { [Math]::Min($k, $rankedCandidateCount) } else { $k }
                $evaluable = $candidateTelemetryPresent -and ($prefixCount -ge $coveredCandidateCount)
                if ($null -ne $rankedCandidateCount -and $rankedCandidateCount -eq 0) { $evaluable = $candidateTelemetryPresent }
                $spatialHit = $null
                if ($evaluable -and $referenceAvailable) {
                    $spatialHit = @($candidateRecords | Where-Object { $_.CandidateRank -le $coveredCandidateCount -and $_.WithinThreeBallRadii -eq $true }).Count -gt 0
                }
                $topKResults.Add([pscustomobject][ordered]@{
                    K = $k
                    Evaluable = [bool]($evaluable -and $referenceAvailable)
                    SpatialCandidateNearReference = $spatialHit
                    OutcomeKind = if ($labelClass -eq 'BlackPresent') { 'PositiveSpatialHitAtK' } elseif ($labelClass -eq 'EmptyPocketControl') { 'EmptyPocketSpatialHitAtK' } else { 'UnclassifiedSpatialOutcome' }
                    CandidateCountConsidered = $coveredCandidateCount
                })
            }

            $frame = [pscustomobject][ordered]@{
                RunId = [string]$runId
                ReportKind = $reportKind
                ReportPath = $reportPath
                ManifestPath = $manifestPath
                FrameId = $frameId
                EvidenceStatus = $evidenceStatus
                AnalysisWidth = [int]$analysisWidth
                LabelClass = $labelClass
                LabelStatus = $labelStatus
                LabelPurpose = $purpose
                ReferencePointSourceField = $manifestPointField
                ReferencePointAvailable = $referenceAvailable
                ReferencePointRole = if ($labelClass -eq 'BlackPresent') { 'BlackLabelPoint' } elseif ($labelClass -eq 'EmptyPocketControl') { 'EmptyPocketSpatialAnchor' } else { 'UnclassifiedReference' }
                ReferencePointSourceX = $manifestPointX
                ReferencePointSourceY = $manifestPointY
                ReferencePointAnalysisX = $referenceX
                ReferencePointAnalysisY = $referenceY
                QualifyingDistanceRadiusFactor = 3
                QualifyingDistanceRadiusPx = if ($null -ne $qualificationRadius) { [Math]::Round($qualificationRadius, 4) } else { $null }
                QualifyingMaximumSeedRadiusPx = if ($null -ne $maximumSeedRadius) { [Math]::Round($maximumSeedRadius, 4) } else { $null }
                QualifyingSeedTelemetryAvailable = [bool]$qualifyingSeedTelemetryAvailable
                QualifyingSeedExists = $hasQualifyingSeed
                QualifyingSeedCount = if ($qualifyingSeedTelemetryAvailable) { $qualifyingSeeds.Count } else { $null }
                CandidateTelemetryPresent = [bool]$candidateTelemetryPresent
                RankedCandidateCount = $rankedCandidateCount
                CandidateAuditRowCount = $candidateRecords.Count
                CandidateAuditPrefixCount = $prefixCount
                CandidateAuditComplete = [bool]$auditComplete
                ConfigurationMatchesPrimaryWidth = Get-JsonValue $report 'configurationMatchesPrimaryWidth'
                ConfigurationSettingsMatchPrimaryWidth = Get-JsonValue $report 'configurationSettingsMatchPrimaryWidth'
                ConfigurationAlgorithmChangedSinceCapture = Get-JsonValue $report 'configurationAlgorithmChangedSinceCapture'
                ConfigurationComparison = Get-JsonValue $report 'configurationComparison'
                FrameConfigurationMatchesPrimary = Get-JsonValue $sample 'configurationMatchesPrimary'
                TopK = @($topKResults)
                CandidateRows = @($candidateRecords)
            }
            $frames.Add($frame)
            foreach ($candidateRecord in $candidateRecords) { $candidateRows.Add($candidateRecord) }
        }
    }
}

if ($frames.Count -eq 0) {
    throw 'No 800/1000 darkMaskTrace frame records were found in the supplied reports.'
}

$summaryRows = [System.Collections.Generic.List[object]]::new()
foreach ($group in @($frames | Group-Object RunId, ReportKind, LabelClass, AnalysisWidth)) {
    $groupFrames = @($group.Group)
    if ($groupFrames.Count -eq 0) { continue }
    $example = $groupFrames[0]
    foreach ($k in $TopK) {
        $evaluatedFrames = @($groupFrames | Where-Object {
            $_.ReferencePointAvailable -and (@($_.TopK | Where-Object { $_.K -eq $k -and $_.Evaluable }).Count -gt 0)
        })
        $hitFrames = @($evaluatedFrames | Where-Object {
            (@($_.TopK | Where-Object { $_.K -eq $k }).Count -gt 0) -and
            ((@($_.TopK | Where-Object { $_.K -eq $k } | Select-Object -First 1)).SpatialCandidateNearReference -eq $true)
        })
        $metric = if ($example.LabelClass -eq 'BlackPresent') { 'PositiveSpatialHitRateAtK' } elseif ($example.LabelClass -eq 'EmptyPocketControl') { 'EmptyPocketSpatialHitRateAtK' } else { 'UnclassifiedSpatialHitRateAtK' }
        $rate = if ($evaluatedFrames.Count -gt 0) { $hitFrames.Count / [double]$evaluatedFrames.Count } else { $null }
        $summaryRows.Add([pscustomobject][ordered]@{
            RunId = $example.RunId
            ReportKind = $example.ReportKind
            LabelClass = $example.LabelClass
            AnalysisWidth = $example.AnalysisWidth
            K = $k
            Metric = $metric
            FrameCount = $groupFrames.Count
            EvaluatedFrameCount = $evaluatedFrames.Count
            SpatialHitFrameCount = $hitFrames.Count
            Rate = if ($null -ne $rate) { [Math]::Round($rate, 6) } else { $null }
        })
    }
}

$csvRows = [System.Collections.Generic.List[object]]::new()
foreach ($frame in $frames) {
    $topKColumns = [ordered]@{}
    foreach ($k in $TopK) {
        $result = @($frame.TopK | Where-Object { $_.K -eq $k } | Select-Object -First 1)
        $topKColumns["TopK${k}Evaluable"] = if ($result.Count -gt 0) { $result[0].Evaluable } else { $false }
        $topKColumns["TopK${k}SpatialHit"] = if ($result.Count -gt 0) { $result[0].SpatialCandidateNearReference } else { $null }
    }
    $base = [ordered]@{
        RowKind = 'FrameSummary'
        RunId = $frame.RunId
        ReportKind = $frame.ReportKind
        FrameId = $frame.FrameId
        EvidenceStatus = $frame.EvidenceStatus
        AnalysisWidth = $frame.AnalysisWidth
        LabelClass = $frame.LabelClass
        LabelStatus = $frame.LabelStatus
        LabelPurpose = $frame.LabelPurpose
        ReferencePointAvailable = $frame.ReferencePointAvailable
        ReferencePointRole = $frame.ReferencePointRole
        ReferencePointSourceField = $frame.ReferencePointSourceField
        ReferencePointSourceX = $frame.ReferencePointSourceX
        ReferencePointSourceY = $frame.ReferencePointSourceY
        ReferencePointAnalysisX = $frame.ReferencePointAnalysisX
        ReferencePointAnalysisY = $frame.ReferencePointAnalysisY
        QualifyingDistanceRadiusFactor = $frame.QualifyingDistanceRadiusFactor
        QualifyingDistanceRadiusPx = $frame.QualifyingDistanceRadiusPx
        QualifyingMaximumSeedRadiusPx = $frame.QualifyingMaximumSeedRadiusPx
        QualifyingSeedTelemetryAvailable = $frame.QualifyingSeedTelemetryAvailable
        QualifyingSeedExists = $frame.QualifyingSeedExists
        QualifyingSeedCount = $frame.QualifyingSeedCount
        RankedCandidateCount = $frame.RankedCandidateCount
        CandidateAuditRowCount = $frame.CandidateAuditRowCount
        CandidateAuditPrefixCount = $frame.CandidateAuditPrefixCount
        CandidateAuditComplete = $frame.CandidateAuditComplete
        ConfigurationMatchesPrimaryWidth = $frame.ConfigurationMatchesPrimaryWidth
        ConfigurationSettingsMatchPrimaryWidth = $frame.ConfigurationSettingsMatchPrimaryWidth
        ConfigurationAlgorithmChangedSinceCapture = $frame.ConfigurationAlgorithmChangedSinceCapture
        ConfigurationComparison = $frame.ConfigurationComparison
        FrameConfigurationMatchesPrimary = $frame.FrameConfigurationMatchesPrimary
        CandidateRank = $null
        CandidatePass = $null
        CandidateStage = $null
        CandidateKind = $null
        CandidateRadiusPx = $null
        CandidateAreaPixels = $null
        CandidateAnalysisX = $null
        CandidateAnalysisY = $null
        DistanceToReferencePx = $null
        WithinThreeBallRadii = $null
        FittedBoundarySupport = $null
        FittedRadiusPx = $null
        FittedCentreAnalysisX = $null
        FittedCentreAnalysisY = $null
        ClassifiedKind = $null
        Decision = $null
    }
    foreach ($key in $topKColumns.Keys) { $base[$key] = $topKColumns[$key] }
    $csvRows.Add([pscustomobject]$base)

    foreach ($candidate in $frame.CandidateRows) {
        $candidateCsv = [ordered]@{}
        foreach ($key in $base.Keys) { $candidateCsv[$key] = $base[$key] }
        $candidateCsv['RowKind'] = 'Candidate'
        $candidateCsv['CandidateRank'] = $candidate.CandidateRank
        $candidateCsv['CandidatePass'] = $candidate.CandidatePass
        $candidateCsv['CandidateStage'] = $candidate.CandidateStage
        $candidateCsv['CandidateKind'] = $candidate.CandidateKind
        $candidateCsv['CandidateRadiusPx'] = $candidate.CandidateRadiusPx
        $candidateCsv['CandidateAreaPixels'] = $candidate.CandidateAreaPixels
        $candidateCsv['CandidateAnalysisX'] = $candidate.CandidateAnalysisX
        $candidateCsv['CandidateAnalysisY'] = $candidate.CandidateAnalysisY
        $candidateCsv['DistanceToReferencePx'] = $candidate.DistanceToReferencePx
        $candidateCsv['WithinThreeBallRadii'] = $candidate.WithinThreeBallRadii
        $candidateCsv['FittedBoundarySupport'] = $candidate.FittedBoundarySupport
        $candidateCsv['FittedRadiusPx'] = $candidate.FittedRadiusPx
        $candidateCsv['FittedCentreAnalysisX'] = $candidate.FittedCentreAnalysisX
        $candidateCsv['FittedCentreAnalysisY'] = $candidate.FittedCentreAnalysisY
        $candidateCsv['ClassifiedKind'] = $candidate.ClassifiedKind
        $candidateCsv['Decision'] = $candidate.Decision
        $csvRows.Add([pscustomobject]$candidateCsv)
    }
}

$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
$csvPath = Join-Path $resolvedOutputDirectory 'sparse-black-candidate-recall.csv'
$jsonPath = Join-Path $resolvedOutputDirectory 'sparse-black-candidate-recall.json'
$csvRows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

$json = [pscustomobject][ordered]@{
    schemaVersion = 1
    generatedAt = [DateTimeOffset]::UtcNow
    inputPaths = @($InputPath | ForEach-Object { [IO.Path]::GetFullPath($_) })
    parsedReportCount = $parsedReports
    skippedReportCount = $skippedReports
    reportKindsProcessed = @($frames | Select-Object -ExpandProperty ReportKind -Unique)
    missingEvidenceFrameCount = @($frames | Where-Object { $_.EvidenceStatus -ne 'TracePresent' }).Count
    topK = @($TopK)
    qualificationRule = 'Seed within 3 times the expected ball radius from the manifest-matched labelled interior point or empty-pocket spatial anchor, passing the trace margin/minimum-radius flags and the replay maximum-radius gate; top-K candidates use sparse-black-add proposal centres.'
    topKInterpretation = 'A spatial candidate hit within 3R of the labelled interior point or empty-pocket spatial anchor; it is not exact geometric-centre recall or detector-derived black presence. Fitted support and decision are retained as candidate audit fields only.'
    labelRule = 'Only explicit run-manifest purpose text classifies BlackPresent or EmptyPocketControl. Blacktrace reports use manualBlackLabelSourcePixel; pocket-arc reports use only pocketArcLabelSourcePixel with an exact match to each trace source point. Missing, mismatched, or ambiguous labels remain missing/uncertain/unknown.'
    candidateRule = 'Only initial sparse-black-add candidateAudits rows are ranked candidates. Pocket-arc reports use available width1000 analysis rows even when width800 is absent; missing sample/width/trace evidence is retained as unevaluable. Manual-centre fallback probes and detector output are excluded.'
    configurationFlagsArePreserved = $true
    frames = @($frames)
    candidates = @($candidateRows)
    topKSummary = @($summaryRows)
}
$json | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding utf8

Write-Output "Parsed reports: $parsedReports; skipped reports: $skippedReports; frames: $($frames.Count); candidate rows: $($candidateRows.Count)."
Write-Output "CSV: $csvPath"
Write-Output "JSON: $jsonPath"
