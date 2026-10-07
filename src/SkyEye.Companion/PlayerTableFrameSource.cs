using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Vision.Android;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

public sealed class PlayerTableFrameSource(Func<Bridge.PlayerBridge?> player, Func<CameraConnection> connection, Func<bool> live) : ITableMotionFrameSource
{
    readonly global::Android.OS.Handler main = new(global::Android.OS.Looper.MainLooper!);
    public Task<bool> HasNewFrameAsync(MotionFeedScope scope, long afterFrameId, long afterSourceTimeUs, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Post(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                if (player() is not { } owner || !live() || connection().Scope != scope.Source || !connection().CanControl)
                    throw new InvalidOperationException("Motion source changed.");
                var json = owner.DecodedFrameCandidate();
                if (json is null) { completion.TrySetResult(false); return; }
                using var candidate = JsonDocument.Parse(json); var stamp = candidate.RootElement;
                completion.TrySetResult(stamp.GetProperty("mediaGeneration").GetInt64() != scope.MediaGeneration
                    || stamp.GetProperty("timeline").GetString() != scope.Timeline
                    || stamp.GetProperty("frameId").GetInt64() > afterFrameId && stamp.GetProperty("sourceUs").GetInt64() > afterSourceTimeUs);
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        return completion.Task;
    }
    public Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<TableFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bridge.PlayerBridge? admittedOwner = null;
        void Attempt(int remaining)
        {
            BitmapTableImage? pixels = null;
            try
            {
                cancellation.ThrowIfCancellationRequested(); var owner = player();
                if (owner is null || !live() || connection().Scope != source || !connection().CanControl)
                    throw new InvalidOperationException("Use the current live source for observation.");
                if (admittedOwner is not null && admittedOwner != owner) throw new InvalidOperationException("Live player changed during frame acquisition.");
                admittedOwner = owner;
                using var timing = PipelineDiagnostics.Measure("textureCopyMs");
                using var snapshot = owner.SnapshotDecodedFrame();
                if (snapshot is null)
                {
                    // A render/copy race may be refused even while live video is
                    // healthy. Yield to the next latch without weakening pairing.
                    if (remaining > 0) { main.PostDelayed(() => Attempt(remaining - 1), 40); return; }
                    throw new IOException("No fresh paired decoded live frame is available.");
                }
                try
                {
                    using var metrics = JsonDocument.Parse(snapshot.Metadata()!); var stamp = metrics.RootElement;
                    if (stamp.GetProperty("pairing").GetString() != "exact-surface-timestamp"
                        || stamp.GetProperty("surfaceTimestampNs").GetInt64() != stamp.GetProperty("releaseTimestampNs").GetInt64())
                        throw new IOException("Decoded pixels have no exact source timestamp receipt.");
                    pixels = new(snapshot.TakeBitmap() ?? throw new IOException("Decoded receipt pixels are unavailable."));
                    var age = stamp.GetProperty("ageEstimateMs").GetDouble();
                    if (!double.IsFinite(age) || age is < -200 or > 6000) throw new IOException("The displayed live frame is too old. Wait for moving live video.");
                    cancellation.ThrowIfCancellationRequested();
                    if (player() != owner || !live() || connection().Scope != source || !connection().CanControl)
                        throw new InvalidOperationException("Live observation source changed during frame acquisition.");
                    completion.TrySetResult(new([], DateTimeOffset.UtcNow.AddMilliseconds(-Math.Max(0, age)), stamp.GetProperty("timeline").GetString()!)
                    {
                        Image = pixels, SourceTimeUs = stamp.GetProperty("sourceUs").GetInt64(), FrameSource = "decoded-live-texture",
                        DecodedReceipt = new(stamp.GetProperty("mediaGeneration").GetInt64(), stamp.GetProperty("frameId").GetInt64(),
                            stamp.GetProperty("presentationUs").GetInt64(), stamp.GetProperty("surfaceTimestampNs").GetInt64(),
                            stamp.GetProperty("width").GetInt32(), stamp.GetProperty("height").GetInt32())
                    });
                }
                finally { snapshot.Close(); }
            }
            catch (Exception error) { pixels?.Dispose(); completion.TrySetException(error); }
        }
        main.Post(() => Attempt(3));
        return completion.Task;
    }
}
