using System.Collections.Immutable;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    public PocketLayout? FindPockets(byte[] jpeg, CancellationToken cancellation = default)
    {
        Load(); cancellation.ThrowIfCancellationRequested();
        using var source = Decode(jpeg); using var frame = new MatLease(new Mat());
        double scale = Math.Min(1, 960.0 / source.Value.Cols());
        using var shape = new Size(Math.Round(source.Value.Cols() * scale), Math.Round(source.Value.Rows() * scale));
        Imgproc.Resize(source.Value, frame.Value, shape);
        using var gray = new MatLease(new Mat()); using var circles = new MatLease(new Mat()); using var blur = new Size(7, 7);
        Imgproc.CvtColor(frame.Value, gray.Value, Imgproc.ColorBgr2gray); Imgproc.GaussianBlur(gray.Value, gray.Value, blur, 0);
        int width = gray.Value.Cols(), height = gray.Value.Rows(); double shortSide = Math.Min(width, height);
        // Run Hough on greyscale, rather than a high-threshold Canny image. Dark
        // pocket interiors remain distinguishable from surrounding rails in dim light.
        Imgproc.HoughCircles(gray.Value, circles.Value, Imgproc.HoughGradient, 1.4, shortSide * .23, 60, 35, (int)(shortSide * .032), (int)(shortSide * .085));
        var pixels = Read(gray.Value); var candidates = new List<(Point Centre, double Radius, double Contrast)>();
        int count = Math.Max(circles.Value.Rows(), circles.Value.Cols());
        for (int i = 0; i < count && i < 32; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var c = circles.Value.Get(circles.Value.Rows() == 1 ? 0 : i, circles.Value.Rows() == 1 ? i : 0);
            if (c is not { Length: >= 3 }) continue;
            double x = c[0], y = c[1], radius = c[2];
            var inner = new List<byte>(); var ring = new List<byte>();
            for (int py = Math.Max(0, (int)(y - radius * 1.4)); py < Math.Min(height, y + radius * 1.4); py++)
            for (int px = Math.Max(0, (int)(x - radius * 1.4)); px < Math.Min(width, x + radius * 1.4); px++)
            {
                double distance = (px - x) * (px - x) + (py - y) * (py - y);
                if (distance < radius * radius * .55 * .55) inner.Add(pixels[py * width + px]);
                else if (distance > radius * radius * 1.1 * 1.1 && distance < radius * radius * 1.4 * 1.4) ring.Add(pixels[py * width + px]);
            }
            if (inner.Count < 30 || ring.Count < 30) continue;
            inner.Sort(); ring.Sort(); double body = inner[(int)((inner.Count - 1) * .8)], surround = ring[ring.Count / 2];
            if (surround - body < 8 || body > surround * .7) continue;
            candidates.Add((new(x / width, y / height), radius / shortSide, (surround - body) / 255));
        }
        if (candidates.Count < 5) return null;
        Point[] imageCorners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        var picks = imageCorners.Select(p => candidates.MinBy(c => (c.Centre - p).Length)).ToArray();
        if (picks.Select(p => p.Centre).Distinct().Count() != 4 || picks.Where((p, i) => (p.Centre - imageCorners[i]).Length > .35).Any()
            || picks.Max(p => p.Radius) > picks.Min(p => p.Radius) * 2.5) return null;
        var corners = picks.Select(p => p.Centre).ToArray();
        double area = Math.Abs(Enumerable.Range(0, 4).Sum(i => corners[i].X * corners[(i + 1) % 4].Y - corners[(i + 1) % 4].X * corners[i].Y)) / 2;
        if (area < .35) return null;
        TableCalibration pocketPlane;
        try { pocketPlane = new(corners); } catch (ArgumentException) { return null; }
        // At least one independently detected side pocket must agree with the
        // projective plane. This rejects arbitrary four dark objects at image edges.
        var sides = candidates.Where(c => !corners.Contains(c.Centre))
            .Where(c => new[] { new Point(1, 0), new Point(1, 1) }.Any(p => (pocketPlane.Map(c.Centre) - p).Length < .10)).ToArray();
        if (sides.Length == 0) return null;
        return new(corners.ToImmutableArray(), sides.Select(p => p.Centre).ToImmutableArray(), picks.Min(p => p.Contrast));
    }
}
