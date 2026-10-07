using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SkyEye.Core;

public enum SceneInk { White, Cyan, Yellow, Red, Green }
public enum SceneMarkerKind { Ball, Pocket, Ghost, Contact, Cushion }
public enum ScenePathRole { Unspecified, CueIncoming, ObjectRoute, CueProjected }
public readonly record struct ScenePoint(double X, double Y);
public sealed record ScenePath(ImmutableArray<ScenePoint> Points, SceneInk Ink, bool Selected = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ScenePathRole Role = ScenePathRole.Unspecified);
public sealed record SceneCorridor(ImmutableArray<ScenePoint> Boundary, SceneInk Ink);
public sealed record ShotHudInfo(string CandidateId, string ShotType, int Number, string Provenance, string Model,
    ImmutableArray<string> Assumptions, string? ProjectionUnavailable = null);
public sealed record SceneMarker(ScenePoint Point, double Radius, SceneInk Ink, SceneMarkerKind Kind);
public sealed record SceneAnnotation(ScenePoint Point, string Text);
public sealed record SceneScore(string LeftName, int LeftScore, string RightName, int RightScore);

/// <summary>Only renderable content: normalized image geometry and upright viewport text.
/// No camera addresses, host credentials, controls or assistant transcript enter this contract.</summary>
public sealed class TableScene
{
    public long Version { get; }
    public long MediaGeneration { get; }
    public long TableVersion { get; }
    public long ExpiresAt { get; }
    public ImmutableArray<ScenePath> Paths { get; }
    public ImmutableArray<SceneMarker> Markers { get; }
    public ImmutableArray<SceneAnnotation> Annotations { get; }
    public string Caption { get; }
    public SceneScore? Score { get; }
    public int Schema { get; }
    public ImmutableArray<SceneCorridor> Corridors { get; }
    public ShotHudInfo? ShotHud { get; }

    public TableScene(long version, long mediaGeneration, long tableVersion, DateTimeOffset now, TimeSpan lifetime,
        IEnumerable<ScenePath> paths, IEnumerable<SceneMarker> markers, IEnumerable<SceneAnnotation> annotations,
        string caption = "", SceneScore? score = null, IEnumerable<SceneCorridor>? corridors = null, ShotHudInfo? shotHud = null)
    {
        if (version <= 0 || mediaGeneration <= 0 || tableVersion < 0) throw new ArgumentException("Invalid scene scope.");
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(1)) throw new ArgumentException("Scene lifetime must be at most one minute.");
        Paths = paths.Take(17).ToImmutableArray(); Markers = markers.Take(33).ToImmutableArray(); Annotations = annotations.Take(9).ToImmutableArray();
        if (Paths.Length > 16 || Markers.Length > 32 || Annotations.Length > 8) throw new ArgumentException("Scene is too large.");
        Corridors = (corridors ?? []).Take(5).ToImmutableArray();
        if (Corridors.Length > 4) throw new ArgumentException("Too many aim corridors.");
        foreach (var corridor in Corridors)
        {
            if (corridor.Boundary.IsDefault || corridor.Boundary.Length is < 3 or > 8 || !Enum.IsDefined(corridor.Ink)) throw new ArgumentException("Invalid corridor.");
            foreach (var point in corridor.Boundary) CheckPoint(point);
        }
        foreach (var path in Paths)
        {
            if (path.Points.IsDefault || path.Points.Length is < 2 or > 32 || !Enum.IsDefined(path.Ink) || !Enum.IsDefined(path.Role)) throw new ArgumentException("Invalid scene path.");
            foreach (var point in path.Points) CheckPoint(point);
        }
        foreach (var marker in Markers)
        {
            CheckPoint(marker.Point);
            if (!double.IsFinite(marker.Radius) || marker.Radius is < .001 or > .1 || !Enum.IsDefined(marker.Ink) || !Enum.IsDefined(marker.Kind)) throw new ArgumentException("Invalid scene marker.");
        }
        foreach (var annotation in Annotations) { CheckPoint(annotation.Point); CheckText(annotation.Text, 48); }
        CheckText(caption, 96);
        if (score is not null)
        {
            CheckText(score.LeftName, 24); CheckText(score.RightName, 24);
            if (score.LeftScore is < 0 or > 999 || score.RightScore is < 0 or > 999) throw new ArgumentException("Invalid score display.");
        }
        if (shotHud is not null)
        {
            CheckText(shotHud.CandidateId, 128); CheckText(shotHud.ShotType, 24); CheckText(shotHud.Provenance, 24); CheckText(shotHud.Model, 48);
            if (shotHud.Number is < 0 or > 3 || shotHud.Provenance != "Geometric" || shotHud.Assumptions.IsDefault || shotHud.Assumptions.Length > 4)
                throw new ArgumentException("Invalid shot HUD provenance.");
            foreach (var assumption in shotHud.Assumptions) CheckText(assumption, 72);
            if (shotHud.ProjectionUnavailable is { } reason) CheckText(reason, 96);
        }
        ShotHud = shotHud; Schema = Corridors.Length > 0 || shotHud is not null || Paths.Any(p => p.Role != ScenePathRole.Unspecified)
            || Markers.Any(m => m.Kind is SceneMarkerKind.Contact or SceneMarkerKind.Cushion) ? 2 : 1;
        if (Paths.Any(p => p.Role == ScenePathRole.CueProjected) && (shotHud?.Model != Analysis.CueDirectionProjection.Model
            || !shotHud.Assumptions.Contains(Analysis.CueDirectionProjection.Assumption)))
            throw new ArgumentException("Projected cue path requires its explicit geometric stun assumptions.");
        Version = version; MediaGeneration = mediaGeneration; TableVersion = tableVersion;
        ExpiresAt = (now + lifetime).ToUnixTimeMilliseconds(); Caption = caption; Score = score;
    }
    public bool IsCurrent(long mediaGeneration, DateTimeOffset now) => mediaGeneration == MediaGeneration && now.ToUnixTimeMilliseconds() < ExpiresAt;
    static void CheckPoint(ScenePoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X is < 0 or > 1 || point.Y is < 0 or > 1) throw new ArgumentException("Scene point is outside the image.");
    }
    static void CheckText(string text, int max)
    {
        if (text is null || text.Length > max || text.Any(char.IsControl)) throw new ArgumentException("Invalid scene text.");
    }
}
