param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [Parameter(Mandatory)][string]$FixtureDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskAdb = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
$taskPackage = 'uk.co.skyeye.visionchecks'
$taskDeviceRoot = "/sdcard/Android/data/$taskPackage/files"
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path (Join-Path $taskOutput 'decoded-frame-proof.json')) { throw 'Evidence exists; choose a new output directory.' }
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
function Invoke-TaskAdb([string[]]$Arguments) {
    $taskText = & $taskAdb -s $CompanionSerial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Device check failed: $($Arguments[0])" }
    return ($taskText -join "`n")
}
if (-not $SkipBuild) {
    & dotnet build (Join-Path $taskRoot 'tests/SkyEye.Vision.Device/SkyEye.Vision.Device.csproj') -c Release -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Pairing proof build failed.' }
}
$taskApk = Join-Path $taskRoot 'tests/SkyEye.Vision.Device/bin/Release/net10.0-android/android-arm64/uk.co.skyeye.visionchecks-Signed.apk'
$taskMetadata = Get-Content (Join-Path $FixtureDirectory 'decoded-frame-fixture.json') -Raw | ConvertFrom-Json
if ((Get-FileHash (Join-Path $FixtureDirectory 'decoded-frame-fixture.mp4')).Hash -ne $taskMetadata.sha256 -or
    (Get-FileHash (Join-Path $FixtureDirectory 'decoded-frame-fixture.ts')).Hash -ne $taskMetadata.tsSha256) { throw 'Fixture provenance differs.' }
Invoke-TaskAdb @('install','-r',$taskApk) | Write-Output
Invoke-TaskAdb @('shell','mkdir','-p',$taskDeviceRoot) | Out-Null
foreach ($taskExtension in @('mp4','ts','json')) {
    Invoke-TaskAdb @('push',(Join-Path $FixtureDirectory "decoded-frame-fixture.$taskExtension"),"$taskDeviceRoot/decoded-frame-fixture.$taskExtension") | Out-Null
}
$taskRun = [Guid]::NewGuid().ToString('N')
try {
    Invoke-TaskAdb @('shell','am','start','-n',"$taskPackage/uk.co.skyeye.companion.nativebridge.DecodedFrameProofActivity",'--es','proof-run',$taskRun) | Out-Null
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 1000
        $taskJson = & $taskAdb -s $CompanionSerial shell cat "$taskDeviceRoot/decoded-frame-proof.json" 2>$null
        if ($LASTEXITCODE -eq 0) {
            try { $taskResult = ($taskJson -join "`n") | ConvertFrom-Json } catch { $taskResult = $null }
            if ($taskResult.runId -eq $taskRun) { break }
        }
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    if ($taskResult.runId -ne $taskRun) { throw 'No current device proof report; ensure the phone is unlocked.' }
    $taskResult | Add-Member -NotePropertyName apkSha256 -NotePropertyValue (Get-FileHash $taskApk).Hash
    $taskResult | Add-Member -NotePropertyName device -NotePropertyValue (Invoke-TaskAdb @('shell','getprop','ro.product.model')).Trim()
    $taskResult | Add-Member -NotePropertyName fixtureProvenance -NotePropertyValue $taskMetadata
    [IO.File]::WriteAllText((Join-Path $taskOutput 'decoded-frame-proof.json'), ($taskResult | ConvertTo-Json -Depth 15).Replace("`r`n","`n") + "`n", [Text.UTF8Encoding]::new($false))
    if (-not $taskResult.passed) { throw "Native pairing failed: $($taskResult.error)" }
    Write-Output "PASS physical native pairing: $($taskResult.samples.Count) pixel-ID/PTS matches, stop/restart and live TS source clock"
    Invoke-TaskAdb @('shell','am','instrument','-w',"$taskPackage/uk.co.skyeye.visionchecks.AnnotationClockInstrumentation") | Out-Null
    Invoke-TaskAdb @('pull',"$taskDeviceRoot/annotation-clock-checks.json",(Join-Path $taskOutput 'annotation-clock-checks.json')) | Out-Null
    $taskClock = Get-Content (Join-Path $taskOutput 'annotation-clock-checks.json') -Raw | ConvertFrom-Json
    if (-not $taskClock.passed -or -not $taskClock.exactSurfaceClockChecks) { throw 'Native exact TS epoch/clock proof failed.' }
    Write-Output 'PASS exact native source-clock mapping, TS wrap cases and unrelated receipt/generation rejection'
    Invoke-TaskAdb @('shell','am','instrument','-w',"$taskPackage/uk.co.skyeye.visionchecks.DecodedFrameBindingInstrumentation") | Out-Null
    Invoke-TaskAdb @('pull',"$taskDeviceRoot/decoded-frame-binding.json",(Join-Path $taskOutput 'decoded-frame-binding.json')) | Out-Null
    $taskBinding = Get-Content (Join-Path $taskOutput 'decoded-frame-binding.json') -Raw | ConvertFrom-Json
    if (-not $taskBinding.passed) { throw "C# pairing handoff failed: $($taskBinding.error)" }
    Write-Output 'PASS physical C# frame adapter: 10 raw-bitmap/source-PTS matches and cancelled acquisition rejection'
}
finally {
    Invoke-TaskAdb @('shell','am','start','-n','uk.co.skyeye.companion/uk.co.skyeye.companion.MainActivity') | Out-Null
}
