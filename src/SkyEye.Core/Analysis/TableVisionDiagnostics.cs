namespace SkyEye.Core.Analysis;

public sealed record VisionDiagnosticSample(
    int Sequence,
    DateTimeOffset CapturedAt,
    double FrameAgeMs,
    long MediaGeneration,
    long FrameId,
    long SourceTimeUs,
    string FrameSource,
    VisionResult Detection);

public sealed record VisionDiagnosticsSnapshot(
    bool Active,
    Guid SessionId,
    DateTimeOffset? StartedAt,
    string Status,
    int TotalSamples,
    VisionDiagnosticSample[] Samples)
{
    public VisionDiagnosticLabelFrame? LabelFrame { get; init; }
    public VisionDiagnosticLiveFrame? LiveFrame { get; init; }
    public bool LabelCapturePending { get; init; }
    public string RecordingSessionId { get; init; } = "";
}

/// <summary>The latest detector result and the exact source pixels it analyzed.
/// Only one live frame is retained; the rolling sample history stays metadata-only.</summary>
public sealed record VisionDiagnosticLiveFrame(VisionDiagnosticSample Sample, int Width, int Height, byte[] Jpeg);

/// <summary>A single opt-in diagnostic image held only while a person labels it.</summary>
public sealed record VisionDiagnosticLabelFrame(Guid SessionId, string RecordingSessionId, string SampleId, DateTimeOffset CapturedAt,
    double FrameAgeMs, long AnalysisGeneration, string CalibrationId, string AnalyzerConfiguration, Point[] CalibrationCorners,
    long MediaGeneration, long FrameId, long SourceTimeUs, string Timeline, string FrameSource,
    int Width, int Height, byte[] Jpeg);

/// <summary>Opt-in sequential detector diagnostics. Samples use the existing calibrated
/// frame source and analyzer, and are never published to TableTracker or GameState.</summary>
public sealed partial class TableAnalysisSession
{
    const int MaximumVisionDiagnosticSamples = 60;
    static readonly TimeSpan VisionDiagnosticInterval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan VisionDiagnosticFrameTimeout = TimeSpan.FromSeconds(6);
    readonly List<VisionDiagnosticSample> visionDiagnosticSamples = [];
    CancellationTokenSource? visionDiagnosticsCancellation;
    Task? visionDiagnosticsTask;
    Guid visionDiagnosticsSessionId;
    string visionDiagnosticsRecordingSessionId = "";
    DateTimeOffset? visionDiagnosticsStartedAt;
    VisionDiagnosticLabelFrame? visionDiagnosticsLabelFrame;
    VisionDiagnosticLiveFrame? visionDiagnosticsLiveFrame;
    bool visionDiagnosticLabelCaptureRequested;
    string visionDiagnosticsStatus = "Diagnostics are off.";
    string? visionDiagnosticsStopReason;
    int visionDiagnosticsTotalSamples;

    public event Action? VisionDiagnosticsChanged;

    public VisionDiagnosticsSnapshot VisionDiagnostics
    {
        get
        {
            lock (gate)
                return new VisionDiagnosticsSnapshot(visionDiagnosticsCancellation is { IsCancellationRequested: false }, visionDiagnosticsSessionId,
                    visionDiagnosticsStartedAt, visionDiagnosticsStatus, visionDiagnosticsTotalSamples, visionDiagnosticSamples.ToArray())
                    { LabelFrame = visionDiagnosticsLabelFrame, LiveFrame = visionDiagnosticsLiveFrame,
                        LabelCapturePending = visionDiagnosticLabelCaptureRequested,
                        RecordingSessionId = visionDiagnosticsRecordingSessionId };
        }
    }

    public bool RequestVisionDiagnosticLabelFrame(out string message)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (visionDiagnosticsCancellation is not { IsCancellationRequested: false })
            { message = "Start sequential diagnostics before capturing a frame to label."; return false; }
            if (visionDiagnosticsLabelFrame is not null || visionDiagnosticLabelCaptureRequested)
            { message = "Save or discard the current labelled frame first."; return false; }
            visionDiagnosticLabelCaptureRequested = true;
            visionDiagnosticsStatus = "Waiting for the next fresh frame to label.";
            message = visionDiagnosticsStatus;
        }
        NotifyVisionDiagnosticsChanged();
        return true;
    }

    public void CompleteVisionDiagnosticLabelFrame(string status)
    {
        lock (gate)
        {
            visionDiagnosticsLabelFrame = null;
            visionDiagnosticsStatus = status;
        }
        NotifyVisionDiagnosticsChanged();
    }

    public bool StartVisionDiagnostics(out string message)
    {
        TableAnalysisSnapshot admitted;
        CancellationTokenSource owner;
        TaskCompletionSource completed;
        lock (gate)
        {
            ThrowIfDisposed();
            if (visionDiagnosticsCancellation is not null)
            { message = "Vision diagnostics are already running."; return false; }
            if (state.Source is null || state.Calibration is null || state.Picking)
            { message = "Start an active camera session and calibrate the table first."; return false; }
            if (active is not null || verificationRunning)
            { message = "Wait for the current table check to finish before starting diagnostics."; return false; }
            if (frames is not ITableMotionFrameSource)
            { message = "This frame source cannot provide sequential live frames."; return false; }

            admitted = state;
            owner = new CancellationTokenSource();
            completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            visionDiagnosticsCancellation = owner;
            visionDiagnosticsTask = completed.Task;
            visionDiagnosticsSessionId = Guid.NewGuid();
            visionDiagnosticsRecordingSessionId = admitted.Source!.SessionId;
            visionDiagnosticsStartedAt = time.GetUtcNow();
            visionDiagnosticsLabelFrame = null;
            visionDiagnosticsLiveFrame = null;
            visionDiagnosticLabelCaptureRequested = false;
            visionDiagnosticsStatus = "Sampling fresh frames about once per second.";
            visionDiagnosticsStopReason = null;
            visionDiagnosticsTotalSamples = 0;
            visionDiagnosticSamples.Clear();
            message = visionDiagnosticsStatus;
        }
        NotifyVisionDiagnosticsChanged();
        _ = RunVisionDiagnosticsAsync(admitted, owner, completed);
        return true;
    }

    public async Task StopVisionDiagnosticsAsync()
    {
        Task? pending;
        lock (gate)
        {
            if (visionDiagnosticsCancellation is null) return;
            visionDiagnosticsStopReason = "Stopped by request.";
            visionDiagnosticLabelCaptureRequested = false;
            visionDiagnosticsCancellation.Cancel();
            pending = visionDiagnosticsTask;
        }
        NotifyVisionDiagnosticsChanged();
        if (pending is not null) await pending.ConfigureAwait(false);
    }

    void StopVisionDiagnosticsLocked(string reason)
    {
        if (visionDiagnosticsCancellation is null) return;
        visionDiagnosticsStopReason = reason;
        visionDiagnosticLabelCaptureRequested = false;
        visionDiagnosticsCancellation.Cancel();
    }

    async Task RunVisionDiagnosticsAsync(TableAnalysisSnapshot admitted, CancellationTokenSource owner, TaskCompletionSource completed)
    {
        string? failure = null;
        long lastFrameId = -1, lastSourceTimeUs = -1, mediaGeneration = -1;
        string? timeline = null;
        try
        {
            var polling = (ITableMotionFrameSource)frames;
            while (true)
            {
                owner.Token.ThrowIfCancellationRequested();
                if (lastFrameId >= 0)
                {
                    await Task.Delay(VisionDiagnosticInterval, time, owner.Token).ConfigureAwait(false);
                    var scope = new MotionFeedScope(admitted.Source!, mediaGeneration, admitted.CalibrationId, timeline!);
                    long waitStarted = time.GetTimestamp();
                    while (!await polling.HasNewFrameAsync(scope, lastFrameId, lastSourceTimeUs, owner.Token).ConfigureAwait(false))
                    {
                        owner.Token.ThrowIfCancellationRequested();
                        if (time.GetElapsedTime(waitStarted) > VisionDiagnosticFrameTimeout)
                            throw new TimeoutException("The live source did not deliver another decoded frame.");
                        await Task.Delay(TimeSpan.FromMilliseconds(80), time, owner.Token).ConfigureAwait(false);
                    }
                }

                using var frame = await frames.ReadAsync(admitted.Source!, owner.Token).ConfigureAwait(false);
                if (frame.DecodedReceipt is not { } receipt || frame.SourceTimeUs is not { } sourceTimeUs)
                    throw new InvalidOperationException("The live source did not provide a sequenced frame receipt.");
                if (lastFrameId >= 0 && (receipt.MediaGeneration != mediaGeneration || frame.Timeline != timeline
                    || receipt.FrameId <= lastFrameId || sourceTimeUs <= lastSourceTimeUs))
                    throw new InvalidOperationException("The live source changed while diagnostics were sampling.");

                await analyzerGate.WaitAsync(owner.Token).ConfigureAwait(false);
                VisionResult detection;
                try
                {
                    detection = await Task.Run(() => analyzer.Analyze(frame, admitted.Calibration!, owner.Token), owner.Token).ConfigureAwait(false);
                }
                finally { analyzerGate.Release(); }

                bool captureLabelFrame;
                lock (gate) captureLabelFrame = visionDiagnosticLabelCaptureRequested && visionDiagnosticsCancellation == owner;
                byte[]? labelJpeg = null;
                byte[]? liveJpeg = null;
                try
                {
                    var review = frame.ReviewImage();
                    if (review.Length is > 0 and <= 6 * 1024 * 1024)
                    {
                        liveJpeg = review.ToArray();
                        if (captureLabelFrame && review.Length <= 4 * 1024 * 1024) labelJpeg = liveJpeg.ToArray();
                    }
                }
                catch (IOException) { }

                bool accepted;
                lock (gate)
                {
                    owner.Token.ThrowIfCancellationRequested();
                    accepted = !disposed && visionDiagnosticsCancellation == owner
                        && state.Generation == admitted.Generation && state.Source == admitted.Source
                        && state.Calibration == admitted.Calibration && !state.Picking;
                    if (accepted)
                    {
                        var sample = new VisionDiagnosticSample(++visionDiagnosticsTotalSamples, frame.CapturedAt,
                            (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds, receipt.MediaGeneration, receipt.FrameId,
                            sourceTimeUs, frame.FrameSource, detection);
                        visionDiagnosticSamples.Add(sample);
                        if (visionDiagnosticSamples.Count > MaximumVisionDiagnosticSamples) visionDiagnosticSamples.RemoveAt(0);
                        visionDiagnosticsLiveFrame = liveJpeg is null ? null
                            : new(sample, receipt.Width, receipt.Height, liveJpeg);
                        if (captureLabelFrame && visionDiagnosticLabelCaptureRequested)
                        {
                            visionDiagnosticLabelCaptureRequested = false;
                            if (labelJpeg is null)
                                visionDiagnosticsStatus = "This frame could not be held for review (4 MiB limit). Try another frame.";
                            else
                            {
                                visionDiagnosticsLabelFrame = new(visionDiagnosticsSessionId, visionDiagnosticsRecordingSessionId,
                                    $"{visionDiagnosticsSessionId:N}-{receipt.FrameId:D12}", frame.CapturedAt,
                                    (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds, admitted.Generation,
                                    admitted.CalibrationId, (analyzer as ITableAnalyzerMetadata)?.ConfigurationIdentity
                                        ?? analyzer.GetType().FullName ?? analyzer.GetType().Name,
                                    admitted.Calibration!.Corners.ToArray(), receipt.MediaGeneration,
                                    receipt.FrameId, sourceTimeUs, frame.Timeline, frame.FrameSource, receipt.Width, receipt.Height, labelJpeg);
                                visionDiagnosticsStatus = "Frame captured. Label the image without the detector dump, then save or discard it.";
                            }
                        }
                        else visionDiagnosticsStatus = $"Sampling fresh frames · {visionDiagnosticsTotalSamples} captured; showing the latest {visionDiagnosticSamples.Count}.";
                    }
                }
                if (!accepted) break;
                lastFrameId = receipt.FrameId;
                lastSourceTimeUs = sourceTimeUs;
                mediaGeneration = receipt.MediaGeneration;
                timeline = frame.Timeline;
                NotifyVisionDiagnosticsChanged();
            }
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { failure = "Stopped because the live source did not deliver fresh frames."; }
        catch (Exception) { failure = "Stopped because the live frame or analysis became unavailable."; }
        finally
        {
            lock (gate)
            {
                if (visionDiagnosticsCancellation == owner)
                {
                    visionDiagnosticLabelCaptureRequested = false;
                    visionDiagnosticsLiveFrame = null;
                    visionDiagnosticsStatus = visionDiagnosticsStopReason ?? failure ?? "Stopped because the camera or calibration changed.";
                    visionDiagnosticsCancellation = null;
                    visionDiagnosticsTask = null;
                    visionDiagnosticsStopReason = null;
                }
            }
            owner.Dispose();
            completed.TrySetResult();
            NotifyVisionDiagnosticsChanged();
        }
    }

    void NotifyVisionDiagnosticsChanged()
    {
        try { VisionDiagnosticsChanged?.Invoke(); }
        catch { }
    }
}
