function Test-CameraReplayIdleState([object]$Status, [bool]$ScreenOnly) {
    if ($null -eq $Status) { return $false }
    $stateProperty = $Status.PSObject.Properties['state']
    if ($null -eq $stateProperty) { return $false }
    if ([string]$stateProperty.Value -eq 'Stopped') { return $true }
    if (-not $ScreenOnly -or [string]$stateProperty.Value -ne 'Ready') { return $false }

    # Ready is the camera service's initial, non-capturing state. Accept it only
    # for a saved-frame screen replay and only with explicit idle evidence. The
    # live/chronological replay modes continue to require Stopped.
    foreach ($name in @('armed', 'host', 'historySeconds', 'completedFrames', 'lastFrameSourceTimeUs', 'timeline')) {
        if ($null -eq $Status.PSObject.Properties[$name]) { return $false }
    }
    return (
        $Status.armed -eq $true -and
        $null -eq $Status.host -and
        [double]$Status.historySeconds -eq 0 -and
        [long]$Status.completedFrames -eq 0 -and
        [long]$Status.lastFrameSourceTimeUs -eq 0 -and
        [string]::IsNullOrEmpty([string]$Status.timeline)
    )
}
