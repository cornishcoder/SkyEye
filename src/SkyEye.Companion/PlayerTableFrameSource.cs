using System.Text.Json;
using System.Diagnostics;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Vision.Android;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

public sealed class PlayerTableFrameSource(Func<Bridge.PlayerBridge?> player, Func<CameraConnection> connection, Func<bool> live,
    Func<CancellationToken, Task>? refreshClock = null, int decodedReadbackScale = 1) : ITableMotionFrameSource, ITableMotionFrameProbe,
    ITableMotionFrameCandidateReader, ITableMotionFrameUpdateWaiter, ITableVerificationFrameSource
{
    sealed class MotionFrameUpdateCallback(Action callback) : Java.Lang.Object, Bridge.PlayerBridge.IMotionFrameUpdateListener
    {
        public void OnFrameUpdated() => callback();
    }

    readonly int readbackScale = decodedReadbackScale is 1 or 2 ? decodedReadbackScale
        : throw new ArgumentOutOfRangeException(nameof(decodedReadbackScale));
    const int MotionUpdateFallbackMs = 250;
    public Task RefreshVerificationClockAsync(CancellationToken cancellation) => refreshClock?.Invoke(cancellation) ?? Task.CompletedTask;
    public Task<bool> IsVerificationSourceCurrentAsync(CameraScope source, long mediaGeneration, string timeline, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Post(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (player() is not { } owner || !live() || connection().Scope != source || !connection().CanControl)
                { completion.TrySetResult(false); return; }
                var json = owner.DecodedFrameCandidate();
                if (json is null) { completion.TrySetResult(false); return; }
                using var candidate = JsonDocument.Parse(json); var stamp = candidate.RootElement;
                completion.TrySetResult(stamp.GetProperty("mediaGeneration").GetInt64() == mediaGeneration && stamp.GetProperty("timeline").GetString() == timeline);
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }
    readonly global::Android.OS.Handler main = new(global::Android.OS.Looper.MainLooper!);
    public async Task<bool> HasNewFrameAsync(MotionFeedScope scope, long afterFrameId, long afterSourceTimeUs, CancellationToken cancellation)
        => (await ProbeNewCandidateAsync(scope, afterFrameId, afterSourceTimeUs, cancellation).ConfigureAwait(false)).Disposition
            == MotionFrameCandidateDisposition.New;
    public Task<MotionFrameCandidateProbe> ProbeNewCandidateAsync(MotionFeedScope scope, long afterFrameId,
        long afterSourceTimeUs, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<MotionFrameCandidateProbe>(TaskCreationOptions.RunContinuationsAsynchronously);
        long queuedAt = Stopwatch.GetTimestamp();
        main.Post(() =>
        {
            try
            {
                double queueMs = Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds;
                cancellation.ThrowIfCancellationRequested();
                if (player() is not { } owner || !live() || connection().Scope != scope.Source || !connection().CanControl)
                    throw new FrameAcquisitionException(ObservationReason.SourceChanged, "motion-source-changed");
                completion.TrySetResult(ProbeCandidateOnMain(owner, scope, afterFrameId, afterSourceTimeUs, queueMs));
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }
    MotionFrameCandidateProbe ProbeCandidateOnMain(Bridge.PlayerBridge owner, MotionFeedScope scope,
        long afterFrameId, long afterSourceTimeUs, double queueMs)
    {
        long probeStarted = Stopwatch.GetTimestamp();
        var json = owner.DecodedFrameProbe();
        if (json is null)
            return new(MotionFrameCandidateDisposition.Unavailable, "invalid-probe")
            { MainThreadQueueMs = queueMs, ProbeDurationMs = Stopwatch.GetElapsedTime(probeStarted).TotalMilliseconds };
        using var candidate = JsonDocument.Parse(json); var stamp = candidate.RootElement;
        long? textureUpdateCount = stamp.TryGetProperty("textureUpdateCount", out var updateCount)
            && updateCount.ValueKind == JsonValueKind.Number && updateCount.TryGetInt64(out var parsedUpdateCount) ? parsedUpdateCount : null;
        double? textureAgeMs = stamp.TryGetProperty("textureAgeMs", out var textureAge)
            && textureAge.ValueKind == JsonValueKind.Number && textureAge.TryGetDouble(out var parsedTextureAge)
            && double.IsFinite(parsedTextureAge) ? parsedTextureAge : null;
        var status = stamp.GetProperty("candidateStatus").GetString() ?? "unknown";
        double probeMs = Stopwatch.GetElapsedTime(probeStarted).TotalMilliseconds;
        if (status != "available")
            return new(MotionFrameCandidateDisposition.Unavailable, status)
            { MainThreadQueueMs = queueMs, ProbeDurationMs = probeMs, TextureUpdateCount = textureUpdateCount, TextureAgeMs = textureAgeMs };
        long frameId = stamp.GetProperty("frameId").GetInt64(); long sourceTimeUs = stamp.GetProperty("sourceUs").GetInt64();
        bool changedScope = stamp.GetProperty("mediaGeneration").GetInt64() != scope.MediaGeneration
            || stamp.GetProperty("timeline").GetString() != scope.Timeline;
        bool isNew = changedScope || frameId > afterFrameId && sourceTimeUs > afterSourceTimeUs;
        return new(isNew ? MotionFrameCandidateDisposition.New : MotionFrameCandidateDisposition.Unchanged,
            changedScope ? "scope-changed" : isNew ? "new" : "unchanged", frameId, sourceTimeUs)
        { MainThreadQueueMs = queueMs, ProbeDurationMs = probeMs, TextureUpdateCount = textureUpdateCount, TextureAgeMs = textureAgeMs };
    }
    public Task WaitForNextFrameUpdateAsync(MotionFeedScope scope, long afterFrameId, long afterSourceTimeUs,
        CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bridge.PlayerBridge? waitingOwner = null;
        MotionFrameUpdateCallback? waitingListener = null;
        CancellationTokenRegistration cancellationRegistration = default;
        void ClearWaitingListener()
        {
            if (waitingOwner is { } owner && waitingListener is { } listener) owner.RemoveMotionFrameUpdateListener(listener);
            waitingOwner = null;
            waitingListener = null;
        }
        void CompleteCancelled()
        {
            ClearWaitingListener();
            completion.TrySetCanceled(cancellation);
        }
        cancellationRegistration = cancellation.Register(() =>
        {
            try { main.Post(CompleteCancelled); }
            catch (ObjectDisposedException) { completion.TrySetCanceled(cancellation); }
        });
        _ = completion.Task.ContinueWith(_ => cancellationRegistration.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        Action? fallback = null;
        fallback = () =>
        {
            if (completion.Task.IsCompleted) return;
            ClearWaitingListener();
            completion.TrySetResult(true);
        };
        main.Post(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (player() is not { } owner || !live() || connection().Scope != scope.Source || !connection().CanControl)
                    throw new FrameAcquisitionException(ObservationReason.SourceChanged, "motion-source-changed");
                var probe = ProbeCandidateOnMain(owner, scope, afterFrameId, afterSourceTimeUs, 0);
                if (probe.Disposition == MotionFrameCandidateDisposition.New)
                {
                    completion.TrySetResult(true);
                    return;
                }

                waitingOwner = owner;
                waitingListener = new MotionFrameUpdateCallback(() =>
                {
                    if (completion.Task.IsCompleted) return;
                    ClearWaitingListener();
                    if (cancellation.IsCancellationRequested) { completion.TrySetCanceled(cancellation); return; }
                    if (player() != owner || !live() || connection().Scope != scope.Source || !connection().CanControl)
                    { completion.TrySetException(new FrameAcquisitionException(ObservationReason.SourceChanged, "motion-source-changed")); return; }
                    completion.TrySetResult(true);
                });
                owner.AddMotionFrameUpdateListener(waitingListener);
                main.PostDelayed(fallback, MotionUpdateFallbackMs);
            }
            catch (Exception error)
            {
                ClearWaitingListener();
                completion.TrySetException(error);
            }
        });
        return completion.Task;
    }
    public Task<MotionFrameCandidateRead> ReadNewCandidateAsync(MotionFeedScope scope, long afterFrameId,
        long afterSourceTimeUs, Action beforeReadback, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(beforeReadback);
        var completion = new TaskCompletionSource<MotionFrameCandidateRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bridge.PlayerBridge? admittedOwner = null;
        Bridge.PlayerBridge? waitingOwner = null;
        MotionFrameUpdateCallback? waitingListener = null;
        int acquisitionAttempts = 0;
        double bitmapCopyMs = 0;
        CancellationTokenRegistration cancellationRegistration = default;
        void ClearWaitingListener()
        {
            if (waitingOwner is { } owner && waitingListener is { } listener) owner.RemoveMotionFrameUpdateListener(listener);
            waitingOwner = null;
            waitingListener = null;
        }
        void CompleteCancelled()
        {
            ClearWaitingListener();
            completion.TrySetCanceled(cancellation);
        }
        cancellationRegistration = cancellation.Register(() =>
        {
            try { main.Post(CompleteCancelled); }
            catch (ObjectDisposedException) { completion.TrySetCanceled(cancellation); }
        });
        void Schedule(int remaining, int intentionalDelayMs = 0)
        {
            long queuedAt = Stopwatch.GetTimestamp();
            Action attempt = () => Attempt(remaining, queuedAt, intentionalDelayMs);
            if (intentionalDelayMs > 0) main.PostDelayed(attempt, intentionalDelayMs);
            else main.Post(attempt);
        }
        void Attempt(int remaining, long queuedAt, int intentionalDelayMs)
        {
            double queueMs = Math.Max(0, Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds - intentionalDelayMs);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var owner = player();
                if (owner is null || !live() || connection().Scope != scope.Source || !connection().CanControl)
                    throw new FrameAcquisitionException(ObservationReason.SourceChanged, "motion-source-changed");
                if (admittedOwner is not null && admittedOwner != owner)
                    throw new FrameAcquisitionException(ObservationReason.SourceChanged, "player-changed");
                admittedOwner = owner;
                var probe = ProbeCandidateOnMain(owner, scope, afterFrameId, afterSourceTimeUs, queueMs);
                if (probe.Disposition != MotionFrameCandidateDisposition.New)
                { completion.TrySetResult(new(probe, null)); return; }
                beforeReadback();
                cancellation.ThrowIfCancellationRequested();
                acquisitionAttempts++;
                var frame = TryReadFrameOnce(owner, scope.Source, cancellation, acquisitionAttempts, queueMs,
                    bitmapCopyMs, out var attemptCopyMs);
                bitmapCopyMs += attemptCopyMs;
                if (frame is null)
                {
                    if (remaining > 0)
                    {
                        bool retryStarted = false;
                        Action Retry = () =>
                        {
                            if (retryStarted || completion.Task.IsCompleted) return;
                            retryStarted = true;
                            ClearWaitingListener();
                            Attempt(remaining - 1, Stopwatch.GetTimestamp(), 0);
                        };
                        waitingOwner = owner;
                        waitingListener = new MotionFrameUpdateCallback(Retry);
                        owner.AddMotionFrameUpdateListener(waitingListener);
                        main.PostDelayed(Retry, MotionUpdateFallbackMs);
                        return;
                    }
                    var code = owner.LastCopyFailure();
                    var failureCode = string.IsNullOrWhiteSpace(code) ? "paired-frame-unavailable" : code;
                    Android.Util.Log.Warn("SkyEyeFrameSource", $"Live candidate frame unavailable after {acquisitionAttempts} attempts: {failureCode}");
                    throw new FrameAcquisitionException(failureCode == "unmapped-source-clock" ? ObservationReason.ClockUntrusted : ObservationReason.FrameUnavailable,
                        failureCode, owner.ObservationSourceStatus());
                }
                completion.TrySetResult(new(probe, frame));
            }
            catch (Exception error)
            {
                if (error is not OperationCanceledException)
                    Android.Util.Log.Warn("SkyEyeFrameSource", $"Live candidate acquisition failed after {acquisitionAttempts} attempts: {error.GetType().Name}: {error.Message}");
                ClearWaitingListener();
                cancellationRegistration.Dispose();
                completion.TrySetException(error);
            }
        }
        _ = completion.Task.ContinueWith(_ =>
        {
            cancellationRegistration.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        Schedule(3);
        return completion.Task;
    }
    TableFrame? TryReadFrameOnce(Bridge.PlayerBridge owner, CameraScope source, CancellationToken cancellation,
        int acquisitionAttempts, double mainThreadQueueMs, double previousBitmapCopyMs, out double attemptBitmapCopyMs)
    {
        attemptBitmapCopyMs = 0;
        using var timing = PipelineDiagnostics.Measure("textureCopyMs");
        using var snapshot = owner.SnapshotDecodedFrame(readbackScale);
        attemptBitmapCopyMs = owner.LastBitmapCopyMs();
        if (snapshot is null) return null;
        BitmapTableImage? pixels = null;
        try
        {
            using var metrics = JsonDocument.Parse(snapshot.Metadata()!); var stamp = metrics.RootElement;
            if (stamp.GetProperty("pairing").GetString() != "exact-surface-timestamp"
                || stamp.GetProperty("surfaceTimestampNs").GetInt64() != stamp.GetProperty("releaseTimestampNs").GetInt64())
                throw new FrameAcquisitionException(ObservationReason.SourceChanged, "unpaired-pixels");
            pixels = new(snapshot.TakeBitmap() ?? throw new IOException("Decoded receipt pixels are unavailable."));
            var age = stamp.GetProperty("ageEstimateMs").GetDouble();
            var imageWidth = stamp.TryGetProperty("imageWidth", out var iw) ? iw.GetInt32() : stamp.GetProperty("width").GetInt32();
            var imageHeight = stamp.TryGetProperty("imageHeight", out var ih) ? ih.GetInt32() : stamp.GetProperty("height").GetInt32();
            var uncertainty = stamp.TryGetProperty("clockUncertaintyMs", out var u) ? u.GetDouble() : -1;
            var clockAge = stamp.TryGetProperty("clockSampleAgeMs", out var ca) ? ca.GetDouble() : -1;
            long? textureUpdateCount = stamp.TryGetProperty("textureUpdateCount", out var updateCount)
                && updateCount.ValueKind == JsonValueKind.Number && updateCount.TryGetInt64(out var parsedUpdateCount) ? parsedUpdateCount : null;
            double? textureAgeMs = stamp.TryGetProperty("textureAgeMs", out var textureAge)
                && textureAge.ValueKind == JsonValueKind.Number && textureAge.TryGetDouble(out var parsedTextureAge)
                && double.IsFinite(parsedTextureAge) ? parsedTextureAge : null;
            if (!double.IsFinite(age) || age is < -200 or > 6000) throw new FrameAcquisitionException(ObservationReason.FrameTooOld, "source-age-out-of-range");
            cancellation.ThrowIfCancellationRequested();
            if (player() != owner || !live() || connection().Scope != source || !connection().CanControl)
                throw new FrameAcquisitionException(ObservationReason.SourceChanged, "source-changed-during-copy");
            var frame = new TableFrame([], DateTimeOffset.UtcNow.AddMilliseconds(-Math.Max(0, age)), stamp.GetProperty("timeline").GetString()!)
            {
                Image = pixels, SourceTimeUs = stamp.GetProperty("sourceUs").GetInt64(), FrameSource = "decoded-live-texture",
                AcquisitionAttempts = acquisitionAttempts,
                ImageWidth = imageWidth,
                ImageHeight = imageHeight,
                MainThreadQueueMs = mainThreadQueueMs,
                BitmapCopyMs = previousBitmapCopyMs + attemptBitmapCopyMs,
                TextureUpdateCount = textureUpdateCount,
                TextureAgeMs = textureAgeMs,
                ClockSampleAgeMs = clockAge >= 0 ? clockAge : null,
                ClockUncertaintyMs = uncertainty >= 0 ? uncertainty : null,
                DecodedReceipt = new(stamp.GetProperty("mediaGeneration").GetInt64(), stamp.GetProperty("frameId").GetInt64(),
                    stamp.GetProperty("presentationUs").GetInt64(), stamp.GetProperty("surfaceTimestampNs").GetInt64(),
                    stamp.GetProperty("width").GetInt32(), stamp.GetProperty("height").GetInt32())
            };
            pixels = null;
            return frame;
        }
        finally { pixels?.Dispose(); }
    }
    public Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<TableFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bridge.PlayerBridge? admittedOwner = null;
        int acquisitionAttempts = 0;
        long queuedAt = Stopwatch.GetTimestamp();
        double? mainThreadQueueMs = null;
        double bitmapCopyMs = 0;
        void Attempt(int remaining)
        {
            mainThreadQueueMs ??= Stopwatch.GetElapsedTime(queuedAt).TotalMilliseconds;
            try
            {
                acquisitionAttempts++;
                cancellation.ThrowIfCancellationRequested(); var owner = player();
                if (owner is null || !live() || connection().Scope != source || !connection().CanControl)
                    throw new FrameAcquisitionException(ObservationReason.SourceChanged, "inactive-live-source");
                if (admittedOwner is not null && admittedOwner != owner) throw new FrameAcquisitionException(ObservationReason.SourceChanged, "player-changed");
                admittedOwner = owner;
                var frame = TryReadFrameOnce(owner, source, cancellation, acquisitionAttempts, mainThreadQueueMs.Value,
                    bitmapCopyMs, out var attemptBitmapCopyMs);
                bitmapCopyMs += attemptBitmapCopyMs;
                if (frame is null)
                {
                    // A render/copy race may be refused even while live video is
                    // healthy. Retry on the next texture latch without weakening pairing.
                    if (remaining > 0)
                    {
                        bool retryStarted = false;
                        MotionFrameUpdateCallback? waitingListener = null;
                        void Retry()
                        {
                            if (retryStarted || completion.Task.IsCompleted) return;
                            retryStarted = true;
                            if (waitingListener is { } listener) owner.RemoveMotionFrameUpdateListener(listener);
                            waitingListener = null;
                            Attempt(remaining - 1);
                        }
                        waitingListener = new MotionFrameUpdateCallback(Retry);
                        owner.AddMotionFrameUpdateListener(waitingListener);
                        // A stopped or hidden texture must not hold an observation indefinitely.
                        main.PostDelayed(Retry, MotionUpdateFallbackMs);
                        return;
                    }
                    var code = owner.LastCopyFailure();
                    var failureCode = string.IsNullOrWhiteSpace(code) ? "paired-frame-unavailable" : code;
                    Android.Util.Log.Warn("SkyEyeFrameSource", $"Live observation frame unavailable after {acquisitionAttempts} attempts: {failureCode}");
                    throw new FrameAcquisitionException(failureCode == "unmapped-source-clock" ? ObservationReason.ClockUntrusted : ObservationReason.FrameUnavailable,
                        failureCode, owner.ObservationSourceStatus());
                }
                completion.TrySetResult(frame);
            }
            catch (Exception error)
            {
                if (error is not OperationCanceledException)
                    Android.Util.Log.Warn("SkyEyeFrameSource", $"Live observation acquisition failed after {acquisitionAttempts} attempts: {error.GetType().Name}: {error.Message}");
                completion.TrySetException(error);
            }
        }
        main.Post(() => Attempt(3));
        return completion.Task;
    }
}
