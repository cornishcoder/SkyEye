// D3 slice 4 desktop self-check of the live pieces that do not need an Android bitmap:
//   settled-change-selfcheck --still work/.../542.png --other work/.../288.png --corners "x,y;x,y;x,y;x,y"
// 1. A frame without a raw bitmap gives an unavailable pass ("no raw image") and never throws.
// 2. Feeding the processor a settled view, motion, then another settled view produces one comparison whose
//    rasters are kept for sidecars, and they JPEG-encode to images that decode back at raster size.
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using Ocv = OpenCvSharp;

static class SettledChangeSelfCheck
{
    public static int Run(Dictionary<string, string> o)
    {
        var corners = o["corners"].Split(';').Select(p => p.Split(',')).Select(p => new Point(double.Parse(p[0]), double.Parse(p[1]))).ToArray();
        var calibration = new TableCalibration(corners);
        using var observer = new SettledChangeLiveObserver(calibration, "selfcheck");
        var jpegOnly = new TableFrame([1, 2, 3], DateTimeOffset.UtcNow, "timeline") { SourceTimeUs = 1 };
        var unavailable = observer.Observe(jpegOnly);
        Require(unavailable is { State: SettledChangeState.Unavailable, UnavailableReason: "no raw image", Evidence: null },
            "A frame without a raw bitmap gives an unavailable pass");

        var comparer = new SettledChangeComparer();
        var processor = new SettledChangeProcessor(comparer);
        var a = comparer.Rectify(File.ReadAllBytes(o["still"]), corners);
        var b = comparer.Rectify(File.ReadAllBytes(o["other"]), corners);
        var moving = (byte[])a.Clone();
        for (int i = 0; i < moving.Length; i += 7) moving[i] = (byte)(255 - moving[i]);
        long t = 0; SettledChangePass? last = null; int comparisons = 0;
        void Feed(byte[] raster, int passes)
        {
            for (int i = 0; i < passes; i++)
            {
                t += 350_000; last = processor.Observe(raster, t);
                if (last is { EvidenceIsNew: true, Evidence.Status: SettledChangeStatus.Compared }) comparisons++;
            }
        }
        Feed(a, 6); Feed(moving, 2); Feed(b, 6);
        Require(comparisons == 1 && processor.LastComparisonRasters is { } rasters
            && rasters.Reference.Length == comparer.Settings.RasterWidth * comparer.Settings.RasterHeight * 3,
            "Settled, moving, settled again gives one comparison with its rasters kept");
        var jpeg = comparer.EncodeJpeg(processor.LastComparisonRasters!.Value.Current);
        using var decoded = Ocv.Cv2.ImDecode(jpeg, Ocv.ImreadModes.Color);
        Require(jpeg.Length is > 1000 and < 1_000_000 && decoded.Cols == comparer.Settings.RasterWidth && decoded.Rows == comparer.Settings.RasterHeight,
            $"Rasters encode to a JPEG of raster size ({jpeg.Length} bytes)");
        Console.WriteLine($"PASS D3 live pieces: unavailable pass without a bitmap; one comparison after settle-move-settle with {last!.Evidence!.Comparison!.Regions.Count} region(s); JPEG sidecar {jpeg.Length / 1024} KB at {decoded.Cols}x{decoded.Rows}");

        // Still check 1: settled, a request, the same view: exactly one still check, nothing ball-sized changed.
        var still = new SettledChangeProcessor(comparer); long ts = 0;
        var checks = new List<SettledStillCheck>();
        SettledChangePass Step(SettledChangeProcessor p, byte[] raster)
        {
            ts += 350_000; var pass = p.Observe(raster, ts);
            if (pass.StillCheck is { } c) checks.Add(c);
            return pass;
        }
        for (int i = 0; i < 6; i++) Step(still, a);
        still.RequestStillCheck();
        for (int i = 0; i < 8; i++) Step(still, a);
        Require(checks.Count == 1 && !checks[0].Comparison.Regions.Any(r => r.BallSized) && checks[0].CurrentWindowStartUs > checks[0].AnchorWindowEndUs,
            $"A still check on an unchanged still table finds nothing ({checks.Count} checks, {checks.FirstOrDefault()?.Comparison.Regions.Count(r => r.BallSized)} ball-sized)");

        // Still check 2: settled on 542 (black absent), a request, then 288 (black and cue present) with no moving
        // frames between. Either the processor sees motion (the request is dropped, a normal comparison follows),
        // or the table still reads Settled and the still check must show the ball-sized change.
        var swap = new SettledChangeProcessor(comparer); checks.Clear(); ts = 0;
        for (int i = 0; i < 6; i++) Step(swap, a);
        swap.RequestStillCheck();
        var swapPasses = Enumerable.Range(0, 8).Select(_ => Step(swap, b)).ToList();
        bool sawMotion = swapPasses.Any(p => p.State != SettledChangeState.Settled);
        bool normalComparison = swapPasses.Any(p => p is { EvidenceIsNew: true, Evidence.Status: SettledChangeStatus.Compared });
        string swapResult = sawMotion
            ? $"motion seen (max {swapPasses.Max(p => p.MotionFraction ?? 0):0.0000}); request dropped; normal comparison {normalComparison}; still checks {checks.Count}"
            : $"table read Settled; still checks {checks.Count}, ball-sized changes {checks.Sum(c => c.Comparison.Regions.Count(r => r.BallSized))}";
        Require(sawMotion ? checks.Count == 0 && normalComparison : checks.Count == 1 && checks[0].Comparison.Regions.Any(r => r.BallSized),
            "A ball change after a still-check request is caught by motion or by the still check: " + swapResult);
        // Still check 3: one ball really vanishes with nothing else changing (the lone slow pot). 288 with the black's
        // pixels replaced by 542's cloth. Motion of one ball is near the still threshold, so whichever way it reads,
        // the change must be seen: by motion, or by the still check as a ball-sized change.
        var noBlack = (byte[])b.Clone(); int painted = 0;
        for (int i = 0; i + 2 < b.Length; i += 3)
        {
            int vb = Math.Max(b[i], Math.Max(b[i + 1], b[i + 2])), va = Math.Max(a[i], Math.Max(a[i + 1], a[i + 2]));
            if (vb < 45 && va > 70) { noBlack[i] = a[i]; noBlack[i + 1] = a[i + 1]; noBlack[i + 2] = a[i + 2]; painted++; }
        }
        // Where the black was (raster pixels), to read the still check's change grid there.
        double sumX = 0, sumY = 0; int wR = comparer.Settings.RasterWidth;
        for (int i = 0; i + 2 < b.Length; i += 3)
            if (noBlack[i] != b[i] || noBlack[i + 1] != b[i + 1] || noBlack[i + 2] != b[i + 2]) { sumX += (i / 3) % wR; sumY += (i / 3) / wR; }
        double blackX = painted == 0 ? 0 : sumX / painted, blackY = painted == 0 ? 0 : sumY / painted;
        bool GridSetNear(SettledChangeComparison c, double x, double y, double radius)
        {
            var s = comparer.Settings; int cell = s.GridCellPixels;
            for (int r = 0; r < c.GridRows; r++)
                for (int col = 0; col < c.GridColumns; col++)
                {
                    int bit = r * c.GridColumns + col;
                    if ((c.ChangeGrid[bit / 8] & (1 << (bit % 8))) == 0) continue;
                    double cx = s.Margin + (col + .5) * cell, cy = s.Margin + (r + .5) * cell;
                    if (Math.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y)) <= radius + cell * .71) return true;
                }
            return false;
        }
        var lone = new SettledChangeProcessor(comparer); checks.Clear(); ts = 0;
        for (int i = 0; i < 6; i++) Step(lone, b);
        lone.RequestStillCheck();
        var lonePasses = Enumerable.Range(0, 8).Select(_ => Step(lone, noBlack)).ToList();
        bool loneMotion = lonePasses.Any(p => p.State != SettledChangeState.Settled);
        string loneResult = loneMotion
            ? $"motion seen (max {lonePasses.Max(p => p.MotionFraction ?? 0):0.0000})"
            : $"table read Settled (max motion {lonePasses.Max(p => p.MotionFraction ?? 0):0.0000}); still checks {checks.Count}, ball-sized changes {checks.Sum(c => c.Comparison.Regions.Count(r => r.BallSized))}, " +
              $"regions {string.Join(" ", checks.SelectMany(c => c.Comparison.Regions).Select(r => $"({r.RasterX:0},{r.RasterY:0}) {r.AreaBalls:0.00} ball-areas {r.RejectReason ?? "ball"}"))}, " +
              $"change grid at the black ({blackX:0},{blackY:0}): {(checks.Count == 1 && GridSetNear(checks[0].Comparison, blackX, blackY, comparer.Settings.BallRadius * 1.6))}";
        Require(painted > 100 && (loneMotion || (checks.Count == 1 && GridSetNear(checks[0].Comparison, blackX, blackY, comparer.Settings.BallRadius * 1.6))),
            $"One ball vanishing is seen by motion or by the still check ({painted} px painted): {loneResult}");
        Console.WriteLine($"PASS D3 still check: unchanged table 1 check, 0 ball-sized changes; balls appearing without moving frames: {swapResult}; one ball vanishing ({painted} px): {loneResult}");
        return 0;
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
