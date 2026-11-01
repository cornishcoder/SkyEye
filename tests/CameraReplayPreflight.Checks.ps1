$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/CameraReplayPreflight.ps1')

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$stopped = [pscustomobject]@{ state='Stopped' }
$readyIdle = [pscustomobject]@{
    state='Ready'; armed=$true; host=$null; historySeconds=0
    completedFrames=0; lastFrameSourceTimeUs=0; timeline=''
}
Assert (Test-CameraReplayIdleState $stopped $true) 'Stopped must admit screen replay.'
Assert (Test-CameraReplayIdleState $stopped $false) 'Stopped must admit non-screen replay.'
Assert (Test-CameraReplayIdleState $readyIdle $true) 'Fully evidenced Ready-idle state must admit screen-only replay.'
Assert (-not (Test-CameraReplayIdleState $readyIdle $false)) 'Ready must not admit chronological or live replay.'
$active = [pscustomobject]@{
    state='Active'; armed=$true; host=[pscustomobject]@{ Owner='Pixel 11' }; historySeconds=0.1
    completedFrames=1; lastFrameSourceTimeUs=1; timeline='active-timeline'
}
Assert (-not (Test-CameraReplayIdleState $active $true)) 'Active capture must be rejected.'
$owned = [pscustomobject]@{
    state='Ready'; armed=$true; host=[pscustomobject]@{ Owner='Pixel 11' }; historySeconds=0
    completedFrames=0; lastFrameSourceTimeUs=0; timeline=''
}
Assert (-not (Test-CameraReplayIdleState $owned $true)) 'A Ready state with an active host must be rejected.'
$history = [pscustomobject]@{
    state='Ready'; armed=$true; host=$null; historySeconds=0.1
    completedFrames=0; lastFrameSourceTimeUs=0; timeline=''
}
Assert (-not (Test-CameraReplayIdleState $history $true)) 'Ready state with recording history must be rejected.'
$frames = [pscustomobject]@{
    state='Ready'; armed=$true; host=$null; historySeconds=0
    completedFrames=1; lastFrameSourceTimeUs=0; timeline=''
}
Assert (-not (Test-CameraReplayIdleState $frames $true)) 'Ready state with completed frames must be rejected.'
$sourceTime = [pscustomobject]@{
    state='Ready'; armed=$true; host=$null; historySeconds=0
    completedFrames=0; lastFrameSourceTimeUs=1; timeline=''
}
Assert (-not (Test-CameraReplayIdleState $sourceTime $true)) 'Ready state with a source frame must be rejected.'
$unarmed = [pscustomobject]@{
    state='Ready'; armed=$false; host=$null; historySeconds=0
    completedFrames=0; lastFrameSourceTimeUs=0; timeline=''
}
Assert (-not (Test-CameraReplayIdleState $unarmed $true)) 'Unarmed Ready state must be rejected.'
$incomplete = [pscustomobject]@{ state='Ready'; armed=$true; host=$null }
Assert (-not (Test-CameraReplayIdleState $incomplete $true)) 'Ready without complete idle readback fields must be rejected.'
Write-Output 'PASS camera replay preflight: Stopped accepted; Ready accepted only with complete idle proof for screen-only; active/owned/history/frame states rejected.'
