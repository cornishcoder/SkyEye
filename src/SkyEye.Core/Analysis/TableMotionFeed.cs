using System.Diagnostics;
using SkyEye.Core.Application;

namespace SkyEye.Core.Analysis;

public sealed record MotionFeedScope(CameraScope Source, long MediaGeneration, string CalibrationId, string Timeline);
public sealed record MotionFeedOptions(int FramesPerSecond = 30, long MaximumGapUs = 125000, double MaximumAgeMs = 2000)
{
    internal void Validate()
    {
        if (FramesPerSecond is not (15 or 30 or 60) || MaximumGapUs is < 1000 or > 1000000
            || !double.IsFinite(MaximumAgeMs) || MaximumAgeMs is < 100 or > 6000) throw new ArgumentException("Invalid motion-feed bounds.");
    }
}
public sealed record MotionFrameContext(MotionFeedScope Scope, long FrameId, long SourceTimeUs, bool Reset, double? DeltaSeconds)
{
    public double AcquisitionMs { get; init; }
}
public sealed record RoiMotion(string Id, int Features, int Supported, double? DxPixels, double? DyPixels,
    double? VelocityXPixelsPerSecond, double? VelocityYPixelsPerSecond, string Status);
public sealed record MotionMeasurement(long SourceTimeUs, IReadOnlyList<RoiMotion> Regions, double ConversionMs, double FlowMs);
public interface ITableMotionProcessor
{
    Task<MotionMeasurement> MeasureAsync(TableFrame frame, MotionFrameContext context, CancellationToken cancellation);
}
public interface ITableMotionFrameSource : ITableFrameSource
{
    Task<bool> HasNewFrameAsync(MotionFeedScope scope, long afterFrameId, long afterSourceTimeUs, CancellationToken cancellation);
}
public sealed record MotionFeedSnapshot(long Acquired, long Processed, long Replaced, long DuplicateOrBackwards, long Stale,
    long AcquisitionFailures, long ScopeRejected, long WorkerFailures, long GapResets, int Active, int Pending,
    string? StopReason, MotionMeasurement? Latest, double LastAcquisitionMs, double LastWorkerMs, double LastResultAgeMs)
{
    public int RequestedHz { get; init; }
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public int CadenceSampleCount { get; init; }
    public double? ProcessedSourceCadenceHz { get; init; }
    public string SeedSource { get; init; } = "unknown";
    public long? SeedFrameId { get; init; }
    public double? SeedFrameAgeMs { get; init; }
}

/// <summary>Explicitly started, one active/one latest frame. No detector, rule or event submission.</summary>
public sealed class TableMotionFeed : IAsyncDisposable
{
    readonly object gate = new();
    readonly ITableFrameSource source;
    readonly ITableMotionProcessor processor;
    readonly MotionFeedScope scope;
    readonly MotionFeedOptions options;
    readonly Func<bool> current;
    readonly TimeProvider time;
    readonly CancellationTokenSource stopped = new();
    readonly SemaphoreSlim wake = new(0, 1);
    readonly Queue<long> recentProcessedSourceTimes = new();
    TableFrame? pending;
    Task? run;
    long acquired, processed, replaced, duplicate, stale, acquisitionFailures, scopeRejected, workerFailures, gaps;
    long lastReceivedId = -1, lastReceivedUs = -1, lastProcessedUs = -1;
    int active, width, height;
    string? stopReason;
    MotionMeasurement? latest;
    double acquisitionMs, pendingAcquisitionMs, workerMs, resultAgeMs;

    public TableMotionFeed(ITableFrameSource source, ITableMotionProcessor processor, MotionFeedScope scope,
        Func<bool> current, MotionFeedOptions? options = null, TimeProvider? time = null)
    {
        this.source = source; this.processor = processor; this.scope = scope; this.current = current;
        this.options = options ?? new(); this.options.Validate(); this.time = time ?? TimeProvider.System;
        if (scope.Source is null || scope.MediaGeneration <= 0 || string.IsNullOrWhiteSpace(scope.CalibrationId)
            || string.IsNullOrWhiteSpace(scope.Timeline)) throw new ArgumentException("Motion requires a frozen source/calibration scope.");
    }
    public MotionFeedSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                var cadenceSamples = recentProcessedSourceTimes.Count;
                double? cadence = null;
                if (cadenceSamples > 1)
                {
                    var first = recentProcessedSourceTimes.Peek();
                    var last = recentProcessedSourceTimes.Last();
                    if (last > first) cadence = (cadenceSamples - 1) * 1_000_000d / (last - first);
                }
                return new(acquired, processed, replaced, duplicate, stale, acquisitionFailures, scopeRejected,
                    workerFailures, gaps, active, pending is null ? 0 : 1, stopReason, latest, acquisitionMs, workerMs, resultAgeMs)
                {
                    RequestedHz = options.FramesPerSecond,
                    FrameWidth = width,
                    FrameHeight = height,
                    CadenceSampleCount = cadenceSamples,
                    ProcessedSourceCadenceHz = cadence
                };
            }
        }
    }
    public Task Completion { get { lock (gate) return run ?? Task.CompletedTask; } }
    public void Start()
    {
        lock (gate)
        {
            if (run is not null || stopped.IsCancellationRequested) throw new InvalidOperationException("Create a new scoped feed to restart.");
            run = Task.Run(RunAsync);
        }
    }
    public void Stop(string reason = "Stopped")
    {
        TableFrame? discard;
        lock (gate) { stopReason ??= reason; latest = null; discard = pending; pending = null; }
        stopped.Cancel(); discard?.Dispose();
    }
    public async Task StopAsync(string reason = "Stopped") { Stop(reason); await Completion.ConfigureAwait(false); }
    bool Current()
    {
        try { return !stopped.IsCancellationRequested && Volatile.Read(ref stopReason) is null && current(); } catch { return false; }
    }
    async Task RunAsync()
    {
        try { await Task.WhenAll(ProduceAsync(), ConsumeAsync()).ConfigureAwait(false); }
        finally { Stop(stopReason ?? "Stopped"); }
    }
    async Task ProduceAsync()
    {
        using var cadence = new PeriodicTimer(TimeSpan.FromSeconds(1d / options.FramesPerSecond), time);
        try
        {
            do
            {
                if (!Current()) { Stop("Source, calibration or enablement changed"); break; }
                // A decoder can be polled faster than its source. Keep a valid
                // pending frame when the native source has no newer candidate.
                if (source is ITableMotionFrameSource peek)
                {
                    if (!await peek.HasNewFrameAsync(scope, lastReceivedId, lastReceivedUs, stopped.Token).ConfigureAwait(false))
                    { lock (gate) duplicate++; continue; }
                }
                // Dispose the previous pending frame BEFORE allocating another copy.
                // Even transient native ownership therefore stays at active + acquisition/latest.
                TableFrame? discard;
                lock (gate) { discard = pending; pending = null; if (discard is not null) replaced++; }
                discard?.Dispose();
                TableFrame? frame = null;
                try
                {
                    var clock = Stopwatch.StartNew();
                    frame = await source.ReadAsync(scope.Source, stopped.Token).ConfigureAwait(false);
                    clock.Stop();
                    bool rejectScope;
                    lock (gate)
                    {
                        acquired++; acquisitionMs = clock.Elapsed.TotalMilliseconds;
                        var receipt = frame.DecodedReceipt;
                        rejectScope = !Current() || receipt is null || frame.Image is null || frame.SourceTimeUs is null or < 0
                            || frame.Timeline != scope.Timeline || receipt.MediaGeneration != scope.MediaGeneration
                            || receipt.FrameId <= 0 || receipt.Width <= 0 || receipt.Height <= 0
                            || width != 0 && (receipt.Width != width || receipt.Height != height);
                        if (rejectScope) { scopeRejected++; stopReason ??= "Decoded/source scope changed"; latest = null; }
                        else if (receipt!.FrameId <= lastReceivedId || frame.SourceTimeUs!.Value <= lastReceivedUs) duplicate++;
                        else if (!Fresh(frame)) stale++;
                        else
                        {
                            width = receipt.Width; height = receipt.Height;
                            lastReceivedId = receipt.FrameId; lastReceivedUs = frame.SourceTimeUs!.Value;
                            pending = frame; pendingAcquisitionMs = acquisitionMs; frame = null;
                            if (wake.CurrentCount == 0) wake.Release();
                        }
                    }
                    if (rejectScope) { Stop("Decoded/source scope changed"); break; }
                }
                catch (OperationCanceledException) when (stopped.IsCancellationRequested) { break; }
                catch (IOException) { lock (gate) acquisitionFailures++; }
                catch (Exception) { lock (gate) scopeRejected++; Stop("Acquisition/source failed"); break; }
                finally { frame?.Dispose(); }
            } while (await cadence.WaitForNextTickAsync(stopped.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
        catch (Exception) { lock (gate) scopeRejected++; Stop("Candidate/source failed"); }
        finally { Stop(stopReason ?? "Acquisition ended"); }
    }
    bool Fresh(TableFrame frame)
    {
        double age = (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds;
        return double.IsFinite(age) && age >= -250 && age <= options.MaximumAgeMs;
    }
    async Task ConsumeAsync()
    {
        try
        {
            while (true)
            {
                await wake.WaitAsync(stopped.Token).ConfigureAwait(false);
                TableFrame? frame; MotionFrameContext? context;
                lock (gate)
                {
                    frame = pending; pending = null; context = null;
                    if (frame is not null)
                    {
                        active = 1;
                        bool reset = lastProcessedUs < 0 || frame.SourceTimeUs!.Value - lastProcessedUs > options.MaximumGapUs;
                        if (reset && lastProcessedUs >= 0) gaps++;
                        context = new(scope, frame.DecodedReceipt!.FrameId, frame.SourceTimeUs!.Value, reset,
                            reset ? null : (frame.SourceTimeUs.Value - lastProcessedUs) / 1000000d) { AcquisitionMs = pendingAcquisitionMs };
                    }
                }
                if (frame is null) continue;
                try
                {
                    if (!Current()) { Stop("Source, calibration or enablement changed"); break; }
                    if (!Fresh(frame)) { lock (gate) stale++; continue; }
                    var clock = Stopwatch.StartNew();
                    var result = await processor.MeasureAsync(frame, context!, stopped.Token).ConfigureAwait(false);
                    clock.Stop();
                    lock (gate)
                    {
                        if (!Current()) continue;
                        if (!Fresh(frame)) { stale++; lastProcessedUs = -1; continue; }
                        if (result.SourceTimeUs != context!.SourceTimeUs) throw new InvalidDataException("Worker result lost source time.");
                        if (context.Reset) recentProcessedSourceTimes.Clear();
                        processed++; lastProcessedUs = context.SourceTimeUs; latest = result;
                        recentProcessedSourceTimes.Enqueue(context.SourceTimeUs);
                        while (recentProcessedSourceTimes.Count > 1
                            && (recentProcessedSourceTimes.Count > 601
                                || context.SourceTimeUs - recentProcessedSourceTimes.Peek() > 10_000_000))
                            recentProcessedSourceTimes.Dequeue();
                        workerMs = clock.Elapsed.TotalMilliseconds; resultAgeMs = (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds;
                    }
                }
                catch (OperationCanceledException) when (stopped.IsCancellationRequested) { break; }
                catch (Exception) { lock (gate) workerFailures++; Stop("Motion worker failed"); break; }
                finally { frame.Dispose(); lock (gate) active = 0; }
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); wake.Dispose(); stopped.Dispose(); }
}
