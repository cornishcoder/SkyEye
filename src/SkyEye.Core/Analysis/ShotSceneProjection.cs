using System.Collections.Immutable;

namespace SkyEye.Core.Analysis;

public static class ShotSceneProjection
{
    public static TableScene Create(Shot shot, TableCalibration calibration, long sceneVersion, long mediaGeneration,
        DateTimeOffset now, DateTimeOffset expiresAt, string caption, bool pinned)
    {
        ScenePoint Image(Point point) { var p = calibration.Unmap(point); return new(p.X, p.Y); }
        return new(sceneVersion, mediaGeneration, shot.TableVersion, now, expiresAt - now,
            [new(shot.CuePath.Select(Image).ToImmutableArray(), SceneInk.White, pinned), new(shot.TargetPath.Select(Image).ToImmutableArray(), SceneInk.Cyan, pinned)],
            [new(Image(shot.GhostBall), .012, SceneInk.Cyan, SceneMarkerKind.Ghost), new(Image(shot.Target), .016, SceneInk.Yellow, SceneMarkerKind.Ball), new(Image(shot.Destination), .02, SceneInk.Cyan, SceneMarkerKind.Pocket)],
            [], caption);
    }
}
