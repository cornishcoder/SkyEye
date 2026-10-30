param(
    [string]$SourceRelativePath = 'work/production/evidence/live-detect-pocket-lip-20261030/o08-capture',
    [int[]]$Frames = @(399, 433),
    [string]$RunPrefix = 'pocketlip30',
    [string]$Purpose = 'Current-code exact-frame candidate and boundary audit for the Oct 30 pocket-lip fixture.',
    [int]$EdgeMargin = 23,
    [double]$PocketRadius = 48,
    [double]$BoundarySupport = 0.75,
    [double]$SparseColourSupport = 0.65625,
    [double]$PackedColourBoundarySearchExtent = 0.65,
    [double]$SparseBlackSearchExtent = 1.3,
    [double]$SparseBlackSupport = 0.65625,
    [int]$SparseBlackCandidateLimit = 12,
    [bool]$SparseBlackRankByRadius = $true,
    [switch]$EnableIlluminationNormalization,
    [switch]$TracePackedColourFits,
    [switch]$ObstructionMaskTrace,
    [switch]$TraceBlackMask,
    [int]$BlackLabelX = 1282,
    [int]$BlackLabelY = 536,
    [int]$ObstructionClothHue = -1,
    [int]$ObstructionClothSaturationFloor = -1,
    [int]$ObstructionClothValueFloor = -1,
    [int]$ObstructionClothMedianValue = -1
)

$ErrorActionPreference = 'Stop'
if ($EdgeMargin -lt 0 -or $EdgeMargin -gt 100) { throw 'EdgeMargin must be between 0 and 100 pixels at width 1000.' }
if ($PocketRadius -le 0 -or $PocketRadius -gt 80) { throw 'PocketRadius must be greater than 0 and at most 80 pixels at width 1000.' }
if ($BoundarySupport -lt 0.5625 -or $BoundarySupport -gt 1) { throw 'BoundarySupport must be between 0.5625 and 1.' }
if ($SparseColourSupport -lt 0.5625 -or $SparseColourSupport -gt 1) { throw 'SparseColourSupport must be between 0.5625 and 1.' }
if ($PackedColourBoundarySearchExtent -lt 0.2 -or $PackedColourBoundarySearchExtent -gt 0.8) { throw 'PackedColourBoundarySearchExtent must be between 0.2 and 0.8 ball radii.' }
if ($SparseBlackSearchExtent -lt 0.2 -or $SparseBlackSearchExtent -gt 1.5) { throw 'SparseBlackSearchExtent must be between 0.2 and 1.5 ball radii.' }
if ($SparseBlackSupport -lt 0.5625 -or $SparseBlackSupport -gt 1) { throw 'SparseBlackSupport must be between 0.5625 and 1.' }
if ($SparseBlackCandidateLimit -lt 1 -or $SparseBlackCandidateLimit -gt 64) { throw 'SparseBlackCandidateLimit must be between 1 and 64.' }
if ($BlackLabelX -lt 0 -or $BlackLabelY -lt 0) { throw 'Black label coordinates must be non-negative source-image pixels.' }
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
$evidenceRoot = Join-Path $repoRoot 'work/production/evidence/live-detect-pocket-lip-20261030/device-replays'
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

New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$installResult = Invoke-Adb @('-s', $serial, 'install', '-r', $apk)
if ($installResult.ExitCode -ne 0 -or ($installResult.Output -join "`n") -notmatch 'Success') {
    throw "Could not update the isolated Vision Device package in place: $($installResult.Output -join ' ')"
}
$mkdirResult = Invoke-Adb @('-s', $serial, 'shell', 'mkdir', '-p', $remoteRunDirectory)
if ($mkdirResult.ExitCode -ne 0) { throw "Could not create isolated replay folder: $($mkdirResult.Output -join ' ')" }
foreach ($input in $inputs) {
    foreach ($file in @($input.JsonPath, $input.PixelPath)) {
        $pushResult = Invoke-Adb @('-s', $serial, 'push', $file, "$remoteRunDirectory/$([IO.Path]::GetFileName($file))")
        if ($pushResult.ExitCode -ne 0) { throw "Transfer failed for $file`: $($pushResult.Output -join ' ')" }
    }
}

$instrumentArguments = @('shell', 'am', 'instrument', '-w', '-e', 'run', $run,
    '-e', 'audit', '1', '-e', 'edgeMargin', "$EdgeMargin",
    '-e', 'pocketRadius', "$PocketRadius", '-e', 'boundarySupport', "$BoundarySupport",
    '-e', 'sparseColourSupport', "$SparseColourSupport",
    '-e', 'boundaryExtent', "$PackedColourBoundarySearchExtent",
    '-e', 'sparseBlackExtent', "$SparseBlackSearchExtent", '-e', 'sparseBlackSupport', "$SparseBlackSupport",
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
if ($TraceBlackMask) { $instrumentArguments += @('-e', 'trace', '1', '-e', 'blackLabelX', "$BlackLabelX", '-e', 'blackLabelY', "$BlackLabelY") }
$instrumentArguments += $instrumentation
$instrumentResult = Invoke-Adb (@('-s', $serial) + $instrumentArguments)
$instrumentResult.Output | Set-Content -LiteralPath (Join-Path $runDirectory 'instrumentation.log')
if ($instrumentResult.ExitCode -ne 0) { throw "Boundary audit instrumentation failed: $($instrumentResult.Output -join ' ')" }
if (($instrumentResult.Output -join "`n") -match 'shortMsg=Process crashed') {
    throw "Boundary audit instrumentation process crashed: $($instrumentResult.Output -join ' ')"
}

$reportSuffix = if ($TraceBlackMask) { '-blacktrace' } else { '' }
$reportName = "o08-replay-proof-$run$reportSuffix.json"
$localReport = Join-Path $runDirectory $reportName
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
$cameraAfter = Invoke-RestMethod -Uri $cameraStatusUri -TimeoutSec 5
if ($cameraAfter.state -ne 'Stopped') { throw "Mounted camera changed state during offline replay: $($cameraAfter.state)." }

$manifest = [pscustomobject]@{
    createdAt = [DateTimeOffset]::UtcNow
    purpose = $Purpose
    deviceSerial = $serial
    package = $package
    apkSha256 = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
    run = $run
    localIlluminationNormalization = [bool]$EnableIlluminationNormalization
    tracePackedColourFits = [bool]$TracePackedColourFits
    auditEnabled = $true
    obstructionMaskTrace = [bool]$ObstructionMaskTrace
    diagnosticObstructionClothRangeOverride = if ($specifiedObstructionClothRange -eq 4) {
        [pscustomobject]@{ hue = $ObstructionClothHue; saturationFloor = $ObstructionClothSaturationFloor; valueFloor = $ObstructionClothValueFloor; medianValue = $ObstructionClothMedianValue }
    } else { $null }
    obstructionMasks = $maskArtifacts
    traceBlackMask = [bool]$TraceBlackMask
    manualBlackLabelSourcePixel = if ($TraceBlackMask) { [pscustomobject]@{ x = $BlackLabelX; y = $BlackLabelY } } else { $null }
    blackMaskTraces = $blackMaskArtifacts
    packedClusterImageEdgeMargin = $EdgeMargin
    pocketExclusionRadius = $PocketRadius
    minimumHoughBoundarySupport = $BoundarySupport
    minimumSparseColourBoundarySupport = $SparseColourSupport
    packedColourBoundarySearchExtentFraction = $PackedColourBoundarySearchExtent
    sparseBlackBoundarySearchExtentFraction = $SparseBlackSearchExtent
    minimumSparseBlackBoundarySupport = $SparseBlackSupport
    sparseBlackCandidateLimit = $SparseBlackCandidateLimit
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
Write-Output "Boundary audit saved under $runDirectory"
