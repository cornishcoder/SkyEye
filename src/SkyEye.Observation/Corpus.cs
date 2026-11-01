using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyEye.Observation;

public enum CorpusKind { Real, Synthetic }
public enum CorpusSplit { Unassigned, Development, Holdout, Regression }
public enum SequencePurpose { Shot, StillRegression, Contract, StillVerification, NegativeVerification }
public enum SourceClock { Unknown, RecordingMicroseconds, ClipMicroseconds, SyntheticMicroseconds }
public enum PixelEncoding { Jpeg, Gray8, Png }
public enum LabelVisibility { Visible, Occluded, Uncertain, Absent }
public sealed record MediaSource(string Id, string Origin, string File, string Sha256, int Width, int Height, SourceClock Clock,
    string CalibrationVersion, double? NominalFps = null);
public sealed record CorpusSequence(string Id, string Session, CorpusKind Kind, CorpusSplit Split, SequencePurpose Purpose,
    MediaSource Source, string IndexFile, string LabelsFile, string Notes, ImmutableArray<string> Tags);
public sealed record CorpusManifest(int SchemaVersion, string Id, ImmutableArray<CorpusSequence> Sequences);
public sealed record FrameScope(string Source, string Timeline, long Generation, string Calibration);
// Pixels and timing form one indexed receipt; audit UTC is separate from source time.
public sealed record IndexedFrame(string SampleId, long FrameId, long? SourceTimeUs, FrameScope Scope,
    string PixelFile, string PixelSha256, PixelEncoding Encoding, int Width, int Height, bool EmbeddedFrameId = false);
public sealed record BallLabel(string Id, string Colour, double X, double Y, LabelVisibility Visibility)
{
    // Optional reviewed ellipse radii in the frame's coordinate space. Separate
    // axes avoid treating image-normalized coordinates as square pixels.
    public double? RadiusX { get; init; }
    public double? RadiusY { get; init; }
}
public sealed record FrameLabels(string SampleId, string PixelSha256, string CoordinateSpace,
    ImmutableArray<BallLabel> Balls, string Notes)
{
    // Optional frame-level conditions allow lighting/motion to change within a recording session.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<string> Tags { get; init; }
}
public sealed record LabelInterval(string Kind, long FromUs, long ToUs, string Certainty, ImmutableArray<string> BallIds, string Notes);
public sealed record SequenceLabels(int SchemaVersion, string SequenceId, string Annotator, string CalibrationVersion,
    bool Reviewed, ImmutableArray<FrameLabels> Frames, ImmutableArray<LabelInterval> Intervals);

public static class CorpusSetupGroups
{
    public static ImmutableArray<string> ForSequence(CorpusSequence sequence)
    {
        if (sequence.Tags.IsDefaultOrEmpty) return [];
        return sequence.Tags.Select(tag => tag.StartsWith("setup-group:", StringComparison.Ordinal)
                ? tag["setup-group:".Length..]
                : tag.StartsWith("matched-setup-group-", StringComparison.Ordinal) ? tag : null)
            .Where(id => id is not null).Select(id => id!)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
    }
}

public static class CorpusJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, NewLine = "\n", Converters = { new JsonStringEnumConverter() }, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Options) ?? throw new InvalidDataException("Empty JSON document: " + path);
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
}
