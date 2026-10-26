namespace SkyEye.Core.Analysis;

public sealed record BallBoundary(Point Centre, double Radius, double SupportedSectors, double Score)
{
    public BoundarySectorEvidence[] SectorEvidence { get; init; } = [];
    public BallBoundary? ProposalFit { get; init; }
}
public sealed record BoundarySectorEvidence(int Index, Point InnerPoint, Point OuterPoint,
    Point FineInnerPoint, Point FineOuterPoint, double EdgeContrast, double ExteriorContrast,
    double FineEdgeContrast, bool ExteriorRejected, bool Supported)
{
    public bool NeighborOccluded { get; init; }
}

/// <summary>Bounded current-image refinement of an existing circle proposal.
/// Raw colour edges distinguish painted bodies from cloth/shadow and interior patches.
/// A size prior bounds the search; it never supplies a body without edge support.</summary>
public static class BallBoundaryRefiner
{
    // A one-pixel size error costs the same as two supported-sector rewards.
    // The prior ranks only edge-supported candidates; it does not change the
    // preliminary fit count or the final 18/32 support gate.
    const double RadiusPriorWeight = 4;
    static readonly (double X, double Y)[] directions = Enumerable.Range(0, 32)
        .Select(i => (Math.Cos(i * Math.PI / 16), Math.Sin(i * Math.PI / 16))).ToArray();
    readonly record struct NeighborInterior(Point Centre, double B, double G, double R, bool Distinct);

    /// <summary>Measure the same 32 radial boundary sectors at one selected
    /// fit. This is useful for explaining a rejected fit without retaining
    /// per-candidate evidence throughout the bounded search.</summary>
    public static BoundarySectorEvidence[] Inspect(byte[] bgr, int width, int height, Point proposal,
        double expectedRadius, Point centre, double radius, IReadOnlyList<Point>? neighboringBallCentres = null)
    {
        if (width <= 0 || height <= 0 || bgr.Length != checked(width * height * 3)
            || !double.IsFinite(expectedRadius) || expectedRadius < 5 || expectedRadius > 100
            || !double.IsFinite(radius) || radius < 5 || radius > 125
            || !double.IsFinite(proposal.X) || !double.IsFinite(proposal.Y)
            || !double.IsFinite(centre.X) || !double.IsFinite(centre.Y))
            throw new ArgumentException("Invalid boundary image or fit.");
        var background = new double[3];
        for (int channel = 0; channel < 3; channel++)
        {
            var surrounding = directions.Select(d => (X: (int)Math.Round(proposal.X + d.X * expectedRadius * 1.7), Y: (int)Math.Round(proposal.Y + d.Y * expectedRadius * 1.7)))
                .Where(p => p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)
                .Select(p => bgr[(p.Y * width + p.X) * 3 + channel]).Order().ToArray();
            if (surrounding.Length < 24) return [];
            background[channel] = surrounding[surrounding.Length / 2];
        }

        double offset = Math.Max(2, expectedRadius * .12);
        if (centre.X - radius - offset < 0 || centre.Y - radius - offset < 0
            || centre.X + radius + offset >= width || centre.Y + radius + offset >= height)
            return [];
        var neighborInteriors = SampleNeighborInteriors(bgr, width, height, expectedRadius, neighboringBallCentres);
        var evidence = new BoundarySectorEvidence[directions.Length];
        for (int i = 0; i < directions.Length; i++)
        {
            var d = directions[i];
            var innerPoint = new Point(centre.X + d.X * (radius - offset), centre.Y + d.Y * (radius - offset));
            var outerPoint = new Point(centre.X + d.X * (radius + offset), centre.Y + d.Y * (radius + offset));
            var fineInnerPoint = new Point(centre.X + d.X * (radius - 1), centre.Y + d.Y * (radius - 1));
            var fineOuterPoint = new Point(centre.X + d.X * (radius + 1), centre.Y + d.Y * (radius + 1));
            int inside = ((int)Math.Round(innerPoint.Y) * width + (int)Math.Round(innerPoint.X)) * 3;
            int outside = ((int)Math.Round(outerPoint.Y) * width + (int)Math.Round(outerPoint.X)) * 3;
            int innerFine = ((int)Math.Round(fineInnerPoint.Y) * width + (int)Math.Round(fineInnerPoint.X)) * 3;
            int outerFine = ((int)Math.Round(fineOuterPoint.Y) * width + (int)Math.Round(fineOuterPoint.X)) * 3;
            double edge = 0, exterior = 0, fineEdge = 0;
            for (int k = 0; k < 3; k++)
            {
                edge += Math.Abs(bgr[inside + k] - bgr[outside + k]);
                exterior += Math.Abs(bgr[outside + k] - background[k]);
                fineEdge += Math.Abs(bgr[innerFine + k] - bgr[outerFine + k]);
            }
            edge /= 3; exterior /= 3; fineEdge /= 3;
            bool exteriorRejected = exterior > 40;
            bool neighborOccluded = IsNeighborOccluded(bgr, width, height, outerPoint, centre, expectedRadius, neighborInteriors);
            evidence[i] = new(i, innerPoint, outerPoint, fineInnerPoint, fineOuterPoint,
                edge, exterior, fineEdge, exteriorRejected, !exteriorRejected && !neighborOccluded && edge >= 16)
            { NeighborOccluded = neighborOccluded };
        }
        return evidence;
    }

    /// <summary>A sub-threshold visible fit may be used for a packed ball only
    /// when every failed sector is a measured exterior occlusion by a distinct,
    /// independently detected neighboring ball. The combined visible and
    /// explained coverage must still meet the configured support fraction.</summary>
    public static bool HasConservativeOcclusionCoverage(IReadOnlyList<BoundarySectorEvidence> sectors,
        Point centre, double expectedRadius, IReadOnlyList<Point> neighboringBallCentres,
        double minimumVisibleFraction = .7)
    {
        if (sectors.Count != 32 || !double.IsFinite(expectedRadius) || expectedRadius < 5 || expectedRadius > 100
            || !double.IsFinite(centre.X) || !double.IsFinite(centre.Y)
            || !double.IsFinite(minimumVisibleFraction) || minimumVisibleFraction is < .5 or > 1)
            return false;
        int visible = sectors.Count(sector => sector.Supported);
        int minimumVisible = (int)Math.Floor(minimumVisibleFraction * sectors.Count);
        int minimumCoverage = (int)Math.Ceiling(minimumVisibleFraction * sectors.Count);
        if (visible < minimumVisible) return false;

        double occlusionRadius = expectedRadius * 1.15;
        double distinctBallDistance = expectedRadius * 1.1;
        int explained = 0;
        foreach (var sector in sectors)
        {
            if (sector.Supported) continue;
            if (!(sector.ExteriorRejected || sector.NeighborOccluded) || !neighboringBallCentres.Any(neighbour =>
                    double.IsFinite(neighbour.X) && double.IsFinite(neighbour.Y)
                    && (neighbour - centre).Length >= distinctBallDistance
                    && (sector.OuterPoint - neighbour).Length <= occlusionRadius))
                return false;
            explained++;
        }
        return visible + explained >= minimumCoverage;
    }

    public static BallBoundary? Refine(byte[] bgr, int width, int height, Point proposal, double expectedRadius,
        double minimumSupportedFraction = ObservationPolicy.MinimumSupportedBoundaryFraction,
        bool collectSectorEvidence = false, IReadOnlyList<Point>? neighboringBallCentres = null)
    {
        if (width <= 0 || height <= 0 || bgr.Length != checked(width * height * 3)
            || !double.IsFinite(expectedRadius) || expectedRadius < 5 || expectedRadius > 100
            || !double.IsFinite(proposal.X) || !double.IsFinite(proposal.Y)
            || !double.IsFinite(minimumSupportedFraction) || minimumSupportedFraction is < .5 or > 1)
            throw new ArgumentException("Invalid boundary image or proposal.");
        double step = Math.Max(1.5, expectedRadius / 9), extent = expectedRadius * .4;
        var background = new double[3];
        for (int channel = 0; channel < 3; channel++)
        {
            var surrounding = directions.Select(d => (X: (int)Math.Round(proposal.X + d.X * expectedRadius * 1.7), Y: (int)Math.Round(proposal.Y + d.Y * expectedRadius * 1.7)))
                .Where(p => p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)
                .Select(p => bgr[(p.Y * width + p.X) * 3 + channel]).Order().ToArray();
            if (surrounding.Length < 24) return null;
            background[channel] = surrounding[surrounding.Length / 2];
        }
        var neighborInteriors = SampleNeighborInteriors(bgr, width, height, expectedRadius, neighboringBallCentres);
        BallBoundary? best = null, supportedBest = null, proposalFit = null;
        // Include the Hough/size-prior seed in the search. The coarse grid is
        // offset by its bounded extent and need not visit this exact geometry;
        // omitting it can discard a supported seed in favour of a shifted fit.
        Consider(proposal.X, proposal.Y, expectedRadius, reference: true);
        for (double y = proposal.Y - extent; y <= proposal.Y + extent; y += step)
            for (double x = proposal.X - extent; x <= proposal.X + extent; x += step)
                for (double r = expectedRadius * .75; r <= expectedRadius * 1.25; r += step)
                    Consider(x, y, r);
        if (best is null) return null;
        var coarse = best;
        double fine = step / 3;
        for (double y = coarse.Centre.Y - step; y <= coarse.Centre.Y + step; y += fine)
            for (double x = coarse.Centre.X - step; x <= coarse.Centre.X + step; x += fine)
                for (double r = Math.Max(expectedRadius * .75, coarse.Radius - step); r <= Math.Min(expectedRadius * 1.25, coarse.Radius + step); r += fine)
                    Consider(x, y, r);
        var selected = supportedBest ?? best;
        return selected is null ? null : selected with { ProposalFit = proposalFit };

        void Consider(double x, double y, double radius, bool reference = false)
        {
            double offset = Math.Max(2, expectedRadius * .12);
            if (x - radius - offset < 0 || y - radius - offset < 0 || x + radius + offset >= width || y + radius + offset >= height) return;
            int supported = 0; double score = 0;
            var sectorEvidence = collectSectorEvidence ? new BoundarySectorEvidence[directions.Length] : null;
            for (int i = 0; i < directions.Length; i++)
            {
                var d = directions[i];
                var innerPoint = new Point(x + d.X * (radius - offset), y + d.Y * (radius - offset));
                var outerPoint = new Point(x + d.X * (radius + offset), y + d.Y * (radius + offset));
                var fineInnerPoint = new Point(x + d.X * (radius - 1), y + d.Y * (radius - 1));
                var fineOuterPoint = new Point(x + d.X * (radius + 1), y + d.Y * (radius + 1));
                int inside = ((int)Math.Round(innerPoint.Y) * width + (int)Math.Round(innerPoint.X)) * 3;
                int outside = ((int)Math.Round(outerPoint.Y) * width + (int)Math.Round(outerPoint.X)) * 3;
                int innerFine = ((int)Math.Round(fineInnerPoint.Y) * width + (int)Math.Round(fineInnerPoint.X)) * 3;
                int outerFine = ((int)Math.Round(fineOuterPoint.Y) * width + (int)Math.Round(fineOuterPoint.X)) * 3;
                double edge = 0, exterior = 0, fineEdge = 0;
                for (int k = 0; k < 3; k++) { edge += Math.Abs(bgr[inside + k] - bgr[outside + k]); exterior += Math.Abs(bgr[outside + k] - background[k]); fineEdge += Math.Abs(bgr[innerFine + k] - bgr[outerFine + k]); }
                edge /= 3; exterior /= 3; fineEdge /= 3;
                double measuredEdge = edge, measuredFineEdge = fineEdge;
                // An interior number-patch edge leads to more ball paint. The
                // exterior of a supported body must return to local cloth.
                bool exteriorRejected = exterior > 40;
                bool neighborOccluded = IsNeighborOccluded(bgr, width, height, outerPoint, new Point(x, y), expectedRadius, neighborInteriors);
                if (exteriorRejected || neighborOccluded) { edge = 0; fineEdge = 0; }
                if (edge >= 16) supported++;
                if (sectorEvidence is not null)
                    sectorEvidence[i] = new(i, innerPoint, outerPoint, fineInnerPoint, fineOuterPoint,
                        measuredEdge, exterior, measuredFineEdge, exteriorRejected,
                        !exteriorRejected && !neighborOccluded && measuredEdge >= 16)
                    { NeighborOccluded = neighborOccluded };
                // Cap strong highlights so a few patch/shadow edges cannot dominate.
                score += Math.Min(60, fineEdge) + .1 * Math.Min(60, edge);
            }
            // Coarse candidates need not already meet the final support gate;
            // otherwise grid quantization can prevent subpixel refinement entirely.
            if (supported < 12) return;
            score = supported * 2 + score / directions.Length - RadiusPriorWeight * Math.Abs(radius - expectedRadius);
            var candidate = new BallBoundary(new(x, y), radius, supported / (double)directions.Length, score)
            { SectorEvidence = sectorEvidence ?? [] };
            if (reference && collectSectorEvidence) proposalFit = candidate;
            if (best is null || score > best.Score) best = candidate;
            if (supported >= minimumSupportedFraction * directions.Length
                && (supportedBest is null || score > supportedBest.Score)) supportedBest = candidate;
        }
    }

    static NeighborInterior[] SampleNeighborInteriors(byte[] bgr, int width, int height, double expectedRadius,
        IReadOnlyList<Point>? neighboringBallCentres)
    {
        if (neighboringBallCentres is null || neighboringBallCentres.Count == 0) return [];
        var result = new List<NeighborInterior>(neighboringBallCentres.Count);
        foreach (var centre in neighboringBallCentres)
        {
            if (!double.IsFinite(centre.X) || !double.IsFinite(centre.Y)) continue;
            var channels = new[] { new List<byte>(), new List<byte>(), new List<byte>() };
            double interiorRadius = expectedRadius * .35;
            for (int i = 0; i < 16; i++)
            {
                var direction = directions[i * 2];
                int x = (int)Math.Round(centre.X + direction.X * interiorRadius);
                int y = (int)Math.Round(centre.Y + direction.Y * interiorRadius);
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                int offset = (y * width + x) * 3;
                for (int channel = 0; channel < 3; channel++) channels[channel].Add(bgr[offset + channel]);
            }
            if (channels.Any(channel => channel.Count < 8)) continue;
            double[] median = channels.Select(channel =>
            {
                var ordered = channel.Order().ToArray();
                return (double)ordered[ordered.Length / 2];
            }).ToArray();
            double maximum = median.Max(), minimum = median.Min(), brightness = median.Average();
            bool distinct = maximum - minimum >= 28 || brightness <= 65 || brightness >= 185;
            result.Add(new(centre, median[0], median[1], median[2], distinct));
        }
        return result.ToArray();
    }

    static bool IsNeighborOccluded(byte[] bgr, int width, int height, Point outerPoint, Point centre,
        double expectedRadius, IReadOnlyList<NeighborInterior> neighbors)
    {
        int x = (int)Math.Round(outerPoint.X), y = (int)Math.Round(outerPoint.Y);
        if (x < 0 || x >= width || y < 0 || y >= height) return false;
        int offset = (y * width + x) * 3;
        double b = bgr[offset], g = bgr[offset + 1], r = bgr[offset + 2];
        double maximum = Math.Max(b, Math.Max(g, r)), minimum = Math.Min(b, Math.Min(g, r));
        double brightness = (b + g + r) / 3;
        bool distinctPixel = maximum - minimum >= 24 || brightness <= 65 || brightness >= 185;
        if (!distinctPixel) return false;
        double occlusionRadius = expectedRadius * 1.15;
        double distinctBallDistance = expectedRadius * 1.1;
        return neighbors.Any(neighbor => neighbor.Distinct
            && (neighbor.Centre - centre).Length >= distinctBallDistance
            && (outerPoint - neighbor.Centre).Length <= occlusionRadius
            && (Math.Abs(b - neighbor.B) + Math.Abs(g - neighbor.G) + Math.Abs(r - neighbor.R)) / 3 <= 32);
    }
}
