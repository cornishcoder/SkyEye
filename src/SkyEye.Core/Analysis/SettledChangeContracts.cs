namespace SkyEye.Core.Analysis;

// D3 settled-frame change evidence (docs/claude/d3-settled-change-design.md). Image-free records
// shared by the OpenCV comparer (SkyEye.Vision.Android) and, later, the Live Detect reducer.
// This slice covers comparing two settled rasters; the settle state machine, reducer rules and
// live wiring are not built yet.

public enum ChangeDirection { Arrival, Departure, Unclear }

/// <summary>Thresholds for comparing two settled rasters. Defaults are the design's half-scale values
/// (T4 v3 rule at 440x840: 400x800 table plus a 20 px margin). Tentative; set from shadow logs.</summary>
public sealed record SettledChangeSettings
{
    public int TableWidth { get; init; } = 400;            // raster px across the table's short side
    public int Margin { get; init; } = 20;                 // raster px outside the cushion edge
    public int TilePixels { get; init; } = 40;             // per-tile gain (median) field
    public double Threshold { get; init; } = .18;          // |log-ratio residual| after blur
    public double MinimumRegionBallAreas { get; init; } = .30;
    public double MaximumRegionBallAreas { get; init; } = 5;
    public double MinimumInscribedRadii { get; init; } = .6;
    public double MaximumInscribedRadii { get; init; } = 1.45;
    public double MaximumOutsideFraction { get; init; } = .05;
    public int RailTolerancePixels { get; init; } = 2;
    public int BandInset { get; init; } = 45;               // cushion band = table minus this inset
    public double LightingDriftGate { get; init; } = .08;  // cushion-band fraction above threshold
    public int MaximumRegions { get; init; } = 12;
    public int GridCellPixels { get; init; } = 20;
    public double GridCellFraction { get; init; } = .05;
    public double BallRadiusFraction { get; init; } = .027; // ball radius / table width
    // Slice 2: settle detection at Live Detect cadence (T4 `scan` rule at quarter size).
    public int MotionTilePixels { get; init; } = 16;        // quarter-size raster
    public double MotionThreshold { get; init; } = .08;
    public double StillMotionFraction { get; init; } = .0025;
    public int SettleMinimumPasses { get; init; } = 3;
    public double SettleMinimumSeconds { get; init; } = 1.2;
    public double MaximumPassGapSeconds { get; init; } = 1.5;
    public double MaximumComparisonGapSeconds { get; init; } = 60;
    public int WindowFrames { get; init; } = 3;
    public int TableHeight => 2 * TableWidth;
    public int RasterWidth => TableWidth + 2 * Margin;
    public int RasterHeight => TableHeight + 2 * Margin;
    public double BallRadius => BallRadiusFraction * TableWidth;
    public double BallArea => Math.PI * BallRadius * BallRadius;
}

/// <summary>One change component of at least <see cref="SettledChangeSettings.MinimumRegionBallAreas"/>.
/// Table coordinates follow Ball.Position: X 0..2 along the table, Y 0..1 across.</summary>
public sealed record SettledChangeRegion(
    double TableX, double TableY, double RasterX, double RasterY,
    string Zone, double AreaBalls, double InscribedRadiusBalls, double OutsideFraction, double Peak,
    bool BallSized, string? RejectReason, ChangeDirection Direction,
    double ClothBefore, double ClothAfter, double DarkBefore, double DarkAfter,
    double WhiteBefore, double WhiteAfter);

/// <summary>Result of comparing a reference (before) and current (after) settled raster.</summary>
public sealed record SettledChangeComparison(
    IReadOnlyList<string> GateReasons,
    double CushionBandDrift, double BedDrift, double BedP99,
    int RawComponentCount, IReadOnlyList<SettledChangeRegion> Regions,
    byte[] ChangeGrid, int GridColumns, int GridRows, double ComputeMs)
{
    public bool Gated => GateReasons.Count > 0;
}

public enum SettledChangeState { Unavailable, Moving, Settling, Settled }
public enum SettledChangeStatus { NoReference, Compared }

/// <summary>Evidence for one settled segment: compared against the last settled view before the
/// preceding motion, or NoReference for the first segment after a reset. Gate reasons include the
/// comparison's own gates and <c>gap_too_long</c>. Times are source microseconds.</summary>
public sealed record SettledChangeEvidence(
    Guid Epoch, long Sequence, SettledChangeStatus Status, IReadOnlyList<string> GateReasons,
    long? ReferenceSegmentStartUs, long? ReferenceSegmentEndUs,
    long? ReferenceWindowStartUs, long? ReferenceWindowEndUs,
    long CurrentWindowStartUs, long CurrentWindowEndUs,
    SettledChangeComparison? Comparison);

/// <summary>One processor pass. <see cref="Evidence"/> is the evidence in force (the latest settled
/// segment's); <see cref="EvidenceIsNew"/> is true only on the pass that produced it.</summary>
public sealed record SettledChangePass(
    SettledChangeState State, double? MotionFraction, int StillPasses, double ComputeMs,
    SettledChangeEvidence? Evidence, bool EvidenceIsNew, string? UnavailableReason = null)
{
    public static SettledChangePass Unavailable(string reason, double computeMs = 0) =>
        new(SettledChangeState.Unavailable, null, 0, computeMs, null, false, reason);
}

/// <summary>Live cost guard (Richard, 11 November): once at least <see cref="Window"/> passes are measured,
/// the processor turns itself off for the run when their median cost exceeds <see cref="LimitMs"/>.
/// The median ignores the occasional comparison pass, which is expected to cost more.</summary>
public sealed class SettledChangeCostGuard(int window = 20, double limitMs = 40)
{
    readonly Queue<double> recent = new();
    public int Window { get; } = window;
    public double LimitMs { get; } = limitMs;
    public double? MedianMs { get; private set; }

    /// <summary>Records one pass; true when the guard trips.</summary>
    public bool Add(double ms)
    {
        recent.Enqueue(ms);
        if (recent.Count > Window) recent.Dequeue();
        if (recent.Count < Window) return false;
        var sorted = recent.Order().ToArray();
        MedianMs = (sorted[(Window - 1) / 2] + sorted[Window / 2]) / 2;
        return MedianMs > LimitMs;
    }
}

/// <summary>Settle state machine (design section 2.1 step 3): a pass is still when motion is below
/// <see cref="SettledChangeSettings.StillMotionFraction"/>; a segment is settled after at least
/// <see cref="SettledChangeSettings.SettleMinimumPasses"/> consecutive still passes spanning at least
/// <see cref="SettledChangeSettings.SettleMinimumSeconds"/>. Unknown motion (first pass, or a source gap
/// over <see cref="SettledChangeSettings.MaximumPassGapSeconds"/>) restarts the count. Pure; no images.</summary>
public sealed class SettledSegmentTracker(SettledChangeSettings settings)
{
    int stillPasses;
    long runStartUs, lastStillUs;
    public SettledChangeState State { get; private set; } = SettledChangeState.Unavailable;
    public int StillPasses => stillPasses;
    /// <summary>Start of the current still run (the settled segment's start once settled).</summary>
    public long RunStartUs => runStartUs;
    public long LastStillUs => lastStillUs;

    public readonly record struct Step(SettledChangeState State, bool BecameSettled, bool LeftSettled,
        long SegmentStartUs, long SegmentEndUs);

    public Step Observe(long sourceTimeUs, double? motionFraction)
    {
        bool wasSettled = State == SettledChangeState.Settled;
        long segmentStart = runStartUs, segmentEnd = lastStillUs;
        if (motionFraction is { } motion && motion < settings.StillMotionFraction)
        {
            if (stillPasses == 0) runStartUs = sourceTimeUs;
            stillPasses++; lastStillUs = sourceTimeUs;
            bool settled = stillPasses >= settings.SettleMinimumPasses
                && (sourceTimeUs - runStartUs) / 1e6 >= settings.SettleMinimumSeconds;
            State = settled ? SettledChangeState.Settled : SettledChangeState.Settling;
            return new(State, settled && !wasSettled, false, runStartUs, lastStillUs);
        }
        stillPasses = 0;
        State = motionFraction is null ? SettledChangeState.Settling : SettledChangeState.Moving;
        return new(State, false, wasSettled, segmentStart, segmentEnd);
    }

    public void Reset() { stillPasses = 0; runStartUs = lastStillUs = 0; State = SettledChangeState.Unavailable; }
}
