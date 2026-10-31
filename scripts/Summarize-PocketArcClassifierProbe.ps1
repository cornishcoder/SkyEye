<#
.SYNOPSIS
Flattens width-1000 pocket-aware arc and candidate colour-classifier evidence.

.DESCRIPTION
Reads explicit replay run directories or searches an evidence root recursively
for o08-replay-proof*.json reports. Emits one row per available sample/frame,
with the width-1000 diagnostic probe, classifier evidence, ordinary full-analysis
inventory counts, configuration match flags, and camera state from the run
manifest. Missing source fields remain empty; this script does not infer labels
or replace absent values with defaults.

Camera output is limited to cameraBefore.state and cameraAfter.state. Device
identifiers, access tokens, addresses, and other camera details are not copied.

.PARAMETER RunDirectory
One or more run directories containing a replay report at the top level.

.PARAMETER EvidenceRoot
An evidence directory searched recursively for replay reports.

.PARAMETER Format
Output format: Csv (default) or Json.

.PARAMETER OutputPath
Optional output file. Without it, the selected format is written to the pipeline.

.EXAMPLE
./scripts/Summarize-PocketArcClassifierProbe.ps1 -RunDirectory work/production/evidence/run-a,work/production/evidence/run-b

.EXAMPLE
./scripts/Summarize-PocketArcClassifierProbe.ps1 -EvidenceRoot work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-probes -Format Json -OutputPath work/pocket-arc-classifier-probe.json
#>
[CmdletBinding(DefaultParameterSetName = 'RunDirectory')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'RunDirectory', Position = 0)]
    [Alias('Path')]
    [ValidateNotNullOrEmpty()]
    [string[]] $RunDirectory,

    [Parameter(Mandatory = $true, ParameterSetName = 'EvidenceRoot')]
    [ValidateNotNullOrEmpty()]
    [string] $EvidenceRoot,

    [ValidateSet('Csv', 'Json')]
    [string] $Format = 'Csv',

    [string] $OutputPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-ObjectValue {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    if ($null -eq $InputObject) { return $null }

    if ($InputObject -is [System.Collections.IDictionary]) {
        foreach ($key in $InputObject.Keys) {
            if ([string]::Equals([string]$key, $Name, [System.StringComparison]::OrdinalIgnoreCase)) {
                return $InputObject[$key]
            }
        }
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Get-FirstObjectValue {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory = $true)] [string[]] $Names
    )

    foreach ($name in $Names) {
        $value = Get-ObjectValue -InputObject $InputObject -Name $name
        if ($null -ne $value) { return $value }
    }
    return $null
}

function Get-ArrayItem {
    param([AllowNull()] [object] $Collection, [int] $Index)

    if ($null -eq $Collection -or $Index -lt 0) { return $null }
    $items = @($Collection)
    if ($items.Count -gt $Index) { return $items[$Index] }
    return $null
}

function Get-CollectionCount {
    param([AllowNull()] [object] $Collection)

    if ($null -eq $Collection) { return 0 }
    return @($Collection).Count
}

function Get-ManifestForReport {
    param([Parameter(Mandatory = $true)] [string] $ReportDirectory)

    foreach ($name in @('manifest.json', 'staging-manifest.json')) {
        $path = Join-Path $ReportDirectory $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            try {
                return [pscustomobject]@{
                    Path = $path
                    Data = (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
                }
            } catch {
                Write-Warning "Could not parse manifest '$path': $($_.Exception.Message)"
                return [pscustomobject]@{ Path = $path; Data = $null }
            }
        }
    }

    return [pscustomobject]@{ Path = $null; Data = $null }
}

function Get-ReportFiles {
    param(
        [Parameter(Mandatory = $true)] [string[]] $Directories,
        [switch] $Recurse
    )

    $reports = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($directory in $Directories) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            throw "Directory does not exist or is not a directory: '$directory'."
        }

        $resolved = (Resolve-Path -LiteralPath $directory).Path
        if ($Recurse) {
            $items = Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json' -Recurse
        } else {
            $items = Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json'
        }
        foreach ($item in $items) { $reports.Add($item) }
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($report in $reports) {
        if ($seen.Add($report.FullName)) { $report }
    }
}

function Get-FullAnalysisObservation {
    param(
        [AllowNull()] [object] $Report,
        [AllowNull()] [object] $Sample,
        [int] $Index,
        [AllowNull()] [object] $SampleFrameId
    )

    $fullAnalysis = Get-ObjectValue -InputObject $Report -Name 'fullAnalysis1000'
    $observations = Get-ObjectValue -InputObject $fullAnalysis -Name 'observations'
    $observation = Get-ArrayItem -Collection $observations -Index $Index

    # The full-analysis observation list is emitted in sample order. If both
    # records carry frame IDs, reject an index pairing that disagrees.
    if ($null -ne $observation -and $null -ne $SampleFrameId) {
        $observationFrameId = Get-FirstObjectValue -InputObject $observation -Names @('frameId', 'frameID', 'id')
        if ($null -ne $observationFrameId -and [string]$observationFrameId -ne [string]$SampleFrameId) {
            $observation = $null
        }
    }
    if ($null -ne $observation) { return $observation }

    # Some replay variants keep the same ordinary analysis directly on the
    # sample instead of in fullAnalysis1000.observations.
    $sampleWidth = Get-ObjectValue -InputObject $Sample -Name 'width1000'
    $analysis = Get-ObjectValue -InputObject $sampleWidth -Name 'analysis'
    if ($null -ne $analysis) { return $analysis }
    return Get-ObjectValue -InputObject $Sample -Name 'analysis1000'
}

function Get-CountValue {
    param([AllowNull()] [object] $Analysis, [Parameter(Mandatory = $true)] [string] $Kind)

    $counts = Get-ObjectValue -InputObject $Analysis -Name 'counts'
    return Get-ObjectValue -InputObject $counts -Name $Kind
}

function Get-ProposalSource {
    param([AllowNull()] [object] $Value)

    if ($null -eq $Value) { return $null }
    switch -Exact ([string]$Value) {
        'nearest qualifying dark-mask seed' { return 'nearest seed' }
        'manual-label fallback' { return 'label fallback' }
        default { return [string]$Value }
    }
}

if ($PSCmdlet.ParameterSetName -eq 'EvidenceRoot') {
    $reportFiles = @(Get-ReportFiles -Directories @($EvidenceRoot) -Recurse)
} else {
    if ($RunDirectory.Count -eq 0) { throw 'Supply at least one run directory.' }
    $reportFiles = @(Get-ReportFiles -Directories $RunDirectory)

    foreach ($directory in $RunDirectory) {
        $resolved = (Resolve-Path -LiteralPath $directory).Path
        if (-not (Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json' | Select-Object -First 1)) {
            throw "No o08-replay-proof*.json report was found at the top level of run directory '$resolved'. Use -EvidenceRoot to search nested run directories."
        }
    }
}

if ($reportFiles.Count -eq 0) {
    throw "No o08-replay-proof*.json reports were found under the supplied location(s)."
}

$rows = [System.Collections.Generic.List[object]]::new()
$parsedReportCount = 0
foreach ($reportFile in $reportFiles) {
    try {
        $report = Get-Content -LiteralPath $reportFile.FullName -Raw | ConvertFrom-Json
    } catch {
        Write-Warning "Skipping unreadable replay report '$($reportFile.FullName)': $($_.Exception.Message)"
        continue
    }
    if ($null -eq $report) {
        Write-Warning "Skipping empty replay report '$($reportFile.FullName)'."
        continue
    }
    $parsedReportCount++

    $manifestResult = Get-ManifestForReport -ReportDirectory $reportFile.DirectoryName
    $manifest = $manifestResult.Data
    $runId = Get-FirstObjectValue -InputObject $manifest -Names @('run', 'runId', 'id')
    if ($null -eq $runId) { $runId = Get-FirstObjectValue -InputObject $report -Names @('run', 'runId', 'id') }
    if ($null -eq $runId) { $runId = $reportFile.Directory.Name }

    $samples = Get-ObjectValue -InputObject $report -Name 'samples'
    $fullAnalysis = Get-ObjectValue -InputObject $report -Name 'fullAnalysis1000'
    $observations = Get-ObjectValue -InputObject $fullAnalysis -Name 'observations'
    $manifestFrames = Get-ObjectValue -InputObject $manifest -Name 'sourceFrames'

    $frameCount = [Math]::Max(
        (Get-CollectionCount -Collection $samples),
        [Math]::Max(
            (Get-CollectionCount -Collection $observations),
            (Get-CollectionCount -Collection $manifestFrames)))
    $isRunSummary = $frameCount -eq 0
    if ($isRunSummary) { $frameCount = 1 }

    for ($index = 0; $index -lt $frameCount; $index++) {
        $sample = if (-not $isRunSummary) { Get-ArrayItem -Collection $samples -Index $index } else { $null }
        $manifestFrame = if (-not $isRunSummary) { Get-ArrayItem -Collection $manifestFrames -Index $index } else { $null }
        $sampleFrameId = Get-FirstObjectValue -InputObject $sample -Names @('frameId', 'frameID', 'id')
        $observation = Get-FullAnalysisObservation -Report $report -Sample $sample -Index $index -SampleFrameId $sampleFrameId

        $frameId = $sampleFrameId
        if ($null -eq $frameId) { $frameId = Get-FirstObjectValue -InputObject $observation -Names @('frameId', 'frameID', 'id') }
        if ($null -eq $frameId) { $frameId = Get-FirstObjectValue -InputObject $manifestFrame -Names @('frameId', 'frameID', 'id') }

        $width1000 = Get-ObjectValue -InputObject $sample -Name 'width1000'
        $darkMaskTrace = Get-ObjectValue -InputObject $width1000 -Name 'darkMaskTrace'
        $manualCentreProbe = Get-ObjectValue -InputObject $darkMaskTrace -Name 'manualCentreProbe'
        $arcExperiment = Get-ObjectValue -InputObject $manualCentreProbe -Name 'pocketAwarePartialArcExperiment'
        $candidate = Get-ObjectValue -InputObject $arcExperiment -Name 'candidate'
        $classifier = Get-ObjectValue -InputObject $arcExperiment -Name 'candidateColourClassifier'
        $classifierEvidence = Get-ObjectValue -InputObject $classifier -Name 'evidence'
        $sampleCoverage = Get-ObjectValue -InputObject $classifierEvidence -Name 'sampleCoverage'

        $cameraBefore = Get-ObjectValue -InputObject $manifest -Name 'cameraBefore'
        $cameraAfter = Get-ObjectValue -InputObject $manifest -Name 'cameraAfter'

        $rows.Add([pscustomobject][ordered]@{
            RowKind = if ($isRunSummary) { 'RunSummary' } else { 'Frame' }
            RunId = $runId
            FrameId = $frameId
            ProposalSource = Get-ProposalSource (Get-ObjectValue -InputObject $arcExperiment -Name 'proposalSource')

            DiagnosticArcChecksPass = Get-ObjectValue -InputObject $candidate -Name 'diagnosticArcChecksPass'
            ArcGlobalSupportedSectorCount = Get-ObjectValue -InputObject $candidate -Name 'globalSupportedSectorCount'
            ArcGlobalSupportFraction = Get-ObjectValue -InputObject $candidate -Name 'globalSupportFraction'
            ArcVisibleSectorCount = Get-ObjectValue -InputObject $candidate -Name 'visibleSectorCount'
            ArcSupportedVisibleSectorCount = Get-ObjectValue -InputObject $candidate -Name 'supportedVisibleSectorCount'
            ArcLongestContiguousSupportedRun = Get-ObjectValue -InputObject $candidate -Name 'longestContiguousSupportedRun'
            ArcSupportedSpanDegrees = Get-ObjectValue -InputObject $candidate -Name 'supportedArcSpanDegrees'
            CandidateToProposalOffset = Get-ObjectValue -InputObject $candidate -Name 'candidateToProposalOffset'
            CandidateToProposalOffsetFraction = Get-ObjectValue -InputObject $candidate -Name 'candidateToProposalOffsetFraction'
            MaximumCandidateToProposalOffset = Get-ObjectValue -InputObject $candidate -Name 'maximumCandidateToProposalOffset'
            MaximumCandidateToProposalOffsetFraction = Get-ObjectValue -InputObject $candidate -Name 'maximumCandidateToProposalOffsetFraction'

            ClassifierAvailable = Get-ObjectValue -InputObject $classifier -Name 'available'
            ClassifierUnavailableReason = Get-ObjectValue -InputObject $classifier -Name 'unavailableReason'
            ClassifierKind = Get-ObjectValue -InputObject $classifierEvidence -Name 'kind'
            ClassifierPattern = Get-ObjectValue -InputObject $classifierEvidence -Name 'pattern'
            ClassifierQuality = Get-ObjectValue -InputObject $classifierEvidence -Name 'quality'
            ClassifierQualityIsProbability = Get-ObjectValue -InputObject $classifierEvidence -Name 'qualityIsProbability'
            BodySampleCount = Get-ObjectValue -InputObject $sampleCoverage -Name 'bodySampleCount'
            UsableBodySampleCount = Get-ObjectValue -InputObject $sampleCoverage -Name 'usableBodySampleCount'
            RimSampleCount = Get-ObjectValue -InputObject $sampleCoverage -Name 'rimSampleCount'
            UsableRimSampleCount = Get-ObjectValue -InputObject $sampleCoverage -Name 'usableRimSampleCount'
            BodyDarkFraction = Get-ObjectValue -InputObject $sampleCoverage -Name 'bodyDarkFraction'
            RimDarkFraction = Get-ObjectValue -InputObject $sampleCoverage -Name 'rimDarkFraction'

            CircleCount1000 = Get-FirstObjectValue -InputObject $observation -Names @('circleCount', 'detectedCount', 'ballCount')
            CueCount1000 = Get-CountValue -Analysis $observation -Kind 'cue'
            RedCount1000 = Get-CountValue -Analysis $observation -Kind 'red'
            YellowCount1000 = Get-CountValue -Analysis $observation -Kind 'yellow'
            BlackCount1000 = Get-CountValue -Analysis $observation -Kind 'black'
            Uncertain1000 = Get-FirstObjectValue -InputObject $observation -Names @('uncertain', 'isUncertain')
            LayoutUncertain1000 = Get-FirstObjectValue -InputObject $observation -Names @('layoutUncertain', 'layoutIsUncertain')
            Obstructed1000 = Get-FirstObjectValue -InputObject $observation -Names @('obstructed', 'isObstructed')

            ReportConfigurationMatchesPrimaryWidth = Get-ObjectValue -InputObject $report -Name 'configurationMatchesPrimaryWidth'
            ReportConfigurationSettingsMatchPrimaryWidth = Get-ObjectValue -InputObject $report -Name 'configurationSettingsMatchPrimaryWidth'
            ReportConfigurationAlgorithmChangedSinceCapture = Get-ObjectValue -InputObject $report -Name 'configurationAlgorithmChangedSinceCapture'
            FrameConfigurationMatchesPrimary = Get-ObjectValue -InputObject $sample -Name 'configurationMatchesPrimary'
            CameraBeforeState = Get-ObjectValue -InputObject $cameraBefore -Name 'state'
            CameraAfterState = Get-ObjectValue -InputObject $cameraAfter -Name 'state'
        })
    }
}

if ($parsedReportCount -eq 0) {
    throw 'No readable replay reports were found; check the report files and JSON syntax.'
}

if ($Format -eq 'Json') {
    $content = ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 8
} else {
    $content = (@($rows.ToArray()) | ConvertTo-Csv -NoTypeInformation) -join [Environment]::NewLine
}

if ($OutputPath) {
    $fullOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = [System.IO.Path]::GetDirectoryName($fullOutputPath)
    if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
        throw "Output directory does not exist: '$outputDirectory'."
    }
    [System.IO.File]::WriteAllText($fullOutputPath, $content, [System.Text.UTF8Encoding]::new($false))
} else {
    Write-Output $content
}
