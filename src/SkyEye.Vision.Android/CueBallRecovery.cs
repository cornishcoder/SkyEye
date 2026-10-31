using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    void ValidateCueBodies(Mat raw, Circle[] circles, BallClassification[] classifications,
        Dictionary<Circle, BallBoundary> boundaries, Dictionary<Circle, BallClassification> rawClassifications,
        CancellationToken cancellation, out int rejected)
    {
        rejected = 0;
        var indices = Enumerable.Range(0, circles.Length).Where(i => classifications[i].Kind == "cue").ToArray();
        if (indices.Length == 0) return;
        byte[]? bgr = null, hsv = null;
        foreach (int index in indices)
        {
            cancellation.ThrowIfCancellationRequested();
            BallBoundary? fit = null; BallClassification? label = null;
            var supported = boundaries.Where(p => SameBody(p.Key, circles[index])).Select(p => p.Value).Distinct().ToArray();
            fit = supported.Length == 1 ? supported[0] : null;
            if (fit is not null || indices.Length <= 4)
            {
                if (fit is null)
                {
                    bgr ??= Read(raw);
                    fit = BallBoundaryRefiner.Refine(bgr, coordinates.Width, coordinates.Height,
                        new(circles[index].X, circles[index].Y), PoolGeometry.Radius * coordinates.Width);
                }
                if (fit is not null && fit.SupportedSectors >= ObservationPolicy.MinimumSupportedBoundaryFraction)
                {
                    var fittedCircle = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
                    if (!rawClassifications.TryGetValue(fittedCircle, out label))
                    {
                        if (hsv is null)
                        {
                            using var rawHsv = new MatLease(new Mat()); Imgproc.CvtColor(raw, rawHsv.Value, Imgproc.ColorBgr2hsv); hsv = Read(rawHsv.Value);
                        }
                        label = Classify(fittedCircle, hsv, true);
                    }
                }
            }
            if (label is null || label.Kind == "unknown")
            {
                // A bright cloth circle is not a second cue ball. Leave its
                // proposal unknown; never force another colour from inventory.
                classifications[index] = classifications[index] with { Kind = "unknown", Quality = 0 };
                rejected++; continue;
            }
            if (label.Kind != "cue") rejected++; // Fresh body evidence may identify the black number-patch owner.
            var body = new Circle(fit!.Centre.X, fit.Centre.Y, fit.Radius);
            circles[index] = body; classifications[index] = label;
            boundaries[body] = fit; rawClassifications[body] = label;
        }
    }

    Circle[] RecoverNeutralCueBodies(Mat raw, Circle[] circles,
        Dictionary<Circle, BallBoundary> boundaries, Dictionary<Circle, BallClassification> classifications,
        CancellationToken cancellation,
        out int candidateCount, out int recoveredCount, out NeutralCueCandidateTelemetry[] candidateAudits,
        out NeutralCueMaskTelemetry? maskTelemetry, bool auditCandidates)
    {
        int width = coordinates.Width, height = coordinates.Height;
        double scale = width / 1000d, expectedRadius = PoolGeometry.Radius * width;
        using var rawHsv = new MatLease(new Mat()); Imgproc.CvtColor(raw, rawHsv.Value, Imgproc.ColorBgr2hsv);
        var hsv = Read(rawHsv.Value);
        var values = new byte[width * height];
        for (int i = 0; i < values.Length; i++) values[i] = hsv[i * 3 + 2];
        using var value = new MatLease(new Mat(height, width, CvType.Cv8uc1)); value.Value.Put(0, 0, values);
        using var background = new MatLease(new Mat());
        // A broad field spans several ball diameters. Thin sunlight stripes must
        // still satisfy compact-body geometry and a full current-image boundary.
        int kernelWidth = Math.Max(7, (int)Math.Round(expectedRadius * 8) | 1);
        using var blurSize = new Size(kernelWidth, kernelWidth);
        Imgproc.Blur(value.Value, background.Value, blurSize);
        var field = Read(background.Value);
        for (int i = 0; i < values.Length; i++)
        {
            if ((i & 8191) == 0) cancellation.ThrowIfCancellationRequested();
            bool cueAppearance = hsv[i * 3 + 1] < 85
                || hsv[i * 3] is >= 8 and <= 30 && hsv[i * 3 + 1] < 160 && hsv[i * 3 + 2] > 130;
            values[i] = cueAppearance && hsv[i * 3 + 2] >= Math.Max(48, Math.Max(field[i] + 8, field[i] * 1.1))
                ? (byte)255 : (byte)0;
        }
        value.Value.Put(0, 0, values);
        using var kernelSize = new Size(3, 3);
        using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, kernelSize));
        Imgproc.MorphologyEx(value.Value, value.Value, Imgproc.MorphOpen, kernel.Value);
        int maskPixels = auditCandidates ? Read(value.Value).Count(pixel => pixel != 0) : 0;
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(value.Value, labels.Value, stats.Value, centres.Value);
        var candidates = new List<Circle>();
        var filterCounts = auditCandidates ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
        var nearPocketComponents = auditCandidates ? new List<NeutralCueMaskComponentTelemetry>() : null;
        const int maximumNearPocketComponents = 128;
        double traceDistance = expectedRadius * 6;
        var pockets = PocketCentres();
        for (int i = 1; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            double w = stats.Value.Get(i, Imgproc.CcStatWidth)![0], h = stats.Value.Get(i, Imgproc.CcStatHeight)![0];
            double area = stats.Value.Get(i, Imgproc.CcStatArea)![0], radius = (w + h) / 4;
            double x = centres.Value.Get(i, 0)![0], y = centres.Value.Get(i, 1)![0];
            double fill = w * h <= 0 ? 0 : area / (w * h);
            double pocketDistance = pockets.Min(p => (new Point(x, y) - p).Length);
            string decision = radius < expectedRadius * .65 || radius > expectedRadius * 1.3
                ? "rejected-radius"
                : w / h is < .75 or > 1.33 ? "rejected-aspect"
                : fill < .6 ? "rejected-fill"
                : x < expectedRadius * 1.7 || y < expectedRadius * 1.7
                    || x > width - expectedRadius * 1.7 || y > height - expectedRadius * 1.7
                    ? "rejected-image-edge"
                : pocketDistance < settings.PocketExclusionRadius * scale
                    ? "rejected-pocket-exclusion"
                : "candidate";
            if (decision == "candidate") candidates.Add(new(x, y, radius));
            if (!auditCandidates) continue;
            var counts = filterCounts!;
            counts[decision] = counts.GetValueOrDefault(decision) + 1;
            if (pocketDistance <= traceDistance && nearPocketComponents!.Count < maximumNearPocketComponents)
                nearPocketComponents.Add(new(i, x, y, (int)w, (int)h, (int)area, radius, fill, pocketDistance, decision));
        }
        maskTelemetry = auditCandidates
            ? new(maskPixels, count - 1, filterCounts!, nearPocketComponents!.ToArray())
            : null;
        candidateCount = candidates.Count; recoveredCount = 0;
        var audits = auditCandidates ? new List<NeutralCueCandidateTelemetry>(Math.Min(4, candidates.Count)) : null;
        candidateAudits = [];
        // Too many bodies are ambiguous, not permission to select an arbitrary
        // cue ball. Bound expensive boundary fitting to four plausible bodies.
        if (candidates.Count > 4) return circles;
        var result = circles.ToList(); byte[]? rawPixels = null;
        foreach (var candidate in candidates)
        {
            cancellation.ThrowIfCancellationRequested();
            rawPixels ??= Read(raw);
            var fit = BallBoundaryRefiner.Refine(rawPixels, width, height, new(candidate.X, candidate.Y), expectedRadius);
            if (fit is null)
            {
                audits?.Add(new(candidate.X, candidate.Y, candidate.Radius, null, null, null, null,
                    null, null, "rejected-no-boundary-fit"));
                continue;
            }
            if (fit.SupportedSectors < ObservationPolicy.MinimumSupportedBoundaryFraction)
            {
                audits?.Add(new(candidate.X, candidate.Y, candidate.Radius, fit.Centre.X, fit.Centre.Y,
                    fit.Radius, fit.SupportedSectors, null, null, "rejected-insufficient-boundary-support"));
                continue;
            }
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var matching = result.Select((c, i) => (Circle: c, Index: i))
                .Where(p => SameBody(p.Circle, body)).ToArray();
            if (matching.Length > 1)
            {
                audits?.Add(new(candidate.X, candidate.Y, candidate.Radius, fit.Centre.X, fit.Centre.Y,
                    fit.Radius, fit.SupportedSectors, null, null, "rejected-ambiguous-existing-match"));
                continue;
            }
            string? existingKind = matching.Length == 1 ? Classify(matching[0].Circle, hsv, true).Kind : null;
            if (matching.Length == 1 && existingKind is not ("unknown" or "cue"))
            {
                audits?.Add(new(candidate.X, candidate.Y, candidate.Radius, fit.Centre.X, fit.Centre.Y,
                    fit.Radius, fit.SupportedSectors, existingKind, null, "rejected-existing-kind-conflict"));
                continue;
            }
            // The whole body and its rim must agree; a cream patch on the black
            // ball cannot supply a white body at the full calibrated diameter.
            bool allowWarmNeutralCueCandidate = matching.Length == 1 && existingKind == "unknown";
            var classification = Classify(body, hsv, true, allowWarmNeutralCueCandidate);
            bool localContrast = HasLowLightNeutralBallContrast(body, hsv, includeWarmCream: true);
            if (classification.Kind != "cue" || !localContrast)
            {
                string decision = classification.Kind != "cue" ? "rejected-not-cue" : "rejected-no-local-contrast";
                audits?.Add(new(candidate.X, candidate.Y, candidate.Radius, fit.Centre.X, fit.Centre.Y,
                    fit.Radius, fit.SupportedSectors, classification.Kind, localContrast, decision)
                { ClassificationEvidence = classification });
                continue;
            }
            if (matching.Length == 1)
            {
                result[matching[0].Index] = body;
            }
            else result.Add(body);
            boundaries[body] = fit; classifications[body] = classification; recoveredCount++;
            audits?.Add(new(candidate.X, candidate.Y, candidate.Radius, fit.Centre.X, fit.Centre.Y,
                fit.Radius, fit.SupportedSectors, classification.Kind, localContrast, "recovered"));
        }
        candidateAudits = audits?.ToArray() ?? [];
        return result.ToArray();
    }

    static bool SameBody(Circle left, Circle right)
        => Math.Sqrt(Math.Pow(left.X - right.X, 2) + Math.Pow(left.Y - right.Y, 2)) < Math.Max(left.Radius, right.Radius) * .75;
}
