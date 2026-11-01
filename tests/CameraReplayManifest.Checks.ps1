$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../scripts/CameraReplayManifest.ps1')

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function New-TestInput([int]$frameId) {
    [pscustomobject]@{
        Receipt = [pscustomobject]@{ indexedFrame = [pscustomobject]@{ frameId = $frameId } }
        PixelPath = "C:\replay\$frameId.png"
        JsonPath = "C:\replay\$frameId.json"
        ReceiptHash = ('{0:x64}' -f $frameId)
        Hash = ('{0:x64}' -f ($frameId + 1))
    }
}

foreach ($inputCount in @(1, 2)) {
    $inputs = @(1..$inputCount | ForEach-Object { New-TestInput $_ })
    $entries = @(New-CameraReplaySourceFrameManifestEntries -Inputs $inputs)
    $json = ([pscustomobject]@{ sourceFrames = $entries } | ConvertTo-Json -Depth 8 -Compress)
    $parsed = $json | ConvertFrom-Json -AsHashtable
    Assert ($parsed.sourceFrames -is [array]) "A $inputCount-frame manifest must serialize sourceFrames as an array."
    Assert ($parsed.sourceFrames.Count -eq $inputCount) "A $inputCount-frame manifest lost source-frame entries."
    Assert ($parsed.sourceFrames[0].frameId -eq 1) 'The source frame identity was not preserved.'
    Assert ($parsed.sourceFrames[0].receiptArtifact -eq 'source-receipts/1.json') 'The receipt artifact mapping was not preserved.'
    Assert ($parsed.sourceFrames[0].receiptSha256 -eq ('{0:x64}' -f 1)) 'The receipt hash was not preserved.'
    Assert ($parsed.sourceFrames[0].sha256 -eq ('{0:x64}' -f 2)) 'The pixel hash was not preserved.'
}

Write-Output 'PASS replay manifest serialization: one- and two-frame sourceFrames remain arrays with receipt and pixel provenance.'
