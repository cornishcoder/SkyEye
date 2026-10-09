param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(5, 3600)][int]$DurationSeconds = 60,
    [ValidateSet(15, 30, 60)][int]$Hz = 30,
    [ValidateRange(1, 5)][int]$SampleIntervalSeconds = 1,
    [switch]$AllowUnconfirmedDiagnosticSeed,
    [string]$AdbPath = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
)
$ErrorActionPreference = 'Stop'
$taskLocalPort = 18768
$taskRemotePort = 8769
$taskAddress = "http://127.0.0.1:$taskLocalPort"
$taskOutputPath = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $taskOutputPath) { throw 'Choose a new output path; this collector will not overwrite evidence.' }
$taskDeviceState = & $AdbPath -s $CompanionSerial get-state 2>$null
if ($LASTEXITCODE -ne 0 -or $taskDeviceState -ne 'device') { throw 'Companion is not connected and authorized.' }
$taskForwardRows = @(& $AdbPath -s $CompanionSerial forward --list)
$taskForward = $taskForwardRows | Where-Object { $_ -match "^$([regex]::Escape($CompanionSerial))\s+tcp:$taskLocalPort\s+tcp:(\d+)$" }
$taskOwnsForward = $false
if ($taskForward) {
    if ($taskForward -notmatch "tcp:$taskRemotePort$") { throw "Local port $taskLocalPort is already forwarded elsewhere." }
} else {
    & $AdbPath -s $CompanionSerial forward --no-rebind "tcp:$taskLocalPort" "tcp:$taskRemotePort" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not reserve local port $taskLocalPort for the diagnostic endpoint." }
    $taskOwnsForward = $true
}

$taskFeedStarted = $false
$taskRows = [System.Collections.Generic.List[object]]::new()
$taskSeenGapFrames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$taskPreviousSample = $null
$taskStartedAt = $null
$taskCaptureError = $null
try {
    $taskPing = Invoke-RestMethod -Method Get -Uri "$taskAddress/ping" -TimeoutSec 5
    if (-not $taskPing.diagnostic -or $taskPing.package -ne 'uk.co.skyeye.companion.lab') {
        throw 'The connected Lab app is not running its opt-in performance diagnostics build.'
    }
    if ($AllowUnconfirmedDiagnosticSeed) {
        $taskCandidate = Invoke-RestMethod -Method Post -Uri "$taskAddress/observe?choices=0" -TimeoutSec 30
        if (-not $taskCandidate.succeeded) { throw "Could not refresh a diagnostic candidate: $($taskCandidate.message)" }
        $taskCandidateMetrics = Invoke-RestMethod -Method Get -Uri "$taskAddress/metrics" -TimeoutSec 5
        if ($taskCandidateMetrics.table.occluded -or $taskCandidateMetrics.table.balls.Count -lt 1 -or $taskCandidateMetrics.table.balls.Count -gt 16) {
            throw 'The fresh diagnostic candidate is not a clear 1–16-ball table; no motion capture was started.'
        }
    }
    $taskSeedQuery = if ($AllowUnconfirmedDiagnosticSeed) { '&seed=fresh-clear-candidate' } else { '' }
    $null = Invoke-RestMethod -Method Post -Uri "$taskAddress/motion-feed?enabled=1&hz=$Hz$taskSeedQuery" -TimeoutSec 10
    $taskFeedStarted = $true
    $taskStartupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        $taskMetrics = Invoke-RestMethod -Method Get -Uri "$taskAddress/metrics" -TimeoutSec 5
        $taskMotion = $taskMetrics.motion
        if ($taskMotion -and -not [string]::IsNullOrWhiteSpace([string]$taskMotion.stopReason)) {
            throw "The motion feed stopped before producing a processed sample: $($taskMotion.stopReason)"
        }
        if ($taskMotion -and $taskMotion.processed -gt 0 -and $taskMotion.latest) { break }
        Start-Sleep -Seconds 1
    } while ([DateTimeOffset]::UtcNow -lt $taskStartupDeadline)
    if (-not $taskMotion -or $taskMotion.processed -eq 0 -or -not $taskMotion.latest) {
        throw 'No processed camera sample arrived within 20 seconds; no measurement was saved.'
    }
    $taskStartedAt = [DateTimeOffset]::UtcNow
    Write-Host "Motion capture is active for $DurationSeconds seconds; take one shot now."
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
    while ([DateTimeOffset]::UtcNow -lt $taskDeadline) {
        $taskMetrics = Invoke-RestMethod -Method Get -Uri "$taskAddress/metrics" -TimeoutSec 5
        $taskMotion = $taskMetrics.motion
        if ($null -eq $taskMotion) { throw 'The Lab returned no motion-feed metrics.' }
        $taskLatest = $taskMotion.latest
        $taskPlayer = $taskMetrics.player
        $taskNow = [DateTimeOffset]::UtcNow
        $taskMotionRegions = @()
        if ($taskLatest -and $taskLatest.regions) {
            $taskMotionRegions = @($taskLatest.regions | ForEach-Object {
                [pscustomobject]@{
                    Id = $_.id
                    Features = $_.features
                    Supported = $_.supported
                    DxPixels = $_.dxPixels
                    DyPixels = $_.dyPixels
                    VelocityXPixelsPerSecond = $_.velocityXPixelsPerSecond
                    VelocityYPixelsPerSecond = $_.velocityYPixelsPerSecond
                    IlluminationNormalizedChangedPixelFraction = $_.illuminationNormalizedChangedPixelFraction
                    MedianBrightnessOffsetGray = $_.medianBrightnessOffsetGray
                    ChangeThresholdGray = $_.changeThresholdGray
                    TemplateDxPixels = $_.templateDxPixels
                    TemplateDyPixels = $_.templateDyPixels
                    TemplateResidual = $_.templateResidual
                    TemplateStatus = $_.templateStatus
                    Status = $_.status
                }
            })
        }
        $taskSupportedRegions = @($taskMotionRegions | Where-Object {
            $_.Status -eq 'supported' -and $null -ne $_.VelocityXPixelsPerSecond -and $null -ne $_.VelocityYPixelsPerSecond
        })
        $taskSupportedSpeeds = @($taskSupportedRegions | ForEach-Object {
            [math]::Sqrt([math]::Pow([double]$_.VelocityXPixelsPerSecond, 2) + [math]::Pow([double]$_.VelocityYPixelsPerSecond, 2))
        })
        $taskSupportedDisplacements = @($taskSupportedRegions | Where-Object {
            $null -ne $_.DxPixels -and $null -ne $_.DyPixels
        } | ForEach-Object {
            [math]::Sqrt([math]::Pow([double]$_.DxPixels, 2) + [math]::Pow([double]$_.DyPixels, 2))
        })
        $taskPeakSupportedSpeed = if ($taskSupportedSpeeds.Count -gt 0) { ($taskSupportedSpeeds | Measure-Object -Maximum).Maximum } else { $null }
        $taskPeakSupportedDisplacement = if ($taskSupportedDisplacements.Count -gt 0) { ($taskSupportedDisplacements | Measure-Object -Maximum).Maximum } else { $null }
        $taskDecoderDelta = $null
        $taskTextureDelta = $null
        $taskAcquiredDelta = $null
        $taskProcessedDelta = $null
        $taskNoNewPollDelta = $null
        $taskUnavailablePollDelta = $null
        $taskGapResetDelta = $null
        $taskCopyFailureCounts = @{}
        $taskCopyFailureDelta = @{}
        if ($taskMetrics.copy.failures) {
            foreach ($taskFailure in $taskMetrics.copy.failures.PSObject.Properties) {
                $taskCopyFailureCounts[$taskFailure.Name] = [long]$taskFailure.Value
            }
        }
        if ($taskPreviousSample -and $taskPreviousSample.MediaGeneration -eq $taskPlayer.mediaGeneration) {
            $taskDecoderDelta = $taskPlayer.decodedFrames - $taskPreviousSample.DecoderFrames
            $taskTextureDelta = $taskPlayer.textureFrames - $taskPreviousSample.TextureFrames
            $taskAcquiredDelta = $taskMotion.acquired - $taskPreviousSample.Acquired
            $taskProcessedDelta = $taskMotion.processed - $taskPreviousSample.Processed
            $taskNoNewPollDelta = $taskMotion.noNewFramePolls - $taskPreviousSample.NoNewFramePolls
            $taskUnavailablePollDelta = $taskMotion.unavailableCandidatePolls - $taskPreviousSample.UnavailableCandidatePolls
            $taskGapResetDelta = $taskMotion.gapResets - $taskPreviousSample.GapResets
            $taskFailureReasons = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($taskReason in $taskCopyFailureCounts.Keys) { $null = $taskFailureReasons.Add($taskReason) }
            foreach ($taskReason in $taskPreviousSample.CopyFailureCounts.Keys) { $null = $taskFailureReasons.Add($taskReason) }
            foreach ($taskReason in $taskFailureReasons) {
                $taskCurrentFailureCount = if ($taskCopyFailureCounts.ContainsKey($taskReason)) { $taskCopyFailureCounts[$taskReason] } else { 0 }
                $taskPreviousFailureCount = if ($taskPreviousSample.CopyFailureCounts.ContainsKey($taskReason)) { $taskPreviousSample.CopyFailureCounts[$taskReason] } else { 0 }
                $taskFailureDelta = $taskCurrentFailureCount - $taskPreviousFailureCount
                if ($taskFailureDelta -gt 0) { $taskCopyFailureDelta[$taskReason] = $taskFailureDelta }
            }
        }
        $taskGapEvents = @()
        foreach ($taskGap in @($taskMotion.recentGaps)) {
            $taskGapKey = "$($taskPlayer.mediaGeneration):$($taskGap.frameId)"
            if ($taskSeenGapFrames.Add($taskGapKey)) { $taskGapEvents += $taskGap }
        }
        $taskRows.Add([pscustomobject]@{
            CapturedAtUtc = $taskNow.ToString('O')
            RequestedHz = $taskMotion.requestedHz
            SeedSource = $taskMotion.seedSource
            SeedFrameId = $taskMotion.seedFrameId
            SeedFrameAgeMs = $taskMotion.seedFrameAgeMs
            FrameWidth = $taskMotion.frameWidth
            FrameHeight = $taskMotion.frameHeight
            Acquired = $taskMotion.acquired
            AcquiredDelta = $taskAcquiredDelta
            Processed = $taskMotion.processed
            ProcessedDelta = $taskProcessedDelta
            Replaced = $taskMotion.replaced
            NoNewFramePolls = $taskMotion.noNewFramePolls
            NoNewFramePollDelta = $taskNoNewPollDelta
            UnavailableCandidatePolls = $taskMotion.unavailableCandidatePolls
            UnavailableCandidatePollDelta = $taskUnavailablePollDelta
            UnavailableCandidateReasons = $taskMotion.unavailableCandidateReasons
            DuplicateOrBackwards = $taskMotion.duplicateOrBackwards
            Stale = $taskMotion.stale
            AcquisitionFailures = $taskMotion.acquisitionFailures
            ScopeRejected = $taskMotion.scopeRejected
            WorkerFailures = $taskMotion.workerFailures
            GapResets = $taskMotion.gapResets
            GapResetDelta = $taskGapResetDelta
            GapEvents = $taskGapEvents
            Active = $taskMotion.active
            Pending = $taskMotion.pending
            CadenceSampleCount = $taskMotion.cadenceSampleCount
            ProcessedSourceCadenceHz = $taskMotion.processedSourceCadenceHz
            GapThresholdUs = $taskMotion.gapThresholdUs
            LastProcessedDeltaUs = $taskMotion.lastProcessedDeltaUs
            LastGapUs = $taskMotion.lastGapUs
            LargestGapUs = $taskMotion.largestGapUs
            RecentDeltaSampleCount = $taskMotion.recentDeltaSampleCount
            RecentDeltaP50Us = $taskMotion.recentDeltaP50Us
            RecentDeltaP95Us = $taskMotion.recentDeltaP95Us
            RecentDeltaMaxUs = $taskMotion.recentDeltaMaxUs
            LastProcessedFrameIdDelta = $taskMotion.lastProcessedFrameIdDelta
            LastGapFrameIdDelta = $taskMotion.lastGapFrameIdDelta
            LastClockSampleAgeMs = $taskMotion.lastClockSampleAgeMs
            LastClockUncertaintyMs = $taskMotion.lastClockUncertaintyMs
            AcquisitionRetries = $taskMotion.acquisitionRetries
            LastAcquisitionAttempts = $taskMotion.lastAcquisitionAttempts
            LastCandidateProbeQueueMs = $taskMotion.lastCandidateProbeQueueMs
            LastCandidateProbeDurationMs = $taskMotion.lastCandidateProbeDurationMs
            LastPolledCandidateFrameId = $taskMotion.lastCandidateFrameId
            LastCandidateSourceTimeUs = $taskMotion.lastCandidateSourceTimeUs
            LastCandidateTextureUpdateCount = $taskMotion.lastCandidateTextureUpdateCount
            LastCandidateTextureAgeMs = $taskMotion.lastCandidateTextureAgeMs
            LastReadMainThreadQueueMs = $taskMotion.lastReadMainThreadQueueMs
            LastBitmapCopyMs = $taskMotion.lastBitmapCopyMs
            LastAcquisitionCandidateFrameId = $taskMotion.lastAcquisitionCandidateFrameId
            LastAcquisitionCandidateSourceTimeUs = $taskMotion.lastAcquisitionCandidateSourceTimeUs
            LastReadFrameId = $taskMotion.lastReadFrameId
            LastReadSourceTimeUs = $taskMotion.lastReadSourceTimeUs
            LastCandidateToReadFrameIdDelta = $taskMotion.lastCandidateToReadFrameIdDelta
            LastCandidateToReadSourceDeltaUs = $taskMotion.lastCandidateToReadSourceDeltaUs
            LastReadTextureUpdateCount = $taskMotion.lastReadTextureUpdateCount
            LastReadTextureAgeMs = $taskMotion.lastReadTextureAgeMs
            LastProcessedTextureUpdateDelta = $taskMotion.lastProcessedTextureUpdateDelta
            CopyFailures = $taskMetrics.copy.failures
            CopyFailureDelta = $taskCopyFailureDelta
            DecoderMediaGeneration = $taskPlayer.mediaGeneration
            PlayerPlaybackState = $taskPlayer.playbackState
            PlayerLastErrorCode = $taskPlayer.lastErrorCode
            PlayerEndedCount = $taskPlayer.endedCount
            PlayerPlaying = $taskPlayer.playing
            PlayerPlayWhenReady = $taskPlayer.playWhenReady
            PlayerSuppressionReason = $taskPlayer.suppressionReason
            DecoderFrames = $taskPlayer.decodedFrames
            DecoderFrameDelta = $taskDecoderDelta
            TextureFrames = $taskPlayer.textureFrames
            TextureFrameDelta = $taskTextureDelta
            TextureFps = $taskPlayer.textureFps
            ReleaseToTextureMs = $taskPlayer.releaseToTextureMs
            TextureAgeMs = $taskPlayer.textureAgeMs
            PlayerLoading = $taskPlayer.loading
            LiveOffsetMs = $taskPlayer.liveOffsetMs
            LastAcquisitionMs = $taskMotion.lastAcquisitionMs
            LastWorkerMs = $taskMotion.lastWorkerMs
            LastResultAgeMs = $taskMotion.lastResultAgeMs
            SourceTimeUs = if ($taskLatest) { $taskLatest.sourceTimeUs } else { $null }
            ConversionMs = if ($taskLatest) { $taskLatest.conversionMs } else { $null }
            FlowMs = if ($taskLatest) { $taskLatest.flowMs } else { $null }
            AnalysisImageWidth = if ($taskLatest) { $taskLatest.analysisImageWidth } else { $null }
            AnalysisImageHeight = if ($taskLatest) { $taskLatest.analysisImageHeight } else { $null }
            RegionCount = if ($taskLatest) { @($taskLatest.regions).Count } else { 0 }
            SupportedRegionCount = $taskSupportedRegions.Count
            MaxSupportedRegionSpeedPixelsPerSecond = $taskPeakSupportedSpeed
            MaxSupportedRegionDisplacementPixels = $taskPeakSupportedDisplacement
            MotionRegions = $taskMotionRegions
            MotionAnalysis = $taskMotion.analysis
            StopReason = $taskMotion.stopReason
        })
        $taskPreviousSample = [pscustomobject]@{
            MediaGeneration = $taskPlayer.mediaGeneration
            DecoderFrames = $taskPlayer.decodedFrames
            TextureFrames = $taskPlayer.textureFrames
            Acquired = $taskMotion.acquired
            Processed = $taskMotion.processed
            NoNewFramePolls = $taskMotion.noNewFramePolls
            UnavailableCandidatePolls = $taskMotion.unavailableCandidatePolls
            GapResets = $taskMotion.gapResets
            CopyFailureCounts = $taskCopyFailureCounts
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$taskMotion.stopReason)) {
            $taskCaptureError = "The motion feed stopped during capture: $($taskMotion.stopReason)"
            break
        }
        Start-Sleep -Seconds $SampleIntervalSeconds
    }
}
finally {
    if ($taskFeedStarted) {
        try { $null = Invoke-RestMethod -Method Post -Uri "$taskAddress/motion-feed?enabled=0" -TimeoutSec 10 } catch { }
    }
    if ($taskOwnsForward) {
        & $AdbPath -s $CompanionSerial forward --remove "tcp:$taskLocalPort" | Out-Null
    }
}

if ($taskRows.Count -eq 0) { throw 'No motion-feed samples were collected.' }
$taskReport = [pscustomobject]@{
    SchemaVersion = 10
    DeviceSerial = $CompanionSerial
    Package = $taskPing.package
    StartedAtUtc = $taskStartedAt.ToString('O')
    EndedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    RequestedHz = $Hz
    DurationSeconds = $DurationSeconds
    SampleIntervalSeconds = $SampleIntervalSeconds
    Completed = $null -eq $taskCaptureError
    CaptureError = $taskCaptureError
    ImagesSaved = $false
    Samples = $taskRows
    MotionSummary = [pscustomobject]@{
        SamplesWithSupportedRegions = @($taskRows | Where-Object SupportedRegionCount -gt 0).Count
        PeakSupportedRegionSpeedPixelsPerSecond = if ($taskRows.Count -gt 0) { ($taskRows | Measure-Object -Property MaxSupportedRegionSpeedPixelsPerSecond -Maximum).Maximum } else { $null }
        PeakSupportedRegionDisplacementPixels = if ($taskRows.Count -gt 0) { ($taskRows | Measure-Object -Property MaxSupportedRegionDisplacementPixels -Maximum).Maximum } else { $null }
        SamplesMoving = @($taskRows | Where-Object { $_.MotionAnalysis.phase -eq 3 }).Count
        SamplesSettling = @($taskRows | Where-Object { $_.MotionAnalysis.phase -eq 1 }).Count
        PeakBallTemplateDisplacementPixelsPerFrame = if ($taskRows.Count -gt 0) { ($taskRows | ForEach-Object { $_.MotionAnalysis.windowPeakDisplacementPixels } | Measure-Object -Maximum).Maximum } else { $null }
    }
    Scope = 'Opt-in candidate availability, main-thread probe/read queue timing, TextureView update counts and age, native bitmap-copy duration, source PTS, per-gap acquisition/worker telemetry, decoder-release/texture and copy-failure counters, player restart/error state and dimensions. Per-region ball-centred template displacement feeds a diagnostic motion phase machine with a bounded two-second peak window. It does not identify durable balls, adjudicate pots, or mutate canonical game state. Thresholds remain provisional until labelled moving and still table evidence separates reliably. An unconfirmed seed is accepted only by the explicit development-only fresh-clear-candidate option.'
}
$taskOutputDirectory = Split-Path -Parent $taskOutputPath
if ($taskOutputDirectory -and -not (Test-Path -LiteralPath $taskOutputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $taskOutputDirectory | Out-Null
}
[IO.File]::WriteAllText($taskOutputPath, ($taskReport | ConvertTo-Json -Depth 10) + "`n")
Write-Output "Saved $($taskRows.Count) O02 motion metric samples to $taskOutputPath"
if ($taskCaptureError) { Write-Warning $taskCaptureError }
