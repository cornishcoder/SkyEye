using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Cv = Org.Opencv.Core.Core;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    readonly record struct ClusterBall(string Kind, Circle Body, double ColourRadius, BallClassification Classification);

    /// <summary>
    /// Split a compact touching group using separate red/yellow pixel masks.
    /// Every returned body needs a ball-sized colour-mask peak and a supported
    /// raw-image boundary fit; table shape and declared inventory never add one.
    /// </summary>
    PackedClusterEvidence AddPackedClusterBodies(Mat rawBgr, List<Circle> circles, double scale,
        Dictionary<Circle, BallClassification>? packedClassifications)
    {
        using var rawHsv = new MatLease(new Mat());
        Imgproc.CvtColor(rawBgr, rawHsv.Value, Imgproc.ColorBgr2hsv);
        byte[] rawPixels = Read(rawHsv.Value), rawBgrPixels = Read(rawBgr);
        using var red = new MatLease(new Mat());
        using var redLowA = new Scalar(0, 100, 30); using var redHighA = new Scalar(10, 255, 255);
        using var redLowB = new Scalar(170, 100, 30); using var redHighB = new Scalar(179, 255, 255);
        using var redWrap = new MatLease(new Mat());
        Cv.InRange(rawHsv.Value, redLowA, redHighA, red.Value);
        Cv.InRange(rawHsv.Value, redLowB, redHighB, redWrap.Value);
        Cv.Bitwise_or(red.Value, redWrap.Value, red.Value);
        using var yellow = new MatLease(new Mat()); using var yellowLow = new Scalar(10, 100, 30); using var yellowHigh = new Scalar(40, 255, 255);
        Cv.InRange(rawHsv.Value, yellowLow, yellowHigh, yellow.Value);

        double minimumPeakRadius = 12 * scale;
        var proposals = FindColourPeakBodies(red.Value, "red", rawPixels, rawBgrPixels, scale, minimumPeakRadius)
            .Concat(FindColourPeakBodies(yellow.Value, "yellow", rawPixels, rawBgrPixels, scale, minimumPeakRadius)).ToArray();
        var cluster = FindDenseColourCluster(proposals, scale);
        if (cluster.Length < 12) return default;

        foreach (var proposal in cluster)
            if (!circles.Any(c => Distance(c, proposal.Body) < settings.Separation * scale))
            {
                circles.Add(proposal.Body);
                if (packedClassifications is not null) packedClassifications[proposal.Body] = proposal.Classification;
            }

        // The black ball's centre lies near its number patch, so a dark-mask
        // interior peak is only a seed. It still has to fit a visible outer edge
        // and classify as black from this frame before it can become a body.
        using var dark = new MatLease(new Mat()); using var darkLow = new Scalar(0, 0, 0);
        using var darkHigh = new Scalar(179, 255, settings.DarkBodyValueCeiling);
        Cv.InRange(rawHsv.Value, darkLow, darkHigh, dark.Value);
        using var cleanupSize = new Size(3, 3);
        using var cleanupKernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, cleanupSize));
        Imgproc.MorphologyEx(dark.Value, dark.Value, Imgproc.MorphOpen, cleanupKernel.Value);
        var bounds = new
        {
            Left = cluster.Min(p => p.Body.X) - 55 * scale,
            Right = cluster.Max(p => p.Body.X) + 55 * scale,
            Top = cluster.Min(p => p.Body.Y) - 55 * scale,
            Bottom = cluster.Max(p => p.Body.Y) + 55 * scale
        };
        bool blackObserved = false;
        foreach (var seed in FindPeakSeeds(dark.Value, scale, 8 * scale))
        {
            if (seed.X < bounds.Left || seed.X > bounds.Right || seed.Y < bounds.Top || seed.Y > bounds.Bottom) continue;
            var fit = BallBoundaryRefiner.Refine(rawBgrPixels, coordinates.Width, coordinates.Height,
                new(seed.X, seed.Y), PoolGeometry.Radius * coordinates.Width, .7);
            if (fit is null || fit.SupportedSectors < .7) continue;
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var classification = Classify(body, rawPixels, inspectRim: true);
            if (classification.Kind != "black"
                || circles.Any(c => Distance(c, body) < settings.Separation * scale)) continue;
            circles.Add(body);
            if (packedClassifications is not null) packedClassifications[body] = classification;
            blackObserved = true;
            break;
        }
        return new(true, cluster.Count(p => p.Kind == "red"), cluster.Count(p => p.Kind == "yellow"), blackObserved);
    }

    ClusterBall[] FindColourPeakBodies(Mat colourMask, string kind, byte[] rawHsv, byte[] rawBgr, double scale, double minimumPeakRadius)
    {
        var candidates = new List<ClusterBall>();
        using var distance = new MatLease(new Mat());
        Imgproc.DistanceTransform(colourMask, distance.Value, Imgproc.DistL2, 3);
        using var dilated = new MatLease(new Mat());
        int neighbourhood = Math.Max(5, (int)Math.Round(17 * scale) | 1);
        using var neighbourhoodSize = new Size(neighbourhood, neighbourhood);
        using var neighbourhoodKernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphRect, neighbourhoodSize));
        Imgproc.Dilate(distance.Value, dilated.Value, neighbourhoodKernel.Value);
        using var peaks = new MatLease(new Mat()); Cv.Compare(distance.Value, dilated.Value, peaks.Value, Cv.CmpEq);
        using var minimum = new Scalar(minimumPeakRadius); using var aboveMinimum = new MatLease(new Mat());
        Cv.Compare(distance.Value, minimum, aboveMinimum.Value, Cv.CmpGt);
        Cv.Bitwise_and(peaks.Value, aboveMinimum.Value, peaks.Value);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(peaks.Value, labels.Value, stats.Value, centres.Value);
        double margin = 65 * scale;
        for (int i = 1; i < count && candidates.Count < 32; i++)
        {
            double x = centres.Value.Get(i, 0)![0], y = centres.Value.Get(i, 1)![0];
            if (x < margin || y < margin || x >= coordinates.Width - margin || y >= coordinates.Height - margin
                || PocketCentres().Any(p => (new Point(x, y) - p).Length < settings.PocketExclusionRadius * scale)) continue;
            double interiorRadius = distance.Value.Get((int)Math.Round(y), (int)Math.Round(x))![0];
            if (interiorRadius < minimumPeakRadius || interiorRadius > settings.MaximumRadius * scale) continue;
            var fit = BallBoundaryRefiner.Refine(rawBgr, coordinates.Width, coordinates.Height,
                new(x, y), PoolGeometry.Radius * coordinates.Width, .5);
            if (fit is null || fit.SupportedSectors < .4) continue;
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var classification = Classify(body, rawHsv, inspectRim: true);
            if (classification.Kind != kind
                || candidates.Any(c => Distance(c.Body, body) < settings.Separation * scale)) continue;
            candidates.Add(new(kind, body, interiorRadius, classification));
        }
        return candidates.ToArray();
    }

    static double Distance(Circle first, Circle second)
        => Math.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    ClusterBall[] FindDenseColourCluster(ClusterBall[] candidates, double scale)
    {
        ClusterBall[] best = [];
        double extent = 400 * scale;
        foreach (double left in candidates.Select(p => p.Body.X).Distinct())
        foreach (double top in candidates.Select(p => p.Body.Y).Distinct())
        {
            var group = candidates.Where(p => p.Body.X >= left && p.Body.X <= left + extent
                && p.Body.Y >= top && p.Body.Y <= top + extent).ToArray();
            if (group.Length > best.Length && group.Length >= 12
                && group.Count(p => p.Kind == "red") >= 5 && group.Count(p => p.Kind == "yellow") >= 5)
                best = group;
        }
        return best;
    }

    IEnumerable<Point> FindPeakSeeds(Mat mask, double scale, double minimumPeakRadius)
    {
        using var distance = new MatLease(new Mat()); Imgproc.DistanceTransform(mask, distance.Value, Imgproc.DistL2, 3);
        using var dilated = new MatLease(new Mat());
        int neighbourhood = Math.Max(5, (int)Math.Round(17 * scale) | 1);
        using var neighbourhoodSize = new Size(neighbourhood, neighbourhood);
        using var neighbourhoodKernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphRect, neighbourhoodSize));
        Imgproc.Dilate(distance.Value, dilated.Value, neighbourhoodKernel.Value);
        using var peaks = new MatLease(new Mat()); Cv.Compare(distance.Value, dilated.Value, peaks.Value, Cv.CmpEq);
        using var minimum = new Scalar(minimumPeakRadius); using var aboveMinimum = new MatLease(new Mat());
        Cv.Compare(distance.Value, minimum, aboveMinimum.Value, Cv.CmpGt); Cv.Bitwise_and(peaks.Value, aboveMinimum.Value, peaks.Value);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(peaks.Value, labels.Value, stats.Value, centres.Value);
        for (int i = 1; i < count && i <= 64; i++)
        {
            double x = centres.Value.Get(i, 0)![0], y = centres.Value.Get(i, 1)![0];
            if (x < 65 * scale || y < 65 * scale || x >= coordinates.Width - 65 * scale || y >= coordinates.Height - 65 * scale) continue;
            if (distance.Value.Get((int)Math.Round(y), (int)Math.Round(x))![0] >= minimumPeakRadius) yield return new(x, y);
        }
    }
}
