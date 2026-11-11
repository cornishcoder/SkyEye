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
        return 0;
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
