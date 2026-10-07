using System.Collections.Immutable;

namespace SkyEye.Core.Analysis;

public static class ShotSceneProjection
{
    public static TableScene Create(Shot shot, TableCalibration calibration, long sceneVersion, long mediaGeneration,
        DateTimeOffset now, DateTimeOffset expiresAt, string caption, bool pinned, SkyEye.Core.Application.ShotAnnotations annotations = SkyEye.Core.Application.ShotAnnotations.All,
        int number = 0, bool detailed = false)
    {
        ScenePoint Image(Point point) { var p = calibration.Unmap(point); return new(p.X, p.Y); }
        var paths = new List<ScenePath>(); var markers = new List<SceneMarker>(); var corridors = new List<SceneCorridor>();
        bool Has(SkyEye.Core.Application.ShotAnnotations flag) => annotations.HasFlag(flag);
        if (Has(Application.ShotAnnotations.CueBallPath)) paths.Add(new(shot.CuePath.Select(Image).ToImmutableArray(), SceneInk.White, pinned, ScenePathRole.CueIncoming));
        if (Has(Application.ShotAnnotations.ObjectBallPath)) paths.Add(new(shot.TargetPath.Select(Image).ToImmutableArray(), SceneInk.Cyan, pinned, ScenePathRole.ObjectRoute));
        if (Has(Application.ShotAnnotations.GhostBall)) markers.Add(new(Image(shot.GhostBall), .012, SceneInk.White, SceneMarkerKind.Ghost));
        if (Has(Application.ShotAnnotations.TargetBall)) markers.Add(new(Image(shot.Target), .016, SceneInk.Yellow, SceneMarkerKind.Ball));
        if (Has(Application.ShotAnnotations.Pocket)) markers.Add(new(Image(shot.Destination), .02, SceneInk.Cyan, SceneMarkerKind.Pocket));
        if (Has(Application.ShotAnnotations.ContactPoint)) markers.Add(new(Image((shot.GhostBall + shot.Target) * .5), .004, SceneInk.Yellow, SceneMarkerKind.Contact));
        if (Has(Application.ShotAnnotations.CushionContacts))
            foreach (var point in shot.CuePath.Skip(1).SkipLast(1).Concat(shot.TargetPath.Skip(1).SkipLast(1)))
                markers.Add(new(Image(point), .009, SceneInk.Cyan, SceneMarkerKind.Cushion));
        if (Has(Application.ShotAnnotations.AimCorridor))
            for (int i = 1; i < shot.CuePath.Length; i++)
            {
                var a = shot.CuePath[i - 1]; var b = shot.CuePath[i]; var delta = b - a;
                if (delta.Length < .001) continue;
                var side = new Point(-delta.Y, delta.X) * (.009 / delta.Length);
                corridors.Add(new([Image(a + side), Image(b + side), Image(b - side), Image(a - side)], SceneInk.White));
            }
        bool projected = Has(Application.ShotAnnotations.CueBallProjectedPath);
        var projection = projected ? GeometricCueProjection.From(shot) : null;
        if (projection?.DisplayEnd is { } end) paths.Add(new([Image(projection.Start), Image(end)], SceneInk.Yellow, pinned, ScenePathRole.CueProjected));
        ShotHudInfo? hud = projected || Has(Application.ShotAnnotations.Provenance)
            ? new(shot.Id, shot.Type, number, "Geometric", projected ? CueDirectionProjection.Model : "candidate-geometry-v1",
                projected ? [CueDirectionProjection.Assumption, "Direction only; no pace, spin or finish estimate"] : [], projection?.UnsupportedReason) { Detailed = detailed } : null;
        string label = Has(Application.ShotAnnotations.ShotLabel) ? detailed ? caption + " · geometric rank" : caption : "";
        return new(sceneVersion, mediaGeneration, shot.TableVersion, now, expiresAt - now, paths, markers, [], label, corridors: corridors, shotHud: hud);
    }
}
