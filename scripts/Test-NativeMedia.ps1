param([string]$JavaHome='C:\Program Files\Android\openjdk\jdk-21.0.8')
$ErrorActionPreference='Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskMedia = Join-Path $taskRoot 'work\native-media'
New-Item -ItemType Directory -Force $taskMedia | Out-Null
& "$JavaHome\bin\javac.exe" -d $taskMedia "$taskRoot\src\SkyEye.Camera\TsWriter.java" "$taskRoot\tests\native\TsFixture.java"
if ($LASTEXITCODE -ne 0) { throw 'Native TS fixture compilation failed.' }
& ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=128x72:rate=30:duration=2' -c:v libx264 -pix_fmt yuv420p -bf 0 -x264-params 'aud=1:repeat-headers=1:keyint=30:scenecut=0' -f h264 -y "$taskMedia\source.h264"
if ($LASTEXITCODE -ne 0) { throw 'Fixture encode failed.' }
foreach ($taskFormat in @('annex','avcc')) {
    & "$JavaHome\bin\java.exe" -cp $taskMedia uk.co.skyeye.camera.nativebridge.TsFixture "$taskMedia\source.h264" "$taskMedia\$taskFormat.ts" $taskFormat
    if ($LASTEXITCODE -ne 0) { throw 'TS mux failed.' }
    & ffmpeg -hide_banner -loglevel error -i "$taskMedia\$taskFormat.ts" -f framemd5 -y "$taskMedia\$taskFormat.md5"
    if ($LASTEXITCODE -ne 0) { throw 'TS decode failed.' }
}
& ffmpeg -hide_banner -loglevel error -i "$taskMedia\source.h264" -f framemd5 -y "$taskMedia\source.md5"
if ($LASTEXITCODE -ne 0) { throw 'Reference decode failed.' }
function Get-FrameHashes([string]$taskPath) { @(Get-Content -LiteralPath $taskPath | Where-Object { -not $_.StartsWith('#') } | ForEach-Object { ($_ -split ',')[-1].Trim() }) }
$taskReference = Get-FrameHashes "$taskMedia\source.md5"
foreach ($taskFormat in @('annex','avcc')) {
    $taskDecoded = Get-FrameHashes "$taskMedia\$taskFormat.md5"
    if ($taskReference.Count -ne 60 -or ($taskReference -join ',') -ne ($taskDecoded -join ',')) { throw "$taskFormat decoded frames differ from reference." }
    Write-Host "PASS $taskFormat video-only MPEG-TS: 60 decoded frames match the source."
}
