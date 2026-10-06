using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkyEye.Core.Analysis;

public enum RecordedGeometryClock { ClipRelative, CameraRecording }
public sealed record ClipRecordingOrigin(string Timeline, long StartUs, int Width, int Height, int Rotation);
public sealed record RecordedGeometrySource(string ContentSha256, RecordedGeometryClock Clock, string Timeline,
    long StartUs, long DurationUs, int Width, int Height, int Rotation, int TransformVersion = 1)
{
    public long EndUs => checked(StartUs + DurationUs);
    public void Validate()
    {
        if (ContentSha256 is not { Length: 64 } || !ContentSha256.All(Uri.IsHexDigit) || !Enum.IsDefined(Clock)
            || StartUs < 0 || DurationUs is <= 0 or > 605_000_000 || StartUs > long.MaxValue - DurationUs
            || Width is < 16 or > 8192 || Height is < 16 or > 8192 || Rotation is not (0 or 90 or 180 or 270) || TransformVersion != 1
            || Clock == RecordedGeometryClock.ClipRelative && (StartUs != 0 || Timeline != ContentSha256)
            || Clock == RecordedGeometryClock.CameraRecording && !Guid.TryParseExact(Timeline, "N", out _))
            throw new ArgumentException("Invalid recorded geometry source.");
    }
}
public sealed record RecordedPocketSample(long SourceTimeUs, PocketLayout? Layout);
public sealed record RecordedGeometrySpan(long StartUs, long EndUs, int GeometryVersion, PocketLayout? Layout);
public sealed record RecordedGeometryJournal(int Schema, RecordedGeometrySource Source, ImmutableArray<RecordedGeometrySpan> Spans)
{
    public void Validate()
    {
        Source.Validate();
        if (Schema != 1 || Spans.IsDefaultOrEmpty || Spans.Length > 512) throw new ArgumentException("Invalid recorded geometry journal.");
        long cursor = Source.StartUs;
        foreach (var span in Spans)
        {
            if (span.StartUs != cursor || span.EndUs <= cursor || span.EndUs > Source.EndUs
                || span.Layout is null && span.GeometryVersion != 0 || span.Layout is not null && (span.GeometryVersion <= 0 || !RecordedGeometry.Acceptable(span.Layout)))
                throw new ArgumentException("Invalid recorded geometry interval.");
            cursor = span.EndUs;
        }
        if (cursor != Source.EndUs) throw new ArgumentException("Geometry journal must include uncertain gaps and tail.");
    }
    public RecordedGeometrySpan? At(RecordedGeometrySource source, long sourceTimeUs)
        => Source == source && sourceTimeUs >= Source.StartUs && sourceTimeUs < Source.EndUs
            ? Spans.FirstOrDefault(s => sourceTimeUs >= s.StartUs && sourceTimeUs < s.EndUs) : null;
    public RecordedGeometrySpan[] Window(RecordedGeometrySource source, long startUs, long endUs)
    {
        if (Source != source || startUs < Source.StartUs || endUs > Source.EndUs || endUs <= startUs) throw new ArgumentException("Geometry window is outside its source.");
        return Spans.Where(s => s.StartUs < endUs && s.EndUs > startUs)
            .Select(s => s with { StartUs = Math.Max(startUs, s.StartUs), EndUs = Math.Min(endUs, s.EndUs) }).ToArray();
    }
}

public static class RecordedGeometry
{
    public const long MaximumSampleGapUs = 4_000_000;
    public static bool Acceptable(PocketLayout layout)
    {
        if (layout.Corners.IsDefault || layout.Corners.Length != 4 || layout.SidePockets.IsDefault || layout.SidePockets.Length is < 1 or > 2
            || !double.IsFinite(layout.Contrast) || layout.Contrast is < (8d / 255) or > 1
            || layout.SidePockets.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X is < 0 or > 1 || p.Y is < 0 or > 1)) return false;
        try { _ = new TableCalibration(layout.Corners.ToArray()); return true; } catch (ArgumentException) { return false; }
    }
    public static bool Same(PocketLayout a, PocketLayout b) => a.Corners.Zip(b.Corners).All(p => (p.First - p.Second).Length < .015);
    public static RecordedGeometryJournal Build(RecordedGeometrySource source, IEnumerable<RecordedPocketSample> observations)
    {
        source.Validate(); var samples = observations.Take(513).ToArray();
        if (samples.Length > 512) throw new ArgumentException("Too many recorded geometry samples.");
        var spans = new List<RecordedGeometrySpan>(); long cursor = source.StartUs; int version = 0;
        RecordedPocketSample? previous = null; PocketLayout? stable = null;
        void Add(long end, PocketLayout? layout, int geometryVersion)
        {
            if (end <= cursor) return;
            if (spans.Count > 0 && spans[^1].GeometryVersion == geometryVersion)
                spans[^1] = spans[^1] with { EndUs = end };
            else spans.Add(new(cursor, end, geometryVersion, layout));
            cursor = end;
        }
        foreach (var raw in samples)
        {
            if (raw.SourceTimeUs < source.StartUs || raw.SourceTimeUs >= source.EndUs || previous is not null && raw.SourceTimeUs <= previous.SourceTimeUs)
                throw new ArgumentException("Recorded geometry timestamps must advance within the source.");
            var sample = raw with { Layout = raw.Layout is { } layout && Acceptable(layout) ? layout : null };
            if (previous is null) Add(sample.SourceTimeUs, null, 0);
            else if (previous.Layout is { } before && sample.Layout is { } after && Same(before, after)
                && sample.SourceTimeUs - previous.SourceTimeUs <= MaximumSampleGapUs)
            {
                if (stable is null || !Same(stable, before) || !Same(stable, after)) { stable = before; version++; }
                Add(sample.SourceTimeUs, stable, version);
            }
            else { Add(sample.SourceTimeUs, null, 0); stable = null; }
            previous = sample;
        }
        // Never extrapolate a previous mount through an unobserved end or gap.
        Add(source.EndUs, null, 0);
        var journal = new RecordedGeometryJournal(1, source, spans.ToImmutableArray()); journal.Validate(); return journal;
    }
}

public sealed class RecordedGeometryStore(string directory)
{
    readonly string root = Path.GetFullPath(directory);
    static string Key(RecordedGeometrySource source) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source, JsonDefaults.Options)));
    public RecordedGeometryJournal? Load(RecordedGeometrySource source)
    {
        source.Validate(); var path = Path.Combine(root, Key(source) + ".json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 512_000) return null;
            var journal = JsonSerializer.Deserialize<RecordedGeometryJournal>(File.ReadAllText(path), JsonDefaults.Options);
            if (journal is null || journal.Source != source) return null;
            journal.Validate(); return journal;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException) { return null; }
    }
    public async Task SaveAsync(RecordedGeometryJournal journal, CancellationToken cancellation)
    {
        journal.Validate(); Directory.CreateDirectory(root);
        string target = Path.Combine(root, Key(journal.Source) + ".json"), part = Path.Combine(root, Guid.NewGuid().ToString("N") + ".part");
        try
        {
            await File.WriteAllTextAsync(part, JsonSerializer.Serialize(journal, JsonDefaults.Options), cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.Move(part, target, overwrite: true);
        }
        finally { File.Delete(part); }
    }
}
