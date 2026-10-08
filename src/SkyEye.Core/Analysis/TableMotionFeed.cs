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
    public int AcquisitionAttempts { get; init; } = 1;
    public long? SourceDeltaUs { get; init; }
    public long? FrameIdDelta { get; init; }
    public double? CandidateProbeQueueMs { get; init; }
    public double? CandidateProbeDurationMs { get; init; }
    public long? CandidateFrameId { get; init; }
    public long? CandidateSourceTimeUs { get; init; }
    public long? CandidateToReadFrameIdDelta { get; init; }
    public long? CandidateToReadSourceDeltaUs { get; init; }
    public long? CandidateTextureUpdateCount { get; init; }
    public double? CandidateTextureAgeMs { get; init; }
    public double? ReadMainThreadQueueMs { get; init; }
    public double? BitmapCopyMs { get; init; }
    public long? TextureUpdateCount { get; init; }
    public long? TextureUpdateDelta { get; init; }
    public double? TextureAgeMs { get; init; }
}
public sealed record MotionGapDiagnostic(long FrameId, long SourceTimeUs, long? FrameIdDelta, long SourceDeltaUs,
    int AcquisitionAttempts, double AcquisitionMs, double WorkerMs, double ResultAgeMs)
{
    public double? CandidateProbeQueueMs { get; init; }
    public double? CandidateProbeDurationMs { get; init; }
    public long? CandidateFrameId { get; init; }
    public long? CandidateSourceTimeUs { get; init; }
    public long? CandidateToReadFrameIdDelta { get; init; }
    public long? CandidateToReadSourceDeltaUs { get; init; }
    public long? CandidateTextureUpdateCount { get; init; }
    public double? CandidateTextureAgeMs { get; init; }
    public double? ReadMainThreadQueueMs { get; init; }
    public double? BitmapCopyMs { get; init; }
    public long? TextureUpdateCount { get; init; }
    public long? TextureUpdateDelta { get; init; }
    public double? TextureAgeMs { get; init; }
}
public sealed record RoiMotion(string Id, int Features, int Supported, double? DxPixels, double? DyPixels,
    double? VelocityXPixelsPerSecond, double? VelocityYPixelsPerSecond, string Status);
public sealed record MotionMeasurement(long SourceTimeUs, IReadOnlyList<RoiMotion> Regions, double ConversionMs, double FlowMs)
{
    public int AnalysisImageWidth { get; init; }
    public int AnalysisImageHeight { get; init; }
}
public interface ITableMotionProcessor
{
    Task<MotionMeasurement> MeasureAsync(TableFrame frame, MotionFrameContext context, CancellationToken cancellation);
}
public interface ITableMotionFrameSource : ITableFrameSource
{
    Task<bool> HasNewFrameAsync(MotionFeedScope scope, long afterFrameId, long afterSourceTimeUs, CancellationToken cancellation);
}
public enum MotionFrameCandidateDisposition { New, Unchanged, Unavailable }
public sealed record MotionFrameCandidateProbe(MotionFrameCandidateDisposition Disposition, string Reason,
    long? FrameId = null, long? SourceTimeUs = null)
{
    public double? MainThreadQueueMs { get; init; }
    public double? ProbeDurationMs { get; init; }
    public long? TextureUpdateCount { get; init; }
    public double? TextureAgeMs { get; init; }
}
public interface ITableMotionFrameProbe
{
    Task<MotionFrameCandidateProbe> ProbeNewCandidateAsync(MotionFeedScope scope, long afterFrameId,
        long afterSourceTimeUs, CancellationToken cancellation);
}
public sealed record MotionFrameCandidateRead(MotionFrameCandidateProbe Probe, TableFrame? Frame);
public interface ITableMotionFrameCandidateReader
{
    /// <summary>Checks and copies a new candidate in one source-thread dispatch.</summary>
    /// <param name="beforeReadback">Called only for a new candidate, immediately before pixel allocation.</param>
    Task<MotionFrameCandidateRead> ReadNewCandidateAsync(MotionFeedScope scope, long afterFrameId,
        long afterSourceTimeUs, Action beforeReadback, CancellationToken cancellation);
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
    public long GapThresholdUs { get; init; }
    public long? LastProcessedDeltaUs { get; init; }
    public long? LastGapUs { get; init; }
    public long? LargestGapUs { get; init; }
    public int RecentDeltaSampleCount { get; init; }
    public long? RecentDeltaP50Us { get; init; }
    public long? RecentDeltaP95Us { get; init; }
    public long? RecentDeltaMaxUs { get; init; }
    public long? LastProcessedFrameIdDelta { get; init; }
    public long? LastGapFrameIdDelta { get; init; }
    public double? LastClockSampleAgeMs { get; init; }
    public double? LastClockUncertaintyMs { get; init; }
    public long AcquisitionRetries { get; init; }
    public int LastAcquisitionAttempts { get; init; } = 1;
    public long NoNewFramePolls { get; init; }
    public long UnavailableCandidatePolls { get; init; }
    public IReadOnlyDictionary<string, long> UnavailableCandidateReasons { get; init; } = new Dictionary<string, long>();
    public double? LastCandidateProbeQueueMs { get; init; }
    public double? LastCandidateProbeDurationMs { get; init; }
    public long? LastCandidateFrameId { get; init; }
    public long? LastCandidateSourceTimeUs { get; init; }
    public long? LastCandidateTextureUpdateCount { get; init; }
    public double? LastCandidateTextureAgeMs { get; init; }
    public double? LastReadMainThreadQueueMs { get; init; }
    public double? LastBitmapCopyMs { get; init; }
    public long? LastAcquisitionCandidateFrameId { get; init; }
    public long? LastAcquisitionCandidateSourceTimeUs { get; init; }
    public long? LastReadFrameId { get; init; }
    public long? LastReadSourceTimeUs { get; init; }
    public long? LastCandidateToReadFrameIdDelta { get; init; }
    public long? LastCandidateToReadSourceDeltaUs { get; init; }
    public long? LastReadTextureUpdateCount { get; init; }
    public double? LastReadTextureAgeMs { get; init; }
    public long? LastProcessedTextureUpdateDelta { get; init; }
    public IReadOnlyList<MotionGapDiagnostic> RecentGaps { get; init; } = Array.Empty<MotionGapDiagnostic>();
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
    readonly Queue<MotionGapDiagnostic> recentGaps = new();
    readonly Queue<long> recentProcessedSourceTimes = new();
    readonly Queue<long> recentProcessedDeltasUs = new();
    TableFrame? pending;
    MotionFrameCandidateProbe? pendingProbe;
    Task? run;
    long acquired, processed, replaced, duplicate, noNewFramePolls, unavailableCandidatePolls, stale, acquisitionFailures, scopeRejected, workerFailures, gaps;
    readonly Dictionary<string, long> unavailableCandidateReasons = new(StringComparer.Ordinal);
    long acquisitionRetries;
    long lastReceivedId = -1, lastReceivedUs = -1, lastProcessedId = -1, lastProcessedUs = -1;
    long? lastProcessedDeltaUs, lastGapUs;
    long? lastProcessedFrameIdDelta, lastGapFrameIdDelta;
    long? lastProcessedTextureUpdateCount, lastProcessedTextureUpdateDelta;
    double? lastClockSampleAgeMs, lastClockUncertaintyMs;
    double? lastCandidateProbeQueueMs, lastCandidateProbeDurationMs, lastCandidateTextureAgeMs;
    long? lastCandidateFrameId, lastCandidateSourceTimeUs, lastCandidateTextureUpdateCount;
    double? lastReadMainThreadQueueMs, lastBitmapCopyMs, lastReadTextureAgeMs;
    long? lastReadFrameId, lastReadSourceTimeUs, lastReadTextureUpdateCount;
    long? lastAcquisitionCandidateFrameId, lastAcquisitionCandidateSourceTimeUs;
    long? lastCandidateToReadFrameIdDelta, lastCandidateToReadSourceDeltaUs;
    long largestGapUs;
    int active, width, height, lastAcquisitionAttempts = 1, pendingAcquisitionAttempts = 1;
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
                    ProcessedSourceCadenceHz = cadence,
                    GapThresholdUs = options.MaximumGapUs,
                    LastProcessedDeltaUs = lastProcessedDeltaUs,
                    LastGapUs = lastGapUs,
                    LargestGapUs = largestGapUs == 0 ? null : largestGapUs,
                    RecentDeltaSampleCount = recentProcessedDeltasUs.Count,
                    RecentDeltaP50Us = Percentile(recentProcessedDeltasUs, 0.50),
                    RecentDeltaP95Us = Percentile(recentProcessedDeltasUs, 0.95),
                    RecentDeltaMaxUs = recentProcessedDeltasUs.Count == 0 ? null : recentProcessedDeltasUs.Max(),
                    LastProcessedFrameIdDelta = lastProcessedFrameIdDelta,
                    LastGapFrameIdDelta = lastGapFrameIdDelta,
                    LastClockSampleAgeMs = lastClockSampleAgeMs,
                    LastClockUncertaintyMs = lastClockUncertaintyMs,
                    AcquisitionRetries = acquisitionRetries,
                    LastAcquisitionAttempts = lastAcquisitionAttempts,
                    NoNewFramePolls = noNewFramePolls,
                    UnavailableCandidatePolls = unavailableCandidatePolls,
                    UnavailableCandidateReasons = new Dictionary<string, long>(unavailableCandidateReasons),
                    LastCandidateProbeQueueMs = lastCandidateProbeQueueMs,
                    LastCandidateProbeDurationMs = lastCandidateProbeDurationMs,
                    LastCandidateFrameId = lastCandidateFrameId,
                    LastCandidateSourceTimeUs = lastCandidateSourceTimeUs,
                    LastCandidateTextureUpdateCount = lastCandidateTextureUpdateCount,
                    LastCandidateTextureAgeMs = lastCandidateTextureAgeMs,
                    LastReadMainThreadQueueMs = lastReadMainThreadQueueMs,
                    LastBitmapCopyMs = lastBitmapCopyMs,
                    LastAcquisitionCandidateFrameId = lastAcquisitionCandidateFrameId,
                    LastAcquisitionCandidateSourceTimeUs = lastAcquisitionCandidateSourceTimeUs,
                    LastReadFrameId = lastReadFrameId,
                    LastReadSourceTimeUs = lastReadSourceTimeUs,
                    LastCandidateToReadFrameIdDelta = lastCandidateToReadFrameIdDelta,
                    LastCandidateToReadSourceDeltaUs = lastCandidateToReadSourceDeltaUs,
                    LastReadTextureUpdateCount = lastReadTextureUpdateCount,
                    LastReadTextureAgeMs = lastReadTextureAgeMs,
                    LastProcessedTextureUpdateDelta = lastProcessedTextureUpdateDelta,
                    RecentGaps = recentGaps.ToArray()
                };
            }
        }
    }
    static long? Percentile(IEnumerable<long> samples, double percentile)
    {
        var ordered = samples.Order().ToArray();
        if (ordered.Length == 0) return null;
        int index = Math.Clamp((int)Math.Ceiling(percentile * ordered.Length) - 1, 0, ordered.Length - 1);
        return ordered[index];
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
        lock (gate) { stopReason ??= reason; latest = null; discard = pending; pending = null; pendingProbe = null; }
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
                MotionFrameCandidateProbe? candidateProbe = null;
                TableFrame? frame = null;
                double? coalescedAcquisitionMs = null;
                // A decoder can be polled faster than its source. Keep a valid
                // pending frame when the native source has no newer candidate.
                try
                {
                    if (source is ITableMotionFrameCandidateReader candidateReader)
                    {
                        var coalescedClock = Stopwatch.StartNew();
                        var candidateRead = await candidateReader.ReadNewCandidateAsync(scope, lastReceivedId, lastReceivedUs,
                            DropPendingBeforeReadback, stopped.Token).ConfigureAwait(false);
                        coalescedClock.Stop();
                        coalescedAcquisitionMs = coalescedClock.Elapsed.TotalMilliseconds;
                        RecordCandidateProbe(candidateRead.Probe);
                        if (!CandidateIsNew(candidateRead.Probe)) continue;
                        candidateProbe = candidateRead.Probe;
                        frame = candidateRead.Frame ?? throw new IOException("New candidate had no paired image.");
                    }
                    else if (source is ITableMotionFrameProbe detailedProbe)
                    {
                        var probe = await detailedProbe.ProbeNewCandidateAsync(scope, lastReceivedId, lastReceivedUs, stopped.Token).ConfigureAwait(false);
                        RecordCandidateProbe(probe);
                        if (!CandidateIsNew(probe)) continue;
                        candidateProbe = probe;
                    }
                    else if (source is ITableMotionFrameSource peek)
                    {
                        if (!await peek.HasNewFrameAsync(scope, lastReceivedId, lastReceivedUs, stopped.Token).ConfigureAwait(false))
                        { lock (gate) noNewFramePolls++; continue; }
                    }
                }
                catch (OperationCanceledException) when (stopped.IsCancellationRequested) { break; }
                catch (FrameAcquisitionException error)
                {
                    if (error.Reason == ObservationReason.SourceChanged)
                    {
                        lock (gate) scopeRejected++;
                        Stop("Candidate/source changed: " + error.Code);
                        break;
                    }
                    // A paired bitmap can be refused while the live decoder is
                    // still advancing. Count the miss and retry on the next tick.
                    lock (gate) acquisitionFailures++;
                    continue;
                }
                catch (IOException)
                {
                    lock (gate) acquisitionFailures++;
                    continue;
                }
                // Coalesced sources dispose pending inside the same callback that
                // immediately allocates its bitmap. Other sources dispose it here.
                if (frame is null) DropPendingBeforeReadback();
                try
                {
                    var clock = Stopwatch.StartNew();
                    frame ??= await source.ReadAsync(scope.Source, stopped.Token).ConfigureAwait(false);
                    clock.Stop();
                    var measuredAcquisitionMs = coalescedAcquisitionMs is { } coalescedMs
                        ? coalescedMs + clock.Elapsed.TotalMilliseconds : clock.Elapsed.TotalMilliseconds;
                    bool rejectScope;
                    lock (gate)
                    {
                        acquired++; acquisitionMs = measuredAcquisitionMs;
                        lastAcquisitionAttempts = Math.Clamp(frame.AcquisitionAttempts, 1, 16);
                        acquisitionRetries += lastAcquisitionAttempts - 1;
                        lastReadMainThreadQueueMs = frame.MainThreadQueueMs;
                        lastBitmapCopyMs = frame.BitmapCopyMs;
                        lastReadFrameId = frame.DecodedReceipt?.FrameId;
                        lastReadSourceTimeUs = frame.SourceTimeUs;
                        lastCandidateToReadFrameIdDelta = candidateProbe?.FrameId is { } candidateId && frame.DecodedReceipt is { } readReceipt
                            ? readReceipt.FrameId - candidateId : null;
                        lastCandidateToReadSourceDeltaUs = candidateProbe?.SourceTimeUs is { } candidateSourceUs && frame.SourceTimeUs is { } readSourceUs
                            ? readSourceUs - candidateSourceUs : null;
                        lastAcquisitionCandidateFrameId = candidateProbe?.FrameId;
                        lastAcquisitionCandidateSourceTimeUs = candidateProbe?.SourceTimeUs;
                        lastReadTextureUpdateCount = frame.TextureUpdateCount;
                        lastReadTextureAgeMs = frame.TextureAgeMs;
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
                            pending = frame; pendingProbe = candidateProbe; pendingAcquisitionMs = acquisitionMs; frame = null;
                            pendingAcquisitionAttempts = lastAcquisitionAttempts;
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
    void RecordCandidateProbe(MotionFrameCandidateProbe probe)
    {
        lock (gate)
        {
            lastCandidateProbeQueueMs = probe.MainThreadQueueMs;
            lastCandidateProbeDurationMs = probe.ProbeDurationMs;
            lastCandidateFrameId = probe.FrameId;
            lastCandidateSourceTimeUs = probe.SourceTimeUs;
            lastCandidateTextureUpdateCount = probe.TextureUpdateCount;
            lastCandidateTextureAgeMs = probe.TextureAgeMs;
        }
    }
    bool CandidateIsNew(MotionFrameCandidateProbe probe)
    {
        if (probe.Disposition == MotionFrameCandidateDisposition.New) return true;
        lock (gate)
        {
            if (probe.Disposition == MotionFrameCandidateDisposition.Unchanged) noNewFramePolls++;
            else
            {
                unavailableCandidatePolls++;
                var reason = probe.Reason is "not-playing" or "no-surface" or "unmapped-surface" or "unmapped-source-clock"
                    ? probe.Reason : "unknown";
                unavailableCandidateReasons[reason] = unavailableCandidateReasons.GetValueOrDefault(reason) + 1;
            }
        }
        return false;
    }
    void DropPendingBeforeReadback()
    {
        TableFrame? discard;
        lock (gate) { discard = pending; pending = null; pendingProbe = null; if (discard is not null) replaced++; }
        discard?.Dispose();
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
                TableFrame? frame; MotionFrameCandidateProbe? candidateProbe; MotionFrameContext? context;
                lock (gate)
                {
                    frame = pending; pending = null; candidateProbe = pendingProbe; pendingProbe = null; context = null;
                    if (frame is not null)
                    {
                        active = 1;
                        long? sourceDeltaUs = lastProcessedUs < 0 ? null : frame.SourceTimeUs!.Value - lastProcessedUs;
                        long? frameIdDelta = lastProcessedId < 0 ? null : frame.DecodedReceipt!.FrameId - lastProcessedId;
                        long? textureUpdateDelta = frame.TextureUpdateCount is { } updateCount && lastProcessedTextureUpdateCount is { } previousUpdateCount
                            ? updateCount - previousUpdateCount : null;
                        bool reset = sourceDeltaUs is null || sourceDeltaUs.Value > options.MaximumGapUs;
                        if (reset && sourceDeltaUs is { } gapUs)
                        {
                            gaps++;
                            lastGapUs = gapUs;
                            lastGapFrameIdDelta = frameIdDelta;
                            largestGapUs = Math.Max(largestGapUs, gapUs);
                        }
                        context = new(scope, frame.DecodedReceipt!.FrameId, frame.SourceTimeUs!.Value, reset,
                            reset ? null : sourceDeltaUs!.Value / 1000000d)
                        { AcquisitionMs = pendingAcquisitionMs, AcquisitionAttempts = pendingAcquisitionAttempts,
                            SourceDeltaUs = sourceDeltaUs, FrameIdDelta = frameIdDelta,
                            CandidateProbeQueueMs = candidateProbe?.MainThreadQueueMs,
                            CandidateProbeDurationMs = candidateProbe?.ProbeDurationMs,
                            CandidateFrameId = candidateProbe?.FrameId,
                            CandidateSourceTimeUs = candidateProbe?.SourceTimeUs,
                            CandidateToReadFrameIdDelta = candidateProbe?.FrameId is { } candidateFrameId
                                ? frame.DecodedReceipt!.FrameId - candidateFrameId : null,
                            CandidateToReadSourceDeltaUs = candidateProbe?.SourceTimeUs is { } candidateSourceTimeUs
                                ? frame.SourceTimeUs!.Value - candidateSourceTimeUs : null,
                            CandidateTextureUpdateCount = candidateProbe?.TextureUpdateCount,
                            CandidateTextureAgeMs = candidateProbe?.TextureAgeMs,
                            ReadMainThreadQueueMs = frame.MainThreadQueueMs, BitmapCopyMs = frame.BitmapCopyMs,
                            TextureUpdateCount = frame.TextureUpdateCount, TextureUpdateDelta = textureUpdateDelta,
                            TextureAgeMs = frame.TextureAgeMs };
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
                        processed++; lastProcessedId = context.FrameId; lastProcessedUs = context.SourceTimeUs; latest = result;
                        if (frame.TextureUpdateCount is { } updateCount) lastProcessedTextureUpdateCount = updateCount;
                        lastProcessedTextureUpdateDelta = context.TextureUpdateDelta;
                        lastProcessedFrameIdDelta = context.FrameIdDelta;
                        lastClockSampleAgeMs = frame.ClockSampleAgeMs;
                        lastClockUncertaintyMs = frame.ClockUncertaintyMs;
                        if (context.SourceDeltaUs is { } sourceDeltaUs)
                        {
                            lastProcessedDeltaUs = sourceDeltaUs;
                            recentProcessedDeltasUs.Enqueue(sourceDeltaUs);
                            while (recentProcessedDeltasUs.Count > 601) recentProcessedDeltasUs.Dequeue();
                        }
                        recentProcessedSourceTimes.Enqueue(context.SourceTimeUs);
                        while (recentProcessedSourceTimes.Count > 1
                            && (recentProcessedSourceTimes.Count > 601
                                || context.SourceTimeUs - recentProcessedSourceTimes.Peek() > 10_000_000))
                            recentProcessedSourceTimes.Dequeue();
                        workerMs = clock.Elapsed.TotalMilliseconds; resultAgeMs = (time.GetUtcNow() - frame.CapturedAt).TotalMilliseconds;
                        if (context.SourceDeltaUs is { } gapUs && gapUs > options.MaximumGapUs)
                        {
                            recentGaps.Enqueue(new(context.FrameId, context.SourceTimeUs, context.FrameIdDelta, gapUs,
                                context.AcquisitionAttempts, context.AcquisitionMs, workerMs, resultAgeMs)
                            {
                                CandidateProbeQueueMs = context.CandidateProbeQueueMs,
                                CandidateProbeDurationMs = context.CandidateProbeDurationMs,
                                CandidateFrameId = context.CandidateFrameId,
                                CandidateSourceTimeUs = context.CandidateSourceTimeUs,
                                CandidateToReadFrameIdDelta = context.CandidateToReadFrameIdDelta,
                                CandidateToReadSourceDeltaUs = context.CandidateToReadSourceDeltaUs,
                                CandidateTextureUpdateCount = context.CandidateTextureUpdateCount,
                                CandidateTextureAgeMs = context.CandidateTextureAgeMs,
                                ReadMainThreadQueueMs = context.ReadMainThreadQueueMs,
                                BitmapCopyMs = context.BitmapCopyMs,
                                TextureUpdateCount = context.TextureUpdateCount,
                                TextureUpdateDelta = context.TextureUpdateDelta,
                                TextureAgeMs = context.TextureAgeMs
                            });
                            while (recentGaps.Count > 64) recentGaps.Dequeue();
                        }
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
