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

    public static BallBoundary? Refine(byte[] bgr, int width, int height, Point proposal, double expectedRadius,
        double minimumSupportedFraction = ObservationPolicy.MinimumSupportedBoundaryFraction,
        bool collectSectorEvidence = false)
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
}
