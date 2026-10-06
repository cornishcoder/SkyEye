namespace SkyEye.Core.Analysis;

public sealed record ShotGeometry(int Width = 1000, double Radius = 27, double CushionInset = 27, double PocketInset = 40, double PocketMouth = 55)
{
    public void Validate()
    {
        if (Width is < 100 or > 2000 || new[] { Radius, CushionInset, PocketInset, PocketMouth }.Any(n => !double.IsFinite(n) || n <= 0 || n >= Width / 4.0) || CushionInset < Radius)
            throw new ArgumentException("Invalid ball, cushion or pocket dimensions.");
    }
}
public sealed record GeometricCandidate(int Pocket, int TargetCushion, int CueCushion, ShotPaths Paths);

/// <summary>Port of shots_calculations.py: reflection candidates, clearance and
/// ranking. Production corrections bound reflections and check every path leg.</summary>
public static class ShotPlanner
{
    // Portrait pockets and cushions use upstream identities; convert only at the app boundary.
    static readonly int[][] TargetCushions = [[2, 1], [3, 2], [2, 0], [2, 0], [0, 1], [3, 0]];
    public static Point? Reflect(Point start, Point end, Point railStart, Point railEnd)
    {
        if (new[] { start, end, railStart, railEnd }.Any(p => !ShotMath.Finite(p))) return null;
        bool horizontal = Math.Abs(railStart.Y - railEnd.Y) < 1e-9, vertical = Math.Abs(railStart.X - railEnd.X) < 1e-9;
        if (horizontal == vertical) return null;
        double axis = horizontal ? railStart.Y : railStart.X;
        double a = horizontal ? start.Y : start.X, b = horizontal ? end.Y : end.X;
        if ((a - axis) * (b - axis) < 0) return null; // both endpoints must lie inside the same cushion
        var mirrored = horizontal ? new Point(end.X, 2 * axis - end.Y) : new Point(2 * axis - end.X, end.Y);
        var direction = mirrored - start; double denominator = horizontal ? direction.Y : direction.X;
        if (Math.Abs(denominator) < 1e-9) return null;
        double t = (axis - a) / denominator;
        if (t <= 1e-9 || t >= 1 - 1e-9) return null;
        var intersection = start + direction * t;
        // Keep the exact rail axis; arithmetic drift before Python's integer conversion
        // otherwise changes a nominal x=40 contact into x=39.
        var bounce = horizontal ? new Point(intersection.X, axis) : new Point(axis, intersection.Y);
        double along = horizontal ? bounce.X : bounce.Y;
        double low = horizontal ? Math.Min(railStart.X, railEnd.X) : Math.Min(railStart.Y, railEnd.Y);
        double high = horizontal ? Math.Max(railStart.X, railEnd.X) : Math.Max(railStart.Y, railEnd.Y);
        return along >= low && along <= high ? bounce : null;
    }
    public static GeometricCandidate[] Generate(Point cue, Point target, ShotGeometry? settings = null, bool pythonRounding = false)
    {
        var geometry = settings ?? new(); geometry.Validate();
        if (!ShotMath.Finite(cue) || !ShotMath.Finite(target)) return [];
        double w = geometry.Width, h = w * 2, inset = geometry.PocketInset, rail = geometry.CushionInset;
        Point[] pockets = [new(inset, inset), new(w - inset, inset), new(inset, w), new(w - inset, w), new(inset, h - inset), new(w - inset, h - inset)];
        (Point Start, Point End)[] rails = [(new(rail, rail), new(w - rail, rail)), (new(w - rail, rail), new(w - rail, h - rail)),
            (new(rail, h - rail), new(w - rail, h - rail)), (new(rail, rail), new(rail, h - rail))];
        var candidates = new List<GeometricCandidate>();
        for (int pocket = 0; pocket < pockets.Length; pocket++)
        foreach (int targetRail in new[] { -1 }.Concat(TargetCushions[pocket]))
        {
            Point? bounce = targetRail < 0 ? null : Reflect(target, pockets[pocket], rails[targetRail].Start, rails[targetRail].End);
            if (targetRail >= 0 && bounce is null) continue;
            var direct = ShotMath.Create(cue, target, pockets[pocket], bounce, radius: geometry.Radius, pythonRounding: pythonRounding);
            if (direct is null) continue;
            candidates.Add(new(pocket, targetRail, -1, direct));
            // Python uses an integer ghost for its reflection intersection.
            Point ghost = pythonRounding ? new((int)direct.Ghost.X, (int)direct.Ghost.Y) : direct.Ghost;
            for (int cueRail = 0; cueRail < rails.Length; cueRail++)
            {
                var reflection = Reflect(cue, ghost, rails[cueRail].Start, rails[cueRail].End);
                if (reflection is null) continue;
                var paths = ShotMath.Create(cue, target, pockets[pocket], bounce, reflection, geometry.Radius, pythonRounding);
                if (paths is not null) candidates.Add(new(pocket, targetRail, cueRail, paths));
            }
        }
        return candidates.ToArray();
    }
    public static bool Valid(GeometricCandidate candidate, Point cue, Point target, Point[] obstacles, ShotGeometry? settings = null)
    {
        var geometry = settings ?? new(); geometry.Validate(); var paths = candidate.Paths;
        double w = geometry.Width, radius = geometry.Radius, rail = geometry.CushionInset;
        bool InBounds(Point p) => ShotMath.Finite(p) && p.X >= rail - 1e-6 && p.X <= w - rail + 1e-6 && p.Y >= rail - 1e-6 && p.Y <= w * 2 - rail + 1e-6;
        // A detected cushion ball can sit a few pixels beyond the ideal centre
        // rail. Keep it as a full collision obstacle; it is never a path endpoint.
        bool ObstacleInBounds(Point p) => ShotMath.Finite(p) && p.X >= 0 && p.X <= w && p.Y >= 0 && p.Y <= w * 2;
        bool Mouth(Point p) => new[] { new Point(0, 0), new Point(w, 0), new Point(0, w), new Point(w, w), new Point(0, w * 2), new Point(w, w * 2) }.Any(pocket => (p - pocket).Length < geometry.PocketMouth);
        if (candidate.Pocket is < 0 or > 5 || !double.IsFinite(paths.Angle) || paths.Angle <= 120 || !double.IsFinite(paths.Difficulty) || paths.CuePath.Length is < 1 or > 2 || paths.TargetPath.Length is < 1 or > 2 || obstacles.Any(p => !ObstacleInBounds(p))) return false;
        if ((paths.CuePath[0].Start - cue).Length > 1e-6 || (paths.TargetPath[0].Start - target).Length > 1e-6 || (paths.CuePath[^1].End - paths.Ghost).Length > 1e-6) return false;
        if (!InBounds(cue) || !InBounds(target) || !InBounds(paths.Ghost) ||
            paths.CuePath.Concat(paths.TargetPath).Any(s => !InBounds(s.Start) || !InBounds(s.End) || s.Length < 1e-6)) return false;
        if (paths.CuePath.Length > 1 && Mouth(paths.CuePath[0].End) || paths.TargetPath.Length > 1 && Mouth(paths.TargetPath[0].End)) return false;
        // IDs are excluded by the caller. Coordinate equality never exempts a third ball.
        if (paths.CuePath.Concat(paths.TargetPath).Any(s => obstacles.Any(p => PoolGeometry.DistanceToSegment(p, s.Start, s.End) < radius * 2 + 4))) return false;
        if (paths.CuePath.Any(s => PoolGeometry.DistanceToSegment(target, s.Start, s.End) < radius * 2 - 1e-6)) return false;
        // Conservative upstream check against the original cue location on the object path.
        if (paths.TargetPath.Any(s => PoolGeometry.DistanceToSegment(cue, s.Start, s.End) < radius * 2 - 1e-6)) return false;
        return true;
    }
    public static Shot[] Suggest(TableState state, DateTimeOffset now)
    {
        PipelineDiagnostics.Count("predictionCalls");
        using var timing = PipelineDiagnostics.Measure("predictionMs");
        if (!Eligible(state, now)) return [];
        var coordinates = new AnalysisCoordinates(); var cue = state.Balls.Single(b => b.Kind == "cue");
        var targets = state.Balls.Where(b => state.Group switch { "red" => b.Kind == "red", "yellow" => b.Kind == "yellow", "black" => b.Kind == "black", "open" => b.Kind is "red" or "yellow", _ => false });
        var shots = new List<Shot>(); Point[] Convert(PathSegment[] path) => path.Select(s => coordinates.ToTable(s.Start)).Append(coordinates.ToTable(path[^1].End)).ToArray();
        foreach (var target in targets)
        {
            Point cuePoint = coordinates.FromTable(cue.Position), targetPoint = coordinates.FromTable(target.Position);
            Point[] obstacles = state.Balls.Where(b => b.Id != cue.Id && b.Id != target.Id).Select(b => coordinates.FromTable(b.Position)).ToArray();
            foreach (var candidate in Generate(cuePoint, targetPoint))
            {
                if (!Valid(candidate, cuePoint, targetPoint, obstacles)) continue;
                var paths = candidate.Paths; int pocket = AnalysisCoordinates.ToSkyEyePocket(candidate.Pocket);
                string type = candidate.TargetCushion >= 0 && candidate.CueCushion >= 0 ? "bank-and-kick" : candidate.TargetCushion >= 0 ? "bank" : candidate.CueCushion >= 0 ? "kick" : "direct";
                string explanation = $"{char.ToUpperInvariant(type[0]) + type[1..]} {target.Kind} pot into pocket {pocket + 1}; cut {180 - paths.Angle:0.0}°. " +
                    (type == "direct" ? "" : "Cushion contacts are ideal geometric reflections. ") + "No spin, pace or positional outcome is predicted." + (state.Group == "open" ? " Check the intended group." : "");
                shots.Add(new($"{state.Version}:{target.Id}:{pocket}:{candidate.TargetCushion}:{candidate.CueCushion}", target.Id, pocket, coordinates.ToTable(paths.Ghost), target.Position,
                    coordinates.ToTable(paths.TargetPath[^1].End), 1 / (1 + paths.Difficulty), explanation, state.Version)
                    { CuePath = Convert(paths.CuePath), TargetPath = Convert(paths.TargetPath), Type = type, CutDegrees = 180 - paths.Angle, Difficulty = paths.Difficulty });
            }
        }
        // Stable tie ordering; ranking never mutates inputs or table state.
        return shots.OrderBy(s => s.Difficulty).ThenBy(s => s.Id, StringComparer.Ordinal).Take(3).ToArray();
    }
    public static bool Eligible(TableState state, DateTimeOffset now)
    {
        if ((now - state.CapturedAt).TotalSeconds is > 6 or < -2 || !state.Settled || state.Occluded || !state.Confirmed || string.IsNullOrWhiteSpace(state.CalibrationId)) return false;
        var balls = state.Balls;
        if (balls.Length > 16 || balls.Select(b => b.Id).Distinct().Count() != balls.Length || balls.Any(b => string.IsNullOrWhiteSpace(b.Id) || b.Kind is not ("cue" or "red" or "yellow" or "black") ||
            !double.IsFinite(b.Confidence) || b.Confidence is < .7 or > 1 || !ShotMath.Finite(b.Position) || b.Position.X is < 0 or > 2 || b.Position.Y is < 0 or > 1)) return false;
        if (balls.Count(b => b.Kind == "cue") != 1 || balls.Count(b => b.Kind == "black") > 1 || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7) return false;
        for (int i = 0; i < balls.Length; i++) for (int j = i + 1; j < balls.Length; j++) if ((balls[i].Position - balls[j].Position).Length < 2 * PoolGeometry.Radius - .002) return false;
        return state.Group is "open" or "red" or "yellow" or "black";
    }
}
