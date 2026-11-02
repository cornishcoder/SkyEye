using SkyEye.Core.Application;

namespace SkyEye.Core.Analysis;

public sealed partial class TableAnalysisSession
{
    bool verificationRunning;
    Guid verificationOperationId;
    long verificationMediaGeneration, verificationAnalysisGeneration;
    bool verificationReviewFrame;
    public Action<VerificationCapture>? VerificationCapture { get; set; }
    public Task VerifyAsync(Guid operationId, long mediaGeneration, CancellationToken cancellation = default, bool reviewFrame = false)
    {
        lock (gate)
        {
            ThrowIfDisposed(); cancellation.ThrowIfCancellationRequested();
            if (verificationRunning && pending is { IsCompleted: false })
            {
                if (operationId == verificationOperationId && mediaGeneration == verificationMediaGeneration && state.Generation == verificationAnalysisGeneration && reviewFrame == verificationReviewFrame) return pending;
                throw new InvalidOperationException("Another table verification is running. Cancel it or wait for its result.");
            }
            if (visionDiagnosticsCancellation is not null) throw new InvalidOperationException("Stop vision diagnostics before verifying the table for advice.");
            if (active is not null || state.Picking) throw new InvalidOperationException("Another table operation is finishing. Retry when it completes.");
            if (operationId == Guid.Empty || mediaGeneration <= 0 || state.Source is null || state.Calibration is null || analyzer is not ITableVerificationAnalyzer)
                throw new InvalidOperationException("Calibrate an active live table before verifying it.");
            var reviewedPriorLayout = tracker.State is { Settled: true, Occluded: false, Confirmed: true } prior
                && prior.CalibrationId == state.CalibrationId
                && lastTimeline is not null
                && (time.GetUtcNow() - prior.CapturedAt).TotalSeconds is >= -2 and <= ObservationPolicy.ReviewFreshnessSeconds
                ? prior : null;
            var reviewedPriorTimeline = reviewedPriorLayout is null ? null : lastTimeline;
            tracker.Reset(); state = state with { Table = null, Vision = null, Jpeg = null, Observation = null, Choices = [], ChoicesComputed = false, VerificationProgress = new(0, ObservationReason.Checking) };
            var admitted = state; var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            active = CancellationTokenSource.CreateLinkedTokenSource(cancellation); verificationRunning = true; pending = completed.Task;
            verificationOperationId = operationId; verificationMediaGeneration = mediaGeneration; verificationAnalysisGeneration = admitted.Generation;
            verificationReviewFrame = reviewFrame;
            _ = RunVerificationAsync(admitted, group, operationId, mediaGeneration, reviewedPriorLayout,
                reviewedPriorTimeline, active, cancellation, completed, reviewFrame);
            return pending;
        }
    }
    public void CancelVerification() { lock (gate) { if (verificationRunning) active?.Cancel(); } }

    async Task RunVerificationAsync(TableAnalysisSnapshot admitted, string admittedGroup, Guid operationId, long mediaGeneration,
        TableState? reviewedPriorLayout, string? reviewedPriorTimeline,
        CancellationTokenSource activeOperation, CancellationToken caller, TaskCompletionSource completed, bool reviewFrame)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(ObservationPolicy.VerificationTimeoutSeconds), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(activeOperation.Token, deadline.Token);
        var start = time.GetTimestamp(); var samples = new List<VerificationSample>();
        var capture = VerificationCapture;
        ITableVerificationWorker? worker = null; Task<(VerificationMeasurement Measurement, byte[]? Review, double WorkerMeasureMs, double EvidenceCaptureMs, double ReviewEncodeMs)>? processing = null; bool leased = false;
        byte[]? finalReview = null;
        Task? retiringAcquisition = null;
        string? timeline = null; DecodedFrameReceipt? lastReceipt = null; DateTimeOffset capturedAt = time.GetUtcNow();
        double maxGapMs = 0; long scheduleStart = start, finalReceiptAt = start;
        ObservationReason reason = ObservationReason.InsufficientEvidence;
        VerificationRejection? rejected = null;
        VerificationFailure? failure = null;
        int candidateSpanStartSampleIndex = 0;
        int maximumSamples = ObservationPolicy.StandardVerificationSamples;
        string stage = "admission";
        bool HasStillSpan(IReadOnlyList<VerificationSample> span) => span.Count >= ObservationPolicy.MinimumVerificationSamples
            && span[^1].SourceTimeUs - span[0].SourceTimeUs >= ObservationPolicy.MinimumStillSeconds * 1_000_000
            && span.All(s => s.Measurement.CoverageValid)
            && span.Skip(1).All(s => s.Measurement.Motion == TableMotion.Still);
        try
        {
            await analyzerGate.WaitAsync(linked.Token).ConfigureAwait(false); leased = true;
            worker = ((ITableVerificationAnalyzer)analyzer).CreateVerification(admitted.Calibration!);
            while (samples.Count < maximumSamples)
            {
                linked.Token.ThrowIfCancellationRequested();
                double clockRefreshMs = 0, targetCadenceWaitMs = 0, newFramePollingMs = 0, acquisitionMs = 0;
                if (samples.Count is 0 or 2 or 4 && frames is ITableVerificationFrameSource clockSource)
                {
                    stage = "clock"; var clockStart = time.GetTimestamp();
                    try { await clockSource.RefreshVerificationClockAsync(linked.Token).WaitAsync(linked.Token).ConfigureAwait(false); }
                    finally { clockRefreshMs = time.GetElapsedTime(clockStart).TotalMilliseconds; }
                }
                if (samples.Count > 0)
                {
                    var wait = TimeSpan.FromSeconds(samples.Count * ObservationPolicy.SampleIntervalSeconds) - time.GetElapsedTime(scheduleStart);
                    if (wait > TimeSpan.Zero)
                    {
                        var waitStart = time.GetTimestamp();
                        try { await Task.Delay(wait, time, linked.Token).ConfigureAwait(false); }
                        finally { targetCadenceWaitMs = time.GetElapsedTime(waitStart).TotalMilliseconds; }
                    }
                }
                if (samples.Count > 0 && frames is ITableMotionFrameSource polling)
                {
                    stage = "poll";
                    var scope = new MotionFeedScope(admitted.Source!, mediaGeneration, admitted.CalibrationId, timeline!);
                    var pollStart = time.GetTimestamp();
                    try
                    {
                        while (!await polling.HasNewFrameAsync(scope, samples[^1].FrameId, samples[^1].SourceTimeUs, linked.Token).ConfigureAwait(false))
                            await Task.Delay(TimeSpan.FromMilliseconds(40), time, linked.Token).ConfigureAwait(false);
                    }
                    finally { newFramePollingMs = time.GetElapsedTime(pollStart).TotalMilliseconds; }
                }
                var readStart = time.GetTimestamp();
                stage = "acquisition";
                TableFrame frame;
                try { frame = await ReadVerificationFrameAsync(admitted.Source!, linked.Token, cleanup => retiringAcquisition = cleanup).ConfigureAwait(false); }
                finally { acquisitionMs = time.GetElapsedTime(readStart).TotalMilliseconds; }
                var receipt = frame.DecodedReceipt; var pts = frame.SourceTimeUs;
                double? sourceGapMs = samples.Count > 0 && pts.HasValue ? (pts.Value - samples[^1].SourceTimeUs) / 1000d : null;
                var age = (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds;
                var mappingAge = frame.ClockSampleAgeMs;
                var uncertainty = frame.ClockUncertaintyMs;
                void RejectFrame() => rejected = new(reason, receipt, frame.Timeline, pts, age, mappingAge, uncertainty)
                {
                    SourceGapMs = sourceGapMs,
                    ClockRefreshMs = clockRefreshMs,
                    TargetCadenceWaitMs = targetCadenceWaitMs,
                    NewFramePollingMs = newFramePollingMs,
                    AcquisitionMs = acquisitionMs,
                };
                reason = receipt is null || pts is null || receipt.MediaGeneration != mediaGeneration || receipt.FrameId <= 0 || pts < 0
                    || samples.Count > 0 && (frame.Timeline != timeline || receipt.Width != lastReceipt!.Width || receipt.Height != lastReceipt.Height)
                    ? ObservationReason.SourceChanged
                    : !double.IsFinite(age) || age is < -200 or > ObservationPolicy.MaximumFinalAgeMs ? ObservationReason.FrameTooOld
                    : mappingAge is null || !double.IsFinite(mappingAge.Value) || mappingAge is < 0 or > ObservationPolicy.MaximumClockSampleAgeMs
                        || uncertainty is null || !double.IsFinite(uncertainty.Value) || uncertainty is < 0 or > 250 ? ObservationReason.ClockUntrusted
                    : ObservationReason.None;
                if (reason != ObservationReason.None) { RejectFrame(); frame.Dispose(); break; }
                if (samples.Count > 0 && (receipt!.FrameId <= samples[^1].FrameId || pts <= samples[^1].SourceTimeUs))
                { reason = ObservationReason.SourceChanged; RejectFrame(); frame.Dispose(); break; }
                var gap = samples.Count == 0 ? 0 : (pts!.Value - samples[^1].SourceTimeUs) / 1000d;
                maxGapMs = Math.Max(maxGapMs, gap);
                if (gap > ObservationPolicy.MaximumVerificationGapSeconds * 1000) { reason = ObservationReason.SampleGap; RejectFrame(); frame.Dispose(); break; }
                timeline = frame.Timeline; capturedAt = frame.CapturedAt; lastReceipt = receipt;
                finalReceiptAt = time.GetTimestamp(); if (samples.Count == 0) scheduleStart = finalReceiptAt;
                bool retryUncertainFinalDetection = samples.Count == ObservationPolicy.StandardVerificationSamples
                    && samples.Count > 0
                    && samples[^1].Measurement.Vision is { HasUncertainLayout: true, Obstructed: false };
                bool detect = samples.Count is 0 or 2 or 4 or 6 || retryUncertainFinalDetection;
                var processStart = time.GetTimestamp();
                // The task owns pixels until native work really finishes, even if the caller's deadline expires.
                processing = Task.Run(() =>
                {
                    using (frame)
                    {
                        stage = "analysis";
                        var measureStart = time.GetTimestamp();
                        var measured = worker.Measure(frame, detect, linked.Token);
                        var workerMeasureMs = time.GetElapsedTime(measureStart).TotalMilliseconds;
                        linked.Token.ThrowIfCancellationRequested();
                        stage = "capture";
                        var captureStart = time.GetTimestamp();
                        capture?.Invoke(new(operationId, admitted.Generation, admitted.CalibrationId, admitted.Calibration!.Corners.ToArray(), frame, measured)
                        {
                            AnalyzerConfigurationIdentity = (analyzer as ITableAnalyzerMetadata)?.ConfigurationIdentity
                                ?? analyzer.GetType().FullName ?? analyzer.GetType().Name,
                            DetectionRequested = detect,
                        });
                        var evidenceCaptureMs = time.GetElapsedTime(captureStart).TotalMilliseconds;
                        stage = "review";
                        var reviewEncodeMs = 0d; byte[]? review = null;
                        if (reviewFrame && detect)
                        {
                            var reviewStart = time.GetTimestamp();
                            review = frame.ReviewImage();
                            reviewEncodeMs = time.GetElapsedTime(reviewStart).TotalMilliseconds;
                        }
                        return (measured, review, workerMeasureMs, evidenceCaptureMs, reviewEncodeMs);
                    }
                }, CancellationToken.None);
                var processed = await processing.WaitAsync(linked.Token).ConfigureAwait(false);
                var measured = processed.Measurement;
                if (processed.Review is not null) finalReview = processed.Review;
                samples.Add(new(receipt!.FrameId, pts!.Value, age, mappingAge!.Value, uncertainty!.Value, acquisitionMs, time.GetElapsedTime(processStart).TotalMilliseconds, measured)
                {
                    CapturedAt = capturedAt,
                    ClockRefreshMs = clockRefreshMs,
                    TargetCadenceWaitMs = targetCadenceWaitMs,
                    NewFramePollingMs = newFramePollingMs,
                    WorkerMeasureMs = processed.WorkerMeasureMs,
                    EvidenceCaptureMs = processed.EvidenceCaptureMs,
                    ReviewEncodeMs = processed.ReviewEncodeMs,
                });
                lock (gate)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (state.Generation != admitted.Generation || state.Source != admitted.Source || group != admittedGroup) throw new OperationCanceledException();
                    state = state with { VerificationProgress = new(samples.Count, measured.Reason) };
                }
                reason = measured.Reason;
                if (reason == ObservationReason.Obstructed)
                {
                    // An obstructed decoded view invalidates the current stillness
                    // anchor, but may be a single transient hand/edge. Keep the
                    // attempt within the ordinary seven-frame/four-second budget,
                    // and require a new complete clear span before advice can be ready.
                    candidateSpanStartSampleIndex = samples.Count;
                    worker.ResetAfterObstruction();
                    continue;
                }
                if (samples.Count == ObservationPolicy.StandardVerificationSamples
                    && (reason is ObservationReason.None or ObservationReason.InsufficientEvidence)
                    && measured.Vision is { HasUncertainLayout: true, Obstructed: false })
                {
                    // Allow one final full detection when the ordinary burst ends
                    // on an ambiguous but visually still frame. A clear re-read
                    // may complete consensus; otherwise the eighth sample keeps
                    // the unresolved layout explicitly ambiguous.
                    maximumSamples = ObservationPolicy.MaximumVerificationSamples;
                }
                if (reason is not (ObservationReason.None or ObservationReason.InsufficientEvidence)) break;
                if (reason == ObservationReason.InsufficientEvidence && samples.Count > 1)
                {
                    // A motion/photometric interval with insufficient support
                    // cannot poison the later stationary span. Keep that current
                    // frame as the new baseline and require a full fresh span
                    // after it; no motion or coverage threshold is relaxed.
                    candidateSpanStartSampleIndex = samples.Count - 1;
                }
                var candidateSamples = samples.Skip(candidateSpanStartSampleIndex).ToArray();
                if (HasStillSpan(candidateSamples)
                    && VerificationConsensus.Resolve(candidateSamples.Where(s => s.Measurement.Vision is not null)
                        .Select(s => s.Measurement.Vision!).ToArray()) is not null) break;
            }
            if (timeline is not null && frames is ITableVerificationFrameSource finalSource
                && !await finalSource.IsVerificationSourceCurrentAsync(admitted.Source!, mediaGeneration, timeline, linked.Token).WaitAsync(linked.Token).ConfigureAwait(false))
                reason = ObservationReason.SourceChanged;
            if (PublishVerification()) completed.TrySetResult(); else completed.TrySetCanceled();
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !activeOperation.IsCancellationRequested && !caller.IsCancellationRequested)
        { reason = ObservationReason.TimedOut; if (PublishVerification()) completed.TrySetResult(); else completed.TrySetCanceled(); }
        catch (OperationCanceledException) { ClearFailedAnalysis(admitted.Generation); completed.TrySetCanceled(); }
        catch (Exception error)
        {
            reason = error is FrameAcquisitionException acquisition ? acquisition.Reason
                : stage == "capture" ? ObservationReason.CaptureFailed
                : stage is "analysis" or "review" ? ObservationReason.AnalysisFailed : ObservationReason.FrameUnavailable;
            failure = new(stage, error is FrameAcquisitionException sourceError ? sourceError.Code : error.GetType().Name)
                { SourceStatus = (error as FrameAcquisitionException)?.SourceStatus };
            if (PublishVerification()) completed.TrySetResult(); else completed.TrySetCanceled();
        }
        finally
        {
            // Keep one native worker/lease until an uncooperative native call returns.
            if (processing is { IsCompleted: false })
            {
                var retiring = worker; bool release = leased;
                _ = processing.ContinueWith(t => { _ = t.Exception; retiring?.Dispose(); if (release) analyzerGate.Release(); Finish(); }, TaskScheduler.Default);
            }
            else if (retiringAcquisition is { IsCompleted: false })
            {
                var retiring = worker; bool release = leased;
                _ = retiringAcquisition.ContinueWith(t => { _ = t.Exception; retiring?.Dispose(); if (release) analyzerGate.Release(); Finish(); }, TaskScheduler.Default);
            }
            else { worker?.Dispose(); if (leased) analyzerGate.Release(); Finish(); }
        }
        void Finish()
        {
            lock (gate) { if (active == activeOperation) { active = null; verificationRunning = false; } }
            activeOperation.Dispose();
        }
        bool PublishVerification()
        {
            lock (gate)
            {
                if (activeOperation.IsCancellationRequested || disposed || state.Generation != admitted.Generation || state.Source != admitted.Source || group != admittedGroup || state.Calibration != admitted.Calibration)
                    return false;
                var candidateSamples = samples.Skip(candidateSpanStartSampleIndex).ToArray();
                var visions = candidateSamples.Where(s => s.Measurement.Vision is not null).Select(s => s.Measurement.Vision!).ToArray();
                var allVisions = samples.Where(s => s.Measurement.Vision is not null).Select(s => s.Measurement.Vision!).ToArray();
                var lastDetection = candidateSamples.LastOrDefault(s => s.Measurement.Vision is not null)
                    ?? samples.LastOrDefault(s => s.Measurement.Vision is not null);
                var consensus = VerificationConsensus.Resolve(visions, out var conflict);
                bool still = HasStillSpan(candidateSamples);
                var finalVision = lastDetection?.Measurement.Vision;
                bool reviewedInventoryMatch = finalVision is { Uncertain: true, HasUncertainLayout: false, Obstructed: false }
                    && reviewedPriorLayout is { Settled: true, Occluded: false, Confirmed: true } prior
                    && prior.CalibrationId == admitted.CalibrationId && prior.Group == admittedGroup
                    && reviewedPriorTimeline is not null && reviewedPriorTimeline == timeline
                    && lastDetection is not null
                    && (lastDetection.CapturedAt - prior.CapturedAt).TotalSeconds is >= -2 and <= ObservationPolicy.ReviewFreshnessSeconds
                    && consensus is not null && TableTracker.SameLayout(prior.Balls, consensus);
                bool inventoryReady = finalVision is { Uncertain: false, HasUncertainLayout: false, Obstructed: false }
                    || reviewedInventoryMatch;
                var sinceReceiptMs = time.GetElapsedTime(finalReceiptAt).TotalMilliseconds;
                var finalAge = samples.Count == 0 ? 0 : samples[^1].AgeMs + samples[^1].ClockUncertaintyMs + sinceReceiptMs;
                var finalClockAge = samples.Count == 0 ? double.PositiveInfinity : samples[^1].ClockSampleAgeMs + sinceReceiptMs;
                if (reason == ObservationReason.None && (!double.IsFinite(finalAge) || finalAge is < -200 or > ObservationPolicy.MaximumFinalAgeMs)) reason = ObservationReason.FrameTooOld;
                if (reason == ObservationReason.None && finalClockAge > ObservationPolicy.MaximumClockSampleAgeMs) reason = ObservationReason.ClockUntrusted;
                if (reason is ObservationReason.None or ObservationReason.InsufficientEvidence)
                    reason = !still ? ObservationReason.InsufficientEvidence
                        : consensus is null || !inventoryReady ? ObservationReason.DetectionAmbiguous : ObservationReason.None;
                bool ready = reason == ObservationReason.None && still && consensus is not null;
                var balls = ready ? consensus! : allVisions.LastOrDefault()?.Balls ?? [];
                var quality = reason switch { ObservationReason.Obstructed => LayoutQuality.Obstructed,
                    ObservationReason.DetectionAmbiguous => LayoutQuality.Ambiguous, ObservationReason.None when ready => LayoutQuality.Clear,
                    _ => LayoutQuality.Unknown };
                var priorLayout = tracker.State;
                bool unclearDetection = reason is ObservationReason.Obstructed or ObservationReason.DetectionAmbiguous;
                bool retainPriorLayout = !ready && unclearDetection
                    && priorLayout is not null && priorLayout.CalibrationId == admitted.CalibrationId
                    && timeline is not null && lastTimeline == timeline;
                var layout = retainPriorLayout
                    ? tracker.Observe(admitted.CalibrationId, timeline!, lastDetection?.CapturedAt ?? capturedAt,
                        priorLayout!.Balls, admittedGroup, uncertain: true, obstructed: reason == ObservationReason.Obstructed)
                    : tracker.Verified(admitted.CalibrationId, timeline ?? "unavailable", lastDetection?.CapturedAt ?? capturedAt, balls, admittedGroup,
                        still && reason is ObservationReason.None or ObservationReason.DetectionAmbiguous, !ready);
                state = state with { Table = layout, Vision = allVisions.LastOrDefault(), Jpeg = finalReview, VerificationProgress = null,
                    Observation = new(admitted.Source!, admitted.Generation, mediaGeneration, timeline ?? "unavailable", layout.CapturedAt,
                        samples.LastOrDefault()?.SourceTimeUs, double.IsFinite(finalAge) ? Math.Max(0, finalAge) : 0, samples.Sum(s => s.AcquisitionMs), samples.Sum(s => s.ProcessingMs), time.GetElapsedTime(start).TotalMilliseconds,
                        layout.Version, ready ? ObservationOutcome.Ready : (unclearDetection || still) ? ObservationOutcome.Unclear : ObservationOutcome.Unsettled, "decoded-live-verification")
                    { Motion = reason == ObservationReason.Moving ? TableMotion.Moving : still ? TableMotion.Still : TableMotion.Unknown, Quality = quality, Reason = reason, DecodedReceipt = lastReceipt,
                        Recognition = lastDetection?.Measurement.Vision?.Recognition,
                        DetectionEvidence = lastDetection is null ? null : new(lastDetection.FrameId, lastDetection.SourceTimeUs,
                            finalAge + (samples[^1].SourceTimeUs - lastDetection.SourceTimeUs) / 1000d),
                        Verification = new(operationId, ObservationPolicy.VerificationConfiguration, admitted.CalibrationId, admittedGroup, mediaGeneration,
                            samples.FirstOrDefault()?.SourceTimeUs, samples.LastOrDefault()?.SourceTimeUs, samples.Count, maxGapMs, double.IsFinite(finalClockAge) ? finalClockAge : -1, samples.ToArray())
                            { CandidateSpanStartSampleIndex = candidateSpanStartSampleIndex,
                                CandidateSpanIndependentFrames = candidateSamples.Length,
                                ReviewedInventoryMatch = reviewedInventoryMatch,
                                RejectedSample = rejected, Failure = failure, ConsensusConflict = conflict } } };
                lastTimeline = timeline;
                return true;
            }
        }
    }
    async Task<TableFrame> ReadVerificationFrameAsync(CameraScope source, CancellationToken cancellation, Action<Task> retiring)
    {
        var read = frames.ReadAsync(source, cancellation);
        try { return await read.WaitAsync(cancellation).ConfigureAwait(false); }
        catch { retiring(read.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); else _ = t.Exception; }, TaskScheduler.Default)); throw; }
    }
}
