param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [string]$CameraAddress = 'http://192.168.1.124:8765',
    [int]$Samples = 60,
    [string]$OutputPath = 'work/t13-baseline.json',
    [string]$AdbPath = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
)
$ErrorActionPreference = 'Stop'
if ($Samples -lt 1 -or $Samples -gt 600) { throw 'Choose 1 to 600 samples.' }
& $AdbPath -s $CompanionSerial forward tcp:18768 tcp:8769
if ($LASTEXITCODE -ne 0) { throw 'Could not forward development metrics.' }
$taskSamples = @()
try {
    for ($i = 0; $i -lt $Samples; $i++) {
        $taskWatch = [Diagnostics.Stopwatch]::StartNew()
        $taskCamera = Invoke-RestMethod "$CameraAddress/status"
        $taskRtt = $taskWatch.Elapsed.TotalMilliseconds
        # Explicit projection: do not persist read/host/media credentials.
        $taskMetric = Invoke-RestMethod http://127.0.0.1:18768/metrics
        $taskSamples += [pscustomobject]@{ camera = [pscustomobject]@{
            state = $taskCamera.state; sessionId = $taskCamera.sessionId; revision = $taskCamera.revision
            timeline = $taskCamera.timeline; sourceTimeUs = $taskCamera.sourceTimeUs
            frames = $taskCamera.completedFrames; battery = $taskCamera.battery
            temperatureC = $taskCamera.temperatureC; clipCount = $taskCamera.clipCount; error = $taskCamera.error
            statusRttMs = $taskRtt
        }; companion = $taskMetric }
        Start-Sleep -Seconds 1
    }
    [pscustomobject]@{ kind = 'software pipeline estimates, not glass-to-glass'; samples = $taskSamples } |
        ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath
} finally { & $AdbPath -s $CompanionSerial forward --remove tcp:18768 }
