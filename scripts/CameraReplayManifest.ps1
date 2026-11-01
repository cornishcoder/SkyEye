function New-CameraReplaySourceFrameManifestEntries {
    param([Parameter(Mandatory)][object[]]$Inputs)

    foreach ($item in $Inputs) {
        [pscustomobject]@{
            frameId = $item.Receipt.indexedFrame.frameId
            pixelFile = [IO.Path]::GetFileName($item.PixelPath)
            receipt = [IO.Path]::GetFileName($item.JsonPath)
            receiptArtifact = 'source-receipts/' + [IO.Path]::GetFileName($item.JsonPath)
            receiptSha256 = $item.ReceiptHash
            sha256 = $item.Hash
        }
    }
}
