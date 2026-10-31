param(
    [string]$SourceRelativePath = 'work/production/evidence/live-detect-pocket-lip-20261030/o08-capture',
    [int[]]$Frames = @(399, 433),
    [switch]$ScreenOnly,
    [switch]$ProbeOnly,
    [string]$RunPrefix = 'pocketlip30',
    [string]$Purpose = 'Current-code exact-frame replay for the selected saved table frames.',
    [string]$EvidenceRelativePath = 'work/production/evidence/live-detect-pocket-lip-20261030/device-replays',
    [int]$EdgeMargin = 23,
    [double]$PocketRadius = 48,
    [double]$BoundarySupport = 0.75,
    [double]$SparseColourSupport = 0.625,
    [double]$Production800BoundarySearchExtent = 0.65,
    [double]$PackedColourBoundarySearchExtent = 0.65,
    [double]$SparseBlackSearchExtent = 1.3,
    [double]$SparseBlackSupport = 0.65625,
    [int]$SparseBlackCandidateLimit = 12,
    [bool]$SparseBlackRankByRadius = $true,
    [double]$SparseBlackMinimumPeakRadiusFactor = 8,
    [switch]$EnableIlluminationNormalization,
    [switch]$TracePackedColourFits,
    [switch]$ObstructionMaskTrace,
    [switch]$TraceBlackMask,
    [int]$BlackLabelX = 1282,
    [int]$BlackLabelY = 536,
    [double]$NeutralCueProbeX800 = -1,
    [double]$NeutralCueProbeY800 = -1,
    [int]$ObstructionClothHue = -1,
    [int]$ObstructionClothSaturationFloor = -1,
    [int]$ObstructionClothValueFloor = -1,
    [int]$ObstructionClothMedianValue = -1,
    [int]$ObstructionNeutralMinimum = -1,
    [int]$ObstructionNeutralMaximum = -1
)

$ErrorActionPreference = 'Stop'
$totalTimer = [Diagnostics.Stopwatch]::StartNew()
$stageTimings = [ordered]@{}
$maximumFrames = if ($ScreenOnly) { 24 } elseif ($ProbeOnly) { 12 } else { 6 }
if ($ScreenOnly -and $ProbeOnly) { throw 'Choose either -ScreenOnly or -ProbeOnly.' }
if ($Frames.Count -lt 1 -or $Frames.Count -gt $maximumFrames) {
    throw "This run mode accepts 1 to $maximumFrames frames."
}
if ($ScreenOnly -and ($EnableIlluminationNormalization -or $TracePackedColourFits -or $ObstructionMaskTrace -or $TraceBlackMask -or
    $NeutralCueProbeX800 -ne -1 -or $NeutralCueProbeY800 -ne -1 -or
    $ObstructionClothHue -ge 0 -or $ObstructionClothSaturationFloor -ge 0 -or $ObstructionClothValueFloor -ge 0 -or $ObstructionClothMedianValue -ge 0 -or
    $ObstructionNeutralMinimum -ge 0 -or $ObstructionNeutralMaximum -ge 0)) {
    throw 'Screen-only mode cannot be combined with ablations or trace artifacts; use the full audit mode for those.'
}
if ($ProbeOnly -and ($EnableIlluminationNormalization -or $TracePackedColourFits -or $ObstructionMaskTrace -or $TraceBlackMask -or
    $ObstructionClothHue -ge 0 -or $ObstructionClothSaturationFloor -ge 0 -or $ObstructionClothValueFloor -ge 0 -or $ObstructionClothMedianValue -ge 0 -or
    $ObstructionNeutralMinimum -ge 0 -or $ObstructionNeutralMaximum -ge 0)) {
    throw 'Probe-only mode supports candidate and neutral-cue telemetry; use full audit mode for other ablations or image traces.'
}
if ($EdgeMargin -lt 0 -or $EdgeMargin -gt 100) { throw 'EdgeMargin must be between 0 and 100 pixels at width 1000.' }
if ($PocketRadius -le 0 -or $PocketRadius -gt 80) { throw 'PocketRadius must be greater than 0 and at most 80 pixels at width 1000.' }
if ($BoundarySupport -lt 0.5625 -or $BoundarySupport -gt 1) { throw 'BoundarySupport must be between 0.5625 and 1.' }
if ($SparseColourSupport -lt 0.5625 -or $SparseColourSupport -gt 1) { throw 'SparseColourSupport must be between 0.5625 and 1.' }
if ($Production800BoundarySearchExtent -lt 0.2 -or $Production800BoundarySearchExtent -gt 0.8) { throw 'Production800BoundarySearchExtent must be between 0.2 and 0.8 ball radii.' }
if ($PackedColourBoundarySearchExtent -lt 0.2 -or $PackedColourBoundarySearchExtent -gt 0.8) { throw 'PackedColourBoundarySearchExtent must be between 0.2 and 0.8 ball radii.' }
if ($SparseBlackSearchExtent -lt 0.2 -or $SparseBlackSearchExtent -gt 1.5) { throw 'SparseBlackSearchExtent must be between 0.2 and 1.5 ball radii.' }
if ($SparseBlackSupport -lt 0.5625 -or $SparseBlackSupport -gt 1) { throw 'SparseBlackSupport must be between 0.5625 and 1.' }
if ($SparseBlackCandidateLimit -lt 1 -or $SparseBlackCandidateLimit -gt 64) { throw 'SparseBlackCandidateLimit must be between 1 and 64.' }
if (-not [double]::IsFinite($SparseBlackMinimumPeakRadiusFactor) -or $SparseBlackMinimumPeakRadiusFactor -lt 1 -or $SparseBlackMinimumPeakRadiusFactor -gt 12) { throw 'SparseBlackMinimumPeakRadiusFactor must be between 1 and 12.' }
if ($BlackLabelX -lt 0 -or $BlackLabelY -lt 0) { throw 'Black label coordinates must be non-negative source-image pixels.' }
$specifiedNeutralCueProbeCoordinates = 0
if ($NeutralCueProbeX800 -ne -1 -or $NeutralCueProbeY800 -ne -1) {
    if (![double]::IsFinite($NeutralCueProbeX800) -or ![double]::IsFinite($NeutralCueProbeY800) -or
        $NeutralCueProbeX800 -lt 0 -or $NeutralCueProbeY800 -lt 0 -or
        $NeutralCueProbeX800 -gt 800 -or $NeutralCueProbeY800 -gt 1600) {
        throw 'Neutral cue probe requires both finite width-800 coordinates inside the 800 by 1600 analysis frame.'
    }
    $specifiedNeutralCueProbeCoordinates = 2
}
$obstructionClothRange = @($ObstructionClothHue, $ObstructionClothSaturationFloor, $ObstructionClothValueFloor, $ObstructionClothMedianValue)
$specifiedObstructionClothRange = @($obstructionClothRange | Where-Object { $_ -ge 0 }).Count
if ($specifiedObstructionClothRange -notin @(0, 4)) { throw 'Obstruction cloth range override requires all four HSV values.' }
if ($specifiedObstructionClothRange -eq 4) {
    if (-not $ObstructionMaskTrace) { throw 'Obstruction cloth range override requires -ObstructionMaskTrace.' }
    if (($ObstructionClothHue -gt 179) -or ($ObstructionClothSaturationFloor -gt 255) -or
        ($ObstructionClothValueFloor -gt 255) -or ($ObstructionClothMedianValue -lt 1) -or ($ObstructionClothMedianValue -gt 255)) {
        throw 'Obstruction cloth hue must be 0–179; saturation/value floors 0–255; median value 1–255.'
    }
}
$obstructionNeutralRange = @($ObstructionNeutralMinimum, $ObstructionNeutralMaximum)
$specifiedObstructionNeutralRange = @($obstructionNeutralRange | Where-Object { $_ -ge 0 }).Count
if ($specifiedObstructionNeutralRange -notin @(0, 2)) { throw 'Obstruction neutral range override requires both values.' }
if ($specifiedObstructionNeutralRange -eq 2) {
    if (-not $ObstructionMaskTrace) { throw 'Obstruction neutral range override requires -ObstructionMaskTrace.' }
    if (($ObstructionNeutralMinimum -gt 255) -or ($ObstructionNeutralMaximum -gt 255) -or
        ($ObstructionNeutralMaximum -lt $ObstructionNeutralMinimum)) {
        throw 'Obstruction neutral range must satisfy 0 <= minimum <= maximum <= 255.'
    }
}

$adb = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
function Invoke-Adb([string[]]$Arguments) {
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $adb @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    [pscustomobject]@{ ExitCode = $exitCode; Output = $output }
}

$serial = '66181FDDV001HS'
$package = 'uk.co.skyeye.visionchecks'
$instrumentation = "$package/uk.co.skyeye.visionchecks.VerificationReplayInstrumentation"
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = if ([IO.Path]::IsPathRooted($SourceRelativePath)) {
    $SourceRelativePath
} else {
    Join-Path $repoRoot $SourceRelativePath
}
$apk = Join-Path $repoRoot 'tests/SkyEye.Vision.Device/bin/Release/net10.0-android/android-arm64/uk.co.skyeye.visionchecks-Signed.apk'
$cameraStatusUri = 'http://192.168.1.124:8765/status'
$run = "$RunPrefix-$(Get-Date -Format 'HHmmss')"
if ($run.Length -gt 32) { throw 'RunPrefix is too long; the timestamped replay name must be at most 32 characters.' }
$remoteFilesRoot = "/sdcard/Android/data/$package/files"
$remoteRunDirectory = "$remoteFilesRoot/o08-replay-$run"
$evidenceRoot = if ([IO.Path]::IsPathRooted($EvidenceRelativePath)) { $EvidenceRelativePath } else { Join-Path $repoRoot $EvidenceRelativePath }
$runDirectory = Join-Path $evidenceRoot $run

if (-not (Test-Path -LiteralPath $adb)) { throw "ADB was not found at $adb." }
if (-not (Test-Path -LiteralPath $apk)) { throw "Replay APK was not found at $apk." }
$cameraBefore = Invoke-RestMethod -Uri $cameraStatusUri -TimeoutSec 5
if ($cameraBefore.state -ne 'Stopped') { throw "Mounted camera must be stopped; state is $($cameraBefore.state)." }

$deviceResult = Invoke-Adb @('devices', '-l')
$devices = @($deviceResult.Output | Where-Object { $_ -match '^\S+\s+device\s+.*model:Pixel_11_Pro_XL(?:\s|$)' })
if ($deviceResult.ExitCode -ne 0 -or $devices.Count -ne 1 -or ($devices[0] -split '\s+')[0] -ne $serial) {
    throw 'Expected the already-connected Pixel 11 Pro XL with serial 66181FDDV001HS.'
}
$packageResult = Invoke-Adb @('-s', $serial, 'shell', 'pm', 'path', $package)
if ($packageResult.ExitCode -ne 0 -or ($packageResult.Output -join "`n") -notmatch 'package:') {
    throw 'The isolated Vision Device package is not installed; Production and camera packages were not targeted.'
}

$stageTimer = [Diagnostics.Stopwatch]::StartNew()
$inputs = foreach ($frame in $frames) {
    $jsonPath = Join-Path $sourceRoot "$frame.json"
    if (-not (Test-Path -LiteralPath $jsonPath)) {
        throw "Missing captured source frame $frame."
    }
    $receipt = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    $pixelFile = [string]$receipt.indexedFrame.pixelFile
    if ([string]::IsNullOrWhiteSpace($pixelFile) -or $pixelFile -ne [IO.Path]::GetFileName($pixelFile)) {
        throw "Frame $frame receipt has an invalid pixelFile name."
    }
    $pixelPath = Join-Path $sourceRoot $pixelFile
    if (-not (Test-Path -LiteralPath $pixelPath)) { throw "Missing source pixel asset $pixelFile for frame $frame." }
    $actualHash = (Get-FileHash -LiteralPath $pixelPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne [string]$receipt.indexedFrame.pixelSha256) {
        throw "Source frame $frame does not match its receipt hash."
    }
    [pscustomobject]@{ Frame = $frame; JsonPath = $jsonPath; PixelPath = $pixelPath; Hash = $actualHash; Receipt = $receipt }
}
for ($i = 1; $i -lt $inputs.Count; $i++) {
    $previous = $inputs[$i - 1].Receipt.indexedFrame
    $current = $inputs[$i].Receipt.indexedFrame
    if ([long]$current.frameId -le [long]$previous.frameId -or [long]$current.sourceTimeUs -le [long]$previous.sourceTimeUs) {
        throw 'Replay inputs must have strictly increasing frame IDs and source timestamps.'
    }
}
$stageTimer.Stop()
$stageTimings.inputHashAndOrderValidationMs = [Math]::Round($stageTimer.Elapsed.TotalMilliseconds, 1)

New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$stageTimer = [Diagnostics.Stopwatch]::StartNew()
$installResult = Invoke-Adb @('-s', $serial, 'install', '-r', $apk)
if ($installResult.ExitCode -ne 0 -or ($installResult.Output -join "`n") -notmatch 'Success') {
    throw "Could not update the isolated Vision Device package in place: $($installResult.Output -join ' ')"
}
$stageTimer.Stop()
$stageTimings.apkInstallMs = [Math]::Round($stageTimer.Elapsed.TotalMilliseconds, 1)
$stageTimer = [Diagnostics.Stopwatch]::StartNew()
$mkdirResult = Invoke-Adb @('-s', $serial, 'shell', 'mkdir', '-p', $remoteRunDirectory)
if ($mkdirResult.ExitCode -ne 0) { throw "Could not create isolated replay folder: $($mkdirResult.Output -join ' ')" }
$pushFiles = @()
foreach ($input in $inputs) {
    $pushFiles += $input.JsonPath
    $pushFiles += $input.PixelPath
}
$pushResult = Invoke-Adb (@('-s', $serial, 'push') + $pushFiles + @("$remoteRunDirectory/"))
if ($pushResult.ExitCode -ne 0) { throw "Input transfer failed: $($pushResult.Output -join ' ')" }
$stageTimer.Stop()
$stageTimings.inputTransferMs = [Math]::Round($stageTimer.Elapsed.TotalMilliseconds, 1)

$screenOnlyArgument = if ($ScreenOnly) { '1' } else { '0' }
$probeOnlyArgument = if ($ProbeOnly) { '1' } else { '0' }
$auditArgument = if ($ScreenOnly) { '0' } else { '1' }
$instrumentArguments = @('shell', 'am', 'instrument', '-w', '-e', 'run', $run,
    '-e', 'screenOnly', $screenOnlyArgument, '-e', 'probeOnly', $probeOnlyArgument, '-e', 'audit', $auditArgument, '-e', 'edgeMargin', "$EdgeMargin",
    '-e', 'pocketRadius', "$PocketRadius", '-e', 'boundarySupport', "$BoundarySupport",
    '-e', 'sparseColourSupport', "$SparseColourSupport",
    '-e', 'production800BoundaryExtent', "$Production800BoundarySearchExtent",
    '-e', 'boundaryExtent', "$PackedColourBoundarySearchExtent",
    '-e', 'sparseBlackExtent', "$SparseBlackSearchExtent", '-e', 'sparseBlackSupport', "$SparseBlackSupport",
    '-e', 'sparseBlackMinimumPeakRadius', "$SparseBlackMinimumPeakRadiusFactor",
    '-e', 'sparseBlackLimit', "$SparseBlackCandidateLimit")
$sparseBlackRankArgument = if ($SparseBlackRankByRadius) { '1' } else { '0' }
$instrumentArguments += @('-e', 'sparseBlackRadiusRank', $sparseBlackRankArgument)
if ($EnableIlluminationNormalization) { $instrumentArguments += @('-e', 'illuminationNormalization', '1') }
if ($TracePackedColourFits) { $instrumentArguments += @('-e', 'tracePackedColourFits', '1') }
if ($ObstructionMaskTrace) { $instrumentArguments += @('-e', 'obstructionMaskTrace', '1') }
if ($specifiedObstructionClothRange -eq 4) {
    $instrumentArguments += @('-e', 'obstructionClothHue', "$ObstructionClothHue",
        '-e', 'obstructionClothSaturationFloor', "$ObstructionClothSaturationFloor",
        '-e', 'obstructionClothValueFloor', "$ObstructionClothValueFloor",
        '-e', 'obstructionClothMedianValue', "$ObstructionClothMedianValue")
}
if ($specifiedObstructionNeutralRange -eq 2) {
    $instrumentArguments += @('-e', 'obstructionNeutralMinimum', "$ObstructionNeutralMinimum",
        '-e', 'obstructionNeutralMaximum', "$ObstructionNeutralMaximum")
}
if ($TraceBlackMask) { $instrumentArguments += @('-e', 'trace', '1', '-e', 'blackLabelX', "$BlackLabelX", '-e', 'blackLabelY', "$BlackLabelY") }
if ($specifiedNeutralCueProbeCoordinates -eq 2) {
    $probeXArgument = $NeutralCueProbeX800.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    $probeYArgument = $NeutralCueProbeY800.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    $instrumentArguments += @('-e', 'neutralCueProbeX800', $probeXArgument,
        '-e', 'neutralCueProbeY800', $probeYArgument)
}
$instrumentArguments += $instrumentation
$stageTimer = [Diagnostics.Stopwatch]::StartNew()
$instrumentResult = Invoke-Adb (@('-s', $serial) + $instrumentArguments)
$stageTimer.Stop()
$stageTimings.instrumentationMs = [Math]::Round($stageTimer.Elapsed.TotalMilliseconds, 1)
$instrumentResult.Output | Set-Content -LiteralPath (Join-Path $runDirectory 'instrumentation.log')
if ($instrumentResult.ExitCode -ne 0) { throw "Boundary audit instrumentation failed: $($instrumentResult.Output -join ' ')" }
if (($instrumentResult.Output -join "`n") -match 'shortMsg=Process crashed') {
    throw "Boundary audit instrumentation process crashed: $($instrumentResult.Output -join ' ')"
}

$reportSuffix = if ($TraceBlackMask) { '-blacktrace' } else { '' }
$reportName = "o08-replay-proof-$run$reportSuffix.json"
$localReport = Join-Path $runDirectory $reportName
$stageTimer = [Diagnostics.Stopwatch]::StartNew()
$pullResult = Invoke-Adb @('-s', $serial, 'pull', "$remoteFilesRoot/$reportName", $localReport)
if ($pullResult.ExitCode -ne 0) { throw "Could not retrieve the replay report: $($pullResult.Output -join ' ')" }
$report = Get-Content -LiteralPath $localReport -Raw | ConvertFrom-Json
if (-not $report.completed -or @($report.samples).Count -ne $frames.Count) {
    $reason = if ([string]::IsNullOrWhiteSpace([string]$report.error)) { 'sample count or completion flag mismatch' } else { [string]$report.error }
    throw "Replay report is incomplete ($reason): $localReport"
}
$maskArtifacts = @()
$blackMaskArtifacts = @()
if ($ObstructionMaskTrace) {
    foreach ($frame in $frames) {
        foreach ($width in @(800, 1000)) {
            $name = "$frame-config$width-obstruction-mask.png"
            $remoteMask = "$remoteRunDirectory/$name"
            $localMask = Join-Path $runDirectory $name
            $pullMaskResult = Invoke-Adb @('-s', $serial, 'pull', $remoteMask, $localMask)
            if ($pullMaskResult.ExitCode -ne 0) { throw "Could not retrieve obstruction mask $name`: $($pullMaskResult.Output -join ' ')" }
            $maskArtifacts += [pscustomobject]@{
                frameId = $frame; configuredWidth = $width; file = $name
                sha256 = (Get-FileHash -LiteralPath $localMask -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
    }
}
if ($TraceBlackMask) {
    $traceRoot = "$remoteRunDirectory/o08-black-trace-$run"
    foreach ($sample in $report.samples) {
        foreach ($widthName in @('width800', 'width1000')) {
            $trace = $sample.$widthName.darkMaskTrace
            foreach ($name in $trace.overlays) {
                $localTrace = Join-Path $runDirectory $name
                $pullTraceResult = Invoke-Adb @('-s', $serial, 'pull', "$traceRoot/$name", $localTrace)
                if ($pullTraceResult.ExitCode -ne 0) { throw "Could not retrieve black mask trace $name`: $($pullTraceResult.Output -join ' ')" }
                $blackMaskArtifacts += [pscustomobject]@{
                    frameId = $sample.frameId; configuredWidth = $trace.analysisWidth; file = $name
                    sha256 = (Get-FileHash -LiteralPath $localTrace -Algorithm SHA256).Hash.ToLowerInvariant()
                }
            }
        }
    }
}
$stageTimer.Stop()
$stageTimings.reportAndArtifactPullMs = [Math]::Round($stageTimer.Elapsed.TotalMilliseconds, 1)
$cameraAfter = Invoke-RestMethod -Uri $cameraStatusUri -TimeoutSec 5
if ($cameraAfter.state -ne 'Stopped') { throw "Mounted camera changed state during offline replay: $($cameraAfter.state)." }
$totalTimer.Stop()

$manifest = [pscustomobject]@{
    createdAt = [DateTimeOffset]::UtcNow
    purpose = $Purpose
    deviceSerial = $serial
    package = $package
    apkSha256 = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
    run = $run
    mode = if ($ScreenOnly) { 'screen-only' } elseif ($ProbeOnly) { 'probe-only' } else { 'full-audit' }
    timingsMs = [pscustomobject]@{
        inputHashAndOrderValidation = $stageTimings.inputHashAndOrderValidationMs
        apkInstall = $stageTimings.apkInstallMs
        inputTransfer = $stageTimings.inputTransferMs
        instrumentation = $stageTimings.instrumentationMs
        reportAndArtifactPull = $stageTimings.reportAndArtifactPullMs
        total = [Math]::Round($totalTimer.Elapsed.TotalMilliseconds, 1)
    }
    phaseTimings = [pscustomobject]@{
        schemaVersion = 1
        unit = 'milliseconds'
        phases = @(
            [pscustomobject]@{
                name = 'localInputHashAndOrderPreparation'
                elapsedMs = $stageTimings.inputHashAndOrderValidationMs
                scope = 'Receipt parsing, source pixel SHA-256 verification, and frame ordering validation.'
            }
            [pscustomobject]@{
                name = 'apkInstall'
                elapsedMs = $stageTimings.apkInstallMs
                scope = 'In-place install of the isolated Vision Device APK.'
            }
            [pscustomobject]@{
                name = 'inputDirectoryAndPush'
                elapsedMs = $stageTimings.inputTransferMs
                scope = 'Remote replay directory creation and source receipt/pixel pushes.'
            }
            [pscustomobject]@{
                name = 'instrumentationWaitAndRun'
                elapsedMs = $stageTimings.instrumentationMs
                scope = 'ADB instrumentation invocation through completion of the -w wait.'
            }
            [pscustomobject]@{
                name = 'reportValidationAndArtifactPulls'
                elapsedMs = $stageTimings.reportAndArtifactPullMs
                scope = 'Report retrieval and validation, optional trace artifact pulls, and pulled artifact hashing.'
            }
        )
    }
    localIlluminationNormalization = [bool]$EnableIlluminationNormalization
    tracePackedColourFits = [bool]$TracePackedColourFits
    auditEnabled = -not $ScreenOnly
    obstructionMaskTrace = [bool]$ObstructionMaskTrace
    diagnosticObstructionClothRangeOverride = if ($specifiedObstructionClothRange -eq 4) {
        [pscustomobject]@{ hue = $ObstructionClothHue; saturationFloor = $ObstructionClothSaturationFloor; valueFloor = $ObstructionClothValueFloor; medianValue = $ObstructionClothMedianValue }
    } else { $null }
    diagnosticObstructionNeutralValueRangeOverride = if ($specifiedObstructionNeutralRange -eq 2) {
        [pscustomobject]@{ minimum = $ObstructionNeutralMinimum; maximum = $ObstructionNeutralMaximum }
    } else { $null }
    obstructionMasks = $maskArtifacts
    traceBlackMask = [bool]$TraceBlackMask
    manualBlackLabelSourcePixel = if ($TraceBlackMask) { [pscustomobject]@{ x = $BlackLabelX; y = $BlackLabelY } } else { $null }
    neutralCueProbeAtWidth800 = if ($specifiedNeutralCueProbeCoordinates -eq 2) { [pscustomobject]@{ x = $NeutralCueProbeX800; y = $NeutralCueProbeY800 } } else { $null }
    blackMaskTraces = $blackMaskArtifacts
    packedClusterImageEdgeMargin = $EdgeMargin
    pocketExclusionRadius = $PocketRadius
    minimumHoughBoundarySupport = $BoundarySupport
    minimumSparseColourBoundarySupport = $SparseColourSupport
    packedColourBoundarySearchExtentFraction = $PackedColourBoundarySearchExtent
    production800BoundarySearchExtentFraction = $Production800BoundarySearchExtent
    sparseBlackBoundarySearchExtentFraction = $SparseBlackSearchExtent
    minimumSparseBlackBoundarySupport = $SparseBlackSupport
    sparseBlackCandidateLimit = $SparseBlackCandidateLimit
    sparseBlackMinimumPeakRadiusFactor = $SparseBlackMinimumPeakRadiusFactor
    sparseBlackRankByRadius = [bool]$SparseBlackRankByRadius
    sourceFrames = $inputs | ForEach-Object {
        [pscustomobject]@{ frameId = $_.Receipt.indexedFrame.frameId; pixelFile = [IO.Path]::GetFileName($_.PixelPath); receipt = [IO.Path]::GetFileName($_.JsonPath); sha256 = $_.Hash }
    }
    report = $reportName
    reportSha256 = (Get-FileHash -LiteralPath $localReport -Algorithm SHA256).Hash.ToLowerInvariant()
    cameraBefore = $cameraBefore
    cameraAfter = $cameraAfter
}
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $runDirectory 'manifest.json')
$timingSummary = $stageTimings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)ms" }
$runKind = if ($ScreenOnly) { 'Detector screen' } elseif ($ProbeOnly) { 'Candidate probe' } else { 'Boundary audit' }
Write-Output "$runKind saved under $runDirectory (total=$([Math]::Round($totalTimer.Elapsed.TotalMilliseconds, 1))ms; $($timingSummary -join ', '))"
