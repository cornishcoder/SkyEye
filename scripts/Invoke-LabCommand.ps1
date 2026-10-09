param(
    [Parameter(Mandatory)][string]$CompanionSerial,
    [Parameter(Mandatory)][string]$Command,
    [hashtable]$Arguments = @{},
    [string]$AdbPath = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
)
$ErrorActionPreference = 'Stop'
$taskLocalPort = 18768
$taskRemotePort = 8769
$taskAddress = "http://127.0.0.1:$taskLocalPort"

$taskDeviceState = & $AdbPath -s $CompanionSerial get-state 2>$null
if ($LASTEXITCODE -ne 0 -or $taskDeviceState -ne 'device') { throw 'Companion is not connected and authorized.' }

$taskForwardRows = @(& $AdbPath -s $CompanionSerial forward --list)
$taskForward = $taskForwardRows | Where-Object { $_ -match "^$([regex]::Escape($CompanionSerial))\s+tcp:$taskLocalPort\s+tcp:(\d+)$" }
$taskOwnsForward = $false
if ($taskForward) {
    if ($taskForward -notmatch "tcp:$taskRemotePort$") { throw "Local port $taskLocalPort is already forwarded elsewhere." }
} else {
    & $AdbPath -s $CompanionSerial forward --no-rebind "tcp:$taskLocalPort" "tcp:$taskRemotePort" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not reserve local port $taskLocalPort for the Lab command API." }
    $taskOwnsForward = $true
}

try {
    $taskPing = Invoke-RestMethod -Method Get -Uri "$taskAddress/ping" -TimeoutSec 5
    if (-not $taskPing.diagnostic -or $taskPing.package -ne 'uk.co.skyeye.companion.lab') {
        throw 'The connected Lab app is not running its opt-in performance build. Build/install with -Performance first.'
    }
    if ($Command -eq 'status') {
        $taskResult = Invoke-RestMethod -Method Get -Uri "$taskAddress/control/status" -TimeoutSec 5
    } elseif ($Command -eq 'commands') {
        $taskResult = Invoke-RestMethod -Method Get -Uri "$taskAddress/control/commands" -TimeoutSec 5
    } elseif ($Command -eq 'scenario') {
        $taskResult = Invoke-RestMethod -Method Get -Uri "$taskAddress/control/scenario" -TimeoutSec 5
    } elseif ($Command -eq 'label-frame') {
        $taskResult = Invoke-RestMethod -Method Get -Uri "$taskAddress/control/label-frame" -TimeoutSec 5
    } else {
        $taskBody = @{ command = $Command; arguments = $Arguments } | ConvertTo-Json -Depth 20 -Compress
        try {
            $taskResult = Invoke-RestMethod -Method Post -Uri "$taskAddress/control/command" -ContentType 'application/json' -Body $taskBody -TimeoutSec 30
        } catch {
            $taskErrorBody = $_.ErrorDetails.Message
            if ([string]::IsNullOrWhiteSpace($taskErrorBody)) { throw }
            try { $taskResult = $taskErrorBody | ConvertFrom-Json } catch { throw }
        }
    }
    $taskResult | ConvertTo-Json -Depth 10
} finally {
    if ($taskOwnsForward) { & $AdbPath -s $CompanionSerial forward --remove "tcp:$taskLocalPort" | Out-Null }
}
