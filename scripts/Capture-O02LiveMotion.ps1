param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [Parameter(Mandatory)][string]$OutputPath,
    [ValidateRange(5, 3600)][int]$DurationSeconds = 60,
    [ValidateSet(15, 30, 60)][int]$Hz = 30,
    [ValidateRange(1, 5)][int]$SampleIntervalSeconds = 1,
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
$taskStartedAt = $null
$taskCaptureError = $null
try {
    $taskPing = Invoke-RestMethod -Method Get -Uri "$taskAddress/ping" -TimeoutSec 5
    if (-not $taskPing.diagnostic -or $taskPing.package -ne 'uk.co.skyeye.companion.lab') {
        throw 'The connected Lab app is not running its opt-in performance diagnostics build.'
    }
    $null = Invoke-RestMethod -Method Post -Uri "$taskAddress/motion-feed?enabled=1&hz=$Hz" -TimeoutSec 10
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
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds($DurationSeconds)
    while ([DateTimeOffset]::UtcNow -lt $taskDeadline) {
        $taskMetrics = Invoke-RestMethod -Method Get -Uri "$taskAddress/metrics" -TimeoutSec 5
        $taskMotion = $taskMetrics.motion
        if ($null -eq $taskMotion) { throw 'The Lab returned no motion-feed metrics.' }
        $taskLatest = $taskMotion.latest
        $taskRows.Add([pscustomobject]@{
            CapturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            RequestedHz = $taskMotion.requestedHz
            SeedSource = $taskMotion.seedSource
            SeedFrameId = $taskMotion.seedFrameId
            SeedFrameAgeMs = $taskMotion.seedFrameAgeMs
            FrameWidth = $taskMotion.frameWidth
            FrameHeight = $taskMotion.frameHeight
            Acquired = $taskMotion.acquired
            Processed = $taskMotion.processed
            Replaced = $taskMotion.replaced
            DuplicateOrBackwards = $taskMotion.duplicateOrBackwards
            Stale = $taskMotion.stale
            AcquisitionFailures = $taskMotion.acquisitionFailures
            ScopeRejected = $taskMotion.scopeRejected
            WorkerFailures = $taskMotion.workerFailures
            GapResets = $taskMotion.gapResets
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
            LastAcquisitionMs = $taskMotion.lastAcquisitionMs
            LastWorkerMs = $taskMotion.lastWorkerMs
            LastResultAgeMs = $taskMotion.lastResultAgeMs
            SourceTimeUs = if ($taskLatest) { $taskLatest.sourceTimeUs } else { $null }
            ConversionMs = if ($taskLatest) { $taskLatest.conversionMs } else { $null }
            FlowMs = if ($taskLatest) { $taskLatest.flowMs } else { $null }
            RegionCount = if ($taskLatest) { @($taskLatest.regions).Count } else { 0 }
            StopReason = $taskMotion.stopReason
        })
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
    SchemaVersion = 2
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
    Scope = 'Opt-in source PTS, frame dimensions, acquisition and worker telemetry. No detector tracking or ball identity claim.'
}
$taskOutputDirectory = Split-Path -Parent $taskOutputPath
if ($taskOutputDirectory -and -not (Test-Path -LiteralPath $taskOutputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $taskOutputDirectory | Out-Null
}
[IO.File]::WriteAllText($taskOutputPath, ($taskReport | ConvertTo-Json -Depth 10) + "`n")
Write-Output "Saved $($taskRows.Count) O02 motion metric samples to $taskOutputPath"
if ($taskCaptureError) { Write-Warning $taskCaptureError }
