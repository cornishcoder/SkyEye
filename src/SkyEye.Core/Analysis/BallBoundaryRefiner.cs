namespace SkyEye.Core.Analysis;

public sealed record BallBoundary(Point Centre, double Radius, double SupportedSectors, double Score)
{
    public BoundarySectorEvidence[] SectorEvidence { get; init; } = [];
    public BallBoundary? ProposalFit { get; init; }
}
public sealed record BoundarySectorEvidence(int Index, Point InnerPoint, Point OuterPoint,
    Point FineInnerPoint, Point FineOuterPoint, double EdgeContrast, double ExteriorContrast,
    double FineEdgeContrast, bool ExteriorRejected, bool Supported);

/// <summary>Bounded current-image refinement of an existing circle proposal.
/// Raw colour edges distinguish painted bodies from cloth/shadow and interior patches.
/// A size prior bounds the search; it never supplies a body without edge support.</summary>
public static class BallBoundaryRefiner
{
    static readonly (double X, double Y)[] directions = Enumerable.Range(0, 32)
        .Select(i => (Math.Cos(i * Math.PI / 16), Math.Sin(i * Math.PI / 16))).ToArray();

    /// <summary>Measure the same 32 radial boundary sectors at one selected
    /// fit. This is useful for explaining a rejected fit without retaining
    /// per-candidate evidence throughout the bounded search.</summary>
    public static BoundarySectorEvidence[] Inspect(byte[] bgr, int width, int height, Point proposal,
        double expectedRadius, Point centre, double radius, string? expectedKind = null)
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
            bool exteriorRejected = RejectExterior(bgr, inside, outside, exterior, expectedKind, null);
            evidence[i] = new(i, innerPoint, outerPoint, fineInnerPoint, fineOuterPoint,
                edge, exterior, fineEdge, exteriorRejected, !exteriorRejected && edge >= 16);
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
            || !double.IsFinite(minimumVisibleFraction) || minimumVisibleFraction < 14d / 32 || minimumVisibleFraction > 1)
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
            if (!sector.ExteriorRejected || !neighboringBallCentres.Any(neighbour =>
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
        bool collectSectorEvidence = false, string? expectedKind = null, BoundaryColourMask? colourMask = null)
    {
        PipelineDiagnostics.Count("boundaryRefineCalls");
        using var scoringTiming = PipelineDiagnostics.Enabled ? PipelineDiagnostics.Measure("boundaryScoringMs") : null;
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
                bool exteriorRejected = RejectExterior(bgr, inside, outside, exterior, expectedKind, colourMask);
                if (exteriorRejected) { edge = 0; fineEdge = 0; }
                if (edge >= 16) supported++;
                if (sectorEvidence is not null)
                    sectorEvidence[i] = new(i, innerPoint, outerPoint, fineInnerPoint, fineOuterPoint,
                        measuredEdge, exterior, measuredFineEdge, exteriorRejected, !exteriorRejected && measuredEdge >= 16);
                // Cap strong highlights so a few patch/shadow edges cannot dominate.
                score += Math.Min(60, fineEdge) + .1 * Math.Min(60, edge);
            }
            // Coarse candidates need not already meet the final support gate;
            // otherwise grid quantization can prevent subpixel refinement entirely.
            if (supported < 12) return;
            score = supported * 2 + score / directions.Length - .1 * Math.Abs(radius - expectedRadius);
            var candidate = new BallBoundary(new(x, y), radius, supported / (double)directions.Length, score)
            { SectorEvidence = sectorEvidence ?? [] };
            if (reference && collectSectorEvidence) proposalFit = candidate;
            if (best is null || score > best.Score) best = candidate;
            if (supported >= minimumSupportedFraction * directions.Length
                && (supportedBest is null || score > supportedBest.Score)) supportedBest = candidate;
        }
    }

    static bool RejectExterior(byte[] bgr, int inside, int outside, double exterior, string? expectedKind,
        BoundaryColourMask? colourMask)
    {
        // A recognized red/yellow body can end at a dark cast shadow before
        // the sample reaches cloth. Require the radial pair to cross that
        // body's production colour mask; a far-cloth mismatch alone is not an
        // exterior-body rejection. This also rejects number-patch edges and
        // same-colour contacts, where the candidate's inner/outer pixels still
        // belong to the same painted body class.
        if (expectedKind is "red" or "yellow")
            return colourMask is null
                ? !MatchesProductionColourMask(bgr, inside, expectedKind)
                    || MatchesProductionColourMask(bgr, outside, expectedKind)
                : !colourMask.Matches(expectedKind, inside) || colourMask.Matches(expectedKind, outside);
        return exterior > 40;
    }

    static bool MatchesProductionColourMask(byte[] bgr, int offset, string kind)
    {
        double blue = bgr[offset], green = bgr[offset + 1], red = bgr[offset + 2];
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double delta = maximum - minimum;
        double hueDegrees = 0;
        if (delta > 0)
        {
            if (maximum == red)
            {
                hueDegrees = 60 * ((green - blue) / delta);
                if (hueDegrees < 0) hueDegrees += 360;
            }
            else if (maximum == green) hueDegrees = 60 * ((blue - red) / delta + 2);
            else hueDegrees = 60 * ((red - green) / delta + 4);
        }
        int hue = (int)Math.Round(hueDegrees / 2);
        if (hue >= 180) hue -= 180;
        int saturation = maximum <= 0 ? 0 : (int)Math.Round(delta * 255 / maximum);
        if (saturation < 100 || maximum < 30) return false;
        return kind == "red"
            ? hue is <= 10 or >= 170
            : hue is >= 10 and <= 40;
    }
}
