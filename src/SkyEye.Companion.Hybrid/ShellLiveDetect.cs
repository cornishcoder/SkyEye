using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Core.Analysis;
using System.Text.Json;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    readonly record struct LiveDetectFrameAnchor(long? FrameId, long? SourceTimeUs, DateTimeOffset? CapturedAt);
    readonly record struct LiveDetectAutoClipContext(SkyEyeState AppState,
        CameraScope? TableSource, LiveDetectFrameAnchor Frame);
    const int LiveDetectAutoClipSeconds = 30;

    CancellationTokenSource? liveDetectCancellation;
    Task? liveDetectTask;
    string? liveDetectStopReason;
    string? liveDetectLogError;
    Guid? liveDetectRunId;
    LiveDetectLogStore liveDetectLog = null!;
    string liveDetectStatus = "Live Detect is off.";
    string liveDetectClipStatus = "Automatic 30-second clips start after Live Detect has run for 30 seconds.";
    string liveDetectInventory = "";
    DateTimeOffset? liveDetectReadAt;
    readonly Queue<string> liveDetectEvents = new();
    bool liveDetectHasResult;

    public bool LiveDetectRunning => liveDetectCancellation is { IsCancellationRequested: false };
    public bool LiveDetectStopping => liveDetectCancellation?.IsCancellationRequested == true;
    public bool LiveDetectBusy => liveDetectCancellation is not null;
    public bool CanStartLiveDetect => CanAnalyzeTable && HasTableCalibration && !EditingTable
        && table.VisionDiagnostics.LabelFrame is null && !table.VisionDiagnostics.LabelCapturePending
        && !CheckingTable && !VerificationCaptureRunning
        && !CandidateShortcutShown && !table.HasPendingWork;
    public bool CanToggleLiveDetect => LiveDetectRunning || liveDetectCancellation is null && CanStartLiveDetect;
    public bool LiveDetectFeedbackVisible => liveDetectCancellation is not null || liveDetectHasResult
        || SessionMode == TableSessionMode.Live && HasTableCalibration;
    public string LiveDetectShortcutLabel => LiveDetectRunning ? "Stop live detect"
        : LiveDetectStopping ? "Finishing live detect"
        : CanStartLiveDetect ? "Start live detect"
        : $"Live detect unavailable: {LiveDetectUnavailableReason}";
    public string LiveDetectTileText => LiveDetectRunning ? "Detecting"
        : LiveDetectStopping ? "Stopping" : "Live detect";
    public string LiveDetectStatus => LiveDetectRunning ? liveDetectStatus
        : LiveDetectStopping ? "Stopping Live Detect after the current frame…" : liveDetectStatus;
    public string LiveDetectInventory => liveDetectInventory;
    public string LiveDetectClipStatus => liveDetectClipStatus;
    public string LiveDetectReadAt => liveDetectReadAt is { } value ? value.ToLocalTime().ToString("HH:mm:ss") : "";
    public string LiveDetectLogStatus => liveDetectLogError is { Length: > 0 } error
        ? $"Log write failed · {error}"
        : $"Logged {liveDetectLog.Summary.RetainedFrames} frames · {liveDetectLog.Summary.RetainedClipSaveOperations} automatic clips · GET /api/live-detect-log";
    public IReadOnlyList<string> LiveDetectEvents => liveDetectEvents.ToArray();

    string LiveDetectUnavailableReason => !CanAnalyzeTable ? "start a live session first"
        : !HasTableCalibration ? "calibrate the table first"
        : EditingTable ? "finish the table edit first"
        : CheckingTable || table.HasPendingWork ? "table analysis is already running"
        : VerificationCaptureRunning ? "frame capture is running"
        : CandidateShortcutShown ? "hide candidate shots first"
        : table.VisionDiagnostics.LabelFrame is not null || table.VisionDiagnostics.LabelCapturePending ? "finish the labelled frame first"
        : "check the live table";

    void InitializeLiveDetectLog()
    {
        var root = Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath ?? FileSystem.AppDataDirectory, "live-detect");
        liveDetectLog = new(Path.Combine(root, "live-detect.jsonl"));
    }

    public Task ToggleLiveDetectAsync() => MainThread.InvokeOnMainThreadAsync(ToggleLiveDetectOnMainThreadAsync);

    async Task ToggleLiveDetectOnMainThreadAsync()
    {
        if (liveDetectCancellation is { IsCancellationRequested: false } current)
        {
            liveDetectStopReason = "Live Detect stopped.";
            current.Cancel();
            Changed?.Invoke();
            return;
        }
        if (liveDetectCancellation is not null) return;
        if (!CanStartLiveDetect)
        {
            liveDetectStatus = $"Live Detect unavailable · {LiveDetectUnavailableReason}.";
            Changed?.Invoke();
            return;
        }

        var diagnostics = table.VisionDiagnostics;
        if (diagnostics.LabelFrame is not null || diagnostics.LabelCapturePending)
        {
            liveDetectStatus = "Finish the labelled frame before starting Live Detect.";
            Changed?.Invoke();
            return;
        }

        bool resumeDiagnostics = diagnostics.Active;
        if (VisionDashboardActive) visionDashboardSamplingPaused = true;
        try
        {
            if (resumeDiagnostics) await table.StopVisionDiagnosticsAsync();
        }
        catch
        {
            if (VisionDashboardActive) visionDashboardSamplingPaused = false;
            throw;
        }

        if (!CanStartLiveDetect)
        {
            if (VisionDashboardActive) visionDashboardSamplingPaused = false;
            liveDetectStatus = $"Live Detect unavailable · {LiveDetectUnavailableReason}.";
            Changed?.Invoke();
            return;
        }

        var snapshot = table.State;
        if (foreground is null || snapshot.Source is null || snapshot.Calibration is null)
        {
            if (VisionDashboardActive) visionDashboardSamplingPaused = false;
            liveDetectStatus = "Live Detect unavailable · start a calibrated live session first.";
            Changed?.Invoke();
            return;
        }

        var operation = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token);
        liveDetectCancellation = operation;
        var runId = Guid.NewGuid();
        liveDetectRunId = runId;
        liveDetectStopReason = null;
        liveDetectLogError = null;
        liveDetectStatus = "Live Detect starting · waiting for the first full analysis.";
        liveDetectClipStatus = "Automatic 30-second clips start after Live Detect has run for 30 seconds.";
        liveDetectInventory = "";
        liveDetectReadAt = null;
        liveDetectEvents.Clear();
        liveDetectHasResult = false;
        Changed?.Invoke();
        liveDetectTask = RunLiveDetectAsync(operation, runId, snapshot.Source, snapshot.Calibration,
            snapshot.CalibrationId, resumeDiagnostics);
    }

    void RequestLiveDetectStop(string reason)
    {
        if (liveDetectCancellation is not { IsCancellationRequested: false } current) return;
        liveDetectStopReason = reason;
        current.Cancel();
    }

    async Task StopLiveDetectAndWaitAsync(string reason)
    {
        RequestLiveDetectStop(reason);
        var current = liveDetectTask;
        if (current is not null)
        {
            try { await current; }
            catch (OperationCanceledException) { }
        }
    }

    async Task RunLiveDetectAsync(CancellationTokenSource operation, Guid runId, CameraScope source,
        TableCalibration calibration, string calibrationId, bool resumeDiagnostics)
    {
        await Task.Yield();
        var cancellation = operation.Token;
        var autoClipSaver = RunLiveDetectClipSavesAsync(cancellation, runId, source);
        AppendLiveDetectRunEvent(runId, "started", "Live Detect started.", null);
        var candidateTracker = new LiveDetectCountCandidateTracker();
        var frameCursor = new LiveDetectFrameCursor();
        int consecutiveAnalysisErrors = 0;
        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                long passStart = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!CanAnalyzeTable || EditingTable || table.State.Source != source
                    || table.State.CalibrationId != calibrationId)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        RequestLiveDetectStop("Live Detect stopped · the live source or calibration changed."));
                    break;
                }

                // A single pass is awaited before the next request. Each pass reads the
                // newest decoded frame, so slow analysis never queues stale frames.
                try
                {
                    await table.UpdateAsync(cancellation);
                    consecutiveAnalysisErrors = 0;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    consecutiveAnalysisErrors++;
                    AppendLiveDetectRunEvent(runId, consecutiveAnalysisErrors >= 3 ? "analysis_failed" : "analysis_retry",
                        consecutiveAnalysisErrors >= 3
                            ? "Table analysis failed three times consecutively; Live Detect stopped."
                            : $"Table analysis failed; retrying ({consecutiveAnalysisErrors} of 3).",
                        error.GetType().Name);
                    if (consecutiveAnalysisErrors >= 3)
                    {
                        liveDetectStopReason = "Live Detect stopped · table analysis failed three times in a row.";
                        break;
                    }
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (ReferenceEquals(liveDetectCancellation, operation) && !cancellation.IsCancellationRequested)
                        {
                            liveDetectStatus = $"Live Detect retrying · analysis error ({error.GetType().Name}), {consecutiveAnalysisErrors} of 3.";
                            Changed?.Invoke();
                        }
                    });
                    await Task.Delay(TimeSpan.FromMilliseconds(350), cancellation);
                    continue;
                }
                cancellation.ThrowIfCancellationRequested();
                var state = table.State;
                var observation = state.Observation;
                var receipt = observation?.DecodedReceipt;
                var vision = state.Vision;
                long priorMediaGeneration = frameCursor.MediaGeneration;
                if (observation is null || receipt is null || observation.Source != source
                    || state.Calibration != calibration || vision is null
                    || observation.SourceTimeUs is not { } sourceTimeUs
                    || !frameCursor.TryAccept(receipt.MediaGeneration, observation.Timeline, receipt.FrameId,
                    sourceTimeUs, out bool timelineChanged))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (ReferenceEquals(liveDetectCancellation, operation) && !cancellation.IsCancellationRequested)
                        {
                            liveDetectStatus = "Live Detect running · waiting for a newer paired camera frame.";
                            liveDetectInventory = liveDetectReadAt is { } priorRead
                                ? $"Last accepted reading {priorRead.ToLocalTime():HH:mm:ss} · waiting for a fresh camera frame."
                                : "Waiting for the first current camera frame.";
                            Changed?.Invoke();
                        }
                    });
                    await Task.Delay(TimeSpan.FromMilliseconds(150), cancellation);
                    continue;
                }

                if (timelineChanged) candidateTracker.Reset();
                if (priorMediaGeneration > 0 && receipt.MediaGeneration != priorMediaGeneration)
                    AppendLiveDetectRunEvent(runId, "decoder_recovered",
                        timelineChanged ? "New camera timeline; inventory baseline reset."
                            : "Decoder recovered; camera timestamps continue with restarted frame IDs.", null);
                var counts = Count(vision.Balls);
                bool candidateEligible = !vision.HasUncertainLayout && !vision.Obstructed;
                var events = new List<string>();
                bool rackReset = false;
                if (candidateTracker.Observe(sourceTimeUs, counts, candidateEligible,
                    vision.PackedCluster is { Detected: true, Complete: true }) is { } candidate)
                {
                    events.AddRange(InventoryEvents(candidate));
                    rackReset = candidate.Kind == LiveDetectChangeKind.Racked;
                }

                var quality = vision.Obstructed ? "obstructed" : vision.HasUncertainLayout ? "uncertain"
                    : vision.Uncertain ? "clear layout · advice unavailable" : "clear detector output";
                var ageSeconds = Math.Max(0, observation.FrameAgeMs) / 1000;
                try
                {
                    liveDetectLog.AppendFrame(runId, DateTimeOffset.UtcNow, new LiveDetectLogFrame(
                        observation.CapturedAt, observation.FrameAgeMs, source.ConnectionId,
                        receipt.MediaGeneration, receipt.FrameId, sourceTimeUs, receipt.PresentationTimeUs,
                        receipt.SurfaceTimestampNs, observation.Timeline, observation.FrameSource,
                        calibrationId, counts.Total, counts.Reds, counts.Yellows, counts.Black,
                        counts.Cue, counts.Other, vision.Uncertain, vision.HasUncertainLayout, vision.Obstructed,
                        vision.ElapsedMs, System.Diagnostics.Stopwatch.GetElapsedTime(passStart).TotalMilliseconds,
                        vision.Warnings.ToArray(), vision.Balls.Select(ball => new LiveDetectLoggedBall(
                            ball.Id, ball.Kind, ball.Position.X, ball.Position.Y, ball.Confidence)).ToArray(),
                        events.ToArray()));
                    liveDetectLogError = null;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    liveDetectLogError = "storage unavailable";
                }
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!ReferenceEquals(liveDetectCancellation, operation) || cancellation.IsCancellationRequested) return;
                    liveDetectHasResult = true;
                    liveDetectReadAt = observation.CapturedAt;
                    liveDetectStatus = $"Live Detect running · full pass {vision.ElapsedMs / 1000:0.0}s.";
                    liveDetectInventory = $"{counts.Total} detected · {counts.Summary} · {quality} · frame age {ageSeconds:0.0}s";
                    if (rackReset) liveDetectEvents.Clear();
                    foreach (var item in events)
                    {
                        liveDetectEvents.Enqueue($"{observation.CapturedAt.ToLocalTime():HH:mm:ss} · {item}");
                        while (liveDetectEvents.Count > 3) liveDetectEvents.Dequeue();
                    }
                    Changed?.Invoke();
                });
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            liveDetectStopReason = $"Live Detect stopped · analysis error ({error.GetType().Name}). Check the live view and calibration.";
            AppendLiveDetectRunEvent(runId, "failed", liveDetectStopReason, error.GetType().Name);
        }
        finally
        {
            if (!cancellation.IsCancellationRequested) operation.Cancel();
            try { await autoClipSaver; }
            catch (OperationCanceledException) { }
            AppendLiveDetectRunEvent(runId, "stopped", liveDetectStopReason ?? "Live Detect stopped.", null);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (ReferenceEquals(liveDetectCancellation, operation))
                {
                    liveDetectCancellation = null;
                    liveDetectRunId = null;
                    liveDetectStatus = liveDetectStopReason ?? "Live Detect stopped.";
                    liveDetectStopReason = null;
                    if (liveDetectClipStatus.StartsWith("Automatic 30-second clips start", StringComparison.Ordinal))
                        liveDetectClipStatus = "Live Detect stopped before the first automatic clip interval completed.";
                    if (VisionDashboardActive) visionDashboardSamplingPaused = false;
                    if (resumeDiagnostics && !disposed && CanStartVisionDiagnostics)
                    {
                        if (table.StartVisionDiagnostics(out var status))
                        {
                            VisionDiagnosticsStatus = status;
                            if (VisionDashboardActive) VisionDashboardStatus = "Dashboard sampling resumed after Live Detect.";
                        }
                        else if (VisionDashboardActive)
                            VisionDashboardStatus = "Dashboard sampling is waiting to resume. " + status;
                    }
                    Changed?.Invoke();
                }
            });
            operation.Dispose();
        }
    }

    async Task RunLiveDetectClipSavesAsync(CancellationToken cancellation, Guid runId, CameraScope source)
    {
        long intervalTicks = LiveDetectAutoClipSeconds * (long)System.Diagnostics.Stopwatch.Frequency;
        long nextSaveAt = System.Diagnostics.Stopwatch.GetTimestamp() + intervalTicks;
        try
        {
            while (true)
            {
                long remaining = nextSaveAt - System.Diagnostics.Stopwatch.GetTimestamp();
                if (remaining > 0)
                    await Task.Delay(TimeSpan.FromSeconds(remaining / (double)System.Diagnostics.Stopwatch.Frequency), cancellation);
                cancellation.ThrowIfCancellationRequested();
                await SaveLiveDetectClipAsync(cancellation, runId, source);
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                nextSaveAt += intervalTicks;
                if (nextSaveAt <= now) nextSaveAt = now + intervalTicks;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    async Task SaveLiveDetectClipAsync(CancellationToken cancellation, Guid runId, CameraScope source)
    {
        cancellation.ThrowIfCancellationRequested();
        var context = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var observation = table.State.Observation;
            var receipt = observation?.DecodedReceipt;
            return new LiveDetectAutoClipContext(app.State, table.State.Source,
                new(receipt?.FrameId, observation?.SourceTimeUs, observation?.CapturedAt));
        });
        var camera = context.AppState.Camera;
        var operationId = Guid.NewGuid();
        var requestedAt = DateTimeOffset.UtcNow;
        var anchor = context.Frame;
        var requested = new LiveDetectLogClipSave(operationId, "requested", requestedAt, null,
            LiveDetectAutoClipSeconds, "Pending", "Automatic Live Detect clip save requested.",
            anchor.FrameId, anchor.SourceTimeUs, anchor.CapturedAt,
            null, null, null, null, null, null, null, null, null);
        AppendLiveDetectClipSave(runId, requestedAt, requested);

        if (!camera.CanControl || camera.Capture != CaptureState.Active || camera.Scope != source
            || context.TableSource != source)
        {
            var skipped = requested with { Stage = "completed", CompletedAt = DateTimeOffset.UtcNow,
                Outcome = "Skipped", Message = "Camera host, capture, or live source was no longer available." };
            AppendLiveDetectClipSave(runId, skipped.CompletedAt.Value, skipped);
            await SetLiveDetectClipStatusAsync($"Automatic clip skipped · camera session is no longer active.");
            return;
        }

        await SetLiveDetectClipStatusAsync($"Saving automatic {LiveDetectAutoClipSeconds}-second clip…");
        CommandReceipt receipt;
        try
        {
            var saveScope = new ClipSaveScope(source.CameraIdentity, source.SessionId,
                source.HostGeneration, source.CaptureRevision);
            receipt = await app.DispatchAsync(new(operationId, CommandOrigin.System,
                context.AppState.Version, new SaveClip(LiveDetectAutoClipSeconds, saveScope)), CancellationToken.None);
        }
        catch (Exception)
        {
            receipt = new(operationId, CommandOutcome.Uncertain, context.AppState.Version,
                "Automatic clip save result is unknown. Check saved camera clips before concluding it failed.");
        }

        var acknowledgement = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var observation = table.State.Observation;
            var decoded = observation?.DecodedReceipt;
            return new LiveDetectFrameAnchor(decoded?.FrameId, observation?.SourceTimeUs, observation?.CapturedAt);
        });
        var completedAt = DateTimeOffset.UtcNow;
        var clip = receipt.SavedClip;
        var outcome = receipt.Outcome.ToString();
        var completed = requested with
        {
            Stage = "completed", CompletedAt = completedAt, Outcome = outcome,
            Message = receipt.Message, AcknowledgementFrameId = acknowledgement.FrameId,
            AcknowledgementSourceTimeUs = acknowledgement.SourceTimeUs,
            AcknowledgementFrameCapturedAt = acknowledgement.CapturedAt,
            ClipId = clip?.Id, ClipName = clip?.Name, ClipDurationSeconds = clip?.Duration,
            ClipCreatedAt = clip?.CreatedAt, ClipBytes = clip?.Bytes, ClipSha256 = clip?.Sha256
        };
        AppendLiveDetectClipSave(runId, completedAt, completed);
        await SetLiveDetectClipStatusAsync(receipt.Succeeded && clip is not null
            ? $"Automatic clip saved · {clip.Duration:0.0}s · {completedAt.ToLocalTime():HH:mm:ss}."
            : receipt.Outcome == CommandOutcome.Uncertain
                ? "Automatic clip save uncertain · check saved clips before concluding it failed."
                : $"Automatic clip save failed · {receipt.Message}");
    }

    void AppendLiveDetectClipSave(Guid runId, DateTimeOffset recordedAt, LiveDetectLogClipSave save)
    {
        try
        {
            liveDetectLog.AppendClipSave(runId, recordedAt, save);
            liveDetectLogError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            liveDetectLogError = "storage unavailable";
        }
    }

    void AppendLiveDetectRunEvent(Guid runId, string status, string message, string? exceptionType)
    {
        try
        {
            var state = table.State;
            var observation = state.Observation;
            var receipt = observation?.DecodedReceipt;
            var recordedAt = DateTimeOffset.UtcNow;
            liveDetectLog.AppendRunEvent(runId, recordedAt, new(status, recordedAt, message,
                exceptionType, receipt?.FrameId, observation?.SourceTimeUs, observation?.CapturedAt));
            liveDetectLogError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            liveDetectLogError = "storage unavailable";
        }
    }

    Task SetLiveDetectClipStatusAsync(string status) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        liveDetectClipStatus = status;
        Changed?.Invoke();
    });

    static LiveDetectCounts Count(Ball[] balls)
    {
        int reds = 0, yellows = 0, black = 0, cue = 0, other = 0;
        foreach (var ball in balls)
        {
            switch (ball.Kind.Trim().ToLowerInvariant())
            {
                case "red": reds++; break;
                case "yellow": yellows++; break;
                case "black": black++; break;
                case "cue": cue++; break;
                default: other++; break;
            }
        }
        return new(reds, yellows, black, cue, other);
    }

    static IEnumerable<string> PossiblePotEvents(LiveDetectCounts before, LiveDetectCounts after)
    {
        if (after.Reds < before.Reds) yield return PossiblePot("red", before.Reds, after.Reds);
        if (after.Yellows < before.Yellows) yield return PossiblePot("yellow", before.Yellows, after.Yellows);
        if (after.Black < before.Black) yield return PossiblePot("black", before.Black, after.Black);
        if (after.Cue < before.Cue) yield return PossiblePot("cue ball", before.Cue, after.Cue);

        static string PossiblePot(string colour, int beforeCount, int afterCount) =>
            $"Possible pot · {colour} detections {beforeCount} → {afterCount}. Confirm on the table before recording.";
    }

    static IEnumerable<string> InventoryEvents(LiveDetectCountCandidate candidate) => candidate.Kind switch
    {
        LiveDetectChangeKind.Racked => [$"Rack detected · {candidate.After.Summary}. New inventory baseline."],
        LiveDetectChangeKind.Increase => [$"Balls returned · {candidate.After.Summary}."],
        LiveDetectChangeKind.Changed => [$"Table inventory changed · {candidate.After.Summary}."],
        _ => PossiblePotEvents(candidate.Before, candidate.After)
    };
}
