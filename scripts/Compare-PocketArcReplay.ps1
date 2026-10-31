<#
.SYNOPSIS
Compares two saved pocket-arc replay reports frame by frame.

.DESCRIPTION
Compares source image hashes, width-1000 inventory, circle centres, the
evaluation-only pocket-aware partial-arc report, and its existing colour
classifier evidence. Timing and unrelated replay fields are intentionally
ignored. Missing fields, null reports, and unmatched frames are never treated
as a successful comparison.

.PARAMETER ReferenceRunDirectory
Run directory containing one o08-replay-proof*.json report.

.PARAMETER CandidateRunDirectory
Run directory containing one o08-replay-proof*.json report.

.PARAMETER Format
Output format: Csv (default) or Json.

.PARAMETER OutputPath
Optional output path. Without it, the selected output is written to the
pipeline.

.EXAMPLE
.\scripts\Compare-PocketArcReplay.ps1 `
  -ReferenceRunDirectory 'work\production\evidence\...\arcclassposv2-121244' `
  -CandidateRunDirectory 'work\production\evidence\...\arcfastpos-123246'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $ReferenceRunDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $CandidateRunDirectory,

    [ValidateSet('Csv', 'Json')]
    [string] $Format = 'Csv',

    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-ReplayReport {
    param([Parameter(Mandatory = $true)] [string] $RunDirectory)

    if (-not (Test-Path -LiteralPath $RunDirectory -PathType Container)) {
        throw "Replay run directory not found: $RunDirectory"
    }

    $reports = @(Get-ChildItem -LiteralPath $RunDirectory -File -Filter 'o08-replay-proof*.json')
    if ($reports.Count -ne 1) {
        throw "Expected exactly one o08-replay-proof*.json report in '$RunDirectory'; found $($reports.Count)."
    }

    $report = Get-Content -LiteralPath $reports[0].FullName -Raw | ConvertFrom-Json
    if ($null -eq $report.samples) {
        throw "Replay report has no samples array: $($reports[0].FullName)"
    }

    return [pscustomobject]@{
        Path = $reports[0].FullName
        Report = $report
    }
}

function Get-PropertyValue {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    if ($null -eq $InputObject) { return [pscustomobject]@{ Found = $false; Value = $null } }

    if ($InputObject -is [System.Collections.IDictionary]) {
        foreach ($key in $InputObject.Keys) {
            if ([string]::Equals([string]$key, $Name, [StringComparison]::OrdinalIgnoreCase)) {
                return [pscustomobject]@{ Found = $true; Value = $InputObject[$key] }
            }
        }
        return [pscustomobject]@{ Found = $false; Value = $null }
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return [pscustomobject]@{ Found = $false; Value = $null } }
    return [pscustomobject]@{ Found = $true; Value = $property.Value }
}

function Get-PropertyPath {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory = $true)] [string] $Path
    )

    $current = $InputObject
    foreach ($part in $Path.Split('.')) {
        $property = Get-PropertyValue -InputObject $current -Name $part
        if (-not $property.Found) { return [pscustomobject]@{ Available = $false; Value = $null } }
        $current = $property.Value
    }
    if ($null -eq $current) { return [pscustomobject]@{ Available = $false; Value = $null } }
    return [pscustomobject]@{ Available = $true; Value = $current }
}

function ConvertTo-CanonicalValue {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return $null }

    if ($Value -is [System.Collections.IDictionary]) {
        $canonical = [ordered]@{}
        foreach ($key in @($Value.Keys | ForEach-Object { [string]$_ } | Sort-Object -CaseSensitive)) {
            $canonical[$key] = ConvertTo-CanonicalValue -Value $Value[$key]
        }
        return ,$canonical
    }

    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $canonical = [ordered]@{}
        foreach ($name in @($Value.PSObject.Properties.Name | Sort-Object -CaseSensitive)) {
            $canonical[$name] = ConvertTo-CanonicalValue -Value $Value.PSObject.Properties[$name].Value
        }
        return ,$canonical
    }

    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $items = [System.Collections.Generic.List[object]]::new()
        foreach ($item in $Value) {
            $items.Add((ConvertTo-CanonicalValue -Value $item))
        }
        return ,$items.ToArray()
    }

    return $Value
}

function Get-CanonicalJson {
    param([AllowNull()] [object] $Value)
    $canonical = ConvertTo-CanonicalValue -Value $Value
    return ConvertTo-Json -InputObject $canonical -Depth 100 -Compress
}

function Compare-AvailableValues {
    param(
        [Parameter(Mandatory = $true)] [object] $Reference,
        [Parameter(Mandatory = $true)] [object] $Candidate
    )

    if (-not $Reference.Available -or -not $Candidate.Available) { return 'Unavailable' }
    if ((Get-CanonicalJson $Reference.Value) -ceq (Get-CanonicalJson $Candidate.Value)) { return 'Match' }
    return 'Mismatch'
}

function Get-InventoryProjection {
    param([AllowNull()] [object] $Sample)

    $analysisState = Get-PropertyPath -InputObject $Sample -Path 'width1000.analysis'
    if (-not $analysisState.Available) { return [pscustomobject]@{ Available = $false; Value = $null } }

    $analysis = $analysisState.Value
    $requiredNames = @('circleCount', 'counts', 'uncertain', 'layoutUncertain', 'obstructed')
    $values = [ordered]@{}
    foreach ($name in $requiredNames) {
        $property = Get-PropertyValue -InputObject $analysis -Name $name
        if (-not $property.Found -or $null -eq $property.Value) {
            return [pscustomobject]@{ Available = $false; Value = $null }
        }
        $values[$name] = $property.Value
    }

    return [pscustomobject]@{ Available = $true; Value = $values }
}

function Get-CircleProjection {
    param([AllowNull()] [object] $Sample)

    $circlesState = Get-PropertyPath -InputObject $Sample -Path 'width1000.analysis.circles'
    if (-not $circlesState.Available -or $circlesState.Value -is [string]) {
        return [pscustomobject]@{ Available = $false; Value = $null }
    }

    $circles = [System.Collections.Generic.List[object]]::new()
    foreach ($circle in $circlesState.Value) {
        $kind = Get-PropertyValue -InputObject $circle -Name 'kind'
        $x = Get-PropertyValue -InputObject $circle -Name 'x'
        $y = Get-PropertyValue -InputObject $circle -Name 'y'
        $radius = Get-PropertyValue -InputObject $circle -Name 'radius'
        if (-not $kind.Found -or -not $x.Found -or -not $y.Found -or -not $radius.Found -or
            $null -eq $kind.Value -or $null -eq $x.Value -or $null -eq $y.Value -or $null -eq $radius.Value) {
            return [pscustomobject]@{ Available = $false; Value = $null }
        }
        $ballId = Get-PropertyValue -InputObject $circle -Name 'ballId'
        $circles.Add([pscustomobject]@{
            ballId = if ($ballId.Found) { $ballId.Value } else { $null }
            kind = $kind.Value
            x = $x.Value
            y = $y.Value
            radius = $radius.Value
        })
    }

    $orderedCircles = @($circles.ToArray() | Sort-Object x, y, kind, ballId)
    return [pscustomobject]@{ Available = $true; Value = $orderedCircles }
}

function New-FrameIndex {
    param([Parameter(Mandatory = $true)] [object[]] $Samples)

    $index = @{}
    foreach ($sample in $Samples) {
        $frameState = Get-PropertyValue -InputObject $sample -Name 'frameId'
        $frameNumber = 0L
        if (-not $frameState.Found -or -not [long]::TryParse([string]$frameState.Value, [ref]$frameNumber)) {
            throw 'Replay report contains a sample without an integer frameId.'
        }
        $key = $frameNumber.ToString([Globalization.CultureInfo]::InvariantCulture)
        if (-not $index.ContainsKey($key)) { $index[$key] = [System.Collections.Generic.List[object]]::new() }
        $index[$key].Add($sample)
    }
    return $index
}

$reference = Read-ReplayReport -RunDirectory $ReferenceRunDirectory
$candidate = Read-ReplayReport -RunDirectory $CandidateRunDirectory
$referenceIndex = New-FrameIndex -Samples @($reference.Report.samples)
$candidateIndex = New-FrameIndex -Samples @($candidate.Report.samples)
$frameIds = @(@($referenceIndex.Keys) + @($candidateIndex.Keys) | Sort-Object { [long]$_ } -Unique)
$rows = [System.Collections.Generic.List[object]]::new()

foreach ($frameId in $frameIds) {
    $referenceSamples = if ($referenceIndex.ContainsKey($frameId)) { $referenceIndex[$frameId] } else { @() }
    $candidateSamples = if ($candidateIndex.ContainsKey($frameId)) { $candidateIndex[$frameId] } else { @() }

    $frameStatus = if ($referenceSamples.Count -gt 1 -and $candidateSamples.Count -gt 1) { 'DuplicateBoth' }
        elseif ($referenceSamples.Count -gt 1) { 'DuplicateReference' }
        elseif ($candidateSamples.Count -gt 1) { 'DuplicateCandidate' }
        elseif ($referenceSamples.Count -eq 0) { 'CandidateOnly' }
        elseif ($candidateSamples.Count -eq 0) { 'ReferenceOnly' }
        else { 'Matched' }

    $pixelHashStatus = 'Unavailable'
    $inventoryStatus = 'Unavailable'
    $circleStatus = 'Unavailable'
    $arcStatus = 'Unavailable'
    $classifierStatus = 'Unavailable'
    $referenceHash = $null
    $candidateHash = $null

    if ($frameStatus -eq 'Matched') {
        $referenceSample = $referenceSamples[0]
        $candidateSample = $candidateSamples[0]
        $referenceHashState = Get-PropertyPath -InputObject $referenceSample -Path 'pixelSha256'
        $candidateHashState = Get-PropertyPath -InputObject $candidateSample -Path 'pixelSha256'
        if ($referenceHashState.Available -and $candidateHashState.Available) {
            $referenceHash = [string]$referenceHashState.Value
            $candidateHash = [string]$candidateHashState.Value
            $pixelHashStatus = if ($referenceHash -ceq $candidateHash) { 'Match' } else { 'Mismatch' }
        }

        $inventoryStatus = Compare-AvailableValues `
            -Reference (Get-InventoryProjection -Sample $referenceSample) `
            -Candidate (Get-InventoryProjection -Sample $candidateSample)
        $circleStatus = Compare-AvailableValues `
            -Reference (Get-CircleProjection -Sample $referenceSample) `
            -Candidate (Get-CircleProjection -Sample $candidateSample)

        $referenceArc = Get-PropertyPath -InputObject $referenceSample `
            -Path 'width1000.darkMaskTrace.manualCentreProbe.pocketAwarePartialArcExperiment'
        $candidateArc = Get-PropertyPath -InputObject $candidateSample `
            -Path 'width1000.darkMaskTrace.manualCentreProbe.pocketAwarePartialArcExperiment'
        $arcStatus = Compare-AvailableValues -Reference $referenceArc -Candidate $candidateArc

        $referenceClassifier = Get-PropertyPath -InputObject $referenceSample `
            -Path 'width1000.darkMaskTrace.manualCentreProbe.pocketAwarePartialArcExperiment.candidateColourClassifier'
        $candidateClassifier = Get-PropertyPath -InputObject $candidateSample `
            -Path 'width1000.darkMaskTrace.manualCentreProbe.pocketAwarePartialArcExperiment.candidateColourClassifier'
        $referenceClassifierAvailable = Get-PropertyValue -InputObject $referenceClassifier.Value -Name 'available'
        $candidateClassifierAvailable = Get-PropertyValue -InputObject $candidateClassifier.Value -Name 'available'
        $referenceClassifierEvidence = Get-PropertyValue -InputObject $referenceClassifier.Value -Name 'evidence'
        $candidateClassifierEvidence = Get-PropertyValue -InputObject $candidateClassifier.Value -Name 'evidence'
        if ($referenceClassifier.Available -and $candidateClassifier.Available -and
            $referenceClassifierAvailable.Found -and $candidateClassifierAvailable.Found -and
            $referenceClassifierAvailable.Value -eq $true -and $candidateClassifierAvailable.Value -eq $true -and
            $referenceClassifierEvidence.Found -and $candidateClassifierEvidence.Found -and
            $null -ne $referenceClassifierEvidence.Value -and $null -ne $candidateClassifierEvidence.Value) {
            $classifierStatus = Compare-AvailableValues `
                -Reference ([pscustomobject]@{ Available = $true; Value = $referenceClassifierEvidence.Value }) `
                -Candidate ([pscustomobject]@{ Available = $true; Value = $candidateClassifierEvidence.Value })
        }
    }

    $statuses = @($pixelHashStatus, $inventoryStatus, $circleStatus, $arcStatus, $classifierStatus)
    $overallStatus = if ($frameStatus -ne 'Matched') { 'Unavailable' }
        elseif ($statuses -contains 'Mismatch') { 'Mismatch' }
        elseif ($statuses -contains 'Unavailable') { 'Unavailable' }
        else { 'Match' }

    $rows.Add([pscustomobject]@{
        frameId = $frameId
        frameStatus = $frameStatus
        referencePixelSha256 = $referenceHash
        candidatePixelSha256 = $candidateHash
        pixelHashStatus = $pixelHashStatus
        inventoryStatus = $inventoryStatus
        ballCentresStatus = $circleStatus
        pocketArcStatus = $arcStatus
        classifierEvidenceStatus = $classifierStatus
        overallStatus = $overallStatus
    })
}

$rowArray = @($rows.ToArray())
$summary = [pscustomobject]@{
    schemaVersion = 1
    referenceReport = $reference.Path
    candidateReport = $candidate.Path
    frameCount = $rowArray.Count
    matchedFrames = @($rowArray | Where-Object frameStatus -eq 'Matched').Count
    parityFrames = @($rowArray | Where-Object overallStatus -eq 'Match').Count
    mismatchFrames = @($rowArray | Where-Object overallStatus -eq 'Mismatch').Count
    unavailableFrames = @($rowArray | Where-Object overallStatus -eq 'Unavailable').Count
    frames = $rowArray
}

$output = if ($Format -eq 'Json') {
    ConvertTo-Json -InputObject $summary -Depth 100
} else {
    $rowArray | ConvertTo-Csv -NoTypeInformation
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $parentPath = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parentPath) -and -not (Test-Path -LiteralPath $parentPath)) {
        New-Item -ItemType Directory -Path $parentPath -Force | Out-Null
    }
    Set-Content -LiteralPath $OutputPath -Value $output -Encoding utf8
} else {
    Write-Output $output
}

Write-Host "Pocket-arc replay comparison: $($summary.parityFrames)/$($summary.frameCount) full parity; $($summary.mismatchFrames) mismatches; $($summary.unavailableFrames) unavailable."
