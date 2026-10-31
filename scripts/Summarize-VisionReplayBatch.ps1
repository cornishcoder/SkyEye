<#
.SYNOPSIS
Summarizes saved Vision Device replay reports into a flat CSV or JSON table.

.DESCRIPTION
Accepts explicit run directories or an evidence root to search recursively for
o08-replay-proof*.json reports. The output has one row per frame when frame
samples are available, with report and manifest metadata repeated on each row.
Missing fields stay empty. Device identifiers and camera access details are not
copied from manifests.

.PARAMETER RunDirectory
One or more run directories containing a replay report. Reports are searched
at the top level of each supplied directory.

.PARAMETER EvidenceRoot
An evidence directory searched recursively for replay reports.

.PARAMETER Format
Output format: Csv (default) or Json.

.PARAMETER OutputPath
Optional output file. Without it, the selected format is written to the pipeline.

.PARAMETER RunSummaryPath
Optional separate output file containing one run-level row per manifest, including
phase timings from versioned phaseTimings or legacy timingsMs. Uses the selected format.

.EXAMPLE
./scripts/Summarize-VisionReplayBatch.ps1 -RunDirectory work/production/evidence/run-a,work/production/evidence/run-b

.EXAMPLE
./scripts/Summarize-VisionReplayBatch.ps1 -EvidenceRoot work/production/evidence/code70-daylight-pocket-capture-20261031 -Format Json -OutputPath work/vision-replay-batch.json

.EXAMPLE
./scripts/Summarize-VisionReplayBatch.ps1 -EvidenceRoot work/production/evidence/code70-daylight-pocket-capture-20261031 -RunSummaryPath work/vision-replay-runs.csv
#>
[CmdletBinding(DefaultParameterSetName = 'RunDirectory')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'RunDirectory', Position = 0)]
    [Alias('Path')]
    [string[]] $RunDirectory,

    [Parameter(Mandatory = $true, ParameterSetName = 'EvidenceRoot')]
    [string] $EvidenceRoot,

    [ValidateSet('Csv', 'Json')]
    [string] $Format = 'Csv',

    [string] $OutputPath,

    [string] $RunSummaryPath
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($OutputPath -and $RunSummaryPath -and [string]::Equals([System.IO.Path]::GetFullPath($OutputPath), [System.IO.Path]::GetFullPath($RunSummaryPath), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputPath and RunSummaryPath must refer to different files.'
}

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

function Format-TimestampValue {
    param([AllowNull()] [object] $Value)
    if ($Value -is [datetimeoffset]) {
        return $Value.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [datetime]) {
        return $Value.ToString('o', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($null -ne $Value) { return [string]$Value }
    return $null
}

function Get-ArrayItem {
    param([AllowNull()] [object] $Collection, [int] $Index)
    if ($null -eq $Collection -or $Index -lt 0) { return $null }
    $items = @($Collection)
    if ($items.Count -gt $Index) { return $items[$Index] }
    return $null
}

function Get-AnalysisForWidth {
    param(
        [AllowNull()] [object] $Sample,
        [AllowNull()] [object] $Report,
        [int] $Index,
        [int] $Width
    )

    $widthKey = "width$Width"
    $sampleWidth = Get-ObjectValue -InputObject $Sample -Name $widthKey
    $analysis = Get-ObjectValue -InputObject $sampleWidth -Name 'analysis'
    if ($null -ne $analysis) { return $analysis }

    # Some screening reports put the per-frame analysis in the report-level
    # observations array while samples hold only frame identity and timings.
    $fullAnalysis = Get-ObjectValue -InputObject $Report -Name "fullAnalysis$Width"
    $observations = Get-ObjectValue -InputObject $fullAnalysis -Name 'observations'
    $analysis = Get-ArrayItem -Collection $observations -Index $Index
    if ($null -ne $analysis) { return $analysis }

    $direct = Get-ObjectValue -InputObject $Sample -Name "analysis$Width"
    if ($null -ne $direct) { return $direct }

    # Accommodate reports that store summary fields directly below widthN.
    foreach ($field in @('circleCount', 'counts', 'uncertain', 'obstructed')) {
        if ($null -ne (Get-ObjectValue -InputObject $sampleWidth -Name $field)) { return $sampleWidth }
    }
    return $null
}

function Get-CountValue {
    param([AllowNull()] [object] $Analysis, [string] $Kind)
    $counts = Get-ObjectValue -InputObject $Analysis -Name 'counts'
    return Get-ObjectValue -InputObject $counts -Name $Kind
}

function Get-DetectedCount {
    param([AllowNull()] [object] $Analysis)

    $count = Get-FirstObjectValue -InputObject $Analysis -Names @('circleCount', 'detectedCount', 'ballCount')
    if ($null -ne $count) { return $count }

    $counts = Get-ObjectValue -InputObject $Analysis -Name 'counts'
    if ($null -ne $counts) {
        $sum = 0.0
        $sawCount = $false
        if ($counts -is [System.Collections.IDictionary]) {
            foreach ($value in $counts.Values) {
                $number = 0.0
                if ([double]::TryParse([string]$value, [ref]$number)) { $sum += $number; $sawCount = $true }
            }
        } else {
            foreach ($property in $counts.PSObject.Properties) {
                $number = 0.0
                if ([double]::TryParse([string]$property.Value, [ref]$number)) { $sum += $number; $sawCount = $true }
            }
        }
        if ($sawCount) { return $sum }
    }

    $circles = Get-ObjectValue -InputObject $Analysis -Name 'circles'
    if ($null -ne $circles) { return $circles.Count }
    return $null
}

function Get-FrameWallMs {
    param([AllowNull()] [object] $Sample, [int] $Width)

    $metricCandidates = @(
        (Get-ObjectValue -InputObject $Sample -Name "analysis${Width}Metrics"),
        (Get-ObjectValue -InputObject $Sample -Name "width${Width}Metrics"),
        (Get-ObjectValue -InputObject (Get-ObjectValue -InputObject $Sample -Name "width$Width") -Name 'metrics')
    )
    foreach ($metrics in $metricCandidates) {
        $value = Get-FirstObjectValue -InputObject $metrics -Names @('wallMs', 'wallTimeMs', 'elapsedMs', 'durationMs')
        if ($null -ne $value) { return $value }
    }
    return $null
}

function Get-RunTimingData {
    param([AllowNull()] [object] $Manifest)

    $phases = [System.Collections.Specialized.OrderedDictionary]::new()
    $timings = Get-ObjectValue -InputObject $Manifest -Name 'timingsMs'
    $versionedTimings = Get-ObjectValue -InputObject $Manifest -Name 'phaseTimings'
    $schemaVersion = Get-ObjectValue -InputObject $versionedTimings -Name 'schemaVersion'
    $unit = Get-ObjectValue -InputObject $versionedTimings -Name 'unit'
    $versionedPhases = Get-ObjectValue -InputObject $versionedTimings -Name 'phases'
    $useVersionedTimings = ($schemaVersion -eq 1) -and
        [string]::Equals([string]$unit, 'milliseconds', [System.StringComparison]::OrdinalIgnoreCase) -and
        ($null -ne $versionedPhases) -and ($versionedPhases -isnot [string])

    if ($useVersionedTimings) {
        foreach ($phase in @($versionedPhases)) {
            $name = Get-ObjectValue -InputObject $phase -Name 'name'
            if ($null -eq $name -or [string]::IsNullOrWhiteSpace([string]$name)) { continue }
            $phases[[string]$name] = Get-ObjectValue -InputObject $phase -Name 'elapsedMs'
        }
        if ($phases.Count -eq 0) { $useVersionedTimings = $false }
    }

    if (-not $useVersionedTimings -and $null -ne $timings) {
        if ($timings -is [System.Collections.IDictionary]) {
            foreach ($key in $timings.Keys) {
                if ([string]::Equals([string]$key, 'total', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
                $value = $timings[$key]
                if ($null -eq $value -or $value -is [string] -or $value -is [ValueType]) {
                    $phases[[string]$key] = $value
                }
            }
        } else {
            foreach ($property in $timings.PSObject.Properties) {
                if ([string]::Equals($property.Name, 'total', [System.StringComparison]::OrdinalIgnoreCase)) { continue }
                $value = $property.Value
                if ($null -eq $value -or $value -is [string] -or $value -is [ValueType]) {
                    $phases[[string]$property.Name] = $value
                }
            }
        }
    }

    return [pscustomobject]@{
        Phases = $phases
        TotalMs = Get-ObjectValue -InputObject $timings -Name 'total'
    }
}

function Get-ManifestForReport {
    param([Parameter(Mandatory = $true)] [string] $ReportDirectory)

    foreach ($name in @('manifest.json', 'staging-manifest.json')) {
        $path = Join-Path $ReportDirectory $name
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            try {
                return [pscustomobject]@{ Path = $path; Data = (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json) }
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
        $items = if ($Recurse) {
            Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json' -Recurse
        } else {
            Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json'
        }
        foreach ($item in $items) { $reports.Add($item) }
    }

    $seen = @{}
    foreach ($report in $reports) {
        $key = $report.FullName.ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = $true
            $report
        }
    }
}

if ($PSCmdlet.ParameterSetName -eq 'EvidenceRoot') {
    $directories = @($EvidenceRoot)
    $reportFiles = @(Get-ReportFiles -Directories $directories -Recurse)
} else {
    if ($RunDirectory.Count -eq 0) { throw 'Supply at least one run directory.' }
    $reportFiles = @(Get-ReportFiles -Directories $RunDirectory)
    foreach ($directory in $RunDirectory) {
        if (Test-Path -LiteralPath $directory -PathType Container) {
            $resolved = (Resolve-Path -LiteralPath $directory).Path
            if (-not (Get-ChildItem -LiteralPath $resolved -File -Filter 'o08-replay-proof*.json' | Select-Object -First 1)) {
                throw "No o08-replay-proof*.json report was found at the top level of run directory '$resolved'. Use -EvidenceRoot to search nested run directories."
            }
        }
    }
}

if ($reportFiles.Count -eq 0) {
    throw "No o08-replay-proof*.json reports were found under the supplied location(s)."
}

$rows = [System.Collections.Generic.List[object]]::new()
$runSummaryEntries = [System.Collections.Generic.List[object]]::new()
$runSummaryKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$parsedReportCount = 0
foreach ($reportFile in $reportFiles) {
    try {
        $report = Get-Content -LiteralPath $reportFile.FullName -Raw | ConvertFrom-Json
    } catch {
        Write-Warning "Skipping unreadable replay report '$($reportFile.FullName)': $($_.Exception.Message)"
        continue
    }
    $parsedReportCount++

    $manifestResult = Get-ManifestForReport -ReportDirectory $reportFile.DirectoryName
    $manifest = $manifestResult.Data
    $runId = Get-FirstObjectValue -InputObject $manifest -Names @('run', 'runId', 'id')
    if ($null -eq $runId) { $runId = Get-FirstObjectValue -InputObject $report -Names @('run', 'runId', 'id') }
    if ($null -eq $runId) { $runId = $reportFile.Directory.Name }

    $mode = Get-FirstObjectValue -InputObject $manifest -Names @('mode')
    if ($null -eq $mode) { $mode = Get-FirstObjectValue -InputObject $report -Names @('mode') }
    $timings = Get-ObjectValue -InputObject $manifest -Name 'timingsMs'
    $manifestFrames = Get-ObjectValue -InputObject $manifest -Name 'sourceFrames'
    $samples = Get-ObjectValue -InputObject $report -Name 'samples'
    $observations800 = Get-ObjectValue -InputObject (Get-ObjectValue -InputObject $report -Name 'fullAnalysis800') -Name 'observations'
    $observations1000 = Get-ObjectValue -InputObject (Get-ObjectValue -InputObject $report -Name 'fullAnalysis1000') -Name 'observations'

    $frameCount = 0
    if ($null -ne $samples) { $frameCount = [Math]::Max($frameCount, $samples.Count) }
    if ($null -ne $observations800) { $frameCount = [Math]::Max($frameCount, $observations800.Count) }
    if ($null -ne $observations1000) { $frameCount = [Math]::Max($frameCount, $observations1000.Count) }
    if ($null -ne $manifestFrames) { $frameCount = [Math]::Max($frameCount, $manifestFrames.Count) }

    if ($RunSummaryPath) {
        # A manifest represents one run even if more than one report was saved
        # beside it. Keep its timing values in the separate summary only once.
        $runSummaryKey = if ($manifestResult.Path) { $manifestResult.Path } else { $reportFile.DirectoryName }
        if ($runSummaryKeys.Add([string]$runSummaryKey)) {
            $runTimingData = Get-RunTimingData -Manifest $manifest
            $runSummaryEntries.Add([pscustomobject]@{
                RunId = $runId
                FrameCount = $frameCount
                SourceConfigurationMatch = Get-ObjectValue -InputObject $report -Name 'configurationMatchesPrimaryWidth'
                Phases = $runTimingData.Phases
                TotalMs = $runTimingData.TotalMs
            })
        }
    }

    $rowIndexes = if ($frameCount -gt 0) { 0..($frameCount - 1) } else { @(-1) }
    foreach ($index in $rowIndexes) {
        $sample = if ($index -ge 0) { Get-ArrayItem -Collection $samples -Index $index } else { $null }
        $manifestFrame = if ($index -ge 0) { Get-ArrayItem -Collection $manifestFrames -Index $index } else { $null }
        $analysis800 = if ($index -ge 0) { Get-AnalysisForWidth -Sample $sample -Report $report -Index $index -Width 800 } else { $null }
        $analysis1000 = if ($index -ge 0) { Get-AnalysisForWidth -Sample $sample -Report $report -Index $index -Width 1000 } else { $null }

        $frameId = Get-FirstObjectValue -InputObject $sample -Names @('frameId', 'frameID', 'id')
        if ($null -eq $frameId) { $frameId = Get-FirstObjectValue -InputObject $manifestFrame -Names @('frameId', 'frameID', 'id') }
        $sourceTimeUs = Get-FirstObjectValue -InputObject $sample -Names @('sourceTimeUs', 'sourcePtsUs', 'timestampUs', 'sourceTimestampUs')
        $capturedAt = Get-FirstObjectValue -InputObject $sample -Names @('capturedAt', 'capturedUtc', 'timestampUtc', 'timestamp')

        $rows.Add([pscustomobject][ordered]@{
            RowKind = if ($index -ge 0) { 'Frame' } else { 'RunSummary' }
            RunId = $runId
            RunDirectory = $reportFile.DirectoryName
            Purpose = Get-ObjectValue -InputObject $manifest -Name 'purpose'
            Mode = $mode
            CreatedAt = Format-TimestampValue (Get-FirstObjectValue -InputObject $manifest -Names @('createdAt', 'startedAt', 'timestamp'))
            ReportPath = $reportFile.FullName
            ManifestPath = $manifestResult.Path
            ApkSha256 = Get-ObjectValue -InputObject $manifest -Name 'apkSha256'
            SourceFrameCount = if ($null -ne $manifestFrames) { $manifestFrames.Count } else { $null }
            ManifestTotalMs = Get-ObjectValue -InputObject $timings -Name 'total'
            ManifestInstrumentationMs = Get-ObjectValue -InputObject $timings -Name 'instrumentation'
            ReportCompleted = Get-ObjectValue -InputObject $report -Name 'completed'
            ReportError = Get-ObjectValue -InputObject $report -Name 'error'
            ConfigurationComparison = Get-ObjectValue -InputObject $report -Name 'configurationComparison'
            ConfigurationMatchesPrimaryWidth = Get-ObjectValue -InputObject $report -Name 'configurationMatchesPrimaryWidth'
            ConfigurationSettingsMatchPrimaryWidth = Get-ObjectValue -InputObject $report -Name 'configurationSettingsMatchPrimaryWidth'
            ConfigurationAlgorithmChangedSinceCapture = Get-ObjectValue -InputObject $report -Name 'configurationAlgorithmChangedSinceCapture'
            Scope = Get-ObjectValue -InputObject $report -Name 'scope'
            FrameId = $frameId
            SourceTimeUs = $sourceTimeUs
            CapturedAt = Format-TimestampValue $capturedAt
            PixelSha256 = Get-FirstObjectValue -InputObject $sample -Names @('pixelSha256', 'sha256')
            DetectionRequested = Get-ObjectValue -InputObject $sample -Name 'detectionRequested'
            FrameConfigurationMatchesPrimary = Get-ObjectValue -InputObject $sample -Name 'configurationMatchesPrimary'
            DetectedCount800 = Get-DetectedCount -Analysis $analysis800
            CueCount800 = Get-CountValue -Analysis $analysis800 -Kind 'cue'
            RedCount800 = Get-CountValue -Analysis $analysis800 -Kind 'red'
            YellowCount800 = Get-CountValue -Analysis $analysis800 -Kind 'yellow'
            BlackCount800 = Get-CountValue -Analysis $analysis800 -Kind 'black'
            Uncertain800 = Get-FirstObjectValue -InputObject $analysis800 -Names @('uncertain', 'isUncertain')
            LayoutUncertain800 = Get-FirstObjectValue -InputObject $analysis800 -Names @('layoutUncertain', 'layoutIsUncertain')
            Obstructed800 = Get-FirstObjectValue -InputObject $analysis800 -Names @('obstructed', 'isObstructed')
            WallMs800 = if ($null -ne $sample) { Get-FrameWallMs -Sample $sample -Width 800 } else { $null }
            DetectedCount1000 = Get-DetectedCount -Analysis $analysis1000
            CueCount1000 = Get-CountValue -Analysis $analysis1000 -Kind 'cue'
            RedCount1000 = Get-CountValue -Analysis $analysis1000 -Kind 'red'
            YellowCount1000 = Get-CountValue -Analysis $analysis1000 -Kind 'yellow'
            BlackCount1000 = Get-CountValue -Analysis $analysis1000 -Kind 'black'
            Uncertain1000 = Get-FirstObjectValue -InputObject $analysis1000 -Names @('uncertain', 'isUncertain')
            LayoutUncertain1000 = Get-FirstObjectValue -InputObject $analysis1000 -Names @('layoutUncertain', 'layoutIsUncertain')
            Obstructed1000 = Get-FirstObjectValue -InputObject $analysis1000 -Names @('obstructed', 'isObstructed')
            WallMs1000 = if ($null -ne $sample) { Get-FrameWallMs -Sample $sample -Width 1000 } else { $null }
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

if ($RunSummaryPath) {
    $phaseNames = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $runSummaryEntries) {
        foreach ($phaseName in $entry.Phases.Keys) {
            if (-not $phaseNames.Contains([string]$phaseName)) { $phaseNames.Add([string]$phaseName) }
        }
    }

    $runSummaryRows = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $runSummaryEntries) {
        $summaryRow = [ordered]@{
            RunId = $entry.RunId
            FrameCount = $entry.FrameCount
            SourceConfigurationMatch = $entry.SourceConfigurationMatch
        }
        foreach ($phaseName in $phaseNames) {
            $phaseColumn = "Phase_${phaseName}_Ms"
            $summaryRow[$phaseColumn] = if ($entry.Phases.Contains($phaseName)) { $entry.Phases[$phaseName] } else { $null }
        }
        $summaryRow['TotalMs'] = $entry.TotalMs
        $runSummaryRows.Add([pscustomobject]$summaryRow)
    }

    if ($Format -eq 'Json') {
        $runSummaryContent = ConvertTo-Json -InputObject @($runSummaryRows.ToArray()) -Depth 8
    } else {
        $runSummaryContent = (@($runSummaryRows.ToArray()) | ConvertTo-Csv -NoTypeInformation) -join [Environment]::NewLine
    }

    $fullRunSummaryPath = [System.IO.Path]::GetFullPath($RunSummaryPath)
    $runSummaryDirectory = [System.IO.Path]::GetDirectoryName($fullRunSummaryPath)
    if (-not (Test-Path -LiteralPath $runSummaryDirectory -PathType Container)) {
        throw "Run summary output directory does not exist: '$runSummaryDirectory'."
    }
    [System.IO.File]::WriteAllText($fullRunSummaryPath, $runSummaryContent, [System.Text.UTF8Encoding]::new($false))
}
