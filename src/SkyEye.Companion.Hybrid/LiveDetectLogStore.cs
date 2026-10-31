using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyEye.Core.Analysis;

namespace SkyEye.Companion.Hybrid;

internal sealed record LiveDetectLoggedBall(string Id, string Colour, double X, double Y, double Confidence);
internal sealed record LiveDetectLoggedPosition(string Colour, double X, double Y);
internal sealed record LiveDetectLogFrameEvidence(Guid? EvidenceId, string? RelativePath, string? ContentType,
    int ByteLength, string? Sha256, string Trigger, bool Available, string? SkipReason = null);
internal sealed record PendingLiveDetectFrameEvidence(EncodedTableFrameEvidence? Encoded, string Trigger, string? SkipReason = null);
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PlayerPlaybackTelemetry? PlayerPlayback = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PocketColourAuditTelemetry? PocketColourAudit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? BaselineMatchSamples = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? SettledBaselineMatchesCurrent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LiveDetectLogFrameEvidence? FrameEvidence = null);
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
    DateTimeOffset? FirstRecordedAt, DateTimeOffset? LastRecordedAt, int RetainedEvidenceFrames, long RetainedEvidenceBytes);
internal sealed record LiveDetectLogReadout(LiveDetectLogSummary Summary, int RequestedLimit,
    int ReturnedRecords, LiveDetectLogRecord[] Records);

/// <summary>Bounded Live Detect frame receipts, source-image sidecars and automatic clip-save receipts.</summary>
internal sealed class LiveDetectLogStore
{
    internal const int CurrentSchemaVersion = 14;
    public const int MaximumRetainedRecords = 2000;
    internal const long MaximumBytes = 4 * 1024 * 1024;
    internal const long MaximumEvidenceBytes = 128 * 1024 * 1024;
    internal const int MaximumEvidenceFiles = 96;
    internal const int MaximumEvidenceFileBytes = 16 * 1024 * 1024;
    const int RetainedRecordTarget = MaximumRetainedRecords * 3 / 4;
    const long RetainedBytesTarget = MaximumBytes * 3 / 4;
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    readonly object gate = new();
    readonly string path;
    readonly string evidenceDirectory;
    readonly HashSet<Guid> clipSaveIds = [];
    readonly HashSet<Guid> evidenceIds = [];
    int retainedRecords, retainedFrames, retainedEventClipOperations, retainedRunEvents, retainedTableEvents;
    int retainedEvidenceFrames;
    long retainedEvidenceBytes;
    DateTimeOffset? firstRecordedAt, lastRecordedAt;

    public LiveDetectLogStore(string path)
    {
        this.path = path;
        evidenceDirectory = Path.Combine(Path.GetDirectoryName(path)!, "evidence");
        lock (gate)
        {
            if (File.Exists(path)) RefreshIndex();
            PruneUnreferencedEvidence();
        }
    }

    public LiveDetectLogSummary Summary
    {
        get
        {
            lock (gate) return new(CurrentSchemaVersion, "live-detect/live-detect.jsonl", retainedRecords,
                retainedFrames, retainedEventClipOperations, retainedRunEvents, retainedTableEvents, firstRecordedAt, lastRecordedAt,
                retainedEvidenceFrames, retainedEvidenceBytes);
        }
    }

    public void AppendFrame(Guid runId, DateTimeOffset recordedAt, LiveDetectLogFrame frame) =>
        Append(new(CurrentSchemaVersion, "frame", runId, recordedAt, Frame: frame));

    public LiveDetectLogFrameEvidence SaveFrameEvidence(EncodedTableFrameEvidence encoded, string trigger)
    {
        if (encoded.Pixels.Length == 0)
            return new(null, null, null, 0, null, trigger, false, "empty image");
        string extension = encoded.Extension.ToLowerInvariant();
        if (extension is not (".png" or ".jpg") || encoded.ContentType is not ("image/png" or "image/jpeg"))
            return new(null, null, null, 0, null, trigger, false, "unsupported image format");
        if (encoded.Pixels.Length > MaximumEvidenceFileBytes)
            return new(null, null, null, encoded.Pixels.Length, null, trigger, false, "frame exceeds per-file limit");

        lock (gate)
        {
            Directory.CreateDirectory(evidenceDirectory);
            var files = Directory.EnumerateFiles(evidenceDirectory)
                .Where(file => Path.GetExtension(file) is ".png" or ".jpg")
                .Select(file => new FileInfo(file)).ToArray();
            long currentBytes = files.Sum(file => file.Length);
            if (files.Length >= MaximumEvidenceFiles || currentBytes + encoded.Pixels.Length > MaximumEvidenceBytes)
                return new(null, null, null, encoded.Pixels.Length, null, trigger, false, "evidence storage limit reached");

            Guid id = Guid.NewGuid();
            string fileName = id.ToString("N") + extension;
            string destination = Path.Combine(evidenceDirectory, fileName);
            AtomicWriteBytes(destination, encoded.Pixels);
            string hash = Convert.ToHexString(SHA256.HashData(encoded.Pixels)).ToLowerInvariant();
            return new(id, "evidence/" + fileName, encoded.ContentType, encoded.Pixels.Length, hash, trigger, true);
        }
    }

    public bool TryGetFrameEvidence(Guid evidenceId, out string filePath, out string contentType)
    {
        lock (gate)
        {
            foreach ((string extension, string type) in new[] { (".png", "image/png"), (".jpg", "image/jpeg") })
            {
                string candidate = Path.Combine(evidenceDirectory, evidenceId.ToString("N") + extension);
                if (evidenceIds.Contains(evidenceId) && File.Exists(candidate))
                {
                    filePath = candidate;
                    contentType = type;
                    return true;
                }
            }
        }
        filePath = "";
        contentType = "application/octet-stream";
        return false;
    }

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
            if (record.Kind == "frame" && record.Frame?.FrameEvidence is { Available: true, EvidenceId: { } evidenceId }
                && evidenceIds.Add(evidenceId))
            {
                retainedEvidenceFrames++;
                retainedEvidenceBytes += record.Frame.FrameEvidence.ByteLength;
            }
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
                retainedFrames, retainedEventClipOperations, retainedRunEvents, retainedTableEvents, firstRecordedAt, lastRecordedAt,
                retainedEvidenceFrames, retainedEvidenceBytes),
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
        PruneUnreferencedEvidence();
    }

    void RefreshIndex()
    {
        retainedRecords = retainedFrames = retainedEventClipOperations = retainedRunEvents = retainedTableEvents = retainedEvidenceFrames = 0;
        retainedEvidenceBytes = 0;
        clipSaveIds.Clear();
        evidenceIds.Clear();
        firstRecordedAt = lastRecordedAt = null;
        foreach (var line in File.ReadLines(path))
        {
            var record = Parse(line);
            if (record is null) continue;
            retainedRecords++;
            if (record.Kind == "frame" && record.Frame is not null) retainedFrames++;
            if (record.Kind == "frame" && record.Frame?.FrameEvidence is { Available: true, EvidenceId: { } id }
                && EvidenceFileExists(id)) evidenceIds.Add(id);
            if (record.Kind == "clipSave" && record.ClipSave is { ActivityId: not null, EventType: not null } save)
                clipSaveIds.Add(save.OperationId);
            if (record.Kind == "runEvent" && record.RunEvent is not null) retainedRunEvents++;
            if (record.Kind == "tableEvent" && record.TableEvent is not null) retainedTableEvents++;
            firstRecordedAt ??= record.RecordedAt;
            lastRecordedAt = record.RecordedAt;
        }
        retainedEventClipOperations = clipSaveIds.Count;
        foreach (Guid id in evidenceIds)
        {
            var file = EvidencePath(id);
            if (file is not null)
            {
                retainedEvidenceFrames++;
                retainedEvidenceBytes += new FileInfo(file).Length;
            }
        }
    }

    void PruneUnreferencedEvidence()
    {
        if (!Directory.Exists(evidenceDirectory)) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(evidenceDirectory))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!Guid.TryParseExact(name, "N", out var id) || !evidenceIds.Contains(id))
                {
                    try { File.Delete(file); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    bool EvidenceFileExists(Guid id) => EvidencePath(id) is { } file && File.Exists(file);

    string? EvidencePath(Guid id)
    {
        string png = Path.Combine(evidenceDirectory, id.ToString("N") + ".png");
        if (File.Exists(png)) return png;
        string jpg = Path.Combine(evidenceDirectory, id.ToString("N") + ".jpg");
        return File.Exists(jpg) ? jpg : null;
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

    static void AtomicWriteBytes(string path, byte[] value)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, value);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

/// <summary>Captures a bounded, source-matched sample when analysis quality or inventory changes.</summary>
internal sealed class LiveDetectFrameEvidenceCapture
{
    internal const int MaximumFramesPerRun = 6;
    static readonly TimeSpan MinimumSampleInterval = TimeSpan.FromSeconds(3);
    int attempts;
    string? previousQuality;
    string? previousInventory;
    DateTimeOffset? lastCaptureAt;

    public PendingLiveDetectFrameEvidence? Observe(TableFrame frame, VisionResult vision)
    {
        string quality = vision.Obstructed ? "obstructed" : vision.HasUncertainLayout ? "uncertain" : "clear";
        var grouped = vision.Balls.GroupBy(ball => ball.Kind, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key + ":" + group.Count());
        string inventory = string.Join(",", grouped);
        string? trigger = attempts == 0 ? "initial"
            : previousQuality != quality ? "quality:" + quality
            : previousInventory != inventory ? "inventory:" + inventory
            : lastCaptureAt is { } last && frame.CapturedAt - last >= MinimumSampleInterval ? "interval:3s"
            : null;
        previousQuality = quality;
        previousInventory = inventory;
        if (trigger is null || attempts >= MaximumFramesPerRun) return null;

        attempts++;
        lastCaptureAt = frame.CapturedAt;
        try
        {
            return new(frame.EncodeForEvidence(), trigger);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return new(null, trigger, error.GetType().Name);
        }
    }
}
