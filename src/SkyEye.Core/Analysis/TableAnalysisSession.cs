using System.Globalization;
using SkyEye.Core.Application;

namespace SkyEye.Core.Analysis;

public sealed record CircleObservation(string BallId, Point Centre, double Radius, string Pattern, ColourCluster[] Clusters);
public sealed record VisionResult(Ball[] Balls, bool Uncertain, string[] Warnings, double ElapsedMs, int CircleCount, bool Normalized, string Runtime, double[] ColourGains, int ClothHue, bool Obstructed, CircleObservation[] Circles);
public interface ITableAnalyzer
{
    VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default);
    VisionResult Analyze(TableFrame frame, TableCalibration calibration, CancellationToken cancellation = default) => Analyze(frame.Jpeg, calibration, cancellation);
    Point[]? FindCorners(byte[] jpeg);
}
public interface ITableFrameImage : IDisposable { byte[] EncodeForReview(); }
public sealed record DecodedFrameReceipt(long MediaGeneration, long FrameId, long PresentationTimeUs,
    long SurfaceTimestampNs, int Width, int Height);
public sealed record TableFrame(byte[] Jpeg, DateTimeOffset CapturedAt, string Timeline) : IDisposable
{
    public ITableFrameImage? Image { get; init; }
    public long? SourceTimeUs { get; init; }
    public string FrameSource { get; init; } = "camera-snapshot";
    public DecodedFrameReceipt? DecodedReceipt { get; init; }
    public byte[] ReviewImage() => Jpeg.Length > 0 ? Jpeg : Image?.EncodeForReview() ?? throw new IOException("Review frame unavailable.");
    public void Dispose() => Image?.Dispose();
}
public interface ITableFrameSource { Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation); }
public interface ICalibrationStore
{
    Point[]? Load(string cameraIdentity);
    void Save(string cameraIdentity, Point[] corners);
    void Forget(string cameraIdentity) { }
}
// Native frame bytes stay here, outside the app/agent control projection.
public sealed record TableAnalysisSnapshot(long Generation, CameraScope? Source, string? CameraIdentity, TableCalibration? Calibration,
    string CalibrationId, bool Picking, TableState? Table, VisionResult? Vision, byte[]? Jpeg)
{
    public TableObservation? Observation { get; init; }
    public Shot[] Choices { get; init; } = [];
    public bool ChoicesComputed { get; init; }
}
public sealed record CalibrationDraft(long Generation, byte[] Jpeg, Point[]? Proposed);

/// <summary>One owner for transient table tracking, calibration and reviewed edits.
/// Native/network work runs outside its lock. Source changes invalidate late work;
/// concurrent update requests share one frame instead of building a frame queue.</summary>
public sealed class TableAnalysisSession(ITableFrameSource frames, ITableAnalyzer analyzer, ICalibrationStore? calibrations = null, TimeProvider? clock = null) : IDisposable
{
    readonly object gate = new();
    readonly SemaphoreSlim analyzerGate = new(1);
    readonly TableTracker tracker = new(15);
    readonly TimeProvider time = clock ?? TimeProvider.System;
    TableAnalysisSnapshot state = new(0, null, null, null, "", false, null, null, null);
    CancellationTokenSource? active;
    Task? pending;
    string group = "open";
    string? lastTimeline;
    bool disposed;
    public TableAnalysisSnapshot State { get { lock (gate) return state; } }

    // Called from the coordinator subscription, before its UI observers. No IO.
    public void Bind(CameraConnection camera, string targetGroup)
    {
        lock (gate)
        {
            if (disposed) return;
            if (targetGroup is not ("open" or "red" or "yellow" or "black")) throw new ArgumentException("Unknown group.");
            var source = camera.CanControl && camera.Capture == CaptureState.Active ? camera.Scope : null;
            bool changedCamera = state.CameraIdentity != camera.CameraIdentity;
            if (source != state.Source || changedCamera)
            {
                InvalidateLocked();
                state = state with { Source = source, CameraIdentity = camera.CameraIdentity,
                    Calibration = changedCamera ? null : state.Calibration, CalibrationId = changedCamera ? "" : state.CalibrationId };
            }
            if (group != targetGroup) { group = targetGroup; state = state with { Table = tracker.SetGroup(group), Choices = [], ChoicesComputed = false }; }
        }
    }
    public bool RestoreCalibration()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (state.Calibration is not null || state.CameraIdentity is null || calibrations is null) return false;
            var corners = calibrations.Load(state.CameraIdentity);
            if (corners is null) return false;
            var calibration = new TableCalibration(corners);
            InvalidateLocked(); state = state with { Calibration = calibration, CalibrationId = Guid.NewGuid().ToString("N") };
            return true;
        }
    }
    public void Invalidate()
    {
        lock (gate) { if (!disposed) InvalidateLocked(); }
    }
    public void InvalidateCalibration(bool forgetStored = false)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (forgetStored && state.CameraIdentity is { } identity) calibrations?.Forget(identity);
            InvalidateLocked(); state = state with { Calibration = null, CalibrationId = "" };
        }
    }
    void InvalidateLocked()
    {
        active?.Cancel(); tracker.Reset(); lastTimeline = null;
        state = state with { Generation = checked(state.Generation + 1), Picking = false, Table = null, Vision = null, Jpeg = null, Observation = null, Choices = [], ChoicesComputed = false };
    }
    public Task UpdateAsync(CancellationToken cancellation = default, bool reviewFrame = false)
    {
        PipelineDiagnostics.Count("analysisRequests");
        lock (gate)
        {
            ThrowIfDisposed(); cancellation.ThrowIfCancellationRequested();
            if (pending is { IsCompleted: false }) { PipelineDiagnostics.Count("coalescedRequests"); return pending; }
            if (state.Source is null || state.Calibration is null || state.Picking) return Task.CompletedTask;
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var admitted = state; var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation); active = linked;
            pending = completed.Task;
            _ = RunUpdateAsync(admitted, linked, completed, reviewFrame);
            return pending;
        }
    }
    async Task RunUpdateAsync(TableAnalysisSnapshot admitted, CancellationTokenSource linked, TaskCompletionSource completed, bool reviewFrame)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var total = PipelineDiagnostics.Measure("observationMs");
        try
        {
            TableFrame frame;
            using (PipelineDiagnostics.Measure("frameAcquisitionMs")) frame = await frames.ReadAsync(admitted.Source!, linked.Token).ConfigureAwait(false);
            using (frame)
            {
                var acquisitionMs = watch.Elapsed.TotalMilliseconds;
                PipelineDiagnostics.Sample("frameAgeMs", (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds);
                await analyzerGate.WaitAsync(linked.Token).ConfigureAwait(false);
                VisionResult detected;
                try { PipelineDiagnostics.Count("fullAnalyses"); using var vision = PipelineDiagnostics.Measure("visionMs"); detected = await Task.Run(() => analyzer.Analyze(frame, admitted.Calibration!, linked.Token), linked.Token).ConfigureAwait(false); }
                finally { analyzerGate.Release(); }
                lock (gate)
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if (!disposed && state.Generation == admitted.Generation && state.Source == admitted.Source && state.Calibration == admitted.Calibration && !state.Picking)
                    {
                        if (state.Table is null || lastTimeline != frame.Timeline || frame.CapturedAt > state.Table.CapturedAt)
                        {
                            using var post = PipelineDiagnostics.Measure("postProcessingMs");
                            var table = tracker.Observe(state.CalibrationId, frame.Timeline, frame.CapturedAt, detected.Balls, group, detected.Uncertain, detected.Obstructed);
                            state = state with { Table = table, Vision = detected, Choices = [], ChoicesComputed = false, Jpeg = reviewFrame ? frame.ReviewImage() : frame.Jpeg.Length == 0 ? null : frame.Jpeg,
                                Observation = new(admitted.Source!, admitted.Generation, 0, frame.Timeline, frame.CapturedAt, frame.SourceTimeUs,
                                    (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds, acquisitionMs, detected.ElapsedMs, watch.Elapsed.TotalMilliseconds,
                                    table.Version, table.Occluded ? ObservationOutcome.Unclear : table.Settled ? ObservationOutcome.Ready : ObservationOutcome.Unsettled, frame.FrameSource)
                                    { DecodedReceipt = frame.DecodedReceipt } }; lastTimeline = frame.Timeline;
                        }
                    }
                }
            }
            completed.TrySetResult();
        }
        catch (OperationCanceledException) { ClearFailedAnalysis(admitted.Generation); completed.TrySetCanceled(); }
        catch (Exception e) { ClearFailedAnalysis(admitted.Generation); completed.TrySetException(e); }
        finally { lock (gate) { if (active == linked) active = null; } linked.Dispose(); }
    }
    void ClearFailedAnalysis(long generation)
    {
        lock (gate) { if (!disposed && state.Generation == generation) InvalidateLocked(); }
    }
    public async Task<CalibrationDraft> PrepareCalibrationAsync(CancellationToken cancellation = default)
    {
        TableAnalysisSnapshot admitted; CancellationTokenSource linked;
        lock (gate)
        {
            ThrowIfDisposed(); cancellation.ThrowIfCancellationRequested();
            if (state.Source is null) throw new InvalidOperationException("Connect to an active camera and become host before calibrating.");
            InvalidateLocked(); state = state with { Picking = true }; admitted = state;
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation); active = linked;
        }
        try
        {
            using var frame = await frames.ReadAsync(admitted.Source!, linked.Token).ConfigureAwait(false);
            var review = frame.ReviewImage();
            await analyzerGate.WaitAsync(linked.Token).ConfigureAwait(false);
            Point[]? proposed;
            try { proposed = await Task.Run(() => analyzer.FindCorners(review), linked.Token).ConfigureAwait(false); }
            finally { analyzerGate.Release(); }
            lock (gate)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (disposed || state.Generation != admitted.Generation || state.Source != admitted.Source || !state.Picking)
                    throw new InvalidOperationException("The camera or calibration changed. Select a fresh table view.");
                state = state with { Jpeg = review };
                return new(state.Generation, review, proposed);
            }
        }
        catch
        {
            lock (gate) { if (!disposed && state.Generation == admitted.Generation) InvalidateLocked(); }
            throw;
        }
        finally { lock (gate) { if (active == linked) active = null; } linked.Dispose(); }
    }
    public void ApplyCalibration(long expectedGeneration, Point[] corners)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (state.Generation != expectedGeneration || !state.Picking || state.Source is null || state.CameraIdentity is null)
                throw new InvalidOperationException("The camera or calibration changed. Select a fresh table view.");
            var calibration = new TableCalibration(corners);
            calibrations?.Save(state.CameraIdentity, calibration.Corners);
            InvalidateLocked(); state = state with { Calibration = calibration, CalibrationId = Guid.NewGuid().ToString("N") };
        }
    }
    public TableState Confirm(long expectedVersion)
    {
        lock (gate)
        {
            CheckEdit(expectedVersion); var table = tracker.Confirm(time.GetUtcNow()); state = state with { Table = table }; return table;
        }
    }
    public Shot[] ComputeChoices(long expectedGeneration, long expectedTableVersion)
    {
        lock (gate)
        {
            if (state.Generation != expectedGeneration) throw new InvalidOperationException("The observation source changed.");
            CheckEdit(expectedTableVersion);
            var confirmed = tracker.Confirm(time.GetUtcNow());
            if (state.ChoicesComputed) return state.Choices;
            var choices = ShotPlanner.Suggest(confirmed, time.GetUtcNow());
            state = state with { Table = confirmed, Choices = choices, ChoicesComputed = true }; return choices;
        }
    }
    public TableState Correct(long expectedVersion, string text)
    {
        lock (gate)
        {
            CheckEdit(expectedVersion);
            if (text.Length > 4096) throw new ArgumentException("Include at most sixteen balls.");
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries); if (lines.Length > 16) throw new ArgumentException("Include at most sixteen balls.");
            var balls = lines.Select((line, i) =>
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 3 || fields[0] is not ("cue" or "red" or "yellow" or "black")) throw new ArgumentException("Each line needs a ball colour and two coordinates.");
                if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) || !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    || !double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 2 || y is < 0 or > 1) throw new ArgumentException("Coordinates must be within the table.");
                return new Ball("manual-" + i, fields[0], new(x, y), 1);
            }).ToArray();
            if (balls.Count(b => b.Kind == "cue") != 1 || balls.Count(b => b.Kind == "black") > 1 || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7)
                throw new ArgumentException("Include one cue ball, at most one black, seven reds and seven yellows.");
            var table = tracker.Correct(balls, time.GetUtcNow()); active?.Cancel();
            state = state with { Table = table, Generation = checked(state.Generation + 1), Observation = null, Choices = [], ChoicesComputed = false }; return table;
        }
    }
    void CheckEdit(long expectedVersion)
    {
        ThrowIfDisposed();
        if (state.Source is null || state.Picking || state.Table?.Version != expectedVersion)
            throw new InvalidOperationException("The layout changed while you were editing. Check the current frame and try again.");
    }
    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose() { lock (gate) { if (disposed) return; InvalidateLocked(); disposed = true; } }
}
