using System.Text.Json;
using System.Diagnostics;

namespace SkyEye.Core.Application;

/// <summary>Convenience choices for the companion UI. Saves are still sent as source-time ranges.</summary>
public static class RecentClipDurationPresets
{
    public static readonly int[] Values = [5, 10, 15, 30, 60, 120, 300, 600];
}

public static class VoiceClipSavePolicy
{
    public static bool TryResolve(int requestedSeconds, out int cameraSeconds)
    {
        cameraSeconds = requestedSeconds;
        return requestedSeconds is > 0 and <= 600;
    }
}

// The text values are excerpts of the player's transcript. Their categories are
// Sky's suggestions, not a ruling or independently observed facts.
public sealed record ShotSampleField(string Kind, string Text);
public sealed record ShotSampleContext(string? CalibrationId, long? AnalysisGeneration,
    string? ObservationTimeline, long? ObservationSourceTimeUs, DateTimeOffset? ObservationCapturedAt,
    string? VerificationConfiguration);
public sealed record ShotSampleReport(string OriginalWords, DateTimeOffset TranscribedAt,
    IReadOnlyList<ShotSampleField> Fields, IReadOnlyList<string> Notes, string TranscriptStatus = "completed");
public sealed record ShotSampleRecord(int Schema, Guid OperationId, string Status, string Provenance,
    ShotSampleReport Report, ClipSaveScope Scope, ShotSampleContext Context, DateTimeOffset? RequestedAt,
    int RequestedSeconds, int? DesiredSampleSeconds, int? PostDeclarationSeconds, string TimingLimitation,
    Clip? Clip = null, string? Message = null, IReadOnlyList<ShotSampleReport>? AdditionalReports = null,
    bool Retrospective = false)
{
    public double? ActualClipSeconds => Clip?.Duration;
    public bool? ClipShorterThanRequested => Clip is null ? null : Clip.Duration < RequestedSeconds;
    public long? SourceStartUs => Clip?.RecordingOrigin?.StartUs;
    public string? SourceTimeline => Clip?.RecordingOrigin?.Timeline;
    public string ShotWindowCoverage => "unknown";
}

/// <summary>Associates a model's quoted words with the latest completed input
/// transcript. It cannot establish the physical shot's source timestamp.</summary>
public sealed class ShotSampleTranscriptGate
{
    readonly object gate = new();
    string? transcript;
    DateTimeOffset transcribedAt;
    long tick, serial, claimed, epoch;
    bool finalized;

    public void Remember(string words)
    {
        lock (gate)
        {
            if (transcript is null || finalized) serial++;
            transcript = words; finalized = true; transcribedAt = DateTimeOffset.UtcNow; tick = Stopwatch.GetTimestamp();
        }
    }
    public void Append(string words)
    {
        if (string.IsNullOrEmpty(words)) return;
        lock (gate)
        {
            if (transcript is null || finalized) { transcript = ""; finalized = false; serial++; }
            // Never silently truncate a player report. Overlong input will be rejected.
            if (transcript.Length <= 2000) transcript += words;
            transcribedAt = DateTimeOffset.UtcNow; tick = Stopwatch.GetTimestamp();
        }
    }
    public void Clear() { lock (gate) { transcript = null; finalized = false; epoch++; } }
    public long Epoch { get { lock (gate) return epoch; } }
    public bool TranscriptReady { get { lock (gate) return transcript is not null && (finalized || Stopwatch.GetElapsedTime(tick) >= TimeSpan.FromMilliseconds(350)); } }
    public bool Contains(string quote)
    { lock (gate) return transcript?.Contains(quote, StringComparison.OrdinalIgnoreCase) == true; }

    public string? RecentWords()
    {
        lock (gate) return transcript is not null && Stopwatch.GetElapsedTime(tick) <= TimeSpan.FromSeconds(20)
            && claimed != serial ? transcript : null;
    }
    public bool RecentAlreadyClaimed()
    {
        lock (gate) return transcript is not null && Stopwatch.GetElapsedTime(tick) <= TimeSpan.FromSeconds(20)
            && claimed == serial;
    }

    public bool TryClaim(string quote, IReadOnlyList<ShotSampleField> fields, IReadOnlyList<string> notes,
        out ShotSampleReport? report, out long timestamp, out string error, long? requiredEpoch = null)
    {
        lock (gate)
        {
            report = null; timestamp = 0;
            if (requiredEpoch is { } expected && epoch != expected || string.IsNullOrWhiteSpace(quote) || transcript is null || !transcript.Contains(quote, StringComparison.OrdinalIgnoreCase)
                || Stopwatch.GetElapsedTime(tick) > TimeSpan.FromSeconds(20))
            { error = "The original player transcript was unavailable or did not match. Say the shot report again."; return false; }
            if (claimed == serial)
            { error = "This spoken report was already submitted; no second clip was requested."; return false; }
            var candidate = new ShotSampleReport(transcript, transcribedAt, fields, notes, finalized ? "completed" : "partial");
            if (!ShotSampleStore.Valid(candidate))
            { error = "A proposed field or note did not appear in the player's words. No sample was saved."; return false; }
            claimed = serial; report = candidate; timestamp = tick; error = ""; return true;
        }
    }
}

/// <summary>Private companion-side sample journal. A pending report is durable
/// before the camera save; a successful camera receipt supplies the clip identity.</summary>
public sealed class ShotSampleStore(string directory)
{
    readonly string root = Path.GetFullPath(directory);
    readonly object gate = new();
    static readonly HashSet<string> Kinds = new(StringComparer.Ordinal)
        { "outcome", "ball", "shot", "position", "other" };

    public static bool Valid(ShotSampleReport report)
        => report is not null && report.OriginalWords is { Length: > 0 and <= 2000 }
        && !string.IsNullOrWhiteSpace(report.OriginalWords)
        && report.TranscriptStatus is "completed" or "partial" or "typed"
        && report.Fields is { Count: <= 20 } && report.Notes is { Count: <= 10 }
        && report.Fields.All(f => f is not null && Kinds.Contains(f.Kind) && Excerpt(report.OriginalWords, f.Text))
        && report.Notes.All(n => Excerpt(report.OriginalWords, n));

    static bool Excerpt(string original, string text) => text is { Length: > 0 and <= 500 }
        && !string.IsNullOrWhiteSpace(text) && original.Contains(text, StringComparison.OrdinalIgnoreCase);

    public void Write(ShotSampleRecord record)
    {
        lock (gate) WriteLocked(record);
    }

    void WriteLocked(ShotSampleRecord record)
    {
        if (record.OperationId == Guid.Empty || !Valid(record.Report)
            || record.AdditionalReports is { Count: > 50 } || record.AdditionalReports?.Any(report => !Valid(report)) == true)
            throw new ArgumentException("Invalid sample report.");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, record.OperationId.ToString("N") + ".json");
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, record, JsonDefaults.Options); file.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public ShotSampleRecord? Read(Guid id)
    {
        if (id == Guid.Empty) return null;
        try
        {
            string path = Path.Combine(root, id.ToString("N") + ".json");
            if (!File.Exists(path) || new FileInfo(path).Length > 65536) return null;
            var record = JsonSerializer.Deserialize<ShotSampleRecord>(File.ReadAllText(path), JsonDefaults.Options);
            return record is { Schema: 1 } && record.OperationId == id && Valid(record.Report)
                && record.AdditionalReports is not { Count: > 50 } && record.AdditionalReports?.Any(report => !Valid(report)) != true ? record : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }

    public ShotSampleRecord? Latest()
    {
        return All().FirstOrDefault();
    }

    public ShotSampleRecord[] All()
    {
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*.json")
            .Select(path => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id) ? Read(id) : null)
            .Where(record => record is not null).Select(record => record!).OrderByDescending(record => record.RequestedAt ?? record.Clip?.CreatedAt ?? DateTimeOffset.MinValue).ToArray();
    }

    public string PathFor(Guid id) => Path.Combine(root, id.ToString("N") + ".json");

    public ShotSampleRecord AppendReport(Guid id, ShotSampleReport report)
    {
        if (!Valid(report)) throw new ArgumentException("Invalid player report.");
        lock (gate)
        {
            var current = Read(id) ?? throw new IOException("Sample metadata is unavailable.");
            if (current.Status is not ("pending" or "saved")) throw new InvalidOperationException("The sample has no confirmed or pending clip.");
            var updated = current with { AdditionalReports = (current.AdditionalReports ?? []).Append(report).ToArray() };
            WriteLocked(updated); return updated;
        }
    }

    public ShotSampleRecord AttachToAcknowledgedClip(AcknowledgedClipSave acknowledgement, ShotSampleReport report)
    {
        if (acknowledgement is null || acknowledgement.OperationId == Guid.Empty || !Valid(report))
            throw new ArgumentException("Invalid acknowledged clip or player report.");
        ClipLibrary.Validate(acknowledgement.Clip);
        lock (gate)
        {
            var current = Read(acknowledgement.OperationId);
            ShotSampleRecord result;
            if (current is null)
            {
                result = new(1, acknowledgement.OperationId, "saved", "player-reported", report,
                    acknowledgement.Scope, new(null, null, null, null, null, null), null,
                    acknowledgement.RequestedSeconds, null, null,
                    "The player attached this report to an existing clip. Exact shot time and reported outcome are not independently established.",
                    acknowledgement.Clip, "Player report attached to the acknowledged clip.", Retrospective: true);
            }
            else
            {
                if (current.Scope != acknowledgement.Scope || current.Status is not ("pending" or "saved")
                    || current.Clip is { } prior && (prior.Id != acknowledgement.Clip.Id
                        || !string.Equals(prior.Sha256, acknowledgement.Clip.Sha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The acknowledged clip does not match the sample.");
                result = current with { Status = "saved", Clip = acknowledgement.Clip,
                    AdditionalReports = (current.AdditionalReports ?? []).Append(report).ToArray(),
                    Message = "Player report attached to the acknowledged clip." };
            }
            WriteLocked(result); return result;
        }
    }

    public ShotSampleRecord Complete(ShotSampleRecord request, string status, Clip? clip, string message)
    {
        lock (gate)
        {
            var current = Read(request.OperationId) ?? throw new IOException("Pending sample metadata is unavailable.");
            var result = current with { Status = status, Clip = clip, Message = message };
            WriteLocked(result); return result;
        }
    }

    // A crash after the camera acknowledgement can leave the sample pending.
    // Recover only from the same operation's durable, validated save receipt.
    public int Recover(ClipSaveExecutor saves)
    {
        if (!Directory.Exists(root)) return 0;
        int recovered = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
            var sample = Read(id);
            if (sample?.Status != "pending") continue;
            var clip = saves.FindAcknowledgedClip(id);
            if (clip is null) continue;
            Write(sample with { Status = "saved", Clip = clip, Message = "Recovered from the durable clip save receipt." });
            recovered++;
        }
        return recovered;
    }
}

public sealed class ShotSampleCollector(ShotSampleStore store, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    int busy;
    public bool Busy => Volatile.Read(ref busy) != 0;

    public async Task<ShotSampleRecord> CollectAsync(ShotSampleRecord request, TimeSpan wait,
        Func<Guid, ClipSaveScope, CancellationToken, Task<CommandReceipt>> save, CancellationToken cancellation)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            return request with { Status = "busy", Message = "A sample is already being saved. This report was not saved." };
        try
        {
            if (request.Status != "pending" || !ShotSampleStore.Valid(request.Report))
                return request with { Status = "failed", Message = "The player transcript or its excerpts could not be verified." };
            try { store.Write(request); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return request with { Status = "failed", Message = "The report could not be stored; no clip save was sent." }; }

            CommandReceipt receipt;
            bool saveStarted = false;
            try
            {
                if (wait > TimeSpan.Zero) await (delay ?? Task.Delay)(wait, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                saveStarted = true;
                receipt = await save(request.OperationId, request.Scope, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A save adapter may have sent the request before cancellation.
                return Finish(request, saveStarted ? "unknown" : "failed", null, saveStarted
                    ? "The sample was interrupted. Check camera clips; its save result is unknown."
                    : "The sample was interrupted before the clip save was sent.");
            }
            catch (Exception)
            { return Finish(request, "unknown", null, "The save result is unknown. Check camera clips; this request was not repeated."); }

            if (receipt.Outcome == CommandOutcome.Applied && receipt.SavedClip is { } clip)
                return Finish(request, "saved", clip, "Clip and player report saved.");
            if (receipt.Outcome == CommandOutcome.Applied)
                return Finish(request, "unknown", null, "The camera did not provide a clip identity. Check camera clips; the report is not linked.");
            return Finish(request, receipt.Outcome == CommandOutcome.Uncertain ? "unknown" : "failed", null, receipt.Message);
        }
        finally { Volatile.Write(ref busy, 0); }
    }

    ShotSampleRecord Finish(ShotSampleRecord request, string status, Clip? clip, string message)
    {
        var result = request with { Status = status, Clip = clip, Message = message };
        try { return store.Complete(request, status, clip, message); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return result with { Status = clip is null ? "unknown" : "metadata-failed", Message = clip is null
            ? "The result could not be stored. Check camera clips."
            : "The clip was saved, but its report could not be linked on this phone. The pending report can be recovered from the save receipt." }; }
    }
}
