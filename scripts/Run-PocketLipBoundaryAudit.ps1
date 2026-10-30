param(
    [string]$SourceRelativePath = 'work/production/evidence/live-detect-pocket-lip-20261030/o08-capture',
    [int[]]$Frames = @(399, 433),
    [string]$RunPrefix = 'pocketlip30',
    [string]$Purpose = 'Current-code exact-frame candidate and boundary audit for the Oct 30 pocket-lip fixture.'
)

$ErrorActionPreference = 'Stop'

$adb = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
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
$remoteFilesRoot = "/sdcard/Android/data/$package/files"
$remoteRunDirectory = "$remoteFilesRoot/o08-replay-$run"
$evidenceRoot = Join-Path $repoRoot 'work/production/evidence/live-detect-pocket-lip-20261030/device-replays'
$runDirectory = Join-Path $evidenceRoot $run

if (-not (Test-Path -LiteralPath $adb)) { throw "ADB was not found at $adb." }
if (-not (Test-Path -LiteralPath $apk)) { throw "Replay APK was not found at $apk." }
$cameraBefore = Invoke-RestMethod -Uri $cameraStatusUri -TimeoutSec 5
if ($cameraBefore.state -ne 'Stopped') { throw "Mounted camera must be stopped; state is $($cameraBefore.state)." }

$devices = @(& $adb devices -l | Where-Object { $_ -match '^\S+\s+device\s+.*model:Pixel_11_Pro_XL(?:\s|$)' })
if ($LASTEXITCODE -ne 0 -or $devices.Count -ne 1 -or ($devices[0] -split '\s+')[0] -ne $serial) {
    throw 'Expected the already-connected Pixel 11 Pro XL with serial 66181FDDV001HS.'
}
$packagePath = & $adb -s $serial shell pm path $package 2>&1
if ($LASTEXITCODE -ne 0 -or ($packagePath -join "`n") -notmatch 'package:') {
    throw 'The isolated Vision Device package is not installed; Production and camera packages were not targeted.'
}

$inputs = foreach ($frame in $frames) {
    $jsonPath = Join-Path $sourceRoot "$frame.json"
    $pngPath = Join-Path $sourceRoot "$frame.png"
    if (-not (Test-Path -LiteralPath $jsonPath) -or -not (Test-Path -LiteralPath $pngPath)) {
        throw "Missing captured source frame $frame."
    }
    $receipt = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    $actualHash = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne [string]$receipt.indexedFrame.pixelSha256) {
        throw "Source frame $frame does not match its receipt hash."
    }
    [pscustomobject]@{ Frame = $frame; JsonPath = $jsonPath; PngPath = $pngPath; Hash = $actualHash; Receipt = $receipt }
}

New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$installOutput = & $adb -s $serial install -r $apk 2>&1
if ($LASTEXITCODE -ne 0 -or ($installOutput -join "`n") -notmatch 'Success') {
    throw "Could not update the isolated Vision Device package in place: $($installOutput -join ' ')"
}
$mkdirOutput = & $adb -s $serial shell mkdir -p $remoteRunDirectory 2>&1
if ($LASTEXITCODE -ne 0) { throw "Could not create isolated replay folder: $mkdirOutput" }
foreach ($input in $inputs) {
    foreach ($file in @($input.JsonPath, $input.PngPath)) {
        $pushOutput = & $adb -s $serial push $file "$remoteRunDirectory/$([IO.Path]::GetFileName($file))" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Transfer failed for $file`: $pushOutput" }
    }
}

$instrumentOutput = & $adb -s $serial shell am instrument -w -e run $run -e audit 1 $instrumentation 2>&1
$instrumentExit = $LASTEXITCODE
$instrumentOutput | Set-Content -LiteralPath (Join-Path $runDirectory 'instrumentation.log')
if ($instrumentExit -ne 0) { throw "Boundary audit instrumentation failed: $($instrumentOutput -join ' ')" }

$reportName = "o08-replay-proof-$run.json"
$localReport = Join-Path $runDirectory $reportName
$pullOutput = & $adb -s $serial pull "$remoteFilesRoot/$reportName" $localReport 2>&1
if ($LASTEXITCODE -ne 0) { throw "Could not retrieve the replay report: $pullOutput" }
$report = Get-Content -LiteralPath $localReport -Raw | ConvertFrom-Json
if (-not $report.completed -or @($report.samples).Count -ne $frames.Count) {
    throw "Replay report is incomplete: $localReport"
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
    auditEnabled = $true
    sourceFrames = $inputs | ForEach-Object {
        [pscustomobject]@{ frameId = $_.Receipt.indexedFrame.frameId; png = [IO.Path]::GetFileName($_.PngPath); receipt = [IO.Path]::GetFileName($_.JsonPath); sha256 = $_.Hash }
    }
    report = $reportName
    reportSha256 = (Get-FileHash -LiteralPath $localReport -Algorithm SHA256).Hash.ToLowerInvariant()
    cameraBefore = $cameraBefore
    cameraAfter = $cameraAfter
}
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $runDirectory 'manifest.json')
Write-Output "Boundary audit saved under $runDirectory"
