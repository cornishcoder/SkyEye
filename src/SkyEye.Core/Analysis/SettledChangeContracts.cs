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
