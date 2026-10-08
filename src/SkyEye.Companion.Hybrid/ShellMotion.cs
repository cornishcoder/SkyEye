using SkyEye.Core.Analysis;
using SkyEye.Core;
using SkyEye.Companion;
using SkyEye.Vision.Android;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    TableMotionFeed? motion;
    RoiMotionProcessor? motionProcessor;
    MotionFeedSnapshot? lastMotion;
    string motionSeedSource = "unknown";
    long? motionSeedFrameId;
    double? motionSeedFrameAgeMs;
    readonly SemaphoreSlim motionGate = new(1, 1);
    public MotionFeedSnapshot? MotionMetrics
    {
        get
        {
            var snapshot = motion?.Snapshot ?? lastMotion;
            return snapshot is null ? null : snapshot with
            { SeedSource = motionSeedSource, SeedFrameId = motionSeedFrameId, SeedFrameAgeMs = motionSeedFrameAgeMs };
        }
    }

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
            if (!active || owner is null || EditingTable || !CanAnalyzeTable || seed.Source is null || seed.Calibration is null)
                throw new InvalidOperationException("Open the active calibrated live table before observing motion.");
            if (seed.Table is not { Confirmed: true, Settled: true, Occluded: false } layout || layout.Balls.Length is < 1 or > 16)
                throw new InvalidOperationException("Confirm a clear, settled live layout before observing motion.");
            Ball[] roiBalls = layout.Balls;
            var layoutAgeMs = (DateTimeOffset.UtcNow - layout.CapturedAt).TotalMilliseconds;
            long? seedFrameId = seed.Observation?.DecodedReceipt?.FrameId;
            string seedSource = "confirmed-table-layout";
            VisionDiagnosticSample? diagnosticSeed = null;
            if (!double.IsFinite(layoutAgeMs) || layoutAgeMs is < -200 or > ObservationPolicy.ReviewFreshnessSeconds * 1000)
            {
                var diagnostic = table.VisionDiagnostics;
                diagnosticSeed = diagnostic.Active ? diagnostic.LiveFrame?.Sample : null;
                var detectorAgeMs = diagnosticSeed is null ? double.PositiveInfinity : (DateTimeOffset.UtcNow - diagnosticSeed.CapturedAt).TotalMilliseconds;
                bool sameScope = diagnosticSeed?.Source is { } evidence
                    && evidence.Camera == seed.CameraIdentity
                    && evidence.Calibration == seed.CalibrationId
                    && evidence.Connection == seed.Source.ConnectionId.ToString("N");
                bool matchesConfirmed = diagnosticSeed is { } sample
                    && !sample.Detection.Uncertain && !sample.Detection.Obstructed
                    && sample.Detection.Balls.Length is > 0 and <= 16
                    && TableTracker.SameLayout(layout.Balls, sample.Detection.Balls);
                if (!sameScope || !double.IsFinite(detectorAgeMs) || detectorAgeMs is < -200 or > ObservationPolicy.ReviewFreshnessSeconds * 1000 || !matchesConfirmed)
                    throw new InvalidOperationException("The confirmed layout is stale and the latest clear detector sample does not match it. Confirm the current balls again.");
                roiBalls = diagnosticSeed!.Detection.Balls;
                seedFrameId = diagnosticSeed.FrameId;
                layoutAgeMs = detectorAgeMs;
                seedSource = "fresh-detector-matched-to-confirmed-layout";
            }
            var frames = new PlayerTableFrameSource(() => player, () => app.State.Camera,
                () => active && sessionMode == TableSessionMode.Live && selection.Kind == ViewingSource.Live,
                decodedReadbackScale: 2);
            using (var pixels = await frames.ReadAsync(seed.Source, foreground!.Token))
            {
                var receipt = pixels.DecodedReceipt ?? throw new InvalidOperationException("A paired live frame is required.");
                if (diagnosticSeed is { } sample && (sample.MediaGeneration != receipt.MediaGeneration || sample.Source?.Timeline != pixels.Timeline
                    || (DateTimeOffset.UtcNow - sample.CapturedAt).TotalMilliseconds is < -200 or > ObservationPolicy.ReviewFreshnessSeconds * 1000))
                    throw new InvalidOperationException("The matched detector sample no longer belongs to the current live video frame.");
                var image = pixels.Image as BitmapTableImage ?? throw new InvalidOperationException("A decoded motion image is required.");
                int imageWidth = pixels.ImageWidth, imageHeight = pixels.ImageHeight;
                if (imageWidth != image.Bitmap.Width || imageHeight != image.Bitmap.Height)
                    throw new InvalidDataException("Motion readback dimensions do not match the decoded image.");
                double sourcePixelsPerImagePixelX = receipt.Width / (double)imageWidth;
                double sourcePixelsPerImagePixelY = receipt.Height / (double)imageHeight;
                var rois = roiBalls.Select((ball, index) =>
                {
                    var point = seed.Calibration.Unmap(ball.Position);
                    int sourceSize = Math.Min(128, Math.Min(receipt.Width, receipt.Height));
                    int width = Math.Clamp((int)Math.Round(sourceSize / sourcePixelsPerImagePixelX), 32, 256);
                    int height = Math.Clamp((int)Math.Round(sourceSize / sourcePixelsPerImagePixelY), 32, 256);
                    return new PixelRoi("region-" + index,
                        Math.Clamp((int)Math.Round(point.X * imageWidth) - width / 2, 0, imageWidth - width),
                        Math.Clamp((int)Math.Round(point.Y * imageHeight) - height / 2, 0, imageHeight - height), width, height);
                }).ToArray();
                var scope = new MotionFeedScope(seed.Source, receipt.MediaGeneration, seed.CalibrationId, pixels.Timeline);
                var processor = new RoiMotionProcessor(rois, sourcePixelsPerImagePixelX, sourcePixelsPerImagePixelY);
                bool Current()
                {
                    var snapshot = table.State;
                    return active && sessionMode == TableSessionMode.Live && !EditingTable && selection.Kind == ViewingSource.Live && player == owner && app.State.Camera.CanControl
                        && app.State.Camera.Scope == scope.Source && snapshot.Generation == seed.Generation && snapshot.CalibrationId == scope.CalibrationId
                        && Interlocked.Read(ref mediaGeneration) == scope.MediaGeneration;
                }
                motionProcessor = processor;
                motionSeedSource = seedSource;
                motionSeedFrameId = seedFrameId;
                motionSeedFrameAgeMs = layoutAgeMs;
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
