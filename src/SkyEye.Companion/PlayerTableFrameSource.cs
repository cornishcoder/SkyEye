using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Vision.Android;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

public sealed class PlayerTableFrameSource(Func<Bridge.PlayerBridge?> player, Func<CameraConnection> connection, Func<bool> live) : ITableFrameSource
{
    readonly global::Android.OS.Handler main = new(global::Android.OS.Looper.MainLooper!);
    public Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<TableFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Post(() =>
        {
            BitmapTableImage? pixels = null;
            try
            {
                cancellation.ThrowIfCancellationRequested(); var owner = player();
                if (owner is null || !live() || connection().Scope != source || !connection().CanControl)
                    throw new InvalidOperationException("Use the current live source for observation.");
                using var timing = PipelineDiagnostics.Measure("textureCopyMs");
                using var snapshot = owner.SnapshotDecodedFrame() ?? throw new IOException("No fresh paired decoded live frame is available.");
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
        });
        return completion.Task;
    }
}
