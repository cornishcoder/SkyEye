using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Core.Analysis;
using SkyEye.GameState;
using System.Collections.Immutable;
using System.Text.Json;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    readonly record struct LiveDetectFrameAnchor(long? FrameId, long? SourceTimeUs, DateTimeOffset? CapturedAt);
    readonly record struct LiveDetectEventClipContext(SkyEyeState AppState,
        CameraScope? TableSource, string? Timeline, LiveDetectFrameAnchor Frame, bool CanAdvanceAnalysis);

    CancellationTokenSource? liveDetectCancellation;
    Task? liveDetectTask;
    string? liveDetectStopReason;
    string? liveDetectLogError;
    Guid? liveDetectRunId;
    LiveDetectLogStore liveDetectLog = null!;
    string liveDetectStatus = "Live Detect is off.";
    string liveDetectClipStatus = "Shot clips save after a movement event reaches a confirmed settled layout.";
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
        : $"Retained log across runs · {liveDetectLog.Summary.RetainedFrames} frames · {liveDetectLog.Summary.RetainedTableEvents} table events · {liveDetectLog.Summary.RetainedEventClipOperations} event clip operations · GET /api/live-detect-log";
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
        liveDetectClipStatus = "Shot clips save after a movement event reaches a confirmed settled layout.";
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
        AppendLiveDetectRunEvent(runId, "started", "Live Detect started.", null);
        var eventTracker = new LiveDetectTableEventTracker();
        var frameCursor = new LiveDetectFrameCursor();
        var freshFrameWait = new LiveDetectFreshFrameWaitTracker();
        var eventClipTasks = new List<Task>();
        using var eventClipSaveGate = new SemaphoreSlim(1, 1);
        int consecutiveAnalysisErrors = 0;
        int consecutiveFrameAcquisitionErrors = 0;
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
                    consecutiveFrameAcquisitionErrors = 0;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
                catch (FrameAcquisitionException error)
                {
                    consecutiveAnalysisErrors = 0;
                    if (error.Reason == ObservationReason.SourceChanged)
                    {
                        liveDetectStopReason = $"Live Detect stopped · camera source changed ({error.Code}).";
                        AppendLiveDetectRunEvent(runId, "frame_source_changed", liveDetectStopReason, error.GetType().Name);
                        break;
                    }

                    consecutiveFrameAcquisitionErrors++;
                    var waitStartedAt = DateTimeOffset.UtcNow;
                    if (freshFrameWait.Begin(waitStartedAt))
                        AppendLiveDetectRunEvent(runId, "fresh_frame_wait_started",
                            $"Waiting for a fresh paired frame after {error.Code}.", error.GetType().Name);
                    string sourceStatus = string.IsNullOrWhiteSpace(error.SourceStatus)
                        ? "source status unavailable" : $"source status {error.SourceStatus}";
                    AppendLiveDetectRunEvent(runId, "frame_acquisition_retry",
                        $"No fresh paired camera frame ({error.Code}; {sourceStatus}); retrying.", error.GetType().Name);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (!ReferenceEquals(liveDetectCancellation, operation) || cancellation.IsCancellationRequested) return;
                        liveDetectStatus = $"Live Detect waiting · fresh camera frame unavailable ({error.Code}); retrying.";
                        liveDetectInventory = liveDetectReadAt is { } priorRead
                            ? $"Last accepted reading {priorRead.ToLocalTime():HH:mm:ss} · waiting for a fresh camera frame."
                            : "Waiting for the first current camera frame.";
                        Changed?.Invoke();
                    });
                    int retryDelayMs = Math.Min(2000, 250 * (1 << Math.Min(consecutiveFrameAcquisitionErrors - 1, 3)));
                    await Task.Delay(TimeSpan.FromMilliseconds(retryDelayMs), cancellation);
                    continue;
                }
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
                    var waitStartedAt = DateTimeOffset.UtcNow;
                    if (freshFrameWait.Begin(waitStartedAt))
                        AppendLiveDetectRunEvent(runId, "fresh_frame_wait_started",
                            "Waiting for a newer uniquely paired camera frame; the last accepted table reading is stale.", null);
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

                if (freshFrameWait.Recover(DateTimeOffset.UtcNow) is { } recoveredAfter)
                    AppendLiveDetectRunEvent(runId, "fresh_frame_wait_recovered",
                        $"A fresh paired camera frame arrived after {recoveredAfter.TotalSeconds:0.0}s.", null);

                if (timelineChanged)
                    eventTracker.Reset();
                if (priorMediaGeneration > 0 && receipt.MediaGeneration != priorMediaGeneration)
                    AppendLiveDetectRunEvent(runId, "decoder_recovered",
                        timelineChanged ? "New camera timeline; inventory baseline reset."
                            : "Decoder recovered; camera timestamps continue with restarted frame IDs.", null);
                var counts = Count(vision.Balls);
                var layout = CaptureLiveDetectLayout(vision.Balls);
                bool rackEvidence = vision.PackedCluster is { Detected: true, Complete: true }
                    && counts == new LiveDetectCounts(7, 7, 1, 1, 0);
                var evidenceSource = new EvidenceSource(source.CameraIdentity, observation.Timeline,
                    state.Generation + 1, calibrationId, source.ConnectionId.ToString("N"));
                var eventBatch = eventTracker.Observe(new LiveDetectTableEventObservation(evidenceSource,
                    source.ConnectionId, receipt.MediaGeneration, receipt.FrameId, observation.CapturedAt,
                    sourceTimeUs, observation.Timeline, calibrationId, counts, layout,
                    vision.Obstructed, vision.HasUncertainLayout,
                    observation.Change is { SpatiallyCompatible: false }, rackEvidence));
                var events = eventBatch.Messages.ToArray();
                string checkOutcome = eventBatch.CheckOutcome;
                string quality = checkOutcome switch
                {
                    "obstructed" => "obstructed",
                    "layout_uncertain" => "uncertain layout",
                    "baseline_pending" => "establishing baseline",
                    "clear_unchanged" => "clear · unchanged",
                    "layout_change_pending" => "clear · layout changing",
                    "inventory_change_pending" => "clear · inventory changing",
                    _ => checkOutcome.Replace('_', ' ')
                };
                if (eventBatch.ClipActivity is { } clipActivity
                    && eventBatch.ClipEventType is { } clipEventType
                    && eventBatch.ClipSettledSourceTimeUs is { } settledSourceTimeUs)
                {
                    eventClipTasks.RemoveAll(task => task.IsCompleted);
                    eventClipTasks.Add(SaveLiveDetectEventClipAsync(runId, source, clipActivity,
                        clipEventType, settledSourceTimeUs, eventClipSaveGate));
                }
                if (vision.Uncertain && !vision.HasUncertainLayout && !vision.Obstructed)
                    quality += " · advice unavailable";
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
                        events.ToArray(), checkOutcome, observation.Change?.SpatiallyCompatible, rackEvidence,
                        LiveDetectLogObstructionAudit.From(vision.ObstructionAudit)));
                    foreach (var tableEvent in eventBatch.TableEvents)
                        liveDetectLog.AppendTableEvent(runId, tableEvent.CapturedAt, tableEvent);
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
                    foreach (var item in events)
                    {
                        liveDetectEvents.Enqueue($"{observation.CapturedAt.ToLocalTime():HH:mm:ss} · {item}");
                        while (liveDetectEvents.Count > 12) liveDetectEvents.Dequeue();
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
            if (freshFrameWait.Stop(DateTimeOffset.UtcNow) is { } unresolvedFor)
                AppendLiveDetectRunEvent(runId, "fresh_frame_wait_unresolved",
                    $"Live Detect stopped before a fresh paired camera frame arrived; the last table reading is stale ({unresolvedFor.TotalSeconds:0.0}s wait).",
                    null);
            try { await Task.WhenAll(eventClipTasks); }
            catch (Exception) { }
            AppendLiveDetectRunEvent(runId, "stopped", liveDetectStopReason ?? "Live Detect stopped.", null);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (ReferenceEquals(liveDetectCancellation, operation))
                {
                    liveDetectCancellation = null;
                    liveDetectRunId = null;
                    liveDetectStatus = liveDetectStopReason ?? "Live Detect stopped.";
                    liveDetectInventory = liveDetectReadAt is { } lastRead
                        ? $"Last accepted reading {lastRead.ToLocalTime():HH:mm:ss} · Live Detect stopped; this is not a current reading."
                        : "No current table reading · Live Detect stopped.";
                    liveDetectStopReason = null;
                    if (liveDetectClipStatus.StartsWith("Shot clips save after a movement event", StringComparison.Ordinal))
                        liveDetectClipStatus = "Live Detect stopped before a settled table event was ready for a clip.";
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

    async Task SaveLiveDetectEventClipAsync(Guid runId, CameraScope source, LiveDetectActivity activity,
        string eventType, long settledSourceTimeUs, SemaphoreSlim saveGate)
    {
        // Camera assembly can legitimately take longer than the post-roll wait.
        // Preserve the existing serialized save queue; bound source-frame waiting below.
        await saveGate.WaitAsync();
        try
        {
            LiveDetectEventClipContext context = default;
            async Task<LiveDetectShotClipWindowPolicy.PostRollObservation> ReadPostRollObservationAsync(
                CancellationToken cancellationToken)
            {
                context = await MainThread.InvokeOnMainThreadAsync(CaptureClipContext);
                if (!CanAdvancePostRoll(context))
                    return new(context.Timeline ?? activity.Timeline, context.Frame.SourceTimeUs, false);

                long requiredSourceTimeUs = checked(settledSourceTimeUs
                    + LiveDetectShotClipWindowPolicy.PostRollSeconds * 1_000_000L);
                if (context.Frame.SourceTimeUs is not { } sourceTimeUs || sourceTimeUs < requiredSourceTimeUs)
                {
                    try
                    {
                        // Live Detect may have been stopped while this clip was queued.
                        // Keep acquiring paired frames for this bounded post-roll window
                        // so stopping detection cannot strand the clip at a stale PTS.
                        await table.UpdateAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // A transient read failure leaves the last actual source PTS
                        // unchanged; the bounded policy will retry before timing out.
                    }
                    context = await MainThread.InvokeOnMainThreadAsync(CaptureClipContext);
                }

                return new(context.Timeline ?? activity.Timeline, context.Frame.SourceTimeUs,
                    CanAdvancePostRoll(context));
            }

            LiveDetectEventClipContext CaptureClipContext()
            {
                var tableState = table.State;
                var observation = tableState.Observation;
                var receipt = observation?.DecodedReceipt;
                var cameraState = app.State;
                bool canAdvance = CanAnalyzeTable && cameraState.Camera.CanControl
                    && cameraState.Camera.Capture == CaptureState.Active
                    && cameraState.Camera.Scope == source && tableState.Source == source;
                return new(cameraState, tableState.Source, observation?.Timeline,
                    new(receipt?.FrameId, observation?.SourceTimeUs, observation?.CapturedAt), canAdvance);
            }

            bool CanAdvancePostRoll(LiveDetectEventClipContext value) => value.CanAdvanceAnalysis
                && value.AppState.Camera.CanControl
                && value.AppState.Camera.Capture == CaptureState.Active
                && value.AppState.Camera.Scope == source && value.TableSource == source;

            var postRoll = await LiveDetectShotClipWindowPolicy.WaitForPostRollAsync(activity.Timeline,
                settledSourceTimeUs, ReadPostRollObservationAsync);
            if (postRoll.Status != LiveDetectShotClipWindowPolicy.PostRollWaitStatus.Ready)
            {
                var skippedAt = DateTimeOffset.UtcNow;
                string statusEventType = postRoll.Status == LiveDetectShotClipWindowPolicy.PostRollWaitStatus.TimedOut
                    ? "event_clip_postroll_timeout" : "event_clip_skipped";
                string message = postRoll.Status switch
                {
                    LiveDetectShotClipWindowPolicy.PostRollWaitStatus.TimelineChanged => "Event clip skipped because the camera timeline changed before post-roll completed.",
                    LiveDetectShotClipWindowPolicy.PostRollWaitStatus.SessionUnavailable => "Event clip skipped because camera control, capture, or the live source ended before post-roll completed.",
                    _ => "Event clip skipped because no same-timeline frame reached the required two-second post-roll within five seconds."
                };
                await SetLiveDetectClipStatusAsync(message);
                AppendLiveDetectTableEvent(runId, skippedAt, new(Guid.NewGuid(), activity.Id,
                    statusEventType, skippedAt, source.ConnectionId, table.State.Observation?.MediaGeneration ?? 0,
                    context.Frame.FrameId ?? 0, context.Frame.SourceTimeUs ?? settledSourceTimeUs,
                    context.Timeline ?? activity.Timeline, table.State.CalibrationId, message,
                    null, null, activity.StartSourceTimeUs, null, null));
                return;
            }

            long requestSourceTimeUs = postRoll.LastObservation.SourceTimeUs!.Value;
            if (!LiveDetectShotClipWindowPolicy.TryCreate(activity.Id,
                activity.Timeline, activity.StartSourceTimeUs, settledSourceTimeUs,
                requestSourceTimeUs, out var window))
            {
                var skippedAt = DateTimeOffset.UtcNow;
                const string message = "Event clip skipped because its padded range exceeds the camera's supported save range.";
                await SetLiveDetectClipStatusAsync(message);
                AppendLiveDetectTableEvent(runId, skippedAt, new(Guid.NewGuid(), activity.Id,
                    "event_clip_skipped", skippedAt, source.ConnectionId,
                    table.State.Observation?.MediaGeneration ?? 0, table.State.Observation?.DecodedReceipt?.FrameId ?? 0,
                    context.Frame.SourceTimeUs ?? settledSourceTimeUs, context.Timeline ?? "", table.State.CalibrationId,
                    message,
                    null, null, activity.StartSourceTimeUs, null, null));
                return;
            }

            var camera = context.AppState.Camera;
            var operationId = Guid.NewGuid();
            var requestedAt = DateTimeOffset.UtcNow;
            var anchor = context.Frame;
            var requested = new LiveDetectLogClipSave(operationId, "requested", requestedAt, null,
                window.SaveSeconds, "Pending", $"Event clip requested for {eventType}.",
                anchor.FrameId, anchor.SourceTimeUs, anchor.CapturedAt,
                null, null, null, null, null, null, null, null, null,
                activity.Id, eventType, window.Timeline, window.StartSourceTimeUs, window.EndSourceTimeUs);
            AppendLiveDetectClipSave(runId, requestedAt, requested);

            if (!camera.CanControl || camera.Capture != CaptureState.Active || camera.Scope != source
                || context.TableSource != source || context.Timeline != window.Timeline)
            {
                var skipped = requested with { Stage = "completed", CompletedAt = DateTimeOffset.UtcNow,
                    Outcome = "Skipped", Message = "Camera host, capture, or live source was no longer available." };
                AppendLiveDetectClipSave(runId, skipped.CompletedAt.Value, skipped);
                await SetLiveDetectClipStatusAsync("Event clip skipped · camera session is no longer active.");
                return;
            }

            await SetLiveDetectClipStatusAsync($"Saving {window.SaveSeconds}-second event range…");
            CommandReceipt saveReceipt;
            try
            {
                var saveScope = new ClipSaveScope(source.CameraIdentity, source.SessionId,
                    source.HostGeneration, source.CaptureRevision);
                saveReceipt = await app.DispatchAsync(new(operationId, CommandOrigin.System,
                    context.AppState.Version, new SaveClip(window.SaveSeconds, saveScope,
                        new ClipSourceRange(window.Timeline, window.StartSourceTimeUs, window.EndSourceTimeUs))), CancellationToken.None);
            }
            catch (Exception)
            {
                saveReceipt = new(operationId, CommandOutcome.Uncertain, context.AppState.Version,
                    "Event clip save result is unknown. Check saved camera clips before concluding it failed.");
            }

            var acknowledgement = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var observation = table.State.Observation;
                var decoded = observation?.DecodedReceipt;
                return new LiveDetectFrameAnchor(decoded?.FrameId, observation?.SourceTimeUs, observation?.CapturedAt);
            });
            var completedAt = DateTimeOffset.UtcNow;
            var clip = saveReceipt.SavedClip;
            bool? windowCovered = clip?.RecordingOrigin is { } clipOrigin
                ? clipOrigin.Timeline == window.Timeline
                    && clipOrigin.StartUs <= window.StartSourceTimeUs
                    && clipOrigin.StartUs + (long)(clip.Duration * 1_000_000) >= window.EndSourceTimeUs - 100_000
                : null;
            var completed = requested with
            {
                Stage = "completed", CompletedAt = completedAt, Outcome = saveReceipt.Outcome.ToString(),
                Message = saveReceipt.Message, AcknowledgementFrameId = acknowledgement.FrameId,
                AcknowledgementSourceTimeUs = acknowledgement.SourceTimeUs,
                AcknowledgementFrameCapturedAt = acknowledgement.CapturedAt,
                ClipId = clip?.Id, ClipName = clip?.Name, ClipDurationSeconds = clip?.Duration,
                ClipCreatedAt = clip?.CreatedAt, ClipBytes = clip?.Bytes, ClipSha256 = clip?.Sha256,
                ClipStartSourceTimeUs = clip?.RecordingOrigin?.StartUs,
                ClipTimeline = clip?.RecordingOrigin?.Timeline, WindowCovered = windowCovered
            };
            AppendLiveDetectClipSave(runId, completedAt, completed);
            await SetLiveDetectClipStatusAsync(saveReceipt.Succeeded && clip is not null
                ? windowCovered == true
                    ? $"Event clip saved · {clip.Duration:0}s · shot window covered."
                    : $"Event clip saved · {clip.Duration:0}s · source-window coverage unverified."
                : saveReceipt.Outcome == CommandOutcome.Uncertain
                    ? "Event clip save uncertain · check saved clips before concluding it failed."
                    : $"Event clip save failed · {saveReceipt.Message}");
        }
        catch (Exception error)
        {
            var recordedAt = DateTimeOffset.UtcNow;
            var state = table.State;
            var observation = state.Observation;
            AppendLiveDetectTableEvent(runId, recordedAt, new(Guid.NewGuid(), activity.Id,
                "event_clip_failed", recordedAt, source.ConnectionId, observation?.MediaGeneration ?? 0,
                observation?.DecodedReceipt?.FrameId ?? 0, observation?.SourceTimeUs ?? settledSourceTimeUs,
                observation?.Timeline ?? "", state.CalibrationId,
                $"Event clip save failed before confirmation ({error.GetType().Name}).", null, null,
                activity.StartSourceTimeUs, null, null));
            await SetLiveDetectClipStatusAsync($"Event clip failed · {error.GetType().Name}.");
        }
        finally { saveGate.Release(); }
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

    void AppendLiveDetectTableEvent(Guid runId, DateTimeOffset recordedAt, LiveDetectLogTableEvent tableEvent)
    {
        try
        {
            liveDetectLog.AppendTableEvent(runId, recordedAt, tableEvent);
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

    static ImmutableArray<SettledBallPosition> CaptureLiveDetectLayout(Ball[] balls)
    {
        if (balls is null || balls.Length is < 1 or > 16) return [];
        var result = ImmutableArray.CreateBuilder<SettledBallPosition>(balls.Length);
        foreach (var ball in balls)
        {
            BallColour colour = ball.Kind.Trim().ToLowerInvariant() switch
            {
                "red" => BallColour.Red,
                "yellow" => BallColour.Yellow,
                "black" => BallColour.Black,
                "cue" or "cue ball" or "cue-ball" => BallColour.Cue,
                _ => BallColour.Unknown
            };
            double x = ball.Position.X / 2d, y = ball.Position.Y;
            if (colour == BallColour.Unknown || !double.IsFinite(x) || !double.IsFinite(y)
                || x is < 0 or > 1 || y is < 0 or > 1) return [];
            result.Add(new(colour, x, y));
        }
        return result.ToImmutable();
    }

}
