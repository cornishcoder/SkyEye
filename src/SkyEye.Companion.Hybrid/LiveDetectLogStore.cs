using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyEye.Core.Analysis;

namespace SkyEye.Companion.Hybrid;

internal sealed record LiveDetectLoggedBall(string Id, string Colour, double X, double Y, double Confidence);
internal sealed record LiveDetectLoggedPosition(string Colour, double X, double Y);
internal sealed record LiveDetectLogObstructionAudit(int AnalysisWidth, int AnalysisHeight, int BorderPixels,
    int ConnectedComponents, int QualifyingComponents, int TriggeringComponents,
    int OmittedComponents, int OmittedTriggeringComponents, ObstructionComponentAudit[] Components)
{
    internal const int MaximumComponents = 3;

    public static LiveDetectLogObstructionAudit? From(ObstructionAuditTelemetry? audit)
    {
        if (audit is null) return null;
        // Keep the largest triggers first, then any remaining largest exempted
        // regions. Counts still describe all components, including omitted ones.
        var components = audit.Components.OrderByDescending(component => component.TriggersObstruction)
            .ThenByDescending(component => component.Area).ThenBy(component => component.Label)
            .Take(MaximumComponents).ToArray();
        return new(audit.AnalysisWidth, audit.AnalysisHeight, audit.BorderPixels,
            audit.ConnectedComponents, audit.QualifyingComponents, audit.TriggeringComponents,
            Math.Max(0, audit.QualifyingComponents - components.Length),
            Math.Max(0, audit.TriggeringComponents - components.Count(component => component.TriggersObstruction)),
            components);
    }
}
internal sealed record LiveDetectLogFrame(DateTimeOffset CapturedAt, double FrameAgeMs,
    Guid CameraConnectionId, long MediaGeneration, long FrameId, long SourceTimeUs,
    long PresentationTimeUs, long SurfaceTimestampNs, string Timeline, string FrameSource,
    string CalibrationId, int BallCount, int Reds, int Yellows, int Black, int Cue, int Other,
    bool Uncertain, bool LayoutUncertain, bool Obstructed, double DetectorElapsedMs, double FullPassElapsedMs,
    string[] Warnings, LiveDetectLoggedBall[] Balls, string[] Events, string CheckOutcome,
    bool? SpatiallyCompatible = null, bool PackedRackEvidence = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LiveDetectLogObstructionAudit? ObstructionAudit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerPlaybackTelemetry? PlayerPlayback = null);
internal sealed record LiveDetectLogClipSave(Guid OperationId, string Stage, DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt, int RequestedSeconds, string Outcome, string Message,
    long? RequestFrameId, long? RequestSourceTimeUs, DateTimeOffset? RequestFrameCapturedAt,
    long? AcknowledgementFrameId, long? AcknowledgementSourceTimeUs, DateTimeOffset? AcknowledgementFrameCapturedAt,
    string? ClipId, string? ClipName, double? ClipDurationSeconds, DateTimeOffset? ClipCreatedAt,
    long? ClipBytes, string? ClipSha256, Guid? ActivityId = null, string? EventType = null,
    string? EventTimeline = null, long? WindowStartSourceTimeUs = null, long? WindowEndSourceTimeUs = null,
    long? ClipStartSourceTimeUs = null, string? ClipTimeline = null, bool? WindowCovered = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid[]? LinkedActivityIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid[]? LinkedTableEventIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? LinkedEventTypes = null);
internal sealed record LiveDetectLogRunEvent(string Status, DateTimeOffset OccurredAt,
    string Message, string? ExceptionType, long? FrameId, long? SourceTimeUs, DateTimeOffset? FrameCapturedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerPlaybackTelemetry? PlayerPlayback = null);
internal sealed record LiveDetectLogTableEvent(Guid EventId, Guid? ActivityId, string Type,
    DateTimeOffset CapturedAt, Guid CameraConnectionId, long MediaGeneration, long FrameId,
    long SourceTimeUs, string Timeline, string CalibrationId, string Message,
    LiveDetectCounts? Before, LiveDetectCounts? After, long? BeforeSourceTimeUs,
    int? MovedBallCount, double? LargestMovement,
    LiveDetectLoggedPosition[]? BeforeLayout = null, LiveDetectLoggedPosition[]? AfterLayout = null);
internal sealed record LiveDetectLogRecord(int SchemaVersion, string Kind, Guid RunId,
    DateTimeOffset RecordedAt, LiveDetectLogFrame? Frame = null, LiveDetectLogClipSave? ClipSave = null,
    LiveDetectLogRunEvent? RunEvent = null, LiveDetectLogTableEvent? TableEvent = null);
internal sealed record LiveDetectLogSummary(int SchemaVersion, string StoragePath, int RetainedRecords,
    int RetainedFrames, int RetainedEventClipOperations, int RetainedRunEvents, int RetainedTableEvents,
    DateTimeOffset? FirstRecordedAt, DateTimeOffset? LastRecordedAt);
internal sealed record LiveDetectLogReadout(LiveDetectLogSummary Summary, int RequestedLimit,
    int ReturnedRecords, LiveDetectLogRecord[] Records);

/// <summary>Bounded, image-free frame and automatic clip-save receipts for later review.</summary>
internal sealed class LiveDetectLogStore
{
    internal const int CurrentSchemaVersion = 10;
    public const int MaximumRetainedRecords = 2000;
    internal const long MaximumBytes = 4 * 1024 * 1024;
    const int RetainedRecordTarget = MaximumRetainedRecords * 3 / 4;
    const long RetainedBytesTarget = MaximumBytes * 3 / 4;
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    readonly object gate = new();
    readonly string path;
    readonly HashSet<Guid> clipSaveIds = [];
    int retainedRecords, retainedFrames, retainedEventClipOperations, retainedRunEvents, retainedTableEvents;
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
            lock (gate) return new(CurrentSchemaVersion, "live-detect/live-detect.jsonl", retainedRecords,
                retainedFrames, retainedEventClipOperations, retainedRunEvents, retainedTableEvents, firstRecordedAt, lastRecordedAt);
        }
    }

    public void AppendFrame(Guid runId, DateTimeOffset recordedAt, LiveDetectLogFrame frame) =>
        Append(new(CurrentSchemaVersion, "frame", runId, recordedAt, Frame: frame));

    public void AppendClipSave(Guid runId, DateTimeOffset recordedAt, LiveDetectLogClipSave clipSave) =>
        Append(new(CurrentSchemaVersion, "clipSave", runId, recordedAt, ClipSave: clipSave));

    public void AppendRunEvent(Guid runId, DateTimeOffset recordedAt, LiveDetectLogRunEvent runEvent) =>
        Append(new(CurrentSchemaVersion, "runEvent", runId, recordedAt, RunEvent: runEvent));

    public void AppendTableEvent(Guid runId, DateTimeOffset recordedAt, LiveDetectLogTableEvent tableEvent) =>
        Append(new(CurrentSchemaVersion, "tableEvent", runId, recordedAt, TableEvent: tableEvent));

    void Append(LiveDetectLogRecord record)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, JsonSerializer.Serialize(record, JsonOptions) + "\n", new UTF8Encoding(false));
            retainedRecords++;
            if (record.Kind == "frame") retainedFrames++;
            if (record.Kind == "clipSave" && record.ClipSave is { ActivityId: not null, EventType: not null } save
                && clipSaveIds.Add(save.OperationId))
                retainedEventClipOperations = clipSaveIds.Count;
            if (record.Kind == "runEvent") retainedRunEvents++;
            if (record.Kind == "tableEvent" && record.TableEvent is not null) retainedTableEvents++;
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
            return new(new(CurrentSchemaVersion, "live-detect/live-detect.jsonl", retainedRecords,
                retainedFrames, retainedEventClipOperations, retainedRunEvents, retainedTableEvents, firstRecordedAt, lastRecordedAt),
                limit, records.Length, records);
        }
    }

    void RetainRecentRecords()
    {
        var recent = new Queue<(string Line, int Bytes)>();
        long retainedBytes = 0;
        foreach (var line in File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            int bytes = Encoding.UTF8.GetByteCount(line) + 1;
            recent.Enqueue((line, bytes));
            retainedBytes += bytes;
            while (recent.Count > RetainedRecordTarget || retainedBytes > RetainedBytesTarget)
            {
                // Keep one valid record if it alone is larger than the trim target,
                // but never let the retained file exceed its hard maximum.
                if (recent.Count == 1 && retainedBytes <= MaximumBytes) break;
                retainedBytes -= recent.Dequeue().Bytes;
            }
        }
        var lines = recent.Select(item => item.Line).ToArray();
        AtomicWrite(path, string.Join("\n", lines) + (lines.Length == 0 ? "" : "\n"));
        RefreshIndex();
    }

    void RefreshIndex()
    {
        retainedRecords = retainedFrames = retainedEventClipOperations = retainedRunEvents = retainedTableEvents = 0;
        clipSaveIds.Clear();
        firstRecordedAt = lastRecordedAt = null;
        foreach (var line in File.ReadLines(path))
        {
            var record = Parse(line);
            if (record is null) continue;
            retainedRecords++;
            if (record.Kind == "frame" && record.Frame is not null) retainedFrames++;
            if (record.Kind == "clipSave" && record.ClipSave is { ActivityId: not null, EventType: not null } save)
                clipSaveIds.Add(save.OperationId);
            if (record.Kind == "runEvent" && record.RunEvent is not null) retainedRunEvents++;
            if (record.Kind == "tableEvent" && record.TableEvent is not null) retainedTableEvents++;
            firstRecordedAt ??= record.RecordedAt;
            lastRecordedAt = record.RecordedAt;
        }
        retainedEventClipOperations = clipSaveIds.Count;
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
