using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Core.Analysis;
using SkyEye.GameState;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Channels;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    readonly record struct LiveDetectFrameAnchor(long? FrameId, long? SourceTimeUs, DateTimeOffset? CapturedAt);
    readonly record struct LiveDetectEventClipContext(SkyEyeState AppState,
        CameraScope? TableSource, long MediaGeneration, string? Timeline, LiveDetectFrameAnchor Frame,
        bool CanAdvanceAnalysis);

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
    string? liveDetectLastClearInventory;
    DateTimeOffset? liveDetectLastClearAt;
    string? liveDetectLastSettledInventory;
    DateTimeOffset? liveDetectLastSettledAt;
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
        ? $"Evidence/logging issue · {error}"
        : $"Retained log across runs · {liveDetectLog.Summary.RetainedFrames} frames · {liveDetectLog.Summary.RetainedEvidenceFrames} source images · {liveDetectLog.Summary.RetainedTableEvents} table events · {liveDetectLog.Summary.RetainedEventClipOperations} event clip operations · GET /api/live-detect-log";
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
            ResetIdentifyBallsOverlay(notify: false);
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
        liveDetectLastClearInventory = null;
        liveDetectLastClearAt = null;
        liveDetectLastSettledInventory = null;
        liveDetectLastSettledAt = null;
        liveDetectEvents.Clear();
        liveDetectHasResult = false;
        ResetIdentifyBallsOverlay(notify: false);
        Changed?.Invoke();
        liveDetectTask = RunLiveDetectAsync(operation, runId, snapshot.Source, snapshot.Calibration,
            snapshot.CalibrationId, resumeDiagnostics);
    }

    void RequestLiveDetectStop(string reason)
    {
        if (liveDetectCancellation is not { IsCancellationRequested: false } current) return;
        liveDetectStopReason = reason;
        current.Cancel();
        ResetIdentifyBallsOverlay(notify: false);
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
        var evidenceCapture = new LiveDetectFrameEvidenceCapture();
        PendingLiveDetectFrameEvidence? liveDetectPendingFrameEvidence = null;
        var eventClipQueue = Channel.CreateUnbounded<LiveDetectEventClipQueueMessage>();
        var eventClipTask = ProcessLiveDetectEventClipQueueAsync(runId, source,
            eventClipQueue.Reader, cancellation);
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
                    liveDetectPendingFrameEvidence = null;
                    await table.UpdateAsync(cancellation,
                        frameObserver: (frame, vision) => liveDetectPendingFrameEvidence = evidenceCapture.Observe(frame, vision));
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
                            $"Waiting for a fresh paired frame after {error.Code}.", error.GetType().Name,
                            playerPlayback: error.PlayerPlayback);
                    string sourceStatus = string.IsNullOrWhiteSpace(error.SourceStatus)
                        ? "source status unavailable" : $"source status {error.SourceStatus}";
                    AppendLiveDetectRunEvent(runId, "frame_acquisition_retry",
                        $"No fresh paired camera frame ({error.Code}; {sourceStatus}); retrying.", error.GetType().Name,
                        playerPlayback: error.PlayerPlayback);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (!ReferenceEquals(liveDetectCancellation, operation) || cancellation.IsCancellationRequested) return;
                        InvalidateIdentifyBallsFrame();
                        liveDetectStatus = $"Live Detect waiting · fresh camera frame unavailable ({error.Code}); retrying.";
                        liveDetectInventory = UnknownLiveDetectInventory("fresh camera frame unavailable", DateTimeOffset.UtcNow);
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
                var pendingFrameEvidence = liveDetectPendingFrameEvidence;
                liveDetectPendingFrameEvidence = null;
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
                            InvalidateIdentifyBallsFrame();
                            liveDetectStatus = "Live Detect running · waiting for a newer paired camera frame.";
                            liveDetectInventory = UnknownLiveDetectInventory("fresh camera frame unavailable", DateTimeOffset.UtcNow);
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
                bool currentInventoryClear = LiveDetectInventoryReadoutPolicy.IsCurrentInventoryClear(
                    vision.Uncertain, vision.Obstructed, vision.HasUncertainLayout, counts);
                if (currentInventoryClear)
                {
                    liveDetectLastClearInventory = $"{counts.Total} detected · {counts.Summary}";
                    liveDetectLastClearAt = observation.CapturedAt;
                }
                bool rackEvidence = vision.PackedCluster is { Detected: true, Complete: true }
                    && counts == new LiveDetectCounts(7, 7, 1, 1, 0);
                var evidenceSource = new EvidenceSource(source.CameraIdentity, observation.Timeline,
                    state.Generation + 1, calibrationId, source.ConnectionId.ToString("N"));
                // D1: pass pocket regions only when they alone set the layout flag.
                var uncertainRegions = vision.HasUncertainLayout && !vision.Obstructed
                    && vision.UncertainRegions is { Length: > 0 } regions && vision.LayoutUncertainOutsideRegions == false
                    ? regions.Select(region => new LiveDetectUncertainRegion(region.Centre.X, region.Centre.Y, region.Radius)).ToImmutableArray()
                    : ImmutableArray<LiveDetectUncertainRegion>.Empty;
                var eventBatch = eventTracker.Observe(new LiveDetectTableEventObservation(evidenceSource,
                    source.ConnectionId, receipt.MediaGeneration, receipt.FrameId, observation.CapturedAt,
                    sourceTimeUs, observation.Timeline, calibrationId, counts, layout,
                    vision.Obstructed, vision.Uncertain, vision.HasUncertainLayout,
                    observation.Change is { SpatiallyCompatible: false }, rackEvidence,
                    uncertainRegions, vision.UncertainOutsideRegions));
                if (eventBatch.SettledBaseline is { } settledBaseline)
                {
                    liveDetectLastSettledInventory = $"{settledBaseline.Total} · {LiveDetectInventoryReadoutPolicy.BaselineSummary(settledBaseline, eventBatch.BaselineBlackNotSeen, eventBatch.BaselineCueNotSeen)}";
                    liveDetectLastSettledAt = eventBatch.SettledBaselineAt;
                }
                else
                {
                    liveDetectLastSettledInventory = null;
                    liveDetectLastSettledAt = null;
                }
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
                if (eventBatch.PocketRegionUncertain)
                    quality += eventBatch.PocketRegionBalls switch
                    {
                        0 => " · pocket mouth uncertain",
                        1 => " · 1 ball at a pocket mouth uncertain",
                        var balls => $" · {balls} balls at a pocket mouth uncertain"
                    };
                foreach (var tableEvent in eventBatch.TableEvents)
                {
                    if (tableEvent.ActivityId is not { } activityId) continue;
                    if (tableEvent.Type == "activity_started")
                        eventClipQueue.Writer.TryWrite(new LiveDetectEventClipActivityBoundaryMessage(
                            activityId, tableEvent.Timeline, tableEvent.SourceTimeUs, Started: true));
                    else if (tableEvent.Type is "activity_ended" or "activity_ended_without_layout_change" or "activity_interrupted")
                        eventClipQueue.Writer.TryWrite(new LiveDetectEventClipActivityBoundaryMessage(
                            activityId, tableEvent.Timeline, tableEvent.SourceTimeUs, Started: false));
                }
                if (eventBatch.ClipActivity is { } clipActivity
                    && eventBatch.ClipEventType is { } clipEventType
                    && eventBatch.ClipEndSourceTimeUs is { } eventEndSourceTimeUs)
                {
                    var candidate = new LiveDetectEventClipCandidate(clipActivity, clipEventType,
                        eventEndSourceTimeUs, eventBatch.TableEvents
                            .Where(tableEvent => tableEvent.ActivityId == clipActivity.Id)
                            .Select(tableEvent => tableEvent.EventId).ToArray());
                    eventClipQueue.Writer.TryWrite(new LiveDetectEventClipCandidateMessage(candidate));
                }
                if (vision.Uncertain && !vision.HasUncertainLayout && !vision.Obstructed)
                    quality += " · advice unavailable";
                var ageSeconds = Math.Max(0, observation.FrameAgeMs) / 1000;
                LiveDetectLogFrameEvidence? frameEvidence = null;
                bool evidenceWriteFailed = false;
                if (pendingFrameEvidence is { } candidateEvidence)
                {
                    try
                    {
                        frameEvidence = candidateEvidence.Encoded is { } encoded
                            ? liveDetectLog.SaveFrameEvidence(encoded, candidateEvidence.Trigger)
                            : new(null, null, null, 0, null, candidateEvidence.Trigger, false,
                                candidateEvidence.SkipReason ?? "source frame unavailable");
                        evidenceWriteFailed = !frameEvidence.Available && frameEvidence.SkipReason is not "evidence storage limit reached";
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                        frameEvidence = new(null, null, null, 0, null, candidateEvidence.Trigger, false, error.GetType().Name);
                        evidenceWriteFailed = true;
                    }
                }
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
                        LiveDetectLogObstructionAudit.From(vision.ObstructionAudit), observation.PlayerPlayback,
                        vision.PocketColourAudit, eventBatch.BaselineMatchSamples,
                        eventBatch.SettledBaselineMatchesCurrent, frameEvidence));
                    foreach (var tableEvent in eventBatch.TableEvents)
                        liveDetectLog.AppendTableEvent(runId, tableEvent.CapturedAt, tableEvent);
                    liveDetectLogError = evidenceWriteFailed ? "source evidence unavailable" : null;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                {
                    liveDetectLogError = "storage unavailable";
                }
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!ReferenceEquals(liveDetectCancellation, operation) || cancellation.IsCancellationRequested) return;
                    UpdateIdentifyBallsFrame(new(vision.Balls.ToArray(), vision.HasUncertainLayout, vision.Obstructed,
                        receipt.MediaGeneration, state.Table?.Version ?? 0, receipt.FrameId, observation.CapturedAt, calibration));
                    liveDetectHasResult = true;
                    liveDetectReadAt = observation.CapturedAt;
                    liveDetectStatus = $"Live Detect running · full pass {vision.ElapsedMs / 1000:0.0}s.";
                    var settledCurrentInventory = LiveDetectInventoryReadoutPolicy.SettledCurrentInventory(
                        currentInventoryClear, eventBatch);
                    liveDetectInventory = settledCurrentInventory is { } settled
                        ? $"Settled current state · {settled.Total} detected · {LiveDetectInventoryReadoutPolicy.BaselineSummary(settled, eventBatch.BaselineBlackNotSeen, eventBatch.BaselineCueNotSeen)} · confirmed by this frame · frame age {ageSeconds:0.0}s"
                        : currentInventoryClear && checkOutcome == "clear_unchanged"
                            && eventBatch.SettledBaseline is not null
                            ? $"Fresh clear frame matches the last settled layout · {counts.Total} current detections · {counts.Summary} · stillness check {eventBatch.BaselineMatchSamples}/{StableTableChangeTracker.RequiredStableSamples} · frame age {ageSeconds:0.0}s"
                        : currentInventoryClear && checkOutcome is ("layout_change_pending" or "inventory_change_pending")
                            ? $"Table change pending · {counts.Total} current detections · {counts.Summary} · {quality} · frame age {ageSeconds:0.0}s"
                        : currentInventoryClear && checkOutcome == "baseline_pending"
                            ? $"Establishing table baseline · {counts.Total} current detections · {counts.Summary} · frame age {ageSeconds:0.0}s"
                        : currentInventoryClear
                            ? $"Current clear reading · {counts.Total} detections · {counts.Summary} · {quality} · frame age {ageSeconds:0.0}s"
                            : UnknownLiveDetectInventory(quality, observation.CapturedAt, ageSeconds);
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
            eventClipQueue.Writer.TryComplete();
            try { await eventClipTask; }
            catch (Exception) { }
            AppendLiveDetectRunEvent(runId, "stopped", liveDetectStopReason ?? "Live Detect stopped.", null);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (ReferenceEquals(liveDetectCancellation, operation))
                {
                    ResetIdentifyBallsOverlay(notify: false);
                    liveDetectCancellation = null;
                    liveDetectRunId = null;
                    liveDetectStatus = liveDetectStopReason ?? "Live Detect stopped.";
                    liveDetectInventory = UnknownLiveDetectInventory("Live Detect stopped; the last reading is not current", DateTimeOffset.UtcNow);
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

    async Task ProcessLiveDetectEventClipQueueAsync(Guid runId, CameraScope source,
        ChannelReader<LiveDetectEventClipQueueMessage> reader, CancellationToken runCancellation)
    {
        LiveDetectEventClipCandidate? carry = null;
        try
        {
            while (true)
            {
                LiveDetectEventClipCandidate? first = carry;
                carry = null;
                while (first is null && await reader.WaitToReadAsync())
                {
                    while (reader.TryRead(out var message))
                    {
                        if (message is LiveDetectEventClipCandidateMessage candidateMessage)
                        {
                            first = candidateMessage.Candidate;
                            break;
                        }
                    }
                }
                if (first is null) return;

                var group = new LiveDetectEventClipGroup(first);
                var pendingOverlappingActivities = new HashSet<Guid>();
                long waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                while (true)
                {
                    bool groupReady = false;
                    while (reader.TryRead(out var message))
                    {
                        if (message is LiveDetectEventClipActivityBoundaryMessage boundary)
                        {
                            if (!boundary.Started)
                            {
                                pendingOverlappingActivities.Remove(boundary.ActivityId);
                            }
                            else if (boundary.Timeline == group.Timeline
                                && !group.ContainsActivity(boundary.ActivityId)
                                && Math.Max(0, boundary.SourceTimeUs
                                    - LiveDetectShotClipWindowPolicy.LeadInSeconds * 1_000_000L)
                                    < group.WindowEndSourceTimeUs)
                            {
                                pendingOverlappingActivities.Add(boundary.ActivityId);
                            }
                        }
                        else if (message is LiveDetectEventClipCandidateMessage candidateMessage)
                        {
                            if (group.TryAdd(candidateMessage.Candidate))
                            {
                                pendingOverlappingActivities.Remove(candidateMessage.Candidate.Activity.Id);
                                waitStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                            }
                            else
                            {
                                carry = candidateMessage.Candidate;
                                groupReady = true;
                                break;
                            }
                        }
                    }
                    if (groupReady) break;

                    var context = await CaptureLiveDetectEventClipContextAsync(source);
                    if (context.Timeline is { } timeline && timeline != group.Timeline)
                        break;
                    if (context.Frame.SourceTimeUs is { } currentSourceTimeUs
                        && currentSourceTimeUs >= group.WindowEndSourceTimeUs
                            + LiveDetectShotClipWindowPolicy.LeadInSeconds * 1_000_000L
                        && pendingOverlappingActivities.Count == 0)
                        break;

                    // Stopping Live Detect closes the observation stream. Save the best
                    // confirmed range rather than waiting for an event that cannot arrive.
                    if (runCancellation.IsCancellationRequested || !CanAdvanceEventClipSource(context))
                        break;

                    if (System.Diagnostics.Stopwatch.GetElapsedTime(waitStarted) >= TimeSpan.FromSeconds(15))
                    {
                        var timeoutAt = DateTimeOffset.UtcNow;
                        const string timeoutMessage = "Shot-clip grouping timed out while waiting for an overlapping activity to settle; saving the confirmed event range.";
                        AppendLiveDetectTableEvent(runId, timeoutAt, new(Guid.NewGuid(), group.ClipActivity.Id,
                            "event_clip_grouping_timeout", timeoutAt, source.ConnectionId,
                            context.MediaGeneration, context.Frame.FrameId ?? 0,
                            context.Frame.SourceTimeUs ?? group.EndSourceTimeUs, group.Timeline,
                            table.State.CalibrationId, timeoutMessage, null, null,
                            group.ActivityStartSourceTimeUs, null, null));
                        break;
                    }

                    try
                    {
                        using var updateTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        await table.UpdateAsync(updateTimeout.Token);
                    }
                    catch (OperationCanceledException) when (!runCancellation.IsCancellationRequested) { }
                    catch (Exception) { await Task.Delay(100); }
                }

                await SaveLiveDetectEventClipAsync(runId, source, group);
            }
        }
        catch (Exception error)
        {
            AppendLiveDetectRunEvent(runId, "event_clip_queue_failed",
                $"Event clip queue failed ({error.GetType().Name}).", error.GetType().Name);
            await SetLiveDetectClipStatusAsync($"Event clip queue failed · {error.GetType().Name}.");
        }
    }

    Task<LiveDetectEventClipContext> CaptureLiveDetectEventClipContextAsync(CameraScope source) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            var tableState = table.State;
            var observation = tableState.Observation;
            var receipt = observation?.DecodedReceipt;
            var cameraState = app.State;
            bool canAdvance = CanAnalyzeTable && cameraState.Camera.CanControl
                && cameraState.Camera.Capture == CaptureState.Active
                && cameraState.Camera.Scope == source && tableState.Source == source;
            return new LiveDetectEventClipContext(cameraState, tableState.Source,
                observation?.MediaGeneration ?? receipt?.MediaGeneration ?? 0, observation?.Timeline,
                new(receipt?.FrameId, observation?.SourceTimeUs, observation?.CapturedAt), canAdvance);
        });

    static bool CanAdvanceEventClipSource(LiveDetectEventClipContext context) => context.CanAdvanceAnalysis
        && context.AppState.Camera.CanControl && context.AppState.Camera.Capture == CaptureState.Active
        && context.AppState.Camera.Scope == context.TableSource;

    async Task SaveLiveDetectEventClipAsync(Guid runId, CameraScope source, LiveDetectEventClipGroup group)
    {
        try
        {
            var activity = group.ClipActivity;
            string eventType = group.EventType;
            long eventEndSourceTimeUs = group.EndSourceTimeUs;
            LiveDetectEventClipContext context = default;
            async Task<LiveDetectShotClipWindowPolicy.PostRollObservation> ReadPostRollObservationAsync(
                CancellationToken cancellationToken)
            {
                context = await CaptureLiveDetectEventClipContextAsync(source);
                if (!CanAdvancePostRoll(context))
                    return new(context.Timeline ?? activity.Timeline, context.Frame.SourceTimeUs, false);

                long requiredSourceTimeUs = checked(eventEndSourceTimeUs
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
                    context = await CaptureLiveDetectEventClipContextAsync(source);
                }

                return new(context.Timeline ?? activity.Timeline, context.Frame.SourceTimeUs,
                    CanAdvancePostRoll(context));
            }

            bool CanAdvancePostRoll(LiveDetectEventClipContext value) => value.CanAdvanceAnalysis
                && value.AppState.Camera.CanControl
                && value.AppState.Camera.Capture == CaptureState.Active
                && value.AppState.Camera.Scope == source && value.TableSource == source;

            var postRoll = await LiveDetectShotClipWindowPolicy.WaitForPostRollAsync(activity.Timeline,
                eventEndSourceTimeUs, ReadPostRollObservationAsync);
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
                    context.Frame.FrameId ?? 0, context.Frame.SourceTimeUs ?? eventEndSourceTimeUs,
                    context.Timeline ?? activity.Timeline, table.State.CalibrationId, message,
                    null, null, activity.StartSourceTimeUs, null, null));
                return;
            }

            long requestSourceTimeUs = postRoll.LastObservation.SourceTimeUs!.Value;
            if (!LiveDetectShotClipWindowPolicy.TryCreate(activity.Id,
                activity.Timeline, activity.StartSourceTimeUs, eventEndSourceTimeUs,
                requestSourceTimeUs, out var window))
            {
                var skippedAt = DateTimeOffset.UtcNow;
                const string message = "Event clip skipped because its padded range exceeds the camera's supported save range.";
                await SetLiveDetectClipStatusAsync(message);
                AppendLiveDetectTableEvent(runId, skippedAt, new(Guid.NewGuid(), activity.Id,
                    "event_clip_skipped", skippedAt, source.ConnectionId,
                    table.State.Observation?.MediaGeneration ?? 0, table.State.Observation?.DecodedReceipt?.FrameId ?? 0,
                    context.Frame.SourceTimeUs ?? eventEndSourceTimeUs, context.Timeline ?? "", table.State.CalibrationId,
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
                activity.Id, eventType, window.Timeline, window.StartSourceTimeUs, window.EndSourceTimeUs,
                LinkedActivityIds: group.ActivityIds, LinkedTableEventIds: group.TableEventIds,
                LinkedEventTypes: group.EventTypes);
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
            if (saveReceipt.Succeeded && clip is not null)
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (app.State.Camera.ConnectionId == camera.ConnectionId)
                        await LoadClipsAsync(CancellationToken.None);
                });
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
            AppendLiveDetectTableEvent(runId, recordedAt, new(Guid.NewGuid(), group.ClipActivity.Id,
                "event_clip_failed", recordedAt, source.ConnectionId, observation?.MediaGeneration ?? 0,
                observation?.DecodedReceipt?.FrameId ?? 0, observation?.SourceTimeUs ?? group.EndSourceTimeUs,
                observation?.Timeline ?? "", state.CalibrationId,
                $"Event clip save failed before confirmation ({error.GetType().Name}).", null, null,
                group.ActivityStartSourceTimeUs, null, null));
            await SetLiveDetectClipStatusAsync($"Event clip failed · {error.GetType().Name}.");
        }
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

    void AppendLiveDetectRunEvent(Guid runId, string status, string message, string? exceptionType,
        PlayerPlaybackTelemetry? playerPlayback = null)
    {
        try
        {
            var state = table.State;
            var observation = state.Observation;
            var receipt = observation?.DecodedReceipt;
            var recordedAt = DateTimeOffset.UtcNow;
            liveDetectLog.AppendRunEvent(runId, recordedAt, new(status, recordedAt, message,
                exceptionType, receipt?.FrameId, observation?.SourceTimeUs, observation?.CapturedAt, playerPlayback));
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

    string UnknownLiveDetectInventory(string reason, DateTimeOffset now, double? frameAgeSeconds = null)
    {
        string lastClear = liveDetectLastClearInventory is { } inventory && liveDetectLastClearAt is { } capturedAt
            ? $"last clear reading {inventory} · {Math.Max(0, (now - capturedAt).TotalSeconds):0}s ago"
            : "no clear inventory reading yet";
        string age = frameAgeSeconds is { } seconds ? $" · frame age {seconds:0.0}s" : "";
        return $"Current table state unknown · {reason} · {LastSettledInventorySummary(now)} · {lastClear}{age}";
    }

    string LastSettledInventorySummary(DateTimeOffset now) =>
        liveDetectLastSettledInventory is { } inventory && liveDetectLastSettledAt is { } capturedAt
            ? $"last settled state {inventory} · {Math.Max(0, (now - capturedAt).TotalSeconds):0}s ago"
            : "no settled baseline yet";

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
