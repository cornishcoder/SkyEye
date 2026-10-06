using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Application;

namespace SkyEye.Core.Analysis;

// Coordinates always refer to the unwarped source image. A later compositor
// applies historical perspective once; viewport UI and credentials are absent.
public sealed record RecordedScene(long Version, long TableVersion, ImmutableArray<ScenePath> Paths,
    ImmutableArray<SceneMarker> Markers, ImmutableArray<SceneAnnotation> Annotations, string Caption, SceneScore? Score)
{
    public void Validate()
    {
        if (Paths.IsDefault || Markers.IsDefault || Annotations.IsDefault) throw new ArgumentException("Missing scene content.");
        _ = new TableScene(Version, 1, TableVersion, DateTimeOffset.UnixEpoch,
            TimeSpan.FromSeconds(1), Paths, Markers, Annotations, Caption, Score);
    }
}
public sealed record AnnotationObservation(string Timeline, long SourceUs, SceneTarget Target, string Geometry,
    RecordedScene? Scene);
public sealed record AnnotationSpan(long StartUs, long EndUs, SceneTarget Target, string Geometry, RecordedScene? Scene);
public sealed record ClipAnnotationJournal(int Schema, string SourceSha256, ClipRecordingOrigin? Origin,
    long DurationUs, string CoordinateSpace, ImmutableArray<AnnotationSpan> Spans)
{
    public void Validate()
    {
        if (Schema != 1 || SourceSha256 is not { Length: 64 } || !SourceSha256.All(Uri.IsHexDigit)
            || DurationUs is <= 0 or > 605_000_000 || CoordinateSpace != "source-image-normalized-v1"
            || Spans.IsDefault || Spans.Length > 12000 || Origin is null && Spans.Length != 0)
            throw new ArgumentException("Invalid clip annotation journal.");
        if (Origin is { } origin && (!Guid.TryParseExact(origin.Timeline, "N", out _) || origin.StartUs < 0
            || origin.StartUs > long.MaxValue - DurationUs || origin.Width is < 16 or > 8192 || origin.Height is < 16 or > 8192
            || origin.Rotation is not (0 or 90 or 180 or 270))) throw new ArgumentException("Invalid annotation origin.");
        if (Spans.Any(s => s is null || s.Target is not (SceneTarget.Phone or SceneTarget.Tv)))
            throw new ArgumentException("Invalid annotation target.");
        foreach (var target in new[] { SceneTarget.Phone, SceneTarget.Tv })
        {
            long end = Origin?.StartUs ?? 0;
            foreach (var span in Spans.Where(s => s.Target == target))
            {
                if (span.StartUs < end || span.EndUs <= span.StartUs || span.EndUs > Origin!.StartUs + DurationUs
                    || !Hash(span.Geometry)) throw new ArgumentException("Invalid annotation interval.");
                span.Scene?.Validate(); end = span.EndUs;
            }
        }
    }
    internal static bool Hash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    public RecordedScene? At(string sourceSha256, ClipRecordingOrigin? origin, long clipUs, SceneTarget target,
        long? tableVersion = null, string? geometry = null)
    {
        if (sourceSha256 != SourceSha256 || origin != Origin || Origin is null || clipUs < 0 || clipUs >= DurationUs) return null;
        long sourceUs = Origin.StartUs + clipUs;
        var span = Spans.FirstOrDefault(s => s.Target == target && s.StartUs <= sourceUs && sourceUs < s.EndUs);
        return span is not null && (geometry is null || geometry == span.Geometry)
            && (tableVersion is null || tableVersion == span.Scene?.TableVersion) ? span.Scene : null;
    }
}

/// <summary>Only observed spans are durable. A crash, stall, invalid mapping or
/// absent renderer leaves a gap, rather than extrapolating live advice.</summary>
public sealed class AnnotationJournalStore
{
    public const long RetentionUs = 620_000_000, MaximumGapUs = 750_000;
    readonly string root;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,object> gates=new(StringComparer.OrdinalIgnoreCase);
    readonly object gate;
    readonly Dictionary<string, List<AnnotationSpan>> timelines = new();
    readonly Dictionary<(string, SceneTarget), AnnotationObservation> last = new();
    public AnnotationJournalStore(string directory) { root = Path.GetFullPath(directory);gate=gates.GetOrAdd(root,_=>new()); Directory.CreateDirectory(root); }
    string FileName(string timeline)
    {
        if (!Guid.TryParseExact(timeline, "N", out _)) throw new ArgumentException("Invalid recording timeline.");
        return Path.Combine(root, timeline + ".json");
    }
    List<AnnotationSpan> Load(string timeline)
    {
        if (timelines.TryGetValue(timeline, out var existing)) return existing;
        var path = FileName(timeline); var spans = new List<AnnotationSpan>();
        try
        {
            if (new FileInfo(path) is { Exists: true, Length: > 0 and < 4_000_000 })
            {
                var recovered = JsonSerializer.Deserialize<AnnotationSpan[]>(File.ReadAllText(path), JsonDefaults.Options);
                if (recovered is not null && recovered.Length <= 12000 && recovered.All(Valid))
                    foreach (var target in new[] { SceneTarget.Phone, SceneTarget.Tv })
                    {
                        long end = 0;
                        foreach (var span in recovered.Where(s => s.Target == target))
                        { if (span.StartUs < end) throw new ArgumentException("Overlapping journal."); spans.Add(span); end = span.EndUs; }
                    }
            }
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException) { spans.Clear(); }
        timelines[timeline] = spans; return spans;
    }
    static bool Valid(AnnotationSpan span)
    {
        if (span is null || span.StartUs < 0 || span.EndUs <= span.StartUs
            || span.Target is not (SceneTarget.Phone or SceneTarget.Tv) || !ClipAnnotationJournal.Hash(span.Geometry)) return false;
        span.Scene?.Validate(); return true;
    }
    public void Observe(AnnotationObservation observation)
    {
        if (observation.SourceUs < 0 || observation.Target is not (SceneTarget.Phone or SceneTarget.Tv)
            || !ClipAnnotationJournal.Hash(observation.Geometry)) throw new ArgumentException("Invalid annotation observation.");
        observation.Scene?.Validate();
        lock (gate)
        {
            var spans = Load(observation.Timeline); var key = (observation.Timeline, observation.Target);
            if (last.TryGetValue(key, out var previous))
            {
                if (observation.SourceUs <= previous.SourceUs) return; // repeated/late frame, never rewrite history
                if (observation.SourceUs - previous.SourceUs <= MaximumGapUs)
                {
                    var before = spans.LastOrDefault(s => s.Target == observation.Target);
                    if (before is not null && before.EndUs == previous.SourceUs && before.Geometry == previous.Geometry
                        && Same(before.Scene, previous.Scene))
                        spans[spans.IndexOf(before)] = before with { EndUs = observation.SourceUs };
                    else spans.Add(new(previous.SourceUs, observation.SourceUs, previous.Target, previous.Geometry, previous.Scene));
                }
            }
            else if (spans.Any(s => s.Target == observation.Target && s.EndUs > observation.SourceUs)) return;
            last[key] = observation;
            spans.RemoveAll(s => s.EndUs < observation.SourceUs - RetentionUs);
            if (spans.Count > 12000) spans.RemoveRange(0, spans.Count - 12000);
        }
    }
    static bool Same(RecordedScene? a, RecordedScene? b) => ReferenceEquals(a, b)
        || JsonSerializer.Serialize(a, JsonDefaults.Options) == JsonSerializer.Serialize(b, JsonDefaults.Options);
    public void Flush(string timeline)
    {
        lock (gate)
        {
            var path = FileName(timeline); var data = JsonSerializer.SerializeToUtf8Bytes(Load(timeline), JsonDefaults.Options);
            if (data.Length > 4_000_000) throw new IOException("Annotation journal exceeds its budget.");
            string part = path + ".part";
            try { File.WriteAllBytes(part, data); File.Move(part, path, true); } finally { File.Delete(part); }
            // Bound abandoned timelines as well as the active rolling window.
            foreach (var old in Directory.EnumerateFiles(root, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(4))
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(old), "N", out _))
                { File.Delete(old); string removed=Path.GetFileNameWithoutExtension(old);timelines.Remove(removed);foreach(var key in last.Keys.Where(k=>k.Item1==removed).ToArray())last.Remove(key); }
        }
    }
    public ClipAnnotationJournal Window(Clip clip)
    {
        ClipLibrary.Validate(clip); long duration = checked((long)Math.Ceiling(clip.Duration * 1_000_000));
        lock (gate)
        {
            var origin = clip.RecordingOrigin;
            var spans = origin is null ? [] : Load(origin.Timeline)
                .Where(s => s.StartUs < origin.StartUs + duration && s.EndUs > origin.StartUs)
                .Select(s => s with { StartUs = Math.Max(s.StartUs, origin.StartUs), EndUs = Math.Min(s.EndUs, origin.StartUs + duration) })
                .OrderBy(s => s.Target).ThenBy(s => s.StartUs).ToArray();
            var journal = new ClipAnnotationJournal(1, clip.Sha256, origin, duration, "source-image-normalized-v1", spans.ToImmutableArray());
            journal.Validate(); return journal;
        }
    }
    public void Preserve(Clip clip)
    {
        lock(gate) PreserveCore(clip);
    }
    void PreserveCore(Clip clip)
    {
        var journal = Window(clip); string directory = Path.Combine(root, "clips"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, clip.Id + ".json");
        // Keep previously captured history when the rolling window has advanced.
        var previous = Preserved(clip);
        if (previous is not null)
        {
            var merged = previous.Spans.Concat(journal.Spans).GroupBy(s => (s.Target, s.StartUs))
                .Select(g => g.OrderByDescending(s => s.EndUs).First()).OrderBy(s => s.Target).ThenBy(s => s.StartUs).ToArray();
            // Updates extend a last span; discard its shorter overlapping predecessor.
            var spans = new List<AnnotationSpan>();
            foreach (var span in merged)
            {
                var before = spans.LastOrDefault(s => s.Target == span.Target);
                if (before is not null && before.EndUs > span.StartUs) continue;
                spans.Add(span);
            }
            journal = journal with { Spans = spans.ToImmutableArray() };
        }
        WriteClip(path, journal);
    }
    public ClipAnnotationJournal? Preserved(Clip clip)
    {
        ClipLibrary.Validate(clip); var path = Path.Combine(root, "clips", clip.Id + ".json");
        try
        {
            if (new FileInfo(path) is not { Exists: true, Length: > 0 and < 4_000_000 }) return null;
            var journal = JsonSerializer.Deserialize<ClipAnnotationJournal>(File.ReadAllText(path), JsonDefaults.Options);
            if (journal is null || journal.SourceSha256 != clip.Sha256 || journal.Origin != clip.RecordingOrigin || journal.DurationUs != (long)Math.Ceiling(clip.Duration*1_000_000)) return null;
            journal.Validate(); return journal;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException) { return null; }
    }
    public static void WriteClip(string path, ClipAnnotationJournal journal)
    {
        journal.Validate(); var data = JsonSerializer.SerializeToUtf8Bytes(journal, JsonDefaults.Options);
        if (data.Length >= 4_000_000) throw new IOException("Clip annotations exceed budget.");
        string part = path + ".part";
        try { File.WriteAllBytes(part, data); File.Move(part, path, true); } finally { File.Delete(part); }
    }
    public static string Fingerprint(string nativeGeometry) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nativeGeometry)));
    public object Metrics()
    {
        lock(gate) return new { timelines=timelines.Count, spans=timelines.Values.Sum(s=>s.Count),
            observations=last.Values.Select(o=>new { o.Target,o.SourceUs,sceneVersion=o.Scene?.Version??0,tableVersion=o.Scene?.TableVersion??0 }).ToArray(),
            preservedClips=Directory.Exists(Path.Combine(root,"clips"))?Directory.EnumerateFiles(Path.Combine(root,"clips"),"*.json").Count():0 };
    }
}
