param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$AdbPath = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
    [string]$BuildToolsDirectory,
    [string]$ApkPath,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPackage = 'uk.co.skyeye.companion'
if (-not $BuildToolsDirectory) { $BuildToolsDirectory = Join-Path $taskRoot 'work/android-sdk-11/build-tools/36.0.0' }
if (-not $ApkPath) { $ApkPath = Join-Path $taskRoot "work/production/bin/$Configuration/net11.0-android/android-arm64/$taskPackage-Signed.apk" }
if ($ApkPath -and -not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build-SkyEye.ps1') -Target Production -Configuration $Configuration }
function Invoke-TaskAdb([string[]]$Arguments) {
    $taskResult = & $AdbPath -s $CompanionSerial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Device operation failed: $($Arguments[0])" }
    return ($taskResult -join "`n").Trim()
}
function Get-TaskCertificate([string]$Path) {
    $taskResult = & (Join-Path $BuildToolsDirectory 'apksigner.bat') verify --print-certs $Path 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
    $taskDigests = @($taskResult | Where-Object { $_ -match '^Signer #\d+ certificate SHA-256 digest: ([0-9a-fA-F]{64})$' } | ForEach-Object { ([string]$_ -split ': ')[-1].ToUpperInvariant() })
    if ($taskDigests.Count -ne 1) { throw 'Expected one signing certificate. Inspect signer rotation before updating.' }
    return $taskDigests[0]
}
function Get-TaskIdentity {
    $taskUid = Invoke-TaskAdb -Arguments @('shell','cmd','package','list','packages','-U',$taskPackage)
    if ($taskUid -notmatch "(?m)^package:$([regex]::Escape($taskPackage)) uid:(\d+)\r?$" ) { throw 'Production app UID unavailable.' }
    $taskUidValue = $Matches[1]
    $taskDump = Invoke-TaskAdb -Arguments @('shell','dumpsys','package',$taskPackage)
    if ($taskDump -notmatch 'ceDataInode=(\d+) deDataInode=(\d+)') { throw 'Production storage identity unavailable.' }
    return "$taskUidValue/$($Matches[1])/$($Matches[2])"
}
if ((Invoke-TaskAdb -Arguments @('get-state')) -ne 'device') { throw 'Companion is not authorized.' }
if ((Invoke-TaskAdb -Arguments @('shell','getprop','ro.product.cpu.abi')) -ne 'arm64-v8a') { throw 'ARM64 companion required.' }
if (-not (Test-Path -LiteralPath $ApkPath -PathType Leaf)) { throw 'Signed production APK missing.' }
$taskBadging = & (Join-Path $BuildToolsDirectory 'aapt2.exe') dump badging $ApkPath 2>&1
if ($LASTEXITCODE -ne 0) { throw 'APK manifest inspection failed.' }
$taskBadgingText = $taskBadging -join "`n"
if ($taskBadgingText -notmatch "package: name='$([regex]::Escape($taskPackage))' versionCode='(\d+)'") { throw 'This is not the production companion APK.' }
$taskNewVersion = [long]$Matches[1]
if ($taskBadgingText -notmatch "launchable-activity: name='uk.co.skyeye.companion.MainActivity'") { throw 'Stable production launcher missing.' }
$taskManifest = & (Join-Path $BuildToolsDirectory 'aapt2.exe') dump xmltree --file AndroidManifest.xml $ApkPath 2>&1
if ($LASTEXITCODE -ne 0) { throw 'APK component inspection failed.' }
$taskManifestText = $taskManifest -join "`n"
foreach ($taskContract in @('uk.co.skyeye.companion.files', 'crc648b11b8ca3be5d4a4.VoiceService')) {
    if (-not $taskManifestText.Contains('"' + $taskContract + '"')) { throw "Stable component missing: $taskContract" }
}
$taskCurrent = Invoke-TaskAdb -Arguments @('shell','pm','path',$taskPackage)
if ($taskCurrent -notmatch '(?m)^package:(.+/base.apk)\r?$') { throw 'An installed production app is required for this in-place migration.' }
$taskRemoteApk = $Matches[1].Trim()
$taskInstalled = Invoke-TaskAdb -Arguments @('shell','dumpsys','package',$taskPackage)
if ($taskInstalled -notmatch 'versionCode=(\d+)') { throw 'Installed version unavailable.' }
if ($taskNewVersion -lt [long]$Matches[1]) { throw 'Refusing a version downgrade; build rollback source with a higher version instead.' }
$taskBefore = Get-TaskIdentity
$taskBackupDir = Join-Path $taskRoot "work/production/backups/$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Path $taskBackupDir -Force | Out-Null
$taskBackup = Join-Path $taskBackupDir 'before.apk'
Invoke-TaskAdb -Arguments @('pull',$taskRemoteApk,$taskBackup) | Out-Null
if ((Get-TaskCertificate $taskBackup) -ne (Get-TaskCertificate $ApkPath)) { throw 'Signing certificates differ. No installation attempted; do not uninstall or clear data.' }
Invoke-TaskAdb -Arguments @('install','--no-incremental','-r',$ApkPath) | Out-Null
if ((Get-TaskIdentity) -ne $taskBefore) { throw 'Storage identity changed unexpectedly; preserve the backup and inspect before launch.' }
Invoke-TaskAdb -Arguments @('shell','am','force-stop',$taskPackage) | Out-Null
Invoke-TaskAdb -Arguments @('shell','am','start','-n',"$taskPackage/uk.co.skyeye.companion.MainActivity") | Out-Null
Write-Host "SkyEye updated in place. UID and private storage retained. Previous APK: $taskBackup"
Write-Host 'The camera installation and other SkyEye packages were not changed.'

