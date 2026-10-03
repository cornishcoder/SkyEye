namespace SkyEye.Core.Analysis;

public readonly record struct PathSegment(Point Start, Point End)
{
    public double Length => (End - Start).Length;
}
public sealed record ShotPaths(Point Ghost, PathSegment[] CuePath, PathSegment[] TargetPath, double Angle, double Length, double Difficulty);

/// <summary>Port of shot_init.py and Shot.py. Geometry is in portrait analysis pixels.</summary>
public static class ShotMath
{
    public static ShotPaths? Create(Point cue, Point target, Point pocket, Point? targetBounce = null, Point? cueBounce = null,
        double radius = 23, bool pythonRounding = false)
    {
        if (!(radius > 0) || !double.IsFinite(radius) || new[] { cue, target, pocket }.Any(p => !Finite(p)) ||
            targetBounce is { } tb && !Finite(tb) || cueBounce is { } cb && !Finite(cb)) return null;
        Point Convert(Point p) => pythonRounding ? new((int)p.X, (int)p.Y) : p;
        PathSegment[] targetPath = targetBounce is { } bounce
            ? [new(target, Convert(bounce)), new(Convert(bounce), Convert(pocket))]
            : [new(target, Convert(pocket))];
        var direction = targetPath[0].End - target;
        if (direction.Length < 1e-9) return null;
        var ghost = target - direction * (2 * radius / direction.Length);
        PathSegment[] cuePath = cueBounce is { } reflected
            ? [new(cue, Convert(reflected)), new(Convert(reflected), Convert(ghost))]
            : [new(cue, Convert(ghost))];
        var incoming = cuePath[^1].Start - cuePath[^1].End;
        if (incoming.Length < 1e-9 || targetPath.Any(s => s.Length < 1e-9) || cuePath.Any(s => s.Length < 1e-9)) return null;
        double angle = Math.Acos(Math.Clamp(incoming.Dot(direction) / (incoming.Length * direction.Length), -1, 1)) * 180 / Math.PI;
        if (pythonRounding) angle = Math.Round(angle, 1, MidpointRounding.ToEven);
        double length = targetPath.Sum(s => s.Length) + cuePath.Sum(s => s.Length);
        int penalty = targetPath.Length > 1 && cuePath.Length > 1 ? 8 : targetPath.Length > 1 || cuePath.Length > 1 ? 4 : 1;
        return new(ghost, cuePath, targetPath, angle, length, ((180 - angle) * 300 + length) * penalty);
    }
    public static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}
