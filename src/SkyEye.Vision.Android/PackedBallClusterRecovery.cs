using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Cv = Org.Opencv.Core.Core;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    readonly record struct ClusterBall(string Kind, Circle Body, Point PeakCentre, double ColourRadius,
        BallClassification Classification);

    /// <summary>
    /// Split a compact touching group using separate red/yellow pixel masks.
    /// Every returned body needs a ball-sized colour-mask peak and a supported
    /// raw-image boundary fit; table shape and declared inventory never add one.
    /// </summary>
    PackedClusterEvidence AddPackedClusterBodies(Mat rawBgr, List<Circle> circles, double scale,
        Dictionary<Circle, BallClassification>? packedClassifications,
        List<VisionCandidateAudit>? candidateAudits = null, string pass = "initial")
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
        var proposals = FindColourPeakBodies(red.Value, "red", rawPixels, rawBgrPixels, scale, minimumPeakRadius,
                candidateAudits, pass)
            .Concat(FindColourPeakBodies(yellow.Value, "yellow", rawPixels, rawBgrPixels, scale, minimumPeakRadius,
                candidateAudits, pass)).ToArray();
        var cluster = FindDenseColourCluster(proposals, scale);
        if (candidateAudits is not null)
            for (int i = 0; i < proposals.Length; i++)
            {
                var proposal = proposals[i];
                bool selected = cluster.Any(member => member.Kind == proposal.Kind && member.Body == proposal.Body);
                candidateAudits.Add(new(pass, "packed-window", proposal.Kind, i,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius, null,
                    proposal.Classification.Kind, selected ? "selected" : "outside-selected-window"));
            }
        if (cluster.Length < 12) return default;

        var existingCentres = circles.Select(c => new Point(c.X, c.Y)).ToArray();
        var admittedSeeds = new List<Point>();
        for (int clusterIndex = 0; clusterIndex < cluster.Length; clusterIndex++)
        {
            var proposal = cluster[clusterIndex];
            // Compare independent colour-mask peaks, not their separately
            // refined body centres. Adjacent touching balls can have fitted
            // centres pulled inward while their raw peaks remain distinct.
            bool duplicate = existingCentres.Any(c => (c - proposal.PeakCentre).Length < settings.Separation * scale)
                || admittedSeeds.Any(c => (c - proposal.PeakCentre).Length < settings.Separation * scale);
            if (!duplicate)
            {
                circles.Add(proposal.Body);
                admittedSeeds.Add(proposal.PeakCentre);
                if (packedClassifications is not null) packedClassifications[proposal.Body] = proposal.Classification;
                candidateAudits?.Add(new(pass, "packed-add", proposal.Kind, clusterIndex,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius, null,
                    proposal.Classification.Kind, "added-to-proposal-set"));
            }
            else candidateAudits?.Add(new(pass, "packed-add", proposal.Kind, clusterIndex,
                new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius, null,
                proposal.Classification.Kind, "rejected-duplicate-existing-proposal"));
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
        var bounds = (
            Left: cluster.Min(p => p.Body.X) - 55 * scale,
            Right: cluster.Max(p => p.Body.X) + 55 * scale,
            Top: cluster.Min(p => p.Body.Y) - 55 * scale,
            Bottom: cluster.Max(p => p.Body.Y) + 55 * scale);
        bool blackObserved = false;
        var darkSeeds = FindPeakSeeds(dark.Value, scale, 8 * scale, bounds).ToArray();
        candidateAudits?.Add(new(pass, "packed-black-search", "black", darkSeeds.Length,
            new((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2), 0, null, null, null, null,
            $"cluster-window-seeds={darkSeeds.Length};component-cap=64-after-window-filter"));
        for (int i = 0; i < darkSeeds.Length; i++)
        {
            var seed = darkSeeds[i];
            if (seed.Centre.X < bounds.Left || seed.Centre.X > bounds.Right || seed.Centre.Y < bounds.Top || seed.Centre.Y > bounds.Bottom)
            { Audit("rejected-outside-cluster-window"); continue; }
            var fit = BallBoundaryRefiner.Refine(rawBgrPixels, coordinates.Width, coordinates.Height,
                seed.Centre, PoolGeometry.Radius * coordinates.Width, .7);
            if (fit is null)
            { Audit("rejected-no-boundary-fit"); continue; }
            if (fit.SupportedSectors < .7)
            { Audit("rejected-insufficient-boundary-support", fit, fit.SupportedSectors); continue; }
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var classification = Classify(body, rawPixels, inspectRim: true);
            if (classification.Kind != "black")
            { Audit("rejected-classification", fit, fit.SupportedSectors, classification.Kind); continue; }
            if (circles.Any(c => Distance(c, body) < settings.Separation * scale))
            { Audit("rejected-duplicate-existing-proposal", fit, fit.SupportedSectors, classification.Kind); continue; }
            circles.Add(body);
            if (packedClassifications is not null) packedClassifications[body] = classification;
            blackObserved = true;
            Audit("added-black-body", fit, fit.SupportedSectors, classification.Kind);
            break;

            void Audit(string decision, BallBoundary? boundary = null, double? support = null, string? label = null)
                => candidateAudits?.Add(new(pass, "packed-black", "black", i, seed.Centre, seed.Radius,
                    boundary?.Centre, boundary?.Radius, support, label, decision));
        }
        return new(true, cluster.Count(p => p.Kind == "red"), cluster.Count(p => p.Kind == "yellow"), blackObserved);
    }

    ClusterBall[] FindColourPeakBodies(Mat colourMask, string kind, byte[] rawHsv, byte[] rawBgr, double scale,
        double minimumPeakRadius, List<VisionCandidateAudit>? candidateAudits, string pass)
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
            var seed = new Circle(x, y, 0);
            if (x < margin || y < margin || x >= coordinates.Width - margin || y >= coordinates.Height - margin)
            { Audit("rejected-margin"); continue; }
            if (PocketCentres().Any(p => (new Point(x, y) - p).Length < settings.PocketExclusionRadius * scale))
            { Audit("rejected-pocket-exclusion"); continue; }
            double interiorRadius = distance.Value.Get((int)Math.Round(y), (int)Math.Round(x))![0];
            seed = new Circle(x, y, interiorRadius);
            if (interiorRadius < minimumPeakRadius || interiorRadius > settings.MaximumRadius * scale)
            { Audit("rejected-peak-radius"); continue; }
            var fit = BallBoundaryRefiner.Refine(rawBgr, coordinates.Width, coordinates.Height,
                new(x, y), PoolGeometry.Radius * coordinates.Width, .5);
            if (fit is null || fit.SupportedSectors < .4)
            { Audit(fit is null ? "rejected-no-boundary-fit" : "rejected-insufficient-boundary-support", fit, fit?.SupportedSectors); continue; }
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var classification = Classify(body, rawHsv, inspectRim: true);
            if (classification.Kind != kind)
            { Audit("rejected-classification", fit, fit.SupportedSectors, classification.Kind); continue; }
            // The fitted centres are allowed to move independently by several
            // pixels. Comparing those fits can collapse two touching balls when
            // both move inward by one grid step. Deduplicate colour peaks by the
            // original mask seeds; keep the refined bodies as separate geometry.
            if (candidates.Any(c => (c.PeakCentre - new Point(x, y)).Length < settings.Separation * scale))
            { Audit("rejected-duplicate-colour-peak", fit, fit.SupportedSectors, classification.Kind); continue; }
            candidates.Add(new(kind, body, new(x, y), interiorRadius, classification));
            Audit("accepted-colour-peak", fit, fit.SupportedSectors, classification.Kind);

            void Audit(string decision, BallBoundary? boundary = null, double? support = null, string? label = null)
                => candidateAudits?.Add(new(pass, "packed-" + kind, kind, i, new(seed.X, seed.Y), seed.Radius,
                    boundary is null ? null : boundary.Centre, boundary?.Radius, support, label, decision));
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

    IEnumerable<(Point Centre, double Radius)> FindPeakSeeds(Mat mask, double scale, double minimumPeakRadius,
        (double Left, double Right, double Top, double Bottom) bounds)
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
        // Connected-component labels are assigned from the top of the full image.
        // A global label cap lets unrelated cushion/pocket peaks consume the
        // budget before the packed rack is reached. Restrict to the observed
        // colour-cluster window first, then apply the same 64-seed work bound.
        int left = Math.Clamp((int)Math.Floor(bounds.Left), 0, coordinates.Width - 1);
        int right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, coordinates.Width - 1);
        int top = Math.Clamp((int)Math.Floor(bounds.Top), 0, coordinates.Height - 1);
        int bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, coordinates.Height - 1);
        using var searchWindow = new MatLease(peaks.Value.Submat(new Rect(left, top, right - left + 1, bottom - top + 1)));
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(searchWindow.Value, labels.Value, stats.Value, centres.Value);
        for (int i = 1; i < count && i <= 64; i++)
        {
            double x = centres.Value.Get(i, 0)![0] + left, y = centres.Value.Get(i, 1)![0] + top;
            if (x < 65 * scale || y < 65 * scale || x >= coordinates.Width - 65 * scale || y >= coordinates.Height - 65 * scale) continue;
            double radius = distance.Value.Get((int)Math.Round(y), (int)Math.Round(x))![0];
            if (radius >= minimumPeakRadius) yield return (new(x, y), radius);
        }
    }
}
