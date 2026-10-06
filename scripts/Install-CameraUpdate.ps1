param(
    [Parameter(Mandatory)][string]$CameraSerial,
    [string]$CameraAddress,
    [string]$AdbPath='C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$taskPackage='uk.co.skyeye.camera'
$taskApk=Join-Path $taskRoot 'src/SkyEye.Camera/bin/Release/net10.0-android/android-arm64/uk.co.skyeye.camera-Signed.apk'
$taskTools=Join-Path $taskRoot 'work/android-sdk-11/build-tools/36.0.0'
function Invoke-CameraAdb([string[]]$Arguments) {
    $taskResult=& $AdbPath -s $CameraSerial @Arguments 2>&1
    if($LASTEXITCODE -ne 0){throw "Camera operation failed: $($Arguments[0])"}
    return ($taskResult -join "`n").Trim()
}
function CameraIdentity {
    $taskDump=Invoke-CameraAdb @('shell','dumpsys','package',$taskPackage)
    $taskUid=Invoke-CameraAdb @('shell','cmd','package','list','packages','-U',$taskPackage)
    if($taskUid -notmatch 'uid:(\d+)'){throw 'Camera UID unavailable'}
    $taskUidValue=$Matches[1]
    if($taskDump -notmatch 'ceDataInode=(\d+) deDataInode=(\d+)'){throw 'Camera storage identity unavailable'}
    return "$taskUidValue/$($Matches[1])/$($Matches[2])"
}
function CameraCertificate([string]$Path) {
    $taskSignature=& (Join-Path $taskTools 'apksigner.bat') verify --print-certs $Path 2>&1
    if($LASTEXITCODE -ne 0){throw 'Camera APK signature verification failed'}
    $taskCerts=@($taskSignature | Where-Object { $_ -match '^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})$' } | ForEach-Object { ([string]$_ -split ': ')[-1] })
    if($taskCerts.Count -ne 1){throw 'Expected one camera signing certificate'}
    return $taskCerts[0]
}
if((Invoke-CameraAdb @('get-state')) -ne 'device'){throw 'Camera debugging is not connected'}
# Updating a running capture would erase rolling history. Wait for an explicit
# stopped camera rather than silently stopping recording during installation.
$taskPid=& $AdbPath -s $CameraSerial shell pidof $taskPackage 2>$null
if($taskPid){
    if(-not $CameraAddress){throw 'Camera app is running. Supply its address to verify recording is stopped before updating.'}
    $taskStatus=Invoke-RestMethod -Uri ($CameraAddress.TrimEnd('/') + '/status') -TimeoutSec 5
    if($taskStatus.state -notin @('Ready','Stopped')){throw 'Recording is active or paused. Stop recording before updating the camera.'}
    Invoke-CameraAdb @('shell','am','force-stop',$taskPackage) | Out-Null
}
$taskManifest=& (Join-Path $taskTools 'aapt2.exe') dump badging $taskApk 2>&1
if($LASTEXITCODE -ne 0 -or ($taskManifest -join "`n") -notmatch "package: name='uk.co.skyeye.camera' versionCode='(\d+)'"){throw 'Wrong camera APK'}
$taskVersion=[long]$Matches[1]
$taskInstalled=Invoke-CameraAdb @('shell','dumpsys','package',$taskPackage)
if($taskInstalled -notmatch 'versionCode=(\d+)' -or $taskVersion -lt [long]$Matches[1]){throw 'Refusing camera version downgrade'}
$taskRemote=Invoke-CameraAdb @('shell','pm','path',$taskPackage)
if($taskRemote -notmatch '(?m)^package:(.+/base.apk)\r?$'){throw 'Installed camera APK unavailable'}
$taskRemoteApk=$Matches[1].Trim()
$taskBefore=CameraIdentity
$taskBackupDirectory=Join-Path $taskRoot "work/camera/backups/$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Path $taskBackupDirectory -Force | Out-Null
$taskBackup=Join-Path $taskBackupDirectory 'before.apk'
Invoke-CameraAdb @('pull',$taskRemoteApk,$taskBackup) | Out-Null
if((CameraCertificate $taskBackup) -ne (CameraCertificate $taskApk)){throw 'Camera signing certificates differ; no update attempted. Do not uninstall or clear data.'}
Invoke-CameraAdb @('install','--no-incremental','-r',$taskApk) | Out-Null
if((CameraIdentity) -ne $taskBefore){throw 'Camera storage identity changed unexpectedly; preserve backup and inspect'}
Invoke-CameraAdb @('shell','am','start','-n',"$taskPackage/uk.co.skyeye.camera.MainActivity") | Out-Null
Write-Host "Camera updated in place; UID/private storage retained. Backup: $taskBackup"
