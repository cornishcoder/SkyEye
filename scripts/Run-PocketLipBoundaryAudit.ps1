<#
.SYNOPSIS
Replays saved frames through the isolated Vision Device package.

.PARAMETER Frames
Ordered source frame IDs. Receipts may be named `<frameId>.json` or discovered
by their indexedFrame.frameId metadata when extracted clip filenames are padded.
The host validates frame identity, timeline, calibration and analyzer identity
before installing the replay package.

.PARAMETER IndependentFrames
Allows equal source timestamps for independent screen-only samples, such as a
same-source synthetic positive/negative pair. Frame IDs must remain ordered;
source timeline, calibration and analyzer identity checks remain enforced.
#>
param(
    [string]$SourceRelativePath = 'work/production/evidence/live-detect-pocket-lip-20261030/o08-capture',
    [int[]]$Frames = @(399, 433),
    [switch]$ScreenOnly,
    [switch]$IndependentFrames,
    [switch]$ProbeOnly,
    [switch]$PocketArcProbeOnly,
    [switch]$PocketArcSweepOnly,
    [string[]]$PocketArcAnchor = @(),
    [string]$PocketArcAnchorManifest,
    [switch]$RawProposalReplay,
    [string]$RawProposalDirectory,
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
    [double]$PocketArcTargetFitAcceptanceRadiusInBallRadii = 0,
    [switch]$EnableIlluminationNormalization,
    [switch]$TracePackedColourFits,
    [switch]$ObstructionMaskTrace,
    [switch]$TraceBlackMask,
    [int]$BlackLabelX = 1282,
    [int]$BlackLabelY = 536,
    [ValidateSet('reviewed-label', 'unreviewed-model-proposal', 'unreviewed-negative-control')]
    [string]$BlackPointSource = 'reviewed-label',
    [double]$NeutralCueProbeX800 = -1,
    [double]$NeutralCueProbeY800 = -1,
    [int]$ObstructionClothHue = -1,
    [int]$ObstructionClothSaturationFloor = -1,
    [int]$ObstructionClothValueFloor = -1,
    [int]$ObstructionClothMedianValue = -1,
    [int]$ObstructionNeutralMinimum = -1,
    [int]$ObstructionNeutralMaximum = -1
)

# Labelled-frame example; replace the frame and coordinates for the selected capture:
# ./scripts/Run-PocketLipBoundaryAudit.ps1 -PocketArcProbeOnly -Frames @(399) -BlackLabelX 102 -BlackLabelY 903

$ErrorActionPreference = 'Stop'
$totalTimer = [Diagnostics.Stopwatch]::StartNew()
$stageTimings = [ordered]@{}
$maximumFrames = if ($ScreenOnly) { 24 } elseif ($ProbeOnly -or $PocketArcProbeOnly -or $PocketArcSweepOnly -or $RawProposalReplay) { 12 } else { 6 }
$selectedModeCount = 0
foreach ($selectedMode in @($ScreenOnly, $ProbeOnly, $PocketArcProbeOnly, $PocketArcSweepOnly, $RawProposalReplay)) {
    if ($selectedMode) { $selectedModeCount++ }
}
if ($selectedModeCount -gt 1) {
    throw 'Choose only one replay mode: -ScreenOnly, -ProbeOnly, -PocketArcProbeOnly, -PocketArcSweepOnly, or -RawProposalReplay.'
}
if ($Frames.Count -lt 1 -or $Frames.Count -gt $maximumFrames) {
    throw "This run mode accepts 1 to $maximumFrames frames."
}
if ($IndependentFrames -and -not $ScreenOnly) {
    throw '-IndependentFrames is supported only with -ScreenOnly; use a chronological mode for temporal/stateful replay.'
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
if ($PocketArcProbeOnly -and (-not $PSBoundParameters.ContainsKey('BlackLabelX') -or -not $PSBoundParameters.ContainsKey('BlackLabelY'))) {
    throw 'Pocket-arc probe-only mode requires explicit -BlackLabelX and -BlackLabelY values for the selected labelled frame.'
}
if ($PocketArcSweepOnly -and ($PocketArcAnchor.Count -lt 2 -or $PocketArcAnchor.Count -gt 12)) {
    if ([string]::IsNullOrWhiteSpace($PocketArcAnchorManifest)) {
        throw 'Pocket-arc sweep mode requires 2 to 12 -PocketArcAnchor values for one frame, or -PocketArcAnchorManifest for per-frame anchors.'
    }
}
if (-not $PocketArcSweepOnly -and ($PocketArcAnchor.Count -gt 0 -or -not [string]::IsNullOrWhiteSpace($PocketArcAnchorManifest))) {
    throw '-PocketArcAnchor and -PocketArcAnchorManifest are supported only with -PocketArcSweepOnly.'
}
if ($PocketArcSweepOnly) {
    if (-not [string]::IsNullOrWhiteSpace($PocketArcAnchorManifest) -and $PocketArcAnchor.Count -gt 0) {
        throw 'Use either -PocketArcAnchorManifest or -PocketArcAnchor, not both.'
    }
    if ([string]::IsNullOrWhiteSpace($PocketArcAnchorManifest) -and $Frames.Count -ne 1) {
        throw 'A shared -PocketArcAnchor list is limited to one frame. Use -PocketArcAnchorManifest for a multi-frame sweep.'
    }
    $anchorNames = @()
    foreach ($anchor in $PocketArcAnchor) {
        if ($anchor -notmatch '^(?<name>[A-Za-z0-9_-]+)=(?<x>\d+),(?<y>\d+)$') {
            throw "Invalid pocket-arc anchor '$anchor'; expected name=x,y with non-negative integer pixels."
        }
        $anchorNames += $Matches.name.ToLowerInvariant()
    }
    if (@($anchorNames | Select-Object -Unique).Count -ne $anchorNames.Count) {
        throw 'Pocket-arc anchor names must be unique, case-insensitively.'
    }
}
if ($BlackPointSource -ne 'reviewed-label' -and -not ($PocketArcProbeOnly -or $PocketArcSweepOnly)) {
    throw 'Unreviewed -BlackPointSource values are supported only with a pocket-arc probe or sweep.'
}
if ($RawProposalReplay -and [string]::IsNullOrWhiteSpace($RawProposalDirectory)) {
    throw 'Raw-proposal replay requires -RawProposalDirectory containing one hash-matched local-model JSON file per frame.'
}
if (-not $RawProposalReplay -and -not [string]::IsNullOrWhiteSpace($RawProposalDirectory)) {
    throw '-RawProposalDirectory is supported only with -RawProposalReplay.'
}
if (-not [double]::IsFinite($PocketArcTargetFitAcceptanceRadiusInBallRadii) -or
    $PocketArcTargetFitAcceptanceRadiusInBallRadii -lt 0 -or $PocketArcTargetFitAcceptanceRadiusInBallRadii -gt 5) {
    throw 'PocketArcTargetFitAcceptanceRadiusInBallRadii must be 0 (disabled) or between 0 and 5 ball radii.'
}
if ($PocketArcTargetFitAcceptanceRadiusInBallRadii -gt 0 -and -not ($PocketArcProbeOnly -or $PocketArcSweepOnly)) {
    throw 'Targeted label-reach pruning is evaluation-only and may be used only with a pocket-arc probe or sweep.'
}
if (($PocketArcProbeOnly -or $PocketArcSweepOnly) -and ($EnableIlluminationNormalization -or $TracePackedColourFits -or $ObstructionMaskTrace -or $TraceBlackMask -or
    $NeutralCueProbeX800 -ne -1 -or $NeutralCueProbeY800 -ne -1 -or
    $ObstructionClothHue -ge 0 -or $ObstructionClothSaturationFloor -ge 0 -or $ObstructionClothValueFloor -ge 0 -or $ObstructionClothMedianValue -ge 0 -or
    $ObstructionNeutralMinimum -ge 0 -or $ObstructionNeutralMaximum -ge 0)) {
    throw 'Pocket-arc probe and sweep modes capture only width-1000 packed-black traces; remove unrelated ablations and trace options.'
}
if ($RawProposalReplay -and ($EnableIlluminationNormalization -or $TracePackedColourFits -or $ObstructionMaskTrace -or $TraceBlackMask -or
    $NeutralCueProbeX800 -ne -1 -or $NeutralCueProbeY800 -ne -1 -or
    $ObstructionClothHue -ge 0 -or $ObstructionClothSaturationFloor -ge 0 -or $ObstructionClothValueFloor -ge 0 -or $ObstructionClothMedianValue -ge 0 -or
    $ObstructionNeutralMinimum -ge 0 -or $ObstructionNeutralMaximum -ge 0)) {
    throw 'Raw-proposal replay cannot include these unrelated ablations or trace artifacts.'
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
function Get-CameraStatusForManifest([object]$Status) {
    $safeStatus = [ordered]@{}
    foreach ($property in $Status.PSObject.Properties) {
        if ($property.Name -eq 'readToken') { continue }
        $value = $property.Value
        if ($property.Name -in @('streamPath', 'phoneStreamPath') -and $value -is [string]) {
            $value = ($value -split '\?', 2)[0]
        }
        $safeStatus[$property.Name] = $value
    }
    [pscustomobject]$safeStatus
}

$serial = '66181FDDV001HS'
$package = 'uk.co.skyeye.visionchecks'
$instrumentation = "$package/uk.co.skyeye.visionchecks.VerificationReplayInstrumentation"
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Assert-VisionReplayReceiptBinding.ps1')
$sourceRoot = if ([IO.Path]::IsPathRooted($SourceRelativePath)) {
    $SourceRelativePath
} else {
    Join-Path $repoRoot $SourceRelativePath
}
$rawProposalRoot = if ([string]::IsNullOrWhiteSpace($RawProposalDirectory)) {
    $null
} elseif ([IO.Path]::IsPathRooted($RawProposalDirectory)) {
    $RawProposalDirectory
} else {
    Join-Path $repoRoot $RawProposalDirectory
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
$indexedReceiptPaths = $null
$previousTimeline = $null
$previousCalibrationSignature = $null
$previousConfigurationIdentity = $null
$hasPreviousConfigurationIdentity = $false
$inputs = foreach ($frame in $frames) {
    $jsonPath = Join-Path $sourceRoot "$frame.json"
    if (-not (Test-Path -LiteralPath $jsonPath)) {
        if ($null -eq $indexedReceiptPaths) {
            $indexedReceiptPaths = @{}
            foreach ($candidatePath in Get-ChildItem -LiteralPath $sourceRoot -Filter '*.json' -File) {
                $candidateReceipt = Get-Content -LiteralPath $candidatePath.FullName -Raw | ConvertFrom-Json
                if ($null -eq $candidateReceipt.indexedFrame -or $null -eq $candidateReceipt.indexedFrame.frameId) { continue }
                $candidateFrameId = [long]$candidateReceipt.indexedFrame.frameId
                if ($indexedReceiptPaths.ContainsKey($candidateFrameId)) {
                    throw "Source directory contains multiple receipts for frame ID $candidateFrameId."
                }
                $indexedReceiptPaths[$candidateFrameId] = $candidatePath.FullName
            }
        }
        $requestedFrameId = [long]$frame
        if (-not $indexedReceiptPaths.ContainsKey($requestedFrameId)) {
            throw "Missing captured source frame $frame (no exact filename or indexedFrame.frameId match)."
        }
        $jsonPath = $indexedReceiptPaths[$requestedFrameId]
    }
    # Parse and hash the same bytes. A later archive copy must still match this hash.
    $receiptBytes = [IO.File]::ReadAllBytes($jsonPath)
    $receiptHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($receiptBytes)).ToLowerInvariant()
    $receiptText = [Text.UTF8Encoding]::new($false, $true).GetString($receiptBytes).TrimStart([char]0xFEFF)
    $receipt = $receiptText | ConvertFrom-Json
    if ([long]$receipt.indexedFrame.frameId -ne [long]$frame) {
        throw "Source receipt '$([IO.Path]::GetFileName($jsonPath))' identifies frame $($receipt.indexedFrame.frameId), not requested frame $frame."
    }
    $frameTimeline = [string]$receipt.indexedFrame.scope.timeline
    if ([string]::IsNullOrWhiteSpace($frameTimeline)) {
        throw "Frame $frame has no source timeline identity."
    }
    if ($null -ne $previousTimeline -and $frameTimeline -ne $previousTimeline) {
        throw "Selected frames cross source timelines at frame $frame; split independent timelines into separate replay runs."
    }
    $previousTimeline = $frameTimeline
    $cornerSignature = (@($receipt.calibrationCorners | ForEach-Object {
        ([double]$_.x).ToString('R', [Globalization.CultureInfo]::InvariantCulture) + ',' +
        ([double]$_.y).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    }) -join ';')
    if ([string]::IsNullOrWhiteSpace($cornerSignature)) {
        throw "Frame $frame has no calibration corners."
    }
    if ($null -ne $previousCalibrationSignature -and $cornerSignature -ne $previousCalibrationSignature) {
        throw "Selected frames use different calibration corners at frame $frame; split them into separate replay runs."
    }
    $previousCalibrationSignature = $cornerSignature
    $frameConfigurationIdentity = if ($null -ne $receipt.PSObject.Properties['analyzerConfigurationIdentity']) {
        [string]$receipt.analyzerConfigurationIdentity
    } else { $null }
    if ($hasPreviousConfigurationIdentity -and $frameConfigurationIdentity -ne $previousConfigurationIdentity) {
        throw "Selected frames use different analyzer configurations at frame $frame; split them into separate replay runs."
    }
    $previousConfigurationIdentity = $frameConfigurationIdentity
    $hasPreviousConfigurationIdentity = $true
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
    $rawProposalPath = $null
    if ($RawProposalReplay) {
        $rawProposalPath = Join-Path $rawProposalRoot "$frame.json"
        if (-not (Test-Path -LiteralPath $rawProposalPath)) { throw "Missing local-model proposal file for frame $frame." }
        $proposal = Get-Content -LiteralPath $rawProposalPath -Raw | ConvertFrom-Json
        if ([long]$proposal.frameId -ne [long]$frame -or [string]$proposal.sourceSha256 -ne $actualHash) {
            throw "Local-model proposals for frame $frame do not match the exact replay source pixels."
        }
        if ($proposal.result.success -ne $true -or @($proposal.result.balls).Count -lt 1 -or @($proposal.result.balls).Count -gt 32) {
            throw "Local-model proposal file for frame $frame has no successful bounded ball list."
        }
        if ([int]$proposal.result.image_size.width -lt 1 -or [int]$proposal.result.image_size.height -lt 1) {
            throw "Local-model proposal file for frame $frame has invalid source image dimensions."
        }
    }
    [pscustomobject]@{ Frame = $frame; JsonPath = $jsonPath; PixelPath = $pixelPath; Hash = $actualHash; ReceiptHash = $receiptHash; Receipt = $receipt; RawProposalPath = $rawProposalPath }
}
for ($i = 1; $i -lt $inputs.Count; $i++) {
    $previous = $inputs[$i - 1].Receipt.indexedFrame
    $current = $inputs[$i].Receipt.indexedFrame
    $timestampsOutOfOrder = if ($IndependentFrames) {
        [long]$current.sourceTimeUs -lt [long]$previous.sourceTimeUs
    } else {
        [long]$current.sourceTimeUs -le [long]$previous.sourceTimeUs
    }
    if ([long]$current.frameId -le [long]$previous.frameId -or $timestampsOutOfOrder) {
        $orderingRule = if ($IndependentFrames) { 'strictly increasing frame IDs and nondecreasing source timestamps' } else { 'strictly increasing frame IDs and source timestamps' }
        throw "Replay inputs must have $orderingRule."
    }
}
$stageTimer.Stop()
$stageTimings.inputHashAndOrderValidationMs = [Math]::Round($stageTimer.Elapsed.TotalMilliseconds, 1)

$normalizedPocketArcAnchorManifest = $null
$pocketArcAnchorManifestBase64 = $null
if (-not [string]::IsNullOrWhiteSpace($PocketArcAnchorManifest)) {
    $anchorManifestPath = if ([IO.Path]::IsPathRooted($PocketArcAnchorManifest)) {
        $PocketArcAnchorManifest
    } else {
        Join-Path $repoRoot $PocketArcAnchorManifest
    }
    if (-not (Test-Path -LiteralPath $anchorManifestPath -PathType Leaf)) {
        throw "Pocket-arc anchor manifest was not found: $anchorManifestPath"
    }
    $anchorManifest = Get-Content -LiteralPath $anchorManifestPath -Raw | ConvertFrom-Json
    if ([int]$anchorManifest.version -ne 1 -or @($anchorManifest.frames).Count -ne $inputs.Count) {
        throw 'Pocket-arc anchor manifest must use version 1 and contain exactly one frame entry per selected source frame.'
    }
    $manifestFrames = @{}
    foreach ($entry in $anchorManifest.frames) {
        $frameId = [long]$entry.frameId
        if ($manifestFrames.ContainsKey($frameId)) { throw "Pocket-arc anchor manifest repeats frame $frameId." }
        $manifestFrames[$frameId] = $entry
    }
    $normalizedFrames = @()
    foreach ($input in $inputs) {
        $frameId = [long]$input.Receipt.indexedFrame.frameId
        if (-not $manifestFrames.ContainsKey($frameId)) { throw "Pocket-arc anchor manifest is missing selected frame $frameId." }
        $entry = $manifestFrames[$frameId]
        $manifestHash = ([string]$entry.pixelSha256).ToLowerInvariant()
        if ($manifestHash -notmatch '^[0-9a-f]{64}$' -or $manifestHash -ne $input.Hash) {
            throw "Pocket-arc anchors for frame $frameId do not match its exact source pixel SHA-256."
        }
        $anchors = @($entry.anchors)
        if ($anchors.Count -lt 2 -or $anchors.Count -gt 12) {
            throw "Pocket-arc anchor manifest frame $frameId must have 2 to 12 anchors."
        }
        $seenAnchorNames = @{}
        $normalizedAnchors = @()
        foreach ($anchor in $anchors) {
            $name = [string]$anchor.name
            $x = [int]$anchor.x
            $y = [int]$anchor.y
            if ($name -notmatch '^[A-Za-z0-9_-]+$') { throw "Frame $frameId has invalid pocket-arc anchor name '$name'." }
            if ($seenAnchorNames.ContainsKey($name.ToLowerInvariant())) { throw "Frame $frameId repeats pocket-arc anchor '$name'." }
            $seenAnchorNames[$name.ToLowerInvariant()] = $true
            if ($x -lt 0 -or $y -lt 0 -or $x -ge [int]$input.Receipt.indexedFrame.width -or $y -ge [int]$input.Receipt.indexedFrame.height) {
                throw "Frame $frameId anchor '$name' is outside the recorded source image dimensions."
            }
            $normalizedAnchors += [pscustomobject]@{ name = $name; x = $x; y = $y }
        }
        $normalizedFrames += [pscustomobject]@{
            frameId = $frameId; pixelSha256 = $manifestHash; anchors = $normalizedAnchors
        }
    }
    $normalizedPocketArcAnchorManifest = [pscustomobject]@{ version = 1; frames = $normalizedFrames }
    $normalizedManifestJson = $normalizedPocketArcAnchorManifest | ConvertTo-Json -Depth 8 -Compress
    $pocketArcAnchorManifestBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($normalizedManifestJson))
}

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
$receiptArchive = Join-Path $runDirectory 'source-receipts'
New-Item -ItemType Directory -Path $receiptArchive | Out-Null
foreach ($input in $inputs) {
    $receiptCopy = Join-Path $receiptArchive ([IO.Path]::GetFileName($input.JsonPath))
    Copy-Item -LiteralPath $input.JsonPath -Destination $receiptCopy
    if ((Get-FileHash -LiteralPath $receiptCopy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $input.ReceiptHash) {
        throw "Source receipt for frame $($input.Frame) changed before transfer."
    }
    $pushFiles += $receiptCopy
    $pushFiles += $input.PixelPath
    if ($RawProposalReplay) {
        $proposalCopy = Join-Path $runDirectory "local-proposals-$($input.Frame).json"
        Copy-Item -LiteralPath $input.RawProposalPath -Destination $proposalCopy
        $pushFiles += $proposalCopy
    }
}
$pushResult = Invoke-Adb (@('-s', $serial, 'push') + $pushFiles + @("$remoteRunDirectory/"))
if ($pushResult.ExitCode -ne 0) { throw "Input transfer failed: $($pushResult.Output -join ' ')" }
$stageTimer.Stop()
$stageTimings.inputTransferMs = [Math]::Round($stageTimer.Elapsed.TotalMilliseconds, 1)

$screenOnlyArgument = if ($ScreenOnly) { '1' } else { '0' }
$independentFramesArgument = if ($IndependentFrames) { '1' } else { '0' }
$probeOnlyArgument = if ($ProbeOnly) { '1' } else { '0' }
$pocketArcProbeOnlyArgument = if ($PocketArcProbeOnly) { '1' } else { '0' }
$pocketArcSweepOnlyArgument = if ($PocketArcSweepOnly) { '1' } else { '0' }
$rawProposalReplayArgument = if ($RawProposalReplay) { '1' } else { '0' }
$auditArgument = if ($ScreenOnly) { '0' } else { '1' }
$instrumentArguments = @('shell', 'am', 'instrument', '-w', '-e', 'run', $run,
    '-e', 'screenOnly', $screenOnlyArgument, '-e', 'independentFrames', $independentFramesArgument, '-e', 'probeOnly', $probeOnlyArgument, '-e', 'pocketArcProbeOnly', $pocketArcProbeOnlyArgument,
    '-e', 'pocketArcSweepOnly', $pocketArcSweepOnlyArgument,
    '-e', 'rawProposalReplay', $rawProposalReplayArgument,
    '-e', 'audit', $auditArgument, '-e', 'edgeMargin', "$EdgeMargin",
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
$arcBlackLabelX = $null
$arcBlackLabelY = $null
if ($TraceBlackMask) {
    $instrumentArguments += @('-e', 'trace', '1', '-e', 'blackLabelX', "$BlackLabelX", '-e', 'blackLabelY', "$BlackLabelY")
} elseif ($PocketArcProbeOnly) {
    $arcBlackLabelX = $BlackLabelX
    $arcBlackLabelY = $BlackLabelY
    $instrumentArguments += @('-e', 'blackLabelX', "$arcBlackLabelX", '-e', 'blackLabelY', "$arcBlackLabelY",
        '-e', 'blackPointSource', $BlackPointSource)
    if ($PocketArcTargetFitAcceptanceRadiusInBallRadii -gt 0) {
        $targetFitAcceptanceRadiusArgument = $PocketArcTargetFitAcceptanceRadiusInBallRadii.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
        $instrumentArguments += @('-e', 'pocketArcTargetFitAcceptanceRadiusRadii', $targetFitAcceptanceRadiusArgument)
    }
} elseif ($PocketArcSweepOnly) {
    if ($normalizedPocketArcAnchorManifest) {
        $manifestCopy = Join-Path $runDirectory 'pocket-arc-anchor-manifest.json'
        $normalizedPocketArcAnchorManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestCopy -Encoding utf8
        $instrumentArguments += @('-e', 'pocketArcAnchorManifestB64', $pocketArcAnchorManifestBase64)
    } else {
        # Colon is a shell-safe anchor separator; semicolons are parsed as remote-shell command delimiters by adb shell.
        $instrumentArguments += @('-e', 'pocketArcAnchors', ($PocketArcAnchor -join ':'))
    }
    $instrumentArguments += @('-e', 'blackPointSource', $BlackPointSource)
    if ($PocketArcTargetFitAcceptanceRadiusInBallRadii -gt 0) {
        $targetFitAcceptanceRadiusArgument = $PocketArcTargetFitAcceptanceRadiusInBallRadii.ToString('R', [Globalization.CultureInfo]::InvariantCulture)
        $instrumentArguments += @('-e', 'pocketArcTargetFitAcceptanceRadiusRadii', $targetFitAcceptanceRadiusArgument)
    }
}
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
$instrumentKind = if ($RawProposalReplay) { 'Raw-proposal replay' } elseif ($PocketArcSweepOnly) { 'Pocket-arc sweep' } elseif ($PocketArcProbeOnly) { 'Pocket-arc replay' } else { 'Boundary audit' }
if ($instrumentResult.ExitCode -ne 0) { throw "$instrumentKind instrumentation failed: $($instrumentResult.Output -join ' ')" }
if (($instrumentResult.Output -join "`n") -match 'shortMsg=Process crashed') {
    throw "$instrumentKind instrumentation process crashed: $($instrumentResult.Output -join ' ')"
}

$reportSuffix = if ($RawProposalReplay) { '-rawproposals' } elseif ($PocketArcSweepOnly) { '-pocketarcsweep' } elseif ($PocketArcProbeOnly) { '-pocketarc' } elseif ($TraceBlackMask) { '-blacktrace' } else { '' }
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
if ($RawProposalReplay -and [int]$report.rawProposalAnalysisWidth -ne 800) {
    throw "Raw-proposal replay report must identify width-800 analysis; got '$($report.rawProposalAnalysisWidth)'."
}
Assert-VisionReplayReceiptBinding -Inputs @($inputs) -Report $report
$maskArtifacts = @()
$blackMaskArtifacts = @()
$pocketArcTraceArtifacts = @()
$pocketArcAnchorRows = @()
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
if ($PocketArcProbeOnly -or $PocketArcSweepOnly) {
    $traceRoot = "$remoteRunDirectory/o08-pocket-arc-trace-$run"
    foreach ($sample in $report.samples) {
        if (-not $sample.detectionRequested) {
            $traceCount = if ($PocketArcSweepOnly) { @($sample.width1000.darkMaskTraces).Count } else { [int]($null -ne $sample.width1000.darkMaskTrace) }
            if ($traceCount -gt 0) {
                throw "Detection-disabled frame $($sample.frameId) unexpectedly contains a pocket-arc trace."
            }
            continue
        }
        $traces = if ($PocketArcSweepOnly) { @($sample.width1000.darkMaskTraces) } else { @([pscustomobject]@{ name = 'single'; darkMaskTrace = $sample.width1000.darkMaskTrace }) }
        $expectedAnchors = if ($normalizedPocketArcAnchorManifest) {
            @($normalizedPocketArcAnchorManifest.frames | Where-Object { [long]$_.frameId -eq [long]$sample.frameId } | Select-Object -ExpandProperty anchors)
        } else {
            @($PocketArcAnchor | ForEach-Object {
                if ($_ -match '^(?<name>[A-Za-z0-9_-]+)=(?<x>\d+),(?<y>\d+)$') {
                    [pscustomobject]@{ name = $Matches.name; x = [int]$Matches.x; y = [int]$Matches.y }
                }
            })
        }
        if ($PocketArcSweepOnly -and $traces.Count -ne $expectedAnchors.Count) {
            throw "Pocket-arc sweep returned $($traces.Count) traces for $($expectedAnchors.Count) expected anchors on frame $($sample.frameId)."
        }
        foreach ($anchorTrace in $traces) {
            $expectedAnchor = @($expectedAnchors | Where-Object { [string]$_.name -ceq [string]$anchorTrace.name }) | Select-Object -First 1
            if ($PocketArcSweepOnly -and $null -eq $expectedAnchor) {
                throw "Pocket-arc sweep returned unexpected anchor '$($anchorTrace.name)' on frame $($sample.frameId)."
            }
            if ($PocketArcSweepOnly -and ([int]$anchorTrace.sourcePixel.x -ne [int]$expectedAnchor.x -or [int]$anchorTrace.sourcePixel.y -ne [int]$expectedAnchor.y)) {
                throw "Pocket-arc sweep returned the wrong source coordinates for anchor '$($anchorTrace.name)' on frame $($sample.frameId)."
            }
            $trace = $anchorTrace.darkMaskTrace
            if ($null -eq $trace -or $trace.analysisWidth -ne 1000) {
                throw "Pocket-arc trace is missing for width-1000 frame $($sample.frameId), anchor $($anchorTrace.name)."
            }
            $overlayNames = @($trace.overlays)
            if ($overlayNames.Count -eq 0) { throw "Pocket-arc trace contains no overlays for frame $($sample.frameId), anchor $($anchorTrace.name)." }
            $pocketArcAnchorRows += [pscustomobject]@{
                frameId = [long]$sample.frameId; sourceTimeUs = [long]$sample.sourceTimeUs
                pixelSha256 = [string]$sample.pixelSha256; anchor = [string]$anchorTrace.name
                pointSource = [string]$anchorTrace.pointSource; sourceX = [int]$anchorTrace.sourcePixel.x
                sourceY = [int]$anchorTrace.sourcePixel.y; diagnosticArcPass = [bool]$trace.manualCentreProbe.pocketAwarePartialArcExperiment.candidate.diagnosticArcChecksPass
                supportedVisibleSectors = [int]$trace.manualCentreProbe.pocketAwarePartialArcExperiment.candidate.supportedVisibleSectorCount
                visibleSectors = [int]$trace.manualCentreProbe.pocketAwarePartialArcExperiment.candidate.visibleSectorCount
                contiguousRun = [int]$trace.manualCentreProbe.pocketAwarePartialArcExperiment.candidate.longestContiguousSupportedRun
                arcSpanDegrees = [double]$trace.manualCentreProbe.pocketAwarePartialArcExperiment.candidate.supportedArcSpanDegrees
                candidateOffsetPx = [double]$trace.manualCentreProbe.pocketAwarePartialArcExperiment.candidate.candidateToProposalOffset
                classifier = [string]$trace.manualCentreProbe.pocketAwarePartialArcExperiment.candidateColourClassifier.evidence.kind
                overlays = $overlayNames -join ';'
            }
            foreach ($name in $overlayNames) {
                if ([string]::IsNullOrWhiteSpace([string]$name) -or $name -ne [IO.Path]::GetFileName([string]$name)) {
                    throw "Pocket-arc trace contains an invalid artifact name for frame $($sample.frameId)."
                }
                $pocketArcTraceArtifacts += [pscustomobject]@{
                    frameId = $sample.frameId; anchor = $anchorTrace.name; configuredWidth = $trace.analysisWidth; file = $name
                }
            }
        }
    }
    if ($pocketArcTraceArtifacts.Count -gt 0) {
        $localTraceRoot = Join-Path $runDirectory 'pulled-pocket-arc-traces'
        New-Item -ItemType Directory -Path $localTraceRoot -Force | Out-Null
        $pullTraceResult = Invoke-Adb @('-s', $serial, 'pull', $traceRoot, $localTraceRoot)
        if ($pullTraceResult.ExitCode -ne 0) { throw "Could not retrieve pocket-arc trace directory: $($pullTraceResult.Output -join ' ')" }
        $pulledTraceFiles = @(Get-ChildItem -LiteralPath $localTraceRoot -Recurse -File)
        $expectedTraceNames = @($pocketArcTraceArtifacts | Select-Object -ExpandProperty file | Sort-Object -Unique)
        $pulledTraceNames = @($pulledTraceFiles | ForEach-Object { $_.Name } | Sort-Object -Unique)
        if (($expectedTraceNames -join "`n") -ne ($pulledTraceNames -join "`n")) {
            throw "Bulk pocket-arc trace pull did not match the report-listed artifact set. Expected [$($expectedTraceNames -join ', ')], found [$($pulledTraceNames -join ', ')]"
        }
        foreach ($artifact in $pocketArcTraceArtifacts) {
            $matches = @($pulledTraceFiles | Where-Object { $_.Name -ceq [string]$artifact.file })
            if ($matches.Count -ne 1) { throw "Bulk pocket-arc pull found $($matches.Count) copies of $($artifact.file)." }
            $localTrace = Join-Path $runDirectory ([string]$artifact.file)
            Copy-Item -LiteralPath $matches[0].FullName -Destination $localTrace -Force
            $artifact | Add-Member -MemberType NoteProperty -Name sha256 -Value (Get-FileHash -LiteralPath $localTrace -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    if ($pocketArcAnchorRows.Count -gt 0) {
        $pocketArcAnchorRows | Export-Csv -NoTypeInformation -Encoding utf8 -LiteralPath (Join-Path $runDirectory 'pocket-arc-anchor-summary.csv')
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
    mode = if ($ScreenOnly) { 'screen-only' } elseif ($ProbeOnly) { 'probe-only' } elseif ($PocketArcSweepOnly) { 'pocket-arc-sweep-only' } elseif ($PocketArcProbeOnly) { 'pocket-arc-probe-only' } elseif ($RawProposalReplay) { 'raw-proposal-replay' } else { 'full-audit' }
    independentFrames = [bool]$IndependentFrames
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
        [pscustomobject]@{ frameId = $_.Receipt.indexedFrame.frameId; pixelFile = [IO.Path]::GetFileName($_.PixelPath); receipt = [IO.Path]::GetFileName($_.JsonPath); receiptArtifact = ('source-receipts/' + [IO.Path]::GetFileName($_.JsonPath)); receiptSha256 = $_.ReceiptHash; sha256 = $_.Hash }
    }
    pocketArcPointSource = if ($PocketArcProbeOnly -or $PocketArcSweepOnly) { $BlackPointSource } else { $null }
    rawModelProposalFiles = if ($RawProposalReplay) { $inputs | ForEach-Object {
        $copiedProposal = Join-Path $runDirectory "local-proposals-$($_.Frame).json"
        [pscustomobject]@{ frameId = $_.Frame; file = [IO.Path]::GetFileName($copiedProposal); sha256 = (Get-FileHash -LiteralPath $copiedProposal -Algorithm SHA256).Hash.ToLowerInvariant(); sourcePixelSha256 = $_.Hash }
    } } else { @() }
    rawProposalAnalysisWidth = if ($RawProposalReplay) { $report.rawProposalAnalysisWidth } else { $null }
    report = $reportName
    reportSha256 = (Get-FileHash -LiteralPath $localReport -Algorithm SHA256).Hash.ToLowerInvariant()
    cameraBefore = Get-CameraStatusForManifest $cameraBefore
    cameraAfter = Get-CameraStatusForManifest $cameraAfter
}
if ($PocketArcProbeOnly) {
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcProbeOnly -Value $true
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcLabelSourcePixel -Value ([pscustomobject]@{ x = $arcBlackLabelX; y = $arcBlackLabelY })
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcTraceArtifacts -Value $pocketArcTraceArtifacts
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcTargetFitAcceptanceRadiusInBallRadii -Value $(if ($PocketArcTargetFitAcceptanceRadiusInBallRadii -gt 0) { $PocketArcTargetFitAcceptanceRadiusInBallRadii } else { $null })
}
if ($PocketArcSweepOnly) {
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcSweepOnly -Value $true
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcAnchors -Value $(if ($normalizedPocketArcAnchorManifest) { $normalizedPocketArcAnchorManifest.frames } else { @($PocketArcAnchor) })
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcAnchorSummaryCsv -Value $(if ($pocketArcAnchorRows.Count -gt 0) { 'pocket-arc-anchor-summary.csv' } else { $null })
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcTraceArtifacts -Value $pocketArcTraceArtifacts
    $manifest | Add-Member -MemberType NoteProperty -Name pocketArcTargetFitAcceptanceRadiusInBallRadii -Value $(if ($PocketArcTargetFitAcceptanceRadiusInBallRadii -gt 0) { $PocketArcTargetFitAcceptanceRadiusInBallRadii } else { $null })
}
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $runDirectory 'manifest.json')
$timingSummary = $stageTimings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)ms" }
$runKind = if ($ScreenOnly) { 'Detector screen' } elseif ($ProbeOnly) { 'Candidate probe' } elseif ($PocketArcSweepOnly) { 'Pocket-arc sweep' } elseif ($PocketArcProbeOnly) { 'Pocket-arc probe' } elseif ($RawProposalReplay) { 'Raw-proposal replay' } else { 'Boundary audit' }
Write-Output "$runKind saved under $runDirectory (total=$([Math]::Round($totalTimer.Elapsed.TotalMilliseconds, 1))ms; $($timingSummary -join ', '))"
