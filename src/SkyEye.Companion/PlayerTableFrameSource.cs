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
                pixels = new(owner.SnapshotForAnalysis() ?? throw new IOException("No fresh decoded live frame is available."));
                using var metrics = JsonDocument.Parse(owner.ObservationFrameMetrics()!); var stamp = metrics.RootElement;
                var age = stamp.GetProperty("ageEstimateMs").GetDouble();
                if (!double.IsFinite(age) || age is < -200 or > 6000) throw new IOException("The displayed live frame is too old. Wait for moving live video.");
                cancellation.ThrowIfCancellationRequested();
                completion.TrySetResult(new([], DateTimeOffset.UtcNow.AddMilliseconds(-Math.Max(0, age)), stamp.GetProperty("timeline").GetString()!)
                { Image = pixels, SourceTimeUs = stamp.GetProperty("sourceUs").GetInt64(), FrameSource = "decoded-live-texture" });
            }
            catch (Exception error) { pixels?.Dispose(); completion.TrySetException(error); }
        });
        return completion.Task;
    }
}
