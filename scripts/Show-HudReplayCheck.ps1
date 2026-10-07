param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [string]$AdbPath = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPackage = 'uk.co.skyeye.visionchecks'
$taskApk = Join-Path $taskRoot 'tests/SkyEye.Vision.Device/bin/Release/net10.0-android/android-arm64/uk.co.skyeye.visionchecks-Signed.apk'
$taskProof = Join-Path $taskRoot 'work/v01-hud-proof'
function Invoke-TaskAdb([string[]]$Arguments) {
    $taskOutput = & $AdbPath -s $CompanionSerial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($taskOutput -join "`n") }
    return ($taskOutput -join "`n").Trim()
}
if (-not (Test-Path -LiteralPath $taskApk -PathType Leaf)) { throw 'Build the isolated SkyEye.Vision.Device Release APK first.' }
$taskAapt = Join-Path $taskRoot 'work/android-sdk-11/build-tools/36.0.0/aapt2.exe'
$taskManifest = & $taskAapt dump badging $taskApk 2>&1
if ($LASTEXITCODE -ne 0 -or ($taskManifest -join "`n") -notmatch "package: name='uk.co.skyeye.visionchecks'") { throw 'Expected the isolated vision-check APK.' }
$taskReport = Get-Content -LiteralPath (Join-Path $taskProof 'hud-export-proof.json') -Raw | ConvertFrom-Json
if (-not $taskReport.passed) { throw 'HUD export proof did not pass.' }
foreach ($taskResult in $taskReport.outputs) {
    $taskFile = Join-Path $taskProof $taskResult.output
    if ((Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash -ne $taskResult.outputSha256) { throw 'Export hash mismatch.' }
}
Invoke-TaskAdb @('install','--no-incremental','-r',$taskApk) | Write-Host
$taskExternal = "/sdcard/Android/data/$taskPackage/files"
Invoke-TaskAdb @('push',(Join-Path $taskProof 'hud-export-phone.mp4'),"$taskExternal/rendered-portrait.mp4") | Out-Null
Invoke-TaskAdb @('push',(Join-Path $taskProof 'hud-export-tv.mp4'),"$taskExternal/rendered-landscape.mp4") | Out-Null
Invoke-TaskAdb @('forward','tcp:18769','tcp:8769') | Out-Null
Invoke-TaskAdb @('shell','am','start','-n',"$taskPackage/uk.co.skyeye.visionchecks.RecordedRenderPreviewActivity") | Write-Host
Write-Host 'On the phone, tap Cast, choose the TV, then Play corrected clip on TV.'
Write-Host 'The HUD is present at 0-2 seconds, clears at 2-3 seconds, and returns at 3-6 seconds.'
Write-Host 'This check installs only the isolated test package and uses the verified prerecorded exports.'
