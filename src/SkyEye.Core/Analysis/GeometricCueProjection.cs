namespace SkyEye.Core.Analysis;

public sealed record CueDirectionProjection(Point Start, Point? Direction, Point? DisplayEnd, string? UnsupportedReason)
{
    public const string Model = "geometric-stun-v1";
    public const string Assumption = "Stun at impact; ideal equal-mass collision";
}

/// <summary>Instantaneous transverse direction for an ideal stun impact.
/// The bounded display arrow is not a travel-distance or finish prediction.</summary>
public static class GeometricCueProjection
{
    public static CueDirectionProjection From(Shot shot)
    {
        CueDirectionProjection Unsupported(string reason) => new(shot.GhostBall, null, null, reason);
        if (shot.Type is not ("direct" or "bank") || shot.CuePath.Length != 2 || shot.TargetPath.Length is < 2 or > 3)
            return Unsupported("Stun direction unavailable for cue-cushion or multi-contact routes");
        if (shot.CuePath.Concat(shot.TargetPath).Any(p => !Finite(p)) || !Finite(shot.GhostBall) || !Finite(shot.Target))
            return Unsupported("Collision geometry is unavailable");
        if ((shot.CuePath[^1] - shot.GhostBall).Length > .001 || (shot.TargetPath[0] - shot.Target).Length > .001)
            return Unsupported("Collision geometry does not match the candidate");
        var incoming = shot.GhostBall - shot.CuePath[0]; var normal = shot.Target - shot.GhostBall;
        var objectRoute = shot.TargetPath[1] - shot.Target;
        if (incoming.Length < .01 || normal.Length < .001 || objectRoute.Length < .01
            || Math.Abs(normal.Length - 2 * PoolGeometry.Radius) > .006)
            return Unsupported("Ball contact geometry is degenerate");
        var v = incoming * (1 / incoming.Length); var n = normal * (1 / normal.Length);
        var objectDirection = objectRoute * (1 / objectRoute.Length);
        if (n.Dot(objectDirection) < .995 || v.Dot(n) <= .05)
            return Unsupported("Supported first-contact direction is unavailable");
        var transverse = v - n * v.Dot(n);
        if (transverse.Length < .025) return Unsupported("Full-ball contact has no reliable outgoing stun direction");
        var direction = transverse * (1 / transverse.Length);
        const double visualLength = .24; double extent = visualLength;
        const double margin = PoolGeometry.Radius;
        if (direction.X > 1e-9) extent = Math.Min(extent, (2 - margin - shot.GhostBall.X) / direction.X);
        if (direction.X < -1e-9) extent = Math.Min(extent, (margin - shot.GhostBall.X) / direction.X);
        if (direction.Y > 1e-9) extent = Math.Min(extent, (1 - margin - shot.GhostBall.Y) / direction.Y);
        if (direction.Y < -1e-9) extent = Math.Min(extent, (margin - shot.GhostBall.Y) / direction.Y);
        if (extent < .06) return Unsupported("Insufficient table space for the direction indicator");
        return new(shot.GhostBall, direction, shot.GhostBall + direction * extent, null);
    }
    static bool Finite(Point p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X is >= 0 and <= 2 && p.Y is >= 0 and <= 1;
}
