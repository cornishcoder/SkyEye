namespace SkyEye.Core;

public readonly record struct Point(double X, double Y)
{
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    public static Point operator *(Point a, double scale) => new(a.X * scale, a.Y * scale);
    public double Length => Math.Sqrt(X * X + Y * Y);
    public double Dot(Point b) => X * b.X + Y * b.Y;
}
public sealed class TableCalibration
{
    public Point[] Corners { get; }
    readonly double[] coefficients;
    public TableCalibration(Point[] corners)
    {
        if (corners.Length != 4 || corners.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X is < 0 or > 1 || p.Y is < 0 or > 1)) throw new ArgumentException("Select four table corners in the image.");
        double sign = 0;
        for (int i = 0; i < 4; i++) { var a = corners[(i + 1) % 4] - corners[i]; var b = corners[(i + 2) % 4] - corners[(i + 1) % 4]; double cross = a.X * b.Y - a.Y * b.X; if (Math.Abs(cross) < 0.0001 || sign != 0 && Math.Sign(cross) != Math.Sign(sign)) throw new ArgumentException("Corners must form a convex table boundary in order."); sign = cross; }
        Corners = corners.ToArray();
        Point[] target = [new(0, 0), new(2, 0), new(2, 1), new(0, 1)]; var matrix = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = corners[i].X, y = corners[i].Y, u = target[i].X, v = target[i].Y;
            matrix[2 * i, 0] = x; matrix[2 * i, 1] = y; matrix[2 * i, 2] = 1; matrix[2 * i, 6] = -u * x; matrix[2 * i, 7] = -u * y; matrix[2 * i, 8] = u;
            matrix[2 * i + 1, 3] = x; matrix[2 * i + 1, 4] = y; matrix[2 * i + 1, 5] = 1; matrix[2 * i + 1, 6] = -v * x; matrix[2 * i + 1, 7] = -v * y; matrix[2 * i + 1, 8] = v;
        }
        for (int c = 0; c < 8; c++)
        {
            int pivot = c; for (int row = c + 1; row < 8; row++) if (Math.Abs(matrix[row, c]) > Math.Abs(matrix[pivot, c])) pivot = row;
            if (Math.Abs(matrix[pivot, c]) < 1e-10) throw new ArgumentException("Calibration is degenerate.");
            for (int col = c; col < 9; col++) (matrix[c, col], matrix[pivot, col]) = (matrix[pivot, col], matrix[c, col]);
            double scale = matrix[c, c]; for (int col = c; col < 9; col++) matrix[c, col] /= scale;
            for (int row = 0; row < 8; row++) if (row != c) { double amount = matrix[row, c]; for (int col = c; col < 9; col++) matrix[row, col] -= amount * matrix[c, col]; }
        }
        coefficients = Enumerable.Range(0, 8).Select(i => matrix[i, 8]).ToArray();
    }
    public Point Map(Point image)
    {
        double denominator = coefficients[6] * image.X + coefficients[7] * image.Y + 1;
        return new((coefficients[0] * image.X + coefficients[1] * image.Y + coefficients[2]) / denominator, (coefficients[3] * image.X + coefficients[4] * image.Y + coefficients[5]) / denominator);
    }
    public Point Unmap(Point table)
    {
        double a = coefficients[0], b = coefficients[1], c = coefficients[2], d = coefficients[3], e = coefficients[4], f = coefficients[5], g = coefficients[6], h = coefficients[7];
        double denominator = (d * h - e * g) * table.X + (b * g - a * h) * table.Y + a * e - b * d;
        return new(((e - f * h) * table.X + (c * h - b) * table.Y + b * f - c * e) / denominator,
            ((f * g - d) * table.X + (a - c * g) * table.Y + c * d - a * f) / denominator);
    }
}
public sealed record Ball(string Id, string Kind, Point Position, double Confidence);
public sealed record TableState(long Version, DateTimeOffset CapturedAt, Ball[] Balls, string Group, bool Settled, bool Occluded, bool Confirmed, string CalibrationId);
public sealed record Shot(string Id, string BallId, int Pocket, Point GhostBall, Point Target, Point Destination, double Score, string Explanation, long TableVersion)
{
    public Point[] CuePath { get; init; } = [];
    public Point[] TargetPath { get; init; } = [];
    public string Type { get; init; } = "direct";
    public double CutDegrees { get; init; }
    public double Difficulty { get; init; }
}

public static class PoolGeometry
{
    public const string RulesVersion = "WEPF International Eightball Revision 2c, May 2023";
    public const string RulesUrl = "https://www.wepf.org/docs/International%20Eightball%20Rules%20%28Revision%202c%29%20-%20A4%20Booklet.pdf";
    public const double Radius = 0.027;
    public static readonly Point[] Pockets = [new(0, 0), new(1, 0), new(2, 0), new(0, 1), new(1, 1), new(2, 1)];
    public static Shot[] Suggest(TableState state, DateTimeOffset now) => Analysis.ShotPlanner.Suggest(state, now);
    public static double DistanceToSegment(Point p, Point a, Point b) { var delta = b - a; double t = delta.Dot(delta) < 1e-12 ? 0 : Math.Clamp((p - a).Dot(delta) / delta.Dot(delta), 0, 1); return (p - (a + delta * t)).Length; }
}

