using SkyEye.Core;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion.Hybrid;

// Normalized camera -> viewport pixels. The same matrix drives the native
// texture, path drawing and inverse picking; it never contains media access.
public sealed class SceneProjection
{
    public double Left { get; }
    public double Top { get; }
    public double Width { get; }
    public double Height { get; }
    public bool Framed { get; }
    public string? Fallback { get; }
    public double[] Values { get; }
    readonly double[] inverse;
    SceneProjection(double left, double top, double width, double height, double[] values, bool framed = false, string? fallback = null)
    {
        Left = left; Top = top; Width = width; Height = height; Values = values; Framed = framed; Fallback = fallback;
        inverse = Inverse(values);
    }
    public static SceneProjection Portrait(double viewportWidth, double viewportHeight, double imageWidth, double imageHeight, string? fallback = null)
    {
        if (!ValidSize(viewportWidth, viewportHeight) || !ValidSize(imageWidth, imageHeight)) throw new ArgumentException("The video size is unavailable.");
        double scale = Math.Min(viewportWidth / imageHeight, viewportHeight / imageWidth);
        double width = imageHeight * scale, height = imageWidth * scale;
        double left = (viewportWidth - width) / 2, top = (viewportHeight - height) / 2;
        return new(left, top, width, height, [0, -width, left + width, height, 0, top, 0, 0, 1], fallback: fallback);
    }
    public static SceneProjection Table(double viewportWidth, double viewportHeight, double imageWidth, double imageHeight, TableCalibration calibration)
    {
        if (!ValidSize(viewportWidth, viewportHeight) || !ValidSize(imageWidth, imageHeight)) throw new ArgumentException("The video size is unavailable.");
        // Extend past the cloth to retain pocket mouths and cushions. If the
        // source cannot contain that border, keep the entire camera frame.
        const double border = .12;
        Point[] outer = [new(-border, -border), new(2 + border, -border), new(2 + border, 1 + border), new(-border, 1 + border)];
        var source = outer.Select(calibration.Unmap).ToArray();
        if (source.Any(p => !InImage(p))) return Portrait(viewportWidth, viewportHeight, imageWidth, imageHeight, "Calibration has too little pocket border. Showing the whole camera.");
        double scale = Math.Min(viewportWidth / (1 + 2 * border), viewportHeight / (2 + 2 * border));
        double width = (1 + 2 * border) * scale, height = (2 + 2 * border) * scale;
        double left = (viewportWidth - width) / 2, top = (viewportHeight - height) / 2;
        Point[] target = [new(left + width, top), new(left + width, top + height), new(left, top + height), new(left, top)];
        try { return new(left, top, width, height, Solve(source, target), framed: true); }
        catch (ArgumentException) { return Portrait(viewportWidth, viewportHeight, imageWidth, imageHeight, "Calibration cannot frame this view. Showing the whole camera."); }
    }
    public Point Screen(Point image) => Map(Values, image);
    public Point? Image(Point screen)
    {
        if (!double.IsFinite(screen.X) || !double.IsFinite(screen.Y) || screen.X < Left || screen.X > Left + Width || screen.Y < Top || screen.Y > Top + Height) return null;
        var image = Map(inverse, screen); return InImage(image) ? image : null;
    }
    static bool ValidSize(double width, double height) => double.IsFinite(width) && double.IsFinite(height) && width > 0 && height > 0;
    static bool InImage(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1;
    static Point Map(double[] a, Point p)
    {
        double denominator = a[6] * p.X + a[7] * p.Y + a[8];
        return new((a[0] * p.X + a[1] * p.Y + a[2]) / denominator, (a[3] * p.X + a[4] * p.Y + a[5]) / denominator);
    }
    static double[] Solve(Point[] source, Point[] target)
    {
        var a = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = source[i].X, y = source[i].Y, u = target[i].X, v = target[i].Y;
            a[2 * i, 0] = x; a[2 * i, 1] = y; a[2 * i, 2] = 1; a[2 * i, 6] = -u * x; a[2 * i, 7] = -u * y; a[2 * i, 8] = u;
            a[2 * i + 1, 3] = x; a[2 * i + 1, 4] = y; a[2 * i + 1, 5] = 1; a[2 * i + 1, 6] = -v * x; a[2 * i + 1, 7] = -v * y; a[2 * i + 1, 8] = v;
        }
        for (int col = 0; col < 8; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < 8; row++) if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col])) pivot = row;
            if (Math.Abs(a[pivot, col]) < 1e-10) throw new ArgumentException("Invalid scene transform.");
            for (int c = col; c < 9; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
            double divisor = a[col, col]; for (int c = col; c < 9; c++) a[col, c] /= divisor;
            for (int row = 0; row < 8; row++) if (row != col) { double factor = a[row, col]; for (int c = col; c < 9; c++) a[row, c] -= factor * a[col, c]; }
        }
        var result = Enumerable.Range(0, 8).Select(i => a[i, 8]).Append(1d).ToArray();
        if (result.Any(v => !double.IsFinite(v))) throw new ArgumentException("Invalid scene transform.");
        // A projective horizon through the image could flip or explode content.
        double[] denominators = [result[8], result[6] + result[8], result[6] + result[7] + result[8], result[7] + result[8]];
        if (denominators.Any(v => v <= 1e-6)) throw new ArgumentException("Invalid scene transform.");
        return result;
    }
    static double[] Inverse(double[] a)
    {
        double[] b = [a[4] * a[8] - a[5] * a[7], a[2] * a[7] - a[1] * a[8], a[1] * a[5] - a[2] * a[4],
            a[5] * a[6] - a[3] * a[8], a[0] * a[8] - a[2] * a[6], a[2] * a[3] - a[0] * a[5],
            a[3] * a[7] - a[4] * a[6], a[1] * a[6] - a[0] * a[7], a[0] * a[4] - a[1] * a[3]];
        double determinant = a[0] * b[0] + a[1] * b[3] + a[2] * b[6];
        if (!double.IsFinite(determinant) || Math.Abs(determinant) < 1e-10) throw new ArgumentException("Invalid scene transform.");
        return b.Select(v => v / determinant).ToArray();
    }
}
