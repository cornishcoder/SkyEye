using System.Diagnostics;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using Cv = Org.Opencv.Core.Core;
using Point = SkyEye.Core.Point;
using CvPoint = Org.Opencv.Core.Point;

namespace SkyEye.Vision.Android;

public sealed record VisionResult(Ball[] Balls, bool Uncertain, string[] Warnings, double ElapsedMs, int CircleCount, bool Normalized, string Runtime, double[] ColourGains, int ClothHue);

/// <summary>C# port of the upstream HSV/morphology/Hough pipeline with calibrated
/// pocket masks, cue-reference normalization and bounded C# colour clustering.</summary>
public sealed class VisionAnalyzer
{
    static readonly object loadGate = new(); static bool loaded;
    readonly AnalysisCoordinates coordinates;
    readonly record struct Circle(double X, double Y, double Radius);
    public VisionAnalyzer(int width = 1000) { coordinates = new(width); }
    static void Load()
    {
        lock (loadGate) { if (loaded) return; if (!OpenCVLoader.InitLocal()) throw new IOException("Table analysis could not load OpenCV."); loaded = true; }
    }
    public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default)
    {
        Load(); cancellation.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew(); var warnings = new List<string>();
        using var source = Decode(jpeg); using var warped = Warp(source.Value, calibration);
        using var hsv = new MatLease(new Mat()); Imgproc.CvtColor(warped.Value, hsv.Value, Imgproc.ColorBgr2hsv);
        byte[] pixels = Read(hsv.Value); var circles = Detect(hsv.Value, pixels);
        var first = circles.Select(c => Classify(c, pixels)).ToArray();
        bool normalized = false; double[] colourGains = [1, 1, 1]; var cueIndices = Enumerable.Range(0, first.Length).Where(i => first[i].Kind == "cue").ToArray();
        if (cueIndices.Length == 1)
        {
            byte[] bgr = Read(warped.Value); var cue = circles[cueIndices[0]];
            var channels = new[] { new List<byte>(), new List<byte>(), new List<byte>() };
            ForPixels(cue, .7, (x, y) => { int offset = (y * coordinates.Width + x) * 3; for (int k = 0; k < 3; k++) channels[k].Add(bgr[offset + k]); });
            double[] gains = channels.Select(c => { c.Sort(); return c.Count == 0 || c[(int)((c.Count - 1) * .95)] < 10 ? 0 : Math.Min(2, 255.0 / c[(int)((c.Count - 1) * .95)]); }).ToArray();
            if (gains.All(g => g > 0))
            {
                for (int i = 0; i < bgr.Length; i++) bgr[i] = (byte)Math.Clamp((int)(bgr[i] * gains[i % 3]), 0, 255);
                warped.Value.Put(0, 0, bgr); Imgproc.CvtColor(warped.Value, hsv.Value, Imgproc.ColorBgr2hsv); pixels = Read(hsv.Value);
                // A slightly lower vote threshold retains circles merged with a cushion shadow.
                circles = Detect(hsv.Value, pixels, 14); normalized = true; colourGains = gains;
            }
        }
        if (!normalized) warnings.Add("No unique cue reference; colours were analyzed without normalization.");
        cancellation.ThrowIfCancellationRequested(); var balls = new List<Ball>();
        foreach (var circle in circles.OrderBy(c => c.Y).ThenBy(c => c.X))
        {
            var label = Classify(circle, pixels); var position = coordinates.ToTable(new(circle.X, circle.Y));
            balls.Add(new(label.Kind + "-" + balls.Count, label.Kind, position, label.Quality));
        }
        bool uncertain = balls.Any(b => b.Kind == "unknown") || balls.Count > 16 || balls.Count(b => b.Kind == "cue") != 1 || balls.Count(b => b.Kind == "black") > 1 || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7;
        if (uncertain) warnings.Add("Check uncertain colours or ball counts before using advice.");
        if (LargeObstruction(hsv.Value, pixels)) { uncertain = true; warnings.Add("A large object covers part of the playable surface."); }
        for (int i = 0; i < balls.Count; i++) for (int j = i + 1; j < balls.Count; j++) if ((balls[i].Position - balls[j].Position).Length < .040) { uncertain = true; warnings.Add("Overlapping circle candidates need checking."); }
        return new(balls.ToArray(), uncertain, warnings.Distinct().ToArray(), watch.Elapsed.TotalMilliseconds, circles.Length, normalized, Cv.Version, colourGains, ClothHue(pixels));
    }
    MatLease Warp(Mat source, TableCalibration calibration)
    {
        CvPoint[] original = calibration.Corners.Select(p => new CvPoint(p.X * source.Cols(), p.Y * source.Rows())).ToArray();
        CvPoint[] portrait = [new(coordinates.Width, 0), new(coordinates.Width, coordinates.Height), new(0, coordinates.Height), new(0, 0)];
        try
        {
            using var src = new MatLease(new MatOfPoint2f(original)); using var dst = new MatLease(new MatOfPoint2f(portrait));
            using var transform = new MatLease(Imgproc.GetPerspectiveTransform(src.Value, dst.Value)); var output = new MatLease(new Mat());
            try { using var size = new Size(coordinates.Width, coordinates.Height); Imgproc.WarpPerspective(source, output.Value, transform.Value, size); return output; }
            catch { output.Dispose(); throw; }
        }
        finally { foreach (var point in original.Concat(portrait)) point.Dispose(); }
    }
    Circle[] Detect(Mat hsv, byte[] pixels, double votes = 15)
    {
        using var mask = ClothMask(hsv, pixels); using var circles = new MatLease(new Mat());
        double scale = coordinates.Width / 1000.0;
        Imgproc.HoughCircles(mask.Value, circles.Value, Imgproc.HoughGradient, 1.3, 45 * scale, 150, votes, (int)(18 * scale), (int)(35 * scale));
        var result = new List<Circle>(); int count = Math.Max(circles.Value.Cols(), circles.Value.Rows());
        for (int i = 0; i < count && result.Count <= 32; i++)
        {
            var value = circles.Value.Get(circles.Value.Rows() == 1 ? 0 : i, circles.Value.Rows() == 1 ? i : 0);
            if (value is null || value.Length < 3) continue;
            var c = new Circle(value[0], value[1], value[2]); double margin = 23 * scale;
            if (c.X < margin || c.Y < margin || c.X > coordinates.Width - margin || c.Y > coordinates.Height - margin) continue;
            // An opening is not a ball; ball-adjacent uncertain cases remain visible through review.
            if (PocketCentres().Any(p => (new Point(c.X, c.Y) - p).Length < 55 * scale)) continue;
            result.Add(c);
        }
        return result.ToArray();
    }
    int ClothHue(byte[] pixels)
    {
        // Dominant cloth hue from interior samples instead of fixed edge pixels.
        int[] histogram = new int[60]; int w = coordinates.Width, h = coordinates.Height;
        for (int y = h / 10; y < h * 9 / 10; y += 10) for (int x = w / 10; x < w * 9 / 10; x += 10)
        { int offset = (y * w + x) * 3; if (pixels[offset + 1] > 40 && pixels[offset + 2] > 35) histogram[pixels[offset] / 3]++; }
        return Array.IndexOf(histogram, histogram.Max()) * 3 + 1;
    }
    MatLease ClothMask(Mat hsv, byte[] pixels)
    {
        int w = coordinates.Width, hue = ClothHue(pixels);
        var mask = new MatLease(new Mat());
        try
        {
            using var low = new Scalar(Math.Max(0, hue - 7), 60, 60); using var high = new Scalar(Math.Min(179, hue + 7), 255, 255);
            Cv.InRange(hsv, low, high, mask.Value);
            if (hue < 7 || hue > 172)
            {
                using var wrapped = new MatLease(new Mat()); using var l = new Scalar(hue < 7 ? 180 + hue - 7 : 0, 60, 60); using var u = new Scalar(hue < 7 ? 179 : hue + 7 - 180, 255, 255);
                Cv.InRange(hsv, l, u, wrapped.Value); Cv.Bitwise_or(mask.Value, wrapped.Value, mask.Value);
            }
            int kernelWidth = Math.Max(3, (int)Math.Round(5 * w / 1000.0) | 1); using var size = new Size(kernelWidth, kernelWidth);
            using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphRect, size));
            Imgproc.MorphologyEx(mask.Value, mask.Value, Imgproc.MorphClose, kernel.Value); Imgproc.MorphologyEx(mask.Value, mask.Value, Imgproc.MorphOpen, kernel.Value);
            using var white = new Scalar(255);
            foreach (var pocket in PocketCentres()) { using var point = new CvPoint(pocket.X, pocket.Y); Imgproc.Circle(mask.Value, point, (int)(55 * w / 1000.0), white, -1); }
            return mask;
        }
        catch { mask.Dispose(); throw; }
    }
    bool LargeObstruction(Mat hsv, byte[] pixels)
    {
        using var mask = ClothMask(hsv, pixels); Cv.Bitwise_not(mask.Value, mask.Value);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(mask.Value, labels.Value, stats.Value, centres.Value);
        double scale = coordinates.Width / 1000.0;
        for (int i = 1; i < count; i++)
        {
            double x = stats.Value.Get(i, Imgproc.CcStatLeft)![0], y = stats.Value.Get(i, Imgproc.CcStatTop)![0];
            double width = stats.Value.Get(i, Imgproc.CcStatWidth)![0], height = stats.Value.Get(i, Imgproc.CcStatHeight)![0], area = stats.Value.Get(i, Imgproc.CcStatArea)![0];
            if (x > 65 * scale && y > 65 * scale && x + width < coordinates.Width - 65 * scale && y + height < coordinates.Height - 65 * scale && width > 120 * scale && height > 120 * scale && area > 5000 * scale * scale) return true;
        }
        return false;
    }
    Point[] PocketCentres() => [new(0, 0), new(coordinates.Width, 0), new(0, coordinates.Width), new(coordinates.Width, coordinates.Width), new(0, coordinates.Height), new(coordinates.Width, coordinates.Height)];
    BallClassification Classify(Circle circle, byte[] hsv)
    {
        var samples = new List<HsvPixel>();
        ForPixels(circle, .7, (x, y) => { int offset = (y * coordinates.Width + x) * 3; samples.Add(new(hsv[offset], hsv[offset + 1], hsv[offset + 2])); });
        return BallClassifier.Classify(samples.ToArray());
    }
    void ForPixels(Circle circle, double fraction, Action<int, int> action)
    {
        double radius = circle.Radius * fraction;
        for (int y = Math.Max(0, (int)(circle.Y - radius)); y <= Math.Min(coordinates.Height - 1, (int)(circle.Y + radius)); y++)
        for (int x = Math.Max(0, (int)(circle.X - radius)); x <= Math.Min(coordinates.Width - 1, (int)(circle.X + radius)); x++)
            if ((x - circle.X) * (x - circle.X) + (y - circle.Y) * (y - circle.Y) <= radius * radius) action(x, y);
    }
    static byte[] Read(Mat mat) { byte[] data = new byte[mat.Rows() * mat.Cols() * mat.Channels()]; mat.Get(0, 0, data); return data; }
    static MatLease Decode(byte[] jpeg)
    {
        if (jpeg.Length is < 4 or > 16 * 1024 * 1024) throw new IOException("Invalid analysis image size.");
        using var encoded = new MatLease(new MatOfByte(jpeg)); var decoded = new MatLease(Imgcodecs.Imdecode(encoded.Value, Imgcodecs.ImreadColor));
        if (decoded.Value.Empty() || decoded.Value.Rows() > 6000 || decoded.Value.Cols() > 6000) { decoded.Dispose(); throw new IOException("Snapshot could not be decoded."); }
        return decoded;
    }
    public Point[]? FindCorners(byte[] jpeg)
    {
        Load(); using var frame = Decode(jpeg); using var gray = new MatLease(new Mat()); using var edges = new MatLease(new Mat()); using var circles = new MatLease(new Mat()); using var size = new Size(7, 7);
        Imgproc.CvtColor(frame.Value, gray.Value, Imgproc.ColorBgr2gray); Imgproc.GaussianBlur(gray.Value, gray.Value, size, 0); Imgproc.Canny(gray.Value, edges.Value, 170, 200);
        double scale = Math.Min(frame.Value.Rows(), frame.Value.Cols()) / 1000.0;
        Imgproc.HoughCircles(edges.Value, circles.Value, Imgproc.HoughGradient, 1.6, 300 * scale, 30, 30, (int)(35 * scale), (int)(100 * scale));
        var candidates = new List<Point>();
        for (int i = 0; i < circles.Value.Cols(); i++) { var c = circles.Value.Get(0, i); if (c is { Length: >= 3 }) candidates.Add(new(c[0] / frame.Value.Cols(), c[1] / frame.Value.Rows())); }
        if (candidates.Count < 4) return null;
        Point[] corners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        var picks = corners.Select(p => candidates.MinBy(c => (c - p).Length)).ToArray();
        if (picks.Distinct().Count() != 4 || picks.Where((p, i) => (p - corners[i]).Length > .35).Any()) return null;
        try { _ = new TableCalibration(picks); return picks; } catch (ArgumentException) { return null; }
    }
    sealed class MatLease(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() { Value.Release(); Value.Dispose(); } }
}

