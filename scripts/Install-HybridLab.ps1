param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$AdbPath = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskState = & $AdbPath -s $CompanionSerial get-state 2>$null
if ($LASTEXITCODE -ne 0 -or $taskState -ne 'device') { throw 'Companion is not connected/authorized.' }
$taskAbi = (& $AdbPath -s $CompanionSerial shell getprop ro.product.cpu.abi).Trim()
if ($LASTEXITCODE -ne 0 -or $taskAbi -ne 'arm64-v8a') { throw 'The lab APK requires an ARM64 companion.' }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build-SkyEye.ps1') -Target Hybrid -Configuration $Configuration }
$taskApk = Join-Path $taskRoot "src\SkyEye.Companion.Hybrid\bin\$Configuration\net11.0-android\android-arm64\uk.co.skyeye.companion.lab-Signed.apk"
if (-not (Test-Path -LiteralPath $taskApk -PathType Leaf)) { throw "Lab APK missing: $taskApk" }
& $AdbPath -s $CompanionSerial shell am force-stop uk.co.skyeye.companion.lab
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the previous lab process.' }
& $AdbPath -s $CompanionSerial install --no-incremental -r $taskApk
if ($LASTEXITCODE -ne 0) { throw 'Lab installation failed.' }
& $AdbPath -s $CompanionSerial shell am start -n uk.co.skyeye.companion.lab/uk.co.skyeye.companion.lab.MainActivity
if ($LASTEXITCODE -ne 0) { throw 'Lab launch failed.' }
Write-Host 'Opened SkyEye Lab; the production SkyEye and camera installations are separate.'
