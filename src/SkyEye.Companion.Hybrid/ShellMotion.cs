using SkyEye.Core.Analysis;
using SkyEye.Companion;
using SkyEye.Vision.Android;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    TableMotionFeed? motion;
    RoiMotionProcessor? motionProcessor;
    MotionFeedSnapshot? lastMotion;
    readonly SemaphoreSlim motionGate = new(1, 1);
    public MotionFeedSnapshot? MotionMetrics => motion?.Snapshot ?? lastMotion;

    // Explicit developer/diagnostic enablement. Normal startup never starts a feed.
    // O03/O04 will supply the product observation-session behavior and ROI recentering.
    public Task SetMotionFeedAsync(bool enabled, int hz = 30) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        await motionGate.WaitAsync();
        try
        {
            await CloseMotionAsync(); if (!enabled) return;
            if (hz is not (15 or 30 or 60)) throw new ArgumentException("Unsupported observation cadence.");
            var seed = table.State; var owner = player;
            if (owner is not null) MediaGeneration();
            if (!active || owner is null || EditingTable || !CanAnalyzeTable || seed.Source is null || seed.Calibration is null
                || seed.Table is not { Confirmed: true, Settled: true, Occluded: false } layout || layout.Balls.Length is < 1 or > 16
                || (DateTimeOffset.UtcNow - layout.CapturedAt).TotalSeconds is > 6 or < -2)
                throw new InvalidOperationException("Confirm a clear settled live layout before observing motion.");
            var frames = new PlayerTableFrameSource(() => player, () => app.State.Camera,
                () => active && sessionMode == TableSessionMode.Live && selection.Kind == ViewingSource.Live);
            using (var pixels = await frames.ReadAsync(seed.Source, foreground!.Token))
            {
                var receipt = pixels.DecodedReceipt ?? throw new InvalidOperationException("A paired live frame is required.");
                var rois = layout.Balls.Select((ball, index) =>
                {
                    var point = seed.Calibration.Unmap(ball.Position);
                    int size = Math.Min(128, Math.Min(receipt.Width, receipt.Height));
                    return new PixelRoi("region-" + index, Math.Clamp((int)Math.Round(point.X * receipt.Width) - size / 2, 0, receipt.Width - size),
                        Math.Clamp((int)Math.Round(point.Y * receipt.Height) - size / 2, 0, receipt.Height - size), size, size);
                }).ToArray();
                var scope = new MotionFeedScope(seed.Source, receipt.MediaGeneration, seed.CalibrationId, pixels.Timeline);
                var processor = new RoiMotionProcessor(rois);
                bool Current()
                {
                    var snapshot = table.State;
                    return active && sessionMode == TableSessionMode.Live && !EditingTable && selection.Kind == ViewingSource.Live && player == owner && app.State.Camera.CanControl
                        && app.State.Camera.Scope == scope.Source && snapshot.Generation == seed.Generation && snapshot.CalibrationId == scope.CalibrationId
                        && Interlocked.Read(ref mediaGeneration) == scope.MediaGeneration;
                }
                motionProcessor = processor;
                motion = new(frames, processor, scope, Current, new(hz));
            }
            // Startup's seed lease is gone before producer/worker ownership begins.
            motion.Start(); _ = ReleaseWhenEnded(motion);
        }
        finally { motionGate.Release(); }
    });
    void StopMotion() { motion?.Stop("Viewing/source session ended"); }
    async Task ReleaseWhenEnded(TableMotionFeed feed)
    {
        await feed.Completion;
        await motionGate.WaitAsync();
        try { if (ReferenceEquals(motion, feed)) await CloseMotionAsync(); }
        finally { motionGate.Release(); }
    }
    async Task CloseMotionAsync()
    {
        var previous = motion; var processor = motionProcessor; motion = null; motionProcessor = null;
        if (previous is not null) { await previous.DisposeAsync(); lastMotion = previous.Snapshot; }
        processor?.Dispose();
    }
    void ReleaseMotion()
    {
        StopMotion();
        _ = Finish();
        async Task Finish() { await motionGate.WaitAsync(); try { await CloseMotionAsync(); } finally { motionGate.Release(); } }
    }
}
