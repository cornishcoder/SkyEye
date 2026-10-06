param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskAdb = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $taskOutput) { throw 'Choose a new evidence directory.' }
$taskMetadata = Get-Content -LiteralPath (Join-Path $FixtureDirectory 'decoded-frame-fixture.json') -Raw | ConvertFrom-Json
if (-not $taskMetadata.motionTexture -or
    (Get-FileHash -LiteralPath (Join-Path $FixtureDirectory 'decoded-frame-fixture.mp4')).Hash -ne $taskMetadata.sha256 -or
    (Get-FileHash -LiteralPath (Join-Path $FixtureDirectory 'decoded-frame-fixture.ts')).Hash -ne $taskMetadata.tsSha256) { throw 'Motion fixture identity differs.' }
function Invoke-TaskAdb([string[]]$Arguments) {
    $taskText = & $taskAdb -s $CompanionSerial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Device check failed: $($Arguments[0])" }
    return ($taskText -join "`n")
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $taskRoot 'tests/SkyEye.Vision.Device/SkyEye.Vision.Device.csproj') -c Release -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Motion proof build failed.' }
}
New-Item -ItemType Directory -Path $taskOutput | Out-Null
$taskApk = Join-Path $taskRoot 'tests/SkyEye.Vision.Device/bin/Release/net10.0-android/android-arm64/uk.co.skyeye.visionchecks-Signed.apk'
Invoke-TaskAdb @('install','-r',$taskApk) | Write-Output
foreach ($taskExtension in @('mp4','ts','json')) {
    Invoke-TaskAdb @('push',(Join-Path $FixtureDirectory "decoded-frame-fixture.$taskExtension"),"/sdcard/Android/data/uk.co.skyeye.visionchecks/files/decoded-frame-fixture.$taskExtension") | Out-Null
}
try {
    foreach ($taskProof in @(@('RoiMotionInstrumentation','roi-motion-proof.json'),@('MotionFeedInstrumentation','motion-feed-proof.json'))) {
        $taskExecution = Invoke-TaskAdb @('shell','am','instrument','-w',"uk.co.skyeye.visionchecks/uk.co.skyeye.visionchecks.$($taskProof[0])")
        if ($taskExecution -notmatch 'INSTRUMENTATION_CODE: -1') { throw 'Instrumentation did not complete normally.' }
        $taskReportPath = Join-Path $taskOutput $taskProof[1]
        Invoke-TaskAdb @('pull',"/sdcard/Android/data/uk.co.skyeye.visionchecks/files/$($taskProof[1])",$taskReportPath) | Out-Null
        $taskReport = Get-Content -LiteralPath $taskReportPath -Raw | ConvertFrom-Json
        if (-not $taskReport.passed) { throw "Native motion proof failed: $($taskReport.error)" }
        Write-Output "PASS $($taskProof[0])"
    }
    $taskProvenance = @{ schemaVersion=1; apkSha256=(Get-FileHash -LiteralPath $taskApk).Hash; fixture=$taskMetadata;
        scope='Synthetic native ROI cases and 640x360/30Hz loopback TS, short runs; not real-table/1080p sustained acceptance' }
    [IO.File]::WriteAllText((Join-Path $taskOutput 'provenance.json'), ($taskProvenance | ConvertTo-Json -Depth 8).Replace("`r`n","`n") + "`n")
}
finally { Invoke-TaskAdb @('shell','am','start','-n','uk.co.skyeye.companion/uk.co.skyeye.companion.MainActivity') | Out-Null }
