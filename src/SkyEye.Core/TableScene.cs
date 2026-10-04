using System.Collections.Immutable;

namespace SkyEye.Core;

public enum SceneInk { White, Cyan, Yellow, Red, Green }
public enum SceneMarkerKind { Ball, Pocket, Ghost }
public readonly record struct ScenePoint(double X, double Y);
public sealed record ScenePath(ImmutableArray<ScenePoint> Points, SceneInk Ink, bool Selected = false);
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

    public TableScene(long version, long mediaGeneration, long tableVersion, DateTimeOffset now, TimeSpan lifetime,
        IEnumerable<ScenePath> paths, IEnumerable<SceneMarker> markers, IEnumerable<SceneAnnotation> annotations,
        string caption = "", SceneScore? score = null)
    {
        if (version <= 0 || mediaGeneration <= 0 || tableVersion < 0) throw new ArgumentException("Invalid scene scope.");
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(1)) throw new ArgumentException("Scene lifetime must be at most one minute.");
        Paths = paths.Take(17).ToImmutableArray(); Markers = markers.Take(33).ToImmutableArray(); Annotations = annotations.Take(9).ToImmutableArray();
        if (Paths.Length > 16 || Markers.Length > 32 || Annotations.Length > 8) throw new ArgumentException("Scene is too large.");
        foreach (var path in Paths)
        {
            if (path.Points.IsDefault || path.Points.Length is < 2 or > 32 || !Enum.IsDefined(path.Ink)) throw new ArgumentException("Invalid scene path.");
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
