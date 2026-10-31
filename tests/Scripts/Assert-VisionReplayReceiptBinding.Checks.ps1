$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
. (Join-Path $PSScriptRoot '../../scripts/Assert-VisionReplayReceiptBinding.ps1')

$inputs = @(
    [pscustomobject]@{ Frame = 10; Hash = 'a' * 64; ReceiptHash = 'b' * 64; Receipt = [pscustomobject]@{ indexedFrame = [pscustomobject]@{ sourceTimeUs = 100 } } },
    [pscustomobject]@{ Frame = 11; Hash = 'c' * 64; ReceiptHash = 'd' * 64; Receipt = [pscustomobject]@{ indexedFrame = [pscustomobject]@{ sourceTimeUs = 200 } } }
)
$valid = [pscustomobject]@{ completed = $true; samples = @(
    [pscustomobject]@{ frameId = 10; sourceTimeUs = 100; pixelSha256 = 'a' * 64; receiptSha256 = 'b' * 64; detectionRequested = $true },
    [pscustomobject]@{ frameId = 11; sourceTimeUs = 200; pixelSha256 = 'c' * 64; receiptSha256 = 'd' * 64; detectionRequested = $false }
) }
function Copy-Report { return $valid | ConvertTo-Json -Depth 8 | ConvertFrom-Json }
function Assert-Rejected($report, [string]$context) {
    try { Assert-VisionReplayReceiptBinding -Inputs $inputs -Report $report }
    catch { return }
    throw "Expected receipt binding rejection: $context"
}

Assert-VisionReplayReceiptBinding -Inputs $inputs -Report $valid
Assert-VisionReplayReceiptBinding -Inputs @($inputs[0]) -Report ([pscustomobject]@{ completed = $true; samples = @($valid.samples[0]) })
$upper = Copy-Report; $upper.samples[0].receiptSha256 = $upper.samples[0].receiptSha256.ToUpperInvariant(); Assert-VisionReplayReceiptBinding -Inputs $inputs -Report $upper
$bad = Copy-Report; $bad.samples[1].receiptSha256 = 'e' * 64; Assert-Rejected $bad 'receipt changed'
$bad = Copy-Report; $bad.samples[1].receiptSha256 = 'not-a-sha256'; Assert-Rejected $bad 'malformed receipt hash'
$bad = Copy-Report; $bad.samples[1].pixelSha256 = 'e' * 64; Assert-Rejected $bad 'pixels changed'
$bad = Copy-Report; $bad.samples[1].sourceTimeUs = 201; Assert-Rejected $bad 'PTS changed'
$bad = Copy-Report; $bad.samples[1].frameId = 12; Assert-Rejected $bad 'frame changed'
$bad = Copy-Report; $bad.samples = @($bad.samples[1], $bad.samples[0]); Assert-Rejected $bad 'sample order changed'
$bad = Copy-Report; $bad.samples[1].PSObject.Properties.Remove('receiptSha256'); Assert-Rejected $bad 'old report without receipt hash'
$bad = Copy-Report; $bad.samples = @($bad.samples[0]); Assert-Rejected $bad 'missing sample'
$bad = Copy-Report; $bad.completed = $false; Assert-Rejected $bad 'incomplete report'
Write-Output 'Replay receipt binding checks passed.'
