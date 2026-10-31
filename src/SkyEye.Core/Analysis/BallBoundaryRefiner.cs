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
    public bool ColourMaskTransitionUsed { get; init; }
    public bool DarkBodyTransitionUsed { get; init; }
}

/// <summary>Evaluation-only result from a pocket-aware partial-arc fit. This
/// probe is not used by production detection and does not relax its support gate.</summary>
public sealed record PocketAwarePartialArcProbeResult(BallBoundary? Candidate,
    int[] PocketOccludedSectorIndices, int[] VisibleSectorIndices,
    int[] SupportedVisibleSectorIndices, int[] UnexplainedVisibleSectorIndices,
    int VisibleSectorCount, int SupportedVisibleSectorCount,
    int LongestContiguousSupportedRun, double SupportedArcSpanDegrees,
    int MinimumVisibleSectors, int MinimumSupportedVisibleSectors,
    int MinimumContiguousSupportedRun, double MinimumSupportedArcSpanDegrees,
    double? CandidateToProposalOffset, double? CandidateToProposalOffsetFraction,
    double MaximumCandidateToProposalOffset, double MaximumCandidateToProposalOffsetFraction,
    bool DiagnosticArcChecksPass);

/// <summary>Bounded current-image refinement of an existing circle proposal.
/// Raw colour edges distinguish painted bodies from cloth/shadow and interior patches.
/// A size prior bounds the search; it never supplies a body without edge support.</summary>
public static class BallBoundaryRefiner
{
    const double MinimumColourMaskEdgeContrast = 8;
    const double MinimumColourMaskFineEdgeContrast = 3;
    const int MinimumNonColourBackgroundSamples = 24;
    const int MinimumColourBackgroundSamples = 17;
    // A fitted, partially occluded edge can shift slightly from its dark seed.
    // Allow a narrow measured tolerance when relating failed edge sectors to
    // independently fitted neighboring ball centres.
    public const double ConservativeOcclusionNeighborRadius = 1.25;
    static readonly (double X, double Y)[] directions = Enumerable.Range(0, 32)
        .Select(i => (Math.Cos(i * Math.PI / 16), Math.Sin(i * Math.PI / 16))).ToArray();

    /// <summary>Measure the same 32 radial boundary sectors at one selected
    /// fit. This is useful for explaining a rejected fit without retaining
    /// per-candidate evidence throughout the bounded search.</summary>
    public static BoundarySectorEvidence[] Inspect(byte[] bgr, int width, int height, Point proposal,
        double expectedRadius, Point centre, double radius, string? expectedKind = null,
        bool allowDarkBodyEdgeTransitions = false, int darkBodyValueCeiling = 40)
    {
        if (width <= 0 || height <= 0 || bgr.Length != checked(width * height * 3)
            || !double.IsFinite(expectedRadius) || expectedRadius < 5 || expectedRadius > 100
            || !double.IsFinite(radius) || radius < 5 || radius > 125
            || !double.IsFinite(proposal.X) || !double.IsFinite(proposal.Y)
            || !double.IsFinite(centre.X) || !double.IsFinite(centre.Y)
            || darkBodyValueCeiling is < 0 or > 255)
            throw new ArgumentException("Invalid boundary image or fit.");
        var background = new double[3];
        for (int channel = 0; channel < 3; channel++)
        {
            var surrounding = directions.Select(d => (X: (int)Math.Round(proposal.X + d.X * expectedRadius * 1.7), Y: (int)Math.Round(proposal.Y + d.Y * expectedRadius * 1.7)))
                .Where(p => p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)
                .Select(p => bgr[(p.Y * width + p.X) * 3 + channel]).Order().ToArray();
            if (surrounding.Length < MinimumBackgroundSamples(expectedKind)) return [];
            background[channel] = surrounding[surrounding.Length / 2];
        }

        double offset = Math.Max(2, expectedRadius * .12);
        if (!FitsSampleBounds(centre.X, centre.Y, radius, offset, width, height))
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
            bool colourMaskTransitionUsed = IsColourMaskTransitionSupport(expectedKind, exteriorRejected, edge, fineEdge);
            bool darkBodyTransitionUsed = allowDarkBodyEdgeTransitions && expectedKind == "black"
                && MaximumChannel(bgr, inside) <= darkBodyValueCeiling
                && MaximumChannel(bgr, outside) > darkBodyValueCeiling
                && edge >= 16;
            bool supported = darkBodyTransitionUsed
                || !exteriorRejected && (edge >= 16 || colourMaskTransitionUsed);
            evidence[i] = new(i, innerPoint, outerPoint, fineInnerPoint, fineOuterPoint,
                edge, exterior, fineEdge, exteriorRejected, supported)
            { ColourMaskTransitionUsed = colourMaskTransitionUsed, DarkBodyTransitionUsed = darkBodyTransitionUsed };
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

        double occlusionRadius = expectedRadius * ConservativeOcclusionNeighborRadius;
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

    /// <summary>List radial sectors whose measured edge-sample segment intersects
    /// any supplied calibrated pocket occlusion disk. Evaluation-only helper.</summary>
    public static int[] GetPocketOccludedSectorIndexes(IReadOnlyList<BoundarySectorEvidence> sectors,
        IReadOnlyList<Point> calibratedPocketCentres, double pocketOcclusionRadius)
    {
        if (sectors.Count != directions.Length || calibratedPocketCentres is null
            || calibratedPocketCentres.Count == 0
            || calibratedPocketCentres.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))
            || !double.IsFinite(pocketOcclusionRadius) || pocketOcclusionRadius <= 0)
            throw new ArgumentException("Invalid pocket occlusion geometry.");
        return sectors.Where(sector => IsPocketOccluded(sector, calibratedPocketCentres, pocketOcclusionRadius))
            .Select(sector => sector.Index).ToArray();
    }

    /// <summary>Search a bounded centre/radius neighborhood while treating only
    /// measured radial sample segments inside the supplied calibrated pocket
    /// masks as geometrically occluded. Remaining supported sectors must still
    /// form a sufficiently long contiguous arc. This evaluation-only probe is
    /// deliberately separate from Refine, whose production gate is unchanged.</summary>
    public static PocketAwarePartialArcProbeResult ProbePocketAwarePartialArc(byte[] bgr,
        int width, int height, Point proposal, double expectedRadius,
        IReadOnlyList<Point> calibratedPocketCentres, double pocketOcclusionRadius,
        string? expectedKind = "black", double maximumCentreOffsetFraction = .8)
    {
        const int minimumVisibleSectors = 16;
        const int minimumSupportedVisibleSectors = 8;
        const int minimumContiguousSupportedRun = 5;
        const double minimumSupportedArcSpanDegrees = 45;
        const double maximumCandidateToProposalOffsetFraction = .8;

        if (width <= 0 || height <= 0 || bgr.Length != checked(width * height * 3)
            || !double.IsFinite(expectedRadius) || expectedRadius < 5 || expectedRadius > 100
            || !double.IsFinite(proposal.X) || !double.IsFinite(proposal.Y)
            || calibratedPocketCentres is null || calibratedPocketCentres.Count == 0
            || calibratedPocketCentres.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))
            || !double.IsFinite(pocketOcclusionRadius) || pocketOcclusionRadius <= 0 || pocketOcclusionRadius > 250
            || !double.IsFinite(maximumCentreOffsetFraction) || maximumCentreOffsetFraction is < .2 or > 1.5)
            throw new ArgumentException("Invalid pocket-aware boundary probe input.");

        double step = Math.Max(1.5, expectedRadius / 9);
        double extent = expectedRadius * maximumCentreOffsetFraction;
        double maximumCandidateToProposalOffset = expectedRadius * maximumCandidateToProposalOffsetFraction;
        BallBoundary? best = null;
        bool bestPassesChecks = false;
        Consider(proposal.X, proposal.Y, expectedRadius);
        for (double y = proposal.Y - extent; y <= proposal.Y + extent; y += step)
            for (double x = proposal.X - extent; x <= proposal.X + extent; x += step)
                for (double radius = expectedRadius * .75; radius <= expectedRadius * 1.25; radius += step)
                    Consider(x, y, radius);

        if (best is not null)
        {
            double fine = step / 3;
            var coarse = best;
            for (double y = coarse.Centre.Y - step; y <= coarse.Centre.Y + step; y += fine)
                for (double x = coarse.Centre.X - step; x <= coarse.Centre.X + step; x += fine)
                    for (double radius = Math.Max(expectedRadius * .75, coarse.Radius - step);
                         radius <= Math.Min(expectedRadius * 1.25, coarse.Radius + step); radius += fine)
                        Consider(x, y, radius);
        }

        if (best is null)
            return new(null, [], [], [], [], 0, 0, 0, 0,
                minimumVisibleSectors, minimumSupportedVisibleSectors, minimumContiguousSupportedRun,
                minimumSupportedArcSpanDegrees, null, null, maximumCandidateToProposalOffset,
                maximumCandidateToProposalOffsetFraction, false);

        var selectedEvidence = best.SectorEvidence;
        var selectedOccluded = GetPocketOccludedSectorIndexes(selectedEvidence,
            calibratedPocketCentres, pocketOcclusionRadius);
        var selectedOccludedSet = selectedOccluded.ToHashSet();
        var selectedVisible = selectedEvidence.Where(sector => !selectedOccludedSet.Contains(sector.Index)).ToArray();
        var selectedSupported = selectedVisible.Where(sector => sector.Supported).ToArray();
        var selectedUnexplained = selectedVisible.Where(sector => !sector.Supported).ToArray();
        int longestRun = LongestCircularRun(selectedSupported.Select(sector => sector.Index).ToHashSet());
        double arcSpan = longestRun <= 1 ? 0 : (longestRun - 1) * (360d / directions.Length);
        double candidateToProposalOffset = (best.Centre - proposal).Length;
        bool checksPass = selectedVisible.Length >= minimumVisibleSectors
            && selectedSupported.Length >= minimumSupportedVisibleSectors
            && longestRun >= minimumContiguousSupportedRun
            && arcSpan >= minimumSupportedArcSpanDegrees
            && candidateToProposalOffset <= maximumCandidateToProposalOffset;
        return new(best, selectedOccluded, selectedVisible.Select(sector => sector.Index).ToArray(),
            selectedSupported.Select(sector => sector.Index).ToArray(),
            selectedUnexplained.Select(sector => sector.Index).ToArray(),
            selectedVisible.Length, selectedSupported.Length, longestRun, arcSpan,
            minimumVisibleSectors, minimumSupportedVisibleSectors, minimumContiguousSupportedRun,
            minimumSupportedArcSpanDegrees, candidateToProposalOffset,
            candidateToProposalOffset / expectedRadius, maximumCandidateToProposalOffset,
            maximumCandidateToProposalOffsetFraction, checksPass);

        void Consider(double x, double y, double radius)
        {
            double offset = Math.Max(2, expectedRadius * .12);
            if (!FitsSampleBounds(x, y, radius, offset, width, height)) return;
            var sectors = Inspect(bgr, width, height, proposal, expectedRadius,
                new Point(x, y), radius, expectedKind);
            if (sectors.Length != directions.Length) return;

            var occluded = GetPocketOccludedSectorIndexes(sectors, calibratedPocketCentres, pocketOcclusionRadius)
                .ToHashSet();
            var visible = sectors.Where(sector => !occluded.Contains(sector.Index)).ToArray();
            if (visible.Length < minimumVisibleSectors) return;
            var supported = visible.Where(sector => sector.Supported).ToArray();
            var supportIndexes = supported.Select(sector => sector.Index).ToHashSet();
            int longest = LongestCircularRun(supportIndexes);
            double span = longest <= 1 ? 0 : (longest - 1) * (360d / directions.Length);
            double candidateToProposalOffset = (new Point(x, y) - proposal).Length;
            bool passesChecks = supported.Length >= minimumSupportedVisibleSectors
                && longest >= minimumContiguousSupportedRun
                && span >= minimumSupportedArcSpanDegrees
                && candidateToProposalOffset <= maximumCandidateToProposalOffset;

            double edgeScore = visible.Sum(sector => Math.Min(60, sector.FineEdgeContrast)
                + .1 * Math.Min(60, sector.EdgeContrast));
            double score = supported.Length * 2 + edgeScore / visible.Length
                - .1 * Math.Abs(radius - expectedRadius);
            var candidate = new BallBoundary(new(x, y), radius,
                supported.Length / (double)directions.Length, score)
            { SectorEvidence = sectors };
            bool take = best is null
                || passesChecks && !bestPassesChecks
                || passesChecks == bestPassesChecks && score > best.Score;
            if (take)
            {
                best = candidate;
                bestPassesChecks = passesChecks;
            }
        }

    }

    static bool IsPocketOccluded(BoundarySectorEvidence sector,
        IReadOnlyList<Point> calibratedPocketCentres, double pocketOcclusionRadius)
        => calibratedPocketCentres.Any(pocket =>
            DistanceToSegment(pocket, sector.InnerPoint, sector.OuterPoint) <= pocketOcclusionRadius);

    static int LongestCircularRun(IReadOnlySet<int> supportedIndexes)
    {
        if (supportedIndexes.Count == 0) return 0;
        if (supportedIndexes.Count == directions.Length) return directions.Length;
        int longest = 0, current = 0;
        for (int i = 0; i < directions.Length * 2; i++)
        {
            if (supportedIndexes.Contains(i % directions.Length))
            {
                current = Math.Min(directions.Length, current + 1);
                longest = Math.Max(longest, current);
            }
            else current = 0;
        }
        return longest;
    }

    static double DistanceToSegment(Point point, Point start, Point end)
    {
        var segment = end - start;
        double lengthSquared = segment.Dot(segment);
        double amount = lengthSquared < 1e-12 ? 0 : Math.Clamp((point - start).Dot(segment) / lengthSquared, 0, 1);
        return (point - (start + segment * amount)).Length;
    }

    public static BallBoundary? Refine(byte[] bgr, int width, int height, Point proposal, double expectedRadius,
        double minimumSupportedFraction = ObservationPolicy.MinimumSupportedBoundaryFraction,
        bool collectSectorEvidence = false, string? expectedKind = null, BoundaryColourMask? colourMask = null,
        double maximumCentreOffsetFraction = .4, bool allowDarkBodyEdgeTransitions = false,
        int darkBodyValueCeiling = 40)
        => RefineCore(bgr, width, height, proposal, expectedRadius, minimumSupportedFraction,
            collectSectorEvidence, expectedKind, colourMask, maximumCentreOffsetFraction,
            allowDarkBodyEdgeTransitions, darkBodyValueCeiling, enableScoreUpperBound: true,
            useGlobalBestForVerification: false, pruneStats: null);

    // Differential verification entry point. Kept internal so normal callers
    // always use the optimized path; tests compare it with the original full scan.
    internal static BallBoundary? RefineForVerification(byte[] bgr, int width, int height, Point proposal,
        double expectedRadius, double minimumSupportedFraction, bool collectSectorEvidence,
        string? expectedKind, BoundaryColourMask? colourMask, double maximumCentreOffsetFraction,
        bool allowDarkBodyEdgeTransitions, int darkBodyValueCeiling, bool enableScoreUpperBound,
        bool useGlobalBestForVerification,
        out int prunedCandidates, out int skippedSectors)
    {
        var stats = new RefinementPruneStats();
        var result = RefineCore(bgr, width, height, proposal, expectedRadius, minimumSupportedFraction,
            collectSectorEvidence, expectedKind, colourMask, maximumCentreOffsetFraction,
            allowDarkBodyEdgeTransitions, darkBodyValueCeiling, enableScoreUpperBound,
            useGlobalBestForVerification, stats);
        prunedCandidates = stats.PrunedCandidates;
        skippedSectors = stats.SkippedSectors;
        return result;
    }

    static BallBoundary? RefineCore(byte[] bgr, int width, int height, Point proposal, double expectedRadius,
        double minimumSupportedFraction, bool collectSectorEvidence, string? expectedKind,
        BoundaryColourMask? colourMask, double maximumCentreOffsetFraction,
        bool allowDarkBodyEdgeTransitions, int darkBodyValueCeiling,
        bool enableScoreUpperBound, bool useGlobalBestForVerification,
        RefinementPruneStats? pruneStats)
    {
        PipelineDiagnostics.Count("boundaryRefineCalls");
        using var scoringTiming = PipelineDiagnostics.Enabled ? PipelineDiagnostics.Measure("boundaryScoringMs") : null;
        if (width <= 0 || height <= 0 || bgr.Length != checked(width * height * 3)
            || !double.IsFinite(expectedRadius) || expectedRadius < 5 || expectedRadius > 100
            || !double.IsFinite(proposal.X) || !double.IsFinite(proposal.Y)
            || !double.IsFinite(minimumSupportedFraction) || minimumSupportedFraction is < .5 or > 1
            || !double.IsFinite(maximumCentreOffsetFraction) || maximumCentreOffsetFraction is < .2 or > 1.5
            || darkBodyValueCeiling is < 0 or > 255)
            throw new ArgumentException("Invalid boundary image or proposal.");
        double step = Math.Max(1.5, expectedRadius / 9), extent = expectedRadius * maximumCentreOffsetFraction;
        var background = new double[3];
        for (int channel = 0; channel < 3; channel++)
        {
            var surrounding = directions.Select(d => (X: (int)Math.Round(proposal.X + d.X * expectedRadius * 1.7), Y: (int)Math.Round(proposal.Y + d.Y * expectedRadius * 1.7)))
                .Where(p => p.X >= 0 && p.X < width && p.Y >= 0 && p.Y < height)
                .Select(p => bgr[(p.Y * width + p.X) * 3 + channel]).Order().ToArray();
            if (surrounding.Length < MinimumBackgroundSamples(expectedKind)) return null;
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
            if (!FitsSampleBounds(x, y, radius, offset, width, height)) return;
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
                bool colourMaskTransitionUsed = IsColourMaskTransitionSupport(expectedKind, exteriorRejected, measuredEdge, measuredFineEdge);
                bool darkBodyTransitionUsed = allowDarkBodyEdgeTransitions && expectedKind == "black"
                    && MaximumChannel(bgr, inside) <= darkBodyValueCeiling
                    && MaximumChannel(bgr, outside) > darkBodyValueCeiling
                    && measuredEdge >= 16;
                if (exteriorRejected && !darkBodyTransitionUsed) { edge = 0; fineEdge = 0; }
                bool supportedSector = darkBodyTransitionUsed
                    || !exteriorRejected && (measuredEdge >= 16 || colourMaskTransitionUsed);
                if (supportedSector) supported++;
                if (sectorEvidence is not null)
                    sectorEvidence[i] = new(i, innerPoint, outerPoint, fineInnerPoint, fineOuterPoint,
                        measuredEdge, exterior, measuredFineEdge, exteriorRejected, supportedSector)
                    { ColourMaskTransitionUsed = colourMaskTransitionUsed, DarkBodyTransitionUsed = darkBodyTransitionUsed };
                // Cap strong highlights so a few patch/shadow edges cannot dominate.
                score += Math.Min(60, fineEdge) + .1 * Math.Min(60, edge);

                // Every remaining sector can add at most two support points and
                // 60 + 0.1*60 = 66 edge-score points. Preserve the separate
                // supported winner: if this candidate can still reach its gate,
                // only an existing supportedBest is a valid cutoff. Otherwise
                // compare against the global best. Keep the exact seed and
                // evidence-collection paths exhaustive so ProposalFit and
                // per-sector diagnostics remain unchanged.
                if (enableScoreUpperBound && !reference && !collectSectorEvidence && best is not null)
                {
                    int remaining = directions.Length - i - 1;
                    double maximumPossibleScore = 2d * (supported + remaining)
                        + (score + 66d * remaining) / directions.Length
                        - .1 * Math.Abs(radius - expectedRadius);
                    bool canReachSupportGate = supported + remaining
                        >= minimumSupportedFraction * directions.Length;
                    double? scoreToBeat = useGlobalBestForVerification
                        ? best.Score
                        : canReachSupportGate ? supportedBest?.Score : best.Score;
                    if (scoreToBeat is double winningScore
                        && maximumPossibleScore + 1e-9 < winningScore)
                    {
                        if (pruneStats is not null)
                        {
                            pruneStats.PrunedCandidates++;
                            pruneStats.SkippedSectors += remaining;
                        }
                        return;
                    }
                }
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

    sealed class RefinementPruneStats
    {
        public int PrunedCandidates { get; set; }
        public int SkippedSectors { get; set; }
    }

    static bool FitsSampleBounds(double x, double y, double radius, double offset, int width, int height)
    {
        // Pixel reads round floating-point coordinates. A continuous sample
        // just below width/height can therefore round to the first pixel past
        // the row or image; validate the rounded extrema before indexing.
        return Math.Round(x - radius - offset) >= 0
            && Math.Round(y - radius - offset) >= 0
            && Math.Round(x + radius + offset) < width
            && Math.Round(y + radius + offset) < height;
    }

    static int MaximumChannel(byte[] bgr, int offset)
        => Math.Max(bgr[offset], Math.Max(bgr[offset + 1], bgr[offset + 2]));

    static int MinimumBackgroundSamples(string? expectedKind)
        // A ball next to the warped table edge can lose several background
        // directions even though its visible colour boundary is usable. Keep
        // the stricter contrast baseline for non-colour fits; a colour fit's
        // median only needs a strict majority of the 32 background samples.
        => expectedKind is "red" or "yellow" ? MinimumColourBackgroundSamples : MinimumNonColourBackgroundSamples;

    static bool IsColourMaskTransitionSupport(string? expectedKind, bool exteriorRejected,
        double edgeContrast, double fineEdgeContrast)
        => !exteriorRejected
            && (expectedKind is "red" or "yellow")
            && edgeContrast < 16
            && edgeContrast >= MinimumColourMaskEdgeContrast
            && fineEdgeContrast >= MinimumColourMaskFineEdgeContrast;

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
