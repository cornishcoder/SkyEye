param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [string]$ReplayFile = 'o08-replay-proof-holdout-v21-20261029.json',
    [ValidateSet(800,1000)][int]$Width = 800
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Solve each direction independently; do not hand-expand an inverse homography.
function Get-Map($from, $to) {
    $m = [double[,]]::new(8, 9)
    for ($i = 0; $i -lt 4; $i++) {
        $x = [double]$from[$i].x; $y = [double]$from[$i].y
        $u = [double]$to[$i].x; $v = [double]$to[$i].y
        $r = 2 * $i
        $m[$r,0]=$x; $m[$r,1]=$y; $m[$r,2]=1; $m[$r,6]=-$u*$x; $m[$r,7]=-$u*$y; $m[$r,8]=$u
        $r++
        $m[$r,3]=$x; $m[$r,4]=$y; $m[$r,5]=1; $m[$r,6]=-$v*$x; $m[$r,7]=-$v*$y; $m[$r,8]=$v
    }
    for ($c = 0; $c -lt 8; $c++) {
        $pivot = $c
        for ($r = $c + 1; $r -lt 8; $r++) { if ([math]::Abs($m[$r,$c]) -gt [math]::Abs($m[$pivot,$c])) { $pivot=$r } }
        if ([math]::Abs($m[$pivot,$c]) -lt 1e-12) { throw 'Singular calibration.' }
        for ($j=$c; $j -lt 9; $j++) { $t=$m[$c,$j]; $m[$c,$j]=$m[$pivot,$j]; $m[$pivot,$j]=$t }
        $scale=$m[$c,$c]
        for ($j=$c; $j -lt 9; $j++) { $m[$c,$j]/=$scale }
        for ($r=0; $r -lt 8; $r++) {
            if ($r -eq $c) { continue }
            $scale=$m[$r,$c]
            for ($j=$c; $j -lt 9; $j++) { $m[$r,$j]-=$scale*$m[$c,$j] }
        }
    }
    $h=[double[]]::new(8)
    for ($i=0; $i -lt 8; $i++) { $h[$i]=$m[$i,8] }
    return ,$h
}
function Map-Point($h, [double]$x, [double]$y) {
    $d=$h[6]*$x+$h[7]*$y+1
    return [pscustomobject]@{x=($h[0]*$x+$h[1]*$y+$h[2])/$d; y=($h[3]*$x+$h[4]*$y+$h[5])/$d}
}

$root=(Resolve-Path -LiteralPath $EvidenceDirectory).Path
$labels=Get-Content (Join-Path $root 'manual-labels-frame-01.json') -Raw | ConvertFrom-Json
$receipt=Get-Content (Join-Path $root '01.json') -Raw | ConvertFrom-Json
$replay=Get-Content (Join-Path $root $ReplayFile) -Raw | ConvertFrom-Json
$sample=@($replay.samples | Where-Object frameId -eq $receipt.indexedFrame.frameId)
if ($sample.Count -ne 1) { throw 'Expected one matching replay frame.' }
$source=Join-Path $root $labels.source.file
$hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if ($hash -ne $labels.source.sha256 -or $hash -ne $receipt.indexedFrame.pixelSha256 -or $hash -ne $sample[0].pixelSha256) { throw 'Source/label/receipt/replay hash mismatch.' }
$table=@(@{x=0;y=0},@{x=2;y=0},@{x=2;y=1},@{x=0;y=1})
$forward=Get-Map $receipt.calibrationCorners $table
$reverse=Get-Map $table $receipt.calibrationCorners
$cornerError=0.0
for ($i=0; $i -lt 4; $i++) {
    $p=Map-Point $reverse $table[$i].x $table[$i].y
    $cornerError=[math]::Max($cornerError,[math]::Sqrt([math]::Pow(($p.x-$receipt.calibrationCorners[$i].x)*$labels.source.width,2)+[math]::Pow(($p.y-$receipt.calibrationCorners[$i].y)*$labels.source.height,2)))
}
if ($cornerError -gt 1e-6) { throw "Corner mapping error: $cornerError pixels." }
$predictions=@($sample[0]."width$Width".analysis.circles | ForEach-Object {
    $p=Map-Point $reverse $_.x $_.y
    $roundtrip=Map-Point $forward $p.x $p.y
    if ([math]::Abs($roundtrip.x-$_.x)+[math]::Abs($roundtrip.y-$_.y) -gt 1e-10) { throw 'Centre round trip failed.' }
    [pscustomobject]@{id=$_.ballId;kind=$_.kind;tableX=$_.x;tableY=$_.y;xPx=$p.x*$labels.source.width;yPx=$p.y*$labels.source.height}
})
$pairs=@(foreach ($label in $labels.balls) { foreach ($prediction in $predictions) {
    if ($label.colour -ne $prediction.kind) { continue }
    $dx=$prediction.xPx-$label.xPx; $dy=$prediction.yPx-$label.yPx
    [pscustomobject]@{labelId=$label.id;predictionId=$prediction.id;dxPx=$dx;dyPx=$dy;errorPx=[math]::Sqrt($dx*$dx+$dy*$dy)}
}})
$usedLabels=[System.Collections.Generic.HashSet[string]]::new()
$usedPredictions=[System.Collections.Generic.HashSet[string]]::new()
$matches=@(foreach ($pair in ($pairs | Sort-Object errorPx)) {
    if ($usedLabels.Contains($pair.labelId) -or $usedPredictions.Contains($pair.predictionId)) { continue }
    $null=$usedLabels.Add($pair.labelId); $null=$usedPredictions.Add($pair.predictionId); $pair
})
if ($matches.Count -ne $labels.balls.Count -or $matches.Count -ne $predictions.Count) { throw 'Unmatched label or prediction.' }
$image=[System.Drawing.Bitmap]::new($source)
$graphics=[System.Drawing.Graphics]::FromImage($image)
$cyan=[System.Drawing.Pen]::new([System.Drawing.Color]::Cyan,2)
$magenta=[System.Drawing.Pen]::new([System.Drawing.Color]::Magenta,2)
try {
    foreach ($label in $labels.balls) {
        $x=[single]$label.xPx; $y=[single]$label.yPx
        $graphics.DrawLine($cyan,$x-9,$y,$x+9,$y); $graphics.DrawLine($cyan,$x,$y-9,$x,$y+9)
    }
    foreach ($prediction in $predictions) {
        $x=[single]$prediction.xPx; $y=[single]$prediction.yPx
        $graphics.DrawEllipse($magenta,$x-5,$y-5,10,10)
    }
    $image.Save((Join-Path $root "manual-centre-overlay-v21-$Width-corrected.png"),[System.Drawing.Imaging.ImageFormat]::Png)
    $objectLabels=@($labels.balls | Where-Object colour -ne 'cue')
    if ($objectLabels.Count -eq 0) { $objectLabels=@($labels.balls) }
    $xs=$objectLabels.xPx | Measure-Object -Minimum -Maximum
    $ys=$objectLabels.yPx | Measure-Object -Minimum -Maximum
    $left=[int][math]::Max(0,[math]::Floor($xs.Minimum-40)); $top=[int][math]::Max(0,[math]::Floor($ys.Minimum-40))
    $right=[int][math]::Min($image.Width,[math]::Ceiling($xs.Maximum+40)); $bottom=[int][math]::Min($image.Height,[math]::Ceiling($ys.Maximum+40))
    $crop=$image.Clone([System.Drawing.Rectangle]::new($left,$top,$right-$left,$bottom-$top),$image.PixelFormat)
    try { $crop.Save((Join-Path $root "manual-centre-overlay-v21-$Width-corrected-rack.png"),[System.Drawing.Imaging.ImageFormat]::Png) }
    finally { $crop.Dispose() }
    $result=[ordered]@{sourceSha256=$hash;frameId=$receipt.indexedFrame.frameId;width=$Width;cornerMappingMaxErrorPx=$cornerError;legend='manual cyan crosses; detector magenta rings';labelStatus=$labels.status;matching='greedy one-to-one same-colour by source-pixel distance; inspect overlay before accepting correspondence';meanErrorPx=($matches.errorPx | Measure-Object -Average).Average;maxErrorPx=($matches.errorPx | Measure-Object -Maximum).Maximum;predictions=$predictions;matches=$matches}
    $result | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $root "manual-centre-audit-v21-$Width.json") -Encoding utf8NoBOM
    [pscustomobject]$result | Select-Object frameId,width,cornerMappingMaxErrorPx,meanErrorPx,maxErrorPx | Format-List
} finally { $graphics.Dispose(); $image.Dispose(); $cyan.Dispose(); $magenta.Dispose() }
