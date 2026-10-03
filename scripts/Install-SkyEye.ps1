param(
    [Parameter(Mandatory)][string]$CameraSerial,
    [Parameter(Mandatory)][string]$CompanionSerial,
    [string]$AdbPath='C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if ($CameraSerial -eq $CompanionSerial) { throw 'Camera and companion must be different devices.' }
foreach ($taskDevice in @($CameraSerial,$CompanionSerial)) {
    $taskState = & $AdbPath -s $taskDevice get-state 2>$null
    if ($LASTEXITCODE -ne 0 -or $taskState -ne 'device') { throw "$taskDevice is not connected/authorized. Connect or pair wireless debugging first." }
    $taskAbi = & $AdbPath -s $taskDevice shell getprop ro.product.cpu.abi
    if ($taskAbi.Trim() -ne 'arm64-v8a') { throw "Unsupported device ABI: $taskAbi" }
    $taskModel = & $AdbPath -s $taskDevice shell getprop ro.product.model
    Write-Host "$taskDevice · $taskModel"
}
Push-Location $taskRoot
try {
    if (-not $SkipBuild) { dotnet build SkyEye.sln -c Debug -m:1; if ($LASTEXITCODE -ne 0) { throw 'Build failed.' } }
    $taskCameraApk = Join-Path $taskRoot 'src\SkyEye.Camera\bin\Debug\net10.0-android\android-arm64\uk.co.skyeye.camera-Signed.apk'
    $taskCompanionApk = Join-Path $taskRoot 'src\SkyEye.Companion\bin\Debug\net10.0-android\android-arm64\uk.co.skyeye.companion-Signed.apk'
    foreach ($taskApk in @($taskCameraApk,$taskCompanionApk)) { if (-not (Test-Path -LiteralPath $taskApk -PathType Leaf)) { throw "APK missing: $taskApk" } }
    & $AdbPath -s $CameraSerial install -r $taskCameraApk; if ($LASTEXITCODE -ne 0) { throw 'Camera installation failed; existing data retained.' }
    & $AdbPath -s $CompanionSerial install -r $taskCompanionApk; if ($LASTEXITCODE -ne 0) { throw 'Companion installation failed; existing data retained.' }
    & $AdbPath -s $CameraSerial shell am start -n uk.co.skyeye.camera/uk.co.skyeye.camera.MainActivity
    & $AdbPath -s $CompanionSerial shell am start -n uk.co.skyeye.companion/uk.co.skyeye.companion.MainActivity
    Write-Host 'Installed. Arm the camera once, then discover it and claim host control in SkyEye.'
}
finally { Pop-Location }
