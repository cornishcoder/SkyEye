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
        bool hasProbe = auditCandidates && diagnosticNeutralCueProbeAt800 is { };
        Point probeCentre = hasProbe
            ? new Point(diagnosticNeutralCueProbeAt800!.Value.X * width / 800d,
                diagnosticNeutralCueProbeAt800.Value.Y * width / 800d)
            : default;
        double probeRadius = expectedRadius * .65;
        int probePixels = 0, probeAppearancePixels = 0, probeAdaptiveValuePixels = 0;
        int probePreMorphPixels = 0, probePostMorphPixels = 0;
        long probeValueSum = 0, probeFieldSum = 0;
        int probeCentreX = hasProbe ? Math.Clamp((int)Math.Round(probeCentre.X), 0, width - 1) : 0;
        int probeCentreY = hasProbe ? Math.Clamp((int)Math.Round(probeCentre.Y), 0, height - 1) : 0;
        int probeCentreValue = 0, probeCentreFieldValue = 0;
        bool probeCentreAppearance = false, probeCentreAdaptiveValue = false;
        bool probeCentrePreMorphMask = false, probeCentrePostMorphMask = false;
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
            int offset = i * 3;
            int pixelX = i % width, pixelY = i / width;
            byte pixelValue = hsv[offset + 2];
            bool cueAppearance = hsv[offset + 1] < 85
                || hsv[offset] is >= 8 and <= 30 && hsv[offset + 1] < 160 && pixelValue > 130;
            bool adaptiveValue = pixelValue >= Math.Max(48, Math.Max(field[i] + 8, field[i] * 1.1));
            bool selected = cueAppearance && adaptiveValue;
            if (hasProbe)
            {
                double dx = pixelX - probeCentre.X, dy = pixelY - probeCentre.Y;
                if (dx * dx + dy * dy <= probeRadius * probeRadius)
                {
                    probePixels++;
                    probeValueSum += pixelValue; probeFieldSum += field[i];
                    if (cueAppearance) probeAppearancePixels++;
                    if (adaptiveValue) probeAdaptiveValuePixels++;
                    if (selected) probePreMorphPixels++;
                }
                if (pixelX == probeCentreX && pixelY == probeCentreY)
                {
                    probeCentreValue = pixelValue; probeCentreFieldValue = field[i];
                    probeCentreAppearance = cueAppearance; probeCentreAdaptiveValue = adaptiveValue;
                    probeCentrePreMorphMask = selected;
                }
            }
            values[i] = selected ? (byte)255 : (byte)0;
        }
        value.Value.Put(0, 0, values);
        using var kernelSize = new Size(3, 3);
        using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, kernelSize));
        Imgproc.MorphologyEx(value.Value, value.Value, Imgproc.MorphOpen, kernel.Value);
        byte[]? postMorphMask = auditCandidates ? Read(value.Value) : null;
        int maskPixels = postMorphMask?.Count(pixel => pixel != 0) ?? 0;
        if (hasProbe && postMorphMask is not null)
        {
            for (int y = Math.Max(0, (int)Math.Floor(probeCentre.Y - probeRadius)); y <= Math.Min(height - 1, (int)Math.Ceiling(probeCentre.Y + probeRadius)); y++)
            for (int x = Math.Max(0, (int)Math.Floor(probeCentre.X - probeRadius)); x <= Math.Min(width - 1, (int)Math.Ceiling(probeCentre.X + probeRadius)); x++)
            {
                double dx = x - probeCentre.X, dy = y - probeCentre.Y;
                int index = y * width + x;
                if (dx * dx + dy * dy <= probeRadius * probeRadius && postMorphMask[index] != 0)
                    probePostMorphPixels++;
            }
            probeCentrePostMorphMask = postMorphMask[probeCentreY * width + probeCentreX] != 0;
        }
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(value.Value, labels.Value, stats.Value, centres.Value);
        int probeCentreComponentLabel = hasProbe ? (int)labels.Value.Get(probeCentreY, probeCentreX)![0] : 0;
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
            if ((pocketDistance <= traceDistance && nearPocketComponents!.Count < maximumNearPocketComponents)
                || i == probeCentreComponentLabel)
                nearPocketComponents!.Add(new(i, x, y, (int)w, (int)h, (int)area, radius, fill, pocketDistance, decision));
        }
        var edgePrunedTelemetry = new List<NeutralCueMaskEdgePrunedComponentTelemetry>();
        if (hasProbe && postMorphMask is not null)
        {
            foreach (double edgeMarginFraction in new[] { .75, 1.0, 1.25, 1.5, 1.7 })
            {
                cancellation.ThrowIfCancellationRequested();
                double edgeMargin = expectedRadius * edgeMarginFraction;
                byte[] edgePrunedMask = (byte[])postMorphMask.Clone();
                int edgePixelCount = 0;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (x < edgeMargin || y < edgeMargin || x >= width - edgeMargin || y >= height - edgeMargin)
                        edgePrunedMask[index] = 0;
                    if (edgePrunedMask[index] != 0) edgePixelCount++;
                }
                using var edgePrunedMat = new MatLease(new Mat(height, width, CvType.Cv8uc1));
                edgePrunedMat.Value.Put(0, 0, edgePrunedMask);
                using var edgeLabels = new MatLease(new Mat()); using var edgeStats = new MatLease(new Mat());
                using var edgeCentres = new MatLease(new Mat());
                int edgeCount = Imgproc.ConnectedComponentsWithStats(edgePrunedMat.Value,
                    edgeLabels.Value, edgeStats.Value, edgeCentres.Value);
                int edgeLabel = (int)edgeLabels.Value.Get(probeCentreY, probeCentreX)![0];
                if (edgeLabel == 0)
                {
                    edgePrunedTelemetry.Add(new(edgeMarginFraction, edgeMargin, edgePixelCount, edgeCount - 1, 0,
                        null, null, null, null, null, null, null, "label-pixel-removed"));
                }
                else
                {
                    double w = edgeStats.Value.Get(edgeLabel, Imgproc.CcStatWidth)![0];
                    double h = edgeStats.Value.Get(edgeLabel, Imgproc.CcStatHeight)![0];
                    double area = edgeStats.Value.Get(edgeLabel, Imgproc.CcStatArea)![0];
                    double x = edgeCentres.Value.Get(edgeLabel, 0)![0], y = edgeCentres.Value.Get(edgeLabel, 1)![0];
                    double radius = (w + h) / 4, fill = w * h <= 0 ? 0 : area / (w * h);
                    double pocketDistance = pockets.Min(p => (new Point(x, y) - p).Length);
                    string decision = radius < expectedRadius * .65 || radius > expectedRadius * 1.3
                        ? "rejected-radius"
                        : w / h is < .75 or > 1.33 ? "rejected-aspect"
                        : fill < .6 ? "rejected-fill"
                        : x < edgeMargin || y < edgeMargin || x > width - edgeMargin || y > height - edgeMargin
                            ? "rejected-image-edge"
                        : pocketDistance < settings.PocketExclusionRadius * scale
                            ? "rejected-pocket-exclusion"
                        : "candidate";
                    edgePrunedTelemetry.Add(new(edgeMarginFraction, edgeMargin, edgePixelCount,
                        edgeCount - 1, edgeLabel, x, y, (int)w, (int)h, (int)area, radius, fill, decision));
                }
            }
        }
        NeutralCueMaskProbeTelemetry? probeTelemetry = null;
        if (hasProbe && probePixels > 0)
        {
            probeTelemetry = new(probeCentre.X, probeCentre.Y, probeRadius, probePixels,
                probeAppearancePixels, probeAdaptiveValuePixels, probePreMorphPixels, probePostMorphPixels,
                probeValueSum / (double)probePixels, probeFieldSum / (double)probePixels,
                probeCentreValue, probeCentreFieldValue, probeCentreAppearance, probeCentreAdaptiveValue,
                probeCentrePreMorphMask, probeCentrePostMorphMask, probeCentreComponentLabel);
        }
        byte[]? diagnosticRawPixels = null;
        NeutralCueMaskHoughTelemetry? houghProbeTelemetry = null;
        if (hasProbe)
        {
            using var houghCircles = new MatLease(new Mat());
            Imgproc.HoughCircles(value.Value, houghCircles.Value, Imgproc.HoughGradient,
                settings.HoughDp, settings.Separation * scale, settings.EdgeThreshold,
                settings.InitialVotes * scale, (int)(settings.MinimumRadius * scale),
                (int)(settings.MaximumRadius * scale));
            int houghCount = Math.Max(houghCircles.Value.Cols(), houghCircles.Value.Rows());
            var nearby = new List<(Circle Circle, double Distance)>();
            for (int index = 0; index < houghCount; index++)
            {
                var proposal = houghCircles.Value.Get(houghCircles.Value.Rows() == 1 ? 0 : index,
                    houghCircles.Value.Rows() == 1 ? index : 0);
                if (proposal is null || proposal.Length < 3) continue;
                var circle = new Circle(proposal[0], proposal[1], proposal[2]);
                double distance = Math.Sqrt(Math.Pow(circle.X - probeCentre.X, 2) + Math.Pow(circle.Y - probeCentre.Y, 2));
                if (distance <= expectedRadius * 3) nearby.Add((circle, distance));
            }
            var houghAudits = new List<NeutralCueMaskHoughCandidateTelemetry>();
            foreach (var candidate in nearby.OrderBy(candidate => candidate.Distance).Take(16))
            {
                diagnosticRawPixels ??= Read(raw);
                var fit = BallBoundaryRefiner.Refine(diagnosticRawPixels, width, height,
                    new(candidate.Circle.X, candidate.Circle.Y), expectedRadius,
                    settings.MinimumHoughBoundarySupport);
                string? classification = null; bool? localContrast = null;
                string decision;
                if (fit is null)
                {
                    decision = "no-boundary-fit";
                }
                else
                {
                    var fittedCircle = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
                    classification = Classify(fittedCircle, hsv, inspectRim: true,
                        allowWarmNeutralCueCandidate: true).Kind;
                    localContrast = HasLowLightNeutralBallContrast(fittedCircle, hsv, includeWarmCream: true);
                    decision = fit.SupportedSectors < settings.MinimumHoughBoundarySupport
                        ? "insufficient-boundary-support"
                        : classification != "cue" ? "not-classified-cue"
                        : !localContrast.Value ? "no-local-contrast"
                        : "labelled-cue-supported";
                }
                houghAudits.Add(new(candidate.Circle.X, candidate.Circle.Y, candidate.Circle.Radius,
                    candidate.Distance, fit?.SupportedSectors, classification, localContrast, decision));
            }
            houghProbeTelemetry = new(houghCount, houghAudits.ToArray());
        }
        NeutralCueMaskBoundaryProbeTelemetry? boundaryProbeTelemetry = null;
        if (hasProbe)
        {
            diagnosticRawPixels ??= Read(raw);
            var fit = BallBoundaryRefiner.Refine(diagnosticRawPixels, width, height, probeCentre, expectedRadius,
                settings.MinimumHoughBoundarySupport, collectSectorEvidence: true);
            if (fit is null)
            {
                boundaryProbeTelemetry = new(null, null, null, null, null, null, "no-boundary-fit");
            }
            else
            {
                var fittedCircle = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
                string classification = Classify(fittedCircle, hsv, inspectRim: true,
                    allowWarmNeutralCueCandidate: true).Kind;
                bool localContrast = HasLowLightNeutralBallContrast(fittedCircle, hsv, includeWarmCream: true);
                string decision = fit.SupportedSectors < settings.MinimumHoughBoundarySupport
                    ? "insufficient-boundary-support"
                    : classification != "cue" ? "not-classified-cue"
                    : !localContrast ? "no-local-contrast"
                    : "labelled-cue-supported";
                boundaryProbeTelemetry = new(fit.Centre.X, fit.Centre.Y, fit.Radius, fit.SupportedSectors,
                    classification, localContrast, decision) { SectorEvidence = fit.SectorEvidence };
            }
        }
        maskTelemetry = auditCandidates
            ? new NeutralCueMaskTelemetry(maskPixels, count - 1, filterCounts!, nearPocketComponents!.ToArray())
            { Probe = probeTelemetry, BoundaryProbe = boundaryProbeTelemetry, HoughProbe = houghProbeTelemetry,
                EdgePrunedComponents = edgePrunedTelemetry.ToArray() }
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
