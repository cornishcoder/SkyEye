using System.Collections.Immutable;

namespace SkyEye.Core.Analysis;

public static class ShotSceneProjection
{
    public static TableScene Create(Shot shot, TableCalibration calibration, long sceneVersion, long mediaGeneration,
        DateTimeOffset now, DateTimeOffset expiresAt, string caption, bool pinned, SkyEye.Core.Application.ShotAnnotations annotations = SkyEye.Core.Application.ShotAnnotations.All)
    {
        ScenePoint Image(Point point) { var p = calibration.Unmap(point); return new(p.X, p.Y); }
        var full = new TableScene(sceneVersion, mediaGeneration, shot.TableVersion, now, expiresAt - now,
            [new(shot.CuePath.Select(Image).ToImmutableArray(), SceneInk.White, pinned), new(shot.TargetPath.Select(Image).ToImmutableArray(), SceneInk.Cyan, pinned)],
            [new(Image(shot.GhostBall), .012, SceneInk.Cyan, SceneMarkerKind.Ghost), new(Image(shot.Target), .016, SceneInk.Yellow, SceneMarkerKind.Ball), new(Image(shot.Destination), .02, SceneInk.Cyan, SceneMarkerKind.Pocket)],
            [], caption);
        bool Has(SkyEye.Core.Application.ShotAnnotations flag) => annotations.HasFlag(flag);
        return new(sceneVersion, mediaGeneration, shot.TableVersion, now, expiresAt - now,
            full.Paths.Where((p, i) => Has(i == 0 ? SkyEye.Core.Application.ShotAnnotations.CueBallPath : SkyEye.Core.Application.ShotAnnotations.ObjectBallPath)),
            full.Markers.Where(m => Has(m.Kind switch { SceneMarkerKind.Ghost => SkyEye.Core.Application.ShotAnnotations.GhostBall, SceneMarkerKind.Ball => SkyEye.Core.Application.ShotAnnotations.TargetBall, _ => SkyEye.Core.Application.ShotAnnotations.Pocket })),
            [], Has(SkyEye.Core.Application.ShotAnnotations.ShotLabel) ? caption : "");
    }
}
