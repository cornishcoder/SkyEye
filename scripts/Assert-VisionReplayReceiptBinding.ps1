# Shared host-side check for reports from the receipt-hashing replay harness.
# A hash match binds bytes, not the correctness of a copied calibration or label.
function Assert-VisionReplayReceiptBinding {
    param(
        [Parameter(Mandatory)][object[]]$Inputs,
        [Parameter(Mandatory)][object]$Report
    )
    if ($Report.completed -ne $true -or $null -eq $Report.samples -or @($Report.samples).Count -ne $Inputs.Count) {
        throw 'Replay receipt binding requires a completed report with one sample per input.'
    }
    for ($i = 0; $i -lt $Inputs.Count; $i++) {
        $sample = $Report.samples[$i]
        $expected = $Inputs[$i]
        if ($null -eq $sample -or $null -eq $sample.frameId -or $null -eq $sample.sourceTimeUs -or
            $null -eq $sample.pixelSha256 -or $null -eq $sample.receiptSha256 -or
            [string]$sample.pixelSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
            [string]$sample.receiptSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
            [long]$sample.frameId -ne [long]$expected.Frame -or
            [long]$sample.sourceTimeUs -ne [long]$expected.Receipt.indexedFrame.sourceTimeUs -or
            [string]$sample.pixelSha256 -ne [string]$expected.Hash -or
            [string]$sample.receiptSha256 -ne [string]$expected.ReceiptHash) {
            throw "Replay receipt, pixels, frame or source PTS mismatch at input index $i; no bound manifest will be written."
        }
    }
}
