using System.Text;
using System.Text.Json;

namespace SkyEye.Companion.Hybrid;

internal sealed record LiveDetectLoggedBall(string Id, string Colour, double X, double Y, double Confidence);
internal sealed record LiveDetectLogFrame(DateTimeOffset CapturedAt, double FrameAgeMs,
    Guid CameraConnectionId, long MediaGeneration, long FrameId, long SourceTimeUs,
    long PresentationTimeUs, long SurfaceTimestampNs, string Timeline, string FrameSource,
    string CalibrationId, int BallCount, int Reds, int Yellows, int Black, int Cue, int Other,
    bool Uncertain, bool LayoutUncertain, bool Obstructed, double DetectorElapsedMs, double FullPassElapsedMs,
    string[] Warnings, LiveDetectLoggedBall[] Balls, string[] Events);
internal sealed record LiveDetectLogClipSave(Guid OperationId, string Stage, DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt, int RequestedSeconds, string Outcome, string Message,
    long? RequestFrameId, long? RequestSourceTimeUs, DateTimeOffset? RequestFrameCapturedAt,
    long? AcknowledgementFrameId, long? AcknowledgementSourceTimeUs, DateTimeOffset? AcknowledgementFrameCapturedAt,
    string? ClipId, string? ClipName, double? ClipDurationSeconds, DateTimeOffset? ClipCreatedAt,
    long? ClipBytes, string? ClipSha256);
internal sealed record LiveDetectLogRunEvent(string Status, DateTimeOffset OccurredAt,
    string Message, string? ExceptionType, long? FrameId, long? SourceTimeUs, DateTimeOffset? FrameCapturedAt);
internal sealed record LiveDetectLogRecord(int SchemaVersion, string Kind, Guid RunId,
    DateTimeOffset RecordedAt, LiveDetectLogFrame? Frame = null, LiveDetectLogClipSave? ClipSave = null,
    LiveDetectLogRunEvent? RunEvent = null);
internal sealed record LiveDetectLogSummary(int SchemaVersion, string StoragePath, int RetainedRecords,
    int RetainedFrames, int RetainedClipSaveOperations, int RetainedRunEvents,
    DateTimeOffset? FirstRecordedAt, DateTimeOffset? LastRecordedAt);
internal sealed record LiveDetectLogReadout(LiveDetectLogSummary Summary, int RequestedLimit,
    int ReturnedRecords, LiveDetectLogRecord[] Records);

/// <summary>Bounded, image-free frame and automatic clip-save receipts for later review.</summary>
internal sealed class LiveDetectLogStore
{
    public const int MaximumRetainedRecords = 2000;
    const long MaximumBytes = 4 * 1024 * 1024;
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    readonly object gate = new();
    readonly string path;
    readonly HashSet<Guid> clipSaveIds = [];
    int retainedRecords, retainedFrames, retainedClipSaveOperations, retainedRunEvents;
    DateTimeOffset? firstRecordedAt, lastRecordedAt;

    public LiveDetectLogStore(string path)
    {
        this.path = path;
        if (!File.Exists(path)) return;
        lock (gate) RefreshIndex();
    }

    public LiveDetectLogSummary Summary
    {
        get
        {
            lock (gate) return new(2, "live-detect/live-detect.jsonl", retainedRecords,
                retainedFrames, retainedClipSaveOperations, retainedRunEvents, firstRecordedAt, lastRecordedAt);
        }
    }

    public void AppendFrame(Guid runId, DateTimeOffset recordedAt, LiveDetectLogFrame frame) =>
        Append(new(2, "frame", runId, recordedAt, Frame: frame));

    public void AppendClipSave(Guid runId, DateTimeOffset recordedAt, LiveDetectLogClipSave clipSave) =>
        Append(new(2, "clipSave", runId, recordedAt, ClipSave: clipSave));

    public void AppendRunEvent(Guid runId, DateTimeOffset recordedAt, LiveDetectLogRunEvent runEvent) =>
        Append(new(2, "runEvent", runId, recordedAt, RunEvent: runEvent));

    void Append(LiveDetectLogRecord record)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(record, JsonOptions) + "\n", new UTF8Encoding(false));
            retainedRecords++;
            if (record.Kind == "frame") retainedFrames++;
            if (record.Kind == "clipSave" && record.ClipSave is { } save && clipSaveIds.Add(save.OperationId))
                retainedClipSaveOperations = clipSaveIds.Count;
            if (record.Kind == "runEvent") retainedRunEvents++;
            firstRecordedAt ??= record.RecordedAt;
            lastRecordedAt = record.RecordedAt;
            var info = new FileInfo(path);
            if (retainedRecords > MaximumRetainedRecords || info.Length > MaximumBytes) RetainRecentRecords();
        }
    }

    public LiveDetectLogReadout ReadLast(int requestedLimit)
    {
        int limit = Math.Clamp(requestedLimit, 1, MaximumRetainedRecords);
        lock (gate)
        {
            LiveDetectLogRecord[] records = File.Exists(path)
                ? File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
                    .TakeLast(limit).Select(Parse).Where(record => record is not null)
                    .Cast<LiveDetectLogRecord>().ToArray()
                : Array.Empty<LiveDetectLogRecord>();
            return new(new(2, "live-detect/live-detect.jsonl", retainedRecords,
                retainedFrames, retainedClipSaveOperations, retainedRunEvents, firstRecordedAt, lastRecordedAt),
                limit, records.Length, records);
        }
    }

    void RetainRecentRecords()
    {
        var recent = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(MaximumRetainedRecords).ToArray();
        AtomicWrite(path, string.Join("\n", recent) + (recent.Length == 0 ? "" : "\n"));
        RefreshIndex();
    }

    void RefreshIndex()
    {
        retainedRecords = retainedFrames = retainedClipSaveOperations = retainedRunEvents = 0;
        clipSaveIds.Clear();
        firstRecordedAt = lastRecordedAt = null;
        foreach (var line in File.ReadLines(path))
        {
            var record = Parse(line);
            if (record is null) continue;
            retainedRecords++;
            if (record.Kind == "frame" && record.Frame is not null) retainedFrames++;
            if (record.Kind == "clipSave" && record.ClipSave is { } save) clipSaveIds.Add(save.OperationId);
            if (record.Kind == "runEvent" && record.RunEvent is not null) retainedRunEvents++;
            firstRecordedAt ??= record.RecordedAt;
            lastRecordedAt = record.RecordedAt;
        }
        retainedClipSaveOperations = clipSaveIds.Count;
    }

    static LiveDetectLogRecord? Parse(string line)
    {
        try { return JsonSerializer.Deserialize<LiveDetectLogRecord>(line, JsonOptions); }
        catch (JsonException) { return null; }
    }

    static void AtomicWrite(string path, string value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, value, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
