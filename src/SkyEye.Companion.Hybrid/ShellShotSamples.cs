using System.Diagnostics;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    ShotSampleStore shotSampleStore = null!;
    ShotSampleCollector shotSamples = null!;
    readonly ShotSampleTranscriptGate sampleTranscriptGate = new();
    string sampleStatus = "";
    ShotSampleRecord[] savedShotSamples = [];
    ShotSampleRecord? latestShotSample;

    public bool SampleCollectionEnabled => Android.App.Application.Context
        .GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!.GetBoolean("shot-samples-enabled", false);
    public bool SampleCollectionBusy => shotSamples?.Busy == true;
    public string SampleCollectionLabel => SampleCollectionEnabled ? "Disable sample collection" : "Enable sample collection";
    public string SampleCollectionStatus => sampleStatus;

    void InitializeShotSamples(ClipSaveExecutor saves)
    {
        shotSampleStore = new(Path.Combine(FileSystem.AppDataDirectory, "shot-samples"));
        shotSamples = new(shotSampleStore);
        try
        {
            shotSampleStore.Recover(saves);
            savedShotSamples = shotSampleStore.All().Where(sample => sample.Status == "saved").ToArray();
            var latest = shotSampleStore.Latest();
            latestShotSample = latest;
            if (latest is { Status: "pending" }) sampleStatus = "Previous sample interrupted; check camera clips. No automatic retry.";
            else if (latest is not null) sampleStatus = SampleMessage(latest);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { sampleStatus = "Sample history is unavailable on this phone."; }
    }

    public void ToggleSampleCollection() => SetSampleCollection(!SampleCollectionEnabled);
    void SetSampleCollection(bool enabled)
    {
        Android.App.Application.Context.GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!
            .Edit()!.PutBoolean("shot-samples-enabled", enabled)!.Apply();
        sampleStatus = enabled ? "Sample collection on · reports save camera clips when recording." : "Sample collection off.";
        Changed?.Invoke();
    }

    void RememberSampleTranscript(string transcript) => sampleTranscriptGate.Remember(transcript);

    void ClearSampleTranscript() => sampleTranscriptGate.Clear();

    object ReadSampleCollection() => new
    {
        enabled = SampleCollectionEnabled, busy = SampleCollectionBusy, status = sampleStatus,
        desiredSampleSeconds = 5, cameraSaveSeconds = 30, settlingSeconds = 2,
        latest = latestShotSample is not { } last ? null : new { operationId = last.OperationId,
            status = last.Status == "pending" && !SampleCollectionBusy ? "unknown" : last.Status,
            clipId = last.Clip?.Id, durationSeconds = last.Clip?.Duration },
        limitation = "The camera currently saves at least 30 seconds. Actual clip duration and source origin are retained; the exact five-second shot window is not measured."
    };

    async Task<object> SetSampleCollectionFromVoiceAsync(JsonElement args, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!args.TryGetProperty("enabled", out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return new { error = "Say whether sample collection should be on or off." };
        bool enabled = value.GetBoolean();
        return await MainThread.InvokeOnMainThreadAsync(() => { SetSampleCollection(enabled); return ReadSampleCollection(); });
    }

    async Task<object> CollectShotSampleFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
    {
        string? quote = StringArg(args, "reported_words");
        if (string.IsNullOrWhiteSpace(quote) || quote.Length > 500) return new { error = "Quote a few exact words from the player's shot report." };
        var fields = new List<ShotSampleField>(); var notes = new List<string>();
        if (args.TryGetProperty("fields", out var rawFields))
        {
            if (rawFields.ValueKind != JsonValueKind.Array || rawFields.GetArrayLength() > 20) return new { error = "The sample fields were invalid." };
            foreach (var field in rawFields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object) return new { error = "The sample fields were invalid." };
                fields.Add(new(StringArg(field, "kind") ?? "", StringArg(field, "text") ?? ""));
            }
        }
        if (args.TryGetProperty("notes", out var rawNotes))
        {
            if (rawNotes.ValueKind != JsonValueKind.Array || rawNotes.GetArrayLength() > 10) return new { error = "The sample notes were invalid." };
            foreach (var note in rawNotes.EnumerateArray())
            { if (note.ValueKind != JsonValueKind.String) return new { error = "The sample notes were invalid." }; notes.Add(note.GetString() ?? ""); }
        }

        // Input transcription may arrive after the model has begun its tool call.
        for (int attempt = 0; attempt < 15; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (sampleTranscriptGate.Contains(quote)) break;
            await Task.Delay(100, cancellation);
        }
        return await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!SampleCollectionEnabled) return (object)new { enabled = false, saved = false, message = "Sample collection is off." };
            if (shotSamples.Busy) return new { enabled = true, saved = false, busy = true, error = "A sample is already saving. This report was not queued." };
            var state = app.State; var camera = state.Camera;
            if (!active || foreground is null || !camera.CanControl || camera.Capture != CaptureState.Active || camera.Scope is not { } scope || string.IsNullOrWhiteSpace(client.HostToken))
                return new { enabled = true, saved = false, error = "An active recording and camera host are required. Sample collection does not start recording." };

            if (!sampleTranscriptGate.TryClaim(quote, fields, notes, out var report, out long tick, out var reportError))
                return new { enabled = true, saved = false, error = reportError };
            var observation = state.Observation is { } obs && obs.Source == scope ? obs : null;
            var context = new ShotSampleContext(table.State.Source == scope && table.State.Calibration is not null ? table.State.CalibrationId : null,
                observation?.AnalysisGeneration, observation?.Timeline, observation?.SourceTimeUs, observation?.CapturedAt,
                observation?.Verification?.Configuration);
            var saveScope = new ClipSaveScope(scope.CameraIdentity, scope.SessionId, scope.HostGeneration, scope.CaptureRevision);
            var record = new ShotSampleRecord(1, VoiceOperationId(callId, "shot-sample"), "pending", "player-reported",
                report!, saveScope, context, DateTimeOffset.UtcNow, 30, 5, 2,
                "Spoken report time is not mapped to camera source time; the exact five-second window and inclusion of the shot are unverified.");
            var wait = TimeSpan.FromSeconds(2) - Stopwatch.GetElapsedTime(tick);
            latestShotSample = record; sampleStatus = "Saving sample…"; Changed?.Invoke();
            var task = FinishShotSampleAsync(record, wait, scope, foreground.Token);
            if (task.IsCompletedSuccessfully)
            {
                var immediate = task.Result;
                return new { enabled = true, pending = false, saved = immediate.Status == "saved", status = immediate.Status, message = SampleMessage(immediate) };
            }
            return new { enabled = true, pending = true, saved = false, operationId = record.OperationId,
                message = "Saving the sample; completion is not yet confirmed. The camera's shortest saved clip is 30 seconds." };
        });
    }

    async Task<ShotSampleRecord> FinishShotSampleAsync(ShotSampleRecord record, TimeSpan wait, CameraScope scope, CancellationToken cancellation)
    {
        var result = await shotSamples.CollectAsync(record, wait, async (id, saveScope, token) =>
        {
            var current = await MainThread.InvokeOnMainThreadAsync(() => app.State);
            if (!active || current.Camera.Scope != scope || !current.Camera.CanControl || current.Camera.Capture != CaptureState.Active)
                return new CommandReceipt(id, CommandOutcome.Failed, current.Version, "Recording or camera ownership changed before the save. No clip was requested.");
            return await app.DispatchAsync(new(id, CommandOrigin.Sky, current.Version, new SaveClip(30, saveScope)), token);
        }, cancellation);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            latestShotSample = result; sampleStatus = SampleMessage(result);
            if (result.Status == "saved") savedShotSamples = shotSampleStore.All().Where(sample => sample.Status == "saved").ToArray();
            if (result.Status is "saved" or "unknown" or "metadata-failed") _ = LoadClipsAsync(CancellationToken.None);
            Changed?.Invoke();
        });
        return result;
    }

    public ShotSampleRecord? SampleForClip(string clipId) => savedShotSamples.FirstOrDefault(sample => sample.Clip?.Id == clipId);

    public void ShareSampleMetadata(string clipId)
    {
        var sample = SampleForClip(clipId);
        var activity = Platform.CurrentActivity;
        if (sample is null || activity is null) { ShowError("Sample metadata is unavailable."); return; }
        var file = shotSampleStore.PathFor(sample.OperationId);
        if (!File.Exists(file)) { ShowError("Sample metadata is unavailable."); return; }
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, activity.PackageName + ".files", new Java.IO.File(file));
        using var intent = new Android.Content.Intent(Android.Content.Intent.ActionSend).SetType("application/json")
            .PutExtra(Android.Content.Intent.ExtraStream, uri).AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission);
        using var chooser = Android.Content.Intent.CreateChooser(intent, "Share sample metadata");
        activity.StartActivity(chooser);
    }

    static string SampleMessage(ShotSampleRecord record) => record.Status switch
    {
        "saved" when record.Clip is { } clip => $"Sample saved · {clip.Duration:0.0}s clip · 5s target timing unverified.",
        "pending" => "Saving sample…",
        _ => record.Message ?? "Sample was not confirmed."
    };
}
