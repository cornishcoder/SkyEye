param(
    [Parameter(Mandatory)][string]$CameraSerial,
    [string]$AdbPath='C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
    [int]$IdleMinutes=0,
    [int]$ForwardPort=18765
)
$ErrorActionPreference='Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskResults = Join-Path $taskRoot ('work\device-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $taskResults | Out-Null
function Invoke-DeviceAdb([string[]]$Arguments) { & $AdbPath -s $CameraSerial @Arguments; if ($LASTEXITCODE -ne 0) { throw ('ADB failed: '+($Arguments -join ' ')) } }
Invoke-DeviceAdb @('get-state')
Invoke-DeviceAdb @('shell','getprop','ro.product.model')
Invoke-DeviceAdb @('shell','getprop','ro.build.version.release')
Invoke-DeviceAdb @('shell','getconf','PAGE_SIZE')
Invoke-DeviceAdb @('shell','pm','grant','uk.co.skyeye.camera','android.permission.CAMERA')
Invoke-DeviceAdb @('shell','pm','grant','uk.co.skyeye.camera','android.permission.RECORD_AUDIO')
Invoke-DeviceAdb @('shell','pm','grant','uk.co.skyeye.camera','android.permission.POST_NOTIFICATIONS')
Invoke-DeviceAdb @('shell','input','keyevent','224')
Invoke-DeviceAdb @('shell','wm','dismiss-keyguard')
Invoke-DeviceAdb @('shell','am','start','-S','-n','uk.co.skyeye.camera/uk.co.skyeye.camera.MainActivity','--ez','arm','true')
Invoke-DeviceAdb @('forward',"tcp:$ForwardPort",'tcp:8765')
$taskBase = "http://127.0.0.1:$ForwardPort"
$taskLease = $null
$taskHeartbeat = $null
try {
    $taskReady = $null
    for ($taskAttempt=0;$taskAttempt -lt 20;$taskAttempt++) {
        try { $taskReady=Invoke-RestMethod "$taskBase/status" -TimeoutSec 5; if ($taskReady.armed) { break } } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $taskReady.armed) { throw 'Camera did not arm.' }
    Invoke-DeviceAdb @('shell','input','keyevent','223')
    if ($IdleMinutes -gt 0) { Start-Sleep -Seconds ($IdleMinutes*60) }
    $taskLease=Invoke-RestMethod "$taskBase/host/claim" -Method Post -ContentType 'application/json' -Body '{"owner":"SkyEye device acceptance"}'
    $taskHeaders=@{'X-SkyEye-Host'=$taskLease.token}
    $taskHeartbeat = Start-Job -ScriptBlock { param($taskUrl,$taskToken) while ($true) { try { Invoke-RestMethod "$taskUrl/host/heartbeat" -Headers @{'X-SkyEye-Host'=$taskToken} -Method Post -ContentType 'application/json' -Body '{}' | Out-Null } catch { break }; Start-Sleep -Seconds 5 } } -ArgumentList $taskBase,$taskLease.token
    function Send-CameraCommand([string]$taskRoute,[int]$taskSeconds=0) {
        $taskBody=@{commandId=[guid]::NewGuid().ToString('N')}; if ($taskSeconds -gt 0) { $taskBody.seconds=$taskSeconds }
        Invoke-RestMethod "$taskBase/$taskRoute" -Headers $taskHeaders -Method Post -ContentType 'application/json' -Body ($taskBody|ConvertTo-Json)
    }
    $taskEvidence=@()
    foreach ($taskCycle in 1..3) {
        Send-CameraCommand 'session/start' | Out-Null
        Start-Sleep -Seconds 12
        Invoke-RestMethod "$taskBase/host/heartbeat" -Headers $taskHeaders -Method Post -ContentType 'application/json' -Body '{}' | Out-Null
        $taskStatus=Invoke-RestMethod "$taskBase/status"
        if ($taskStatus.state -ne 'Active' -or $taskStatus.historySeconds -lt 6) { throw 'Screen-off capture did not accumulate usable history.' }
        $taskClip=Send-CameraCommand 'clips/save' 30
        Invoke-WebRequest "$taskBase/clips/$($taskClip.id)?access=$($taskStatus.readToken)" -OutFile "$taskResults\cycle-$taskCycle.mp4"
        if ((Get-FileHash "$taskResults\cycle-$taskCycle.mp4" -Algorithm SHA256).Hash -ne $taskClip.sha256) { throw 'Downloaded clip hash mismatch.' }
        if (Get-Command ffprobe -ErrorAction SilentlyContinue) {
            $taskMedia = ffprobe -v error -show_entries format=duration:stream=codec_type,width,height -of json "$taskResults\cycle-$taskCycle.mp4" | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0 -or @($taskMedia.streams | Where-Object codec_type -eq 'video').Count -ne 1 -or @($taskMedia.streams | Where-Object codec_type -eq 'audio').Count -ne 1) { throw 'Saved replay must contain video and room-audio tracks.' }
            if ([Math]::Abs([double]$taskMedia.format.duration - [double]$taskClip.duration) -gt .15) { throw 'Reported replay duration differs from the saved media.' }
        }
        Send-CameraCommand 'session/pause' | Out-Null
        Start-Sleep -Seconds 2
        Send-CameraCommand 'session/resume' | Out-Null
        for ($taskAttempt=0;$taskAttempt -lt 20;$taskAttempt++) {
            Start-Sleep -Seconds 1
            $taskResumed=Invoke-RestMethod "$taskBase/status"
            if ($taskResumed.state -ne 'Active' -or $taskResumed.historySeconds -gt 0) { break }
        }
        if ($taskResumed.state -ne 'Active' -or $taskResumed.historySeconds -le 0) { throw 'Screen-off resume failed.' }
        Send-CameraCommand 'session/stop' | Out-Null
        $taskPower=Invoke-DeviceAdb @('shell','dumpsys','power')
        $taskScreenOff = ($taskPower -match 'mWakefulness=Asleep' -or $taskPower -match 'mInteractive=false')
        if (-not $taskScreenOff) { throw 'The camera display was not confirmed off.' }
        $taskEvidence+=@{cycle=$taskCycle;state='passed';historySeconds=$taskStatus.historySeconds;clipSeconds=$taskClip.duration;battery=$taskStatus.battery;temperatureC=$taskStatus.temperatureC;screenOff=$taskScreenOff}
        Invoke-RestMethod "$taskBase/host/heartbeat" -Headers $taskHeaders -Method Post -ContentType 'application/json' -Body '{}' | Out-Null
    }
    $taskEvidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$taskResults\acceptance.json"
    Write-Host "PASS repeated screen-off capture/control, clip save and verified transfer. Evidence: $taskResults"
    Write-Host 'ADB forwarding verifies the capture lifecycle. LAN playback, Cast, audio quality and endurance need separate tests.'
}
finally {
    if ($taskHeartbeat) { Stop-Job $taskHeartbeat; Remove-Job $taskHeartbeat }
    if ($taskLease) { try { Send-CameraCommand 'session/stop' | Out-Null; Invoke-RestMethod "$taskBase/host/release" -Headers $taskHeaders -Method Post -ContentType 'application/json' -Body '{}' | Out-Null } catch { } }
    & $AdbPath -s $CameraSerial forward --remove "tcp:$ForwardPort" | Out-Null
}
