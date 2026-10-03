namespace SkyEye.Core.Analysis;

/// <summary>Clockwise portrait convention used by the upstream 1000x2000 analysis.</summary>
public sealed class AnalysisCoordinates
{
    public int Width { get; }
    public int Height => Width * 2;
    public AnalysisCoordinates(int width = 1000)
    {
        if (width is < 100 or > 2000) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
    }
    public Point FromTable(Point table) => new(Width * (1 - table.Y), Width * table.X);
    public Point ToTable(Point analysis) => new(analysis.Y / Width, 1 - analysis.X / Width);
    // Upstream pocket order: UL, UR, CL, CR, LL, LR.
    public static int ToSkyEyePocket(int upstreamPocket) => upstreamPocket switch
    {
        0 => 3, 1 => 0, 2 => 4, 3 => 1, 4 => 5, 5 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(upstreamPocket))
    };
}
