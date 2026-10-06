using System.Text.Json;
using SkyEye.Companion;
using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Vision.Android;
using Native = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.DecodedFrameBindingInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class DecodedFrameBindingInstrumentation : global::Android.App.Instrumentation
{
    public DecodedFrameBindingInstrumentation() { }
    public DecodedFrameBindingInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart();
        var root = TargetContext!.GetExternalFilesDir(null)!.AbsolutePath;
        object report;
        try
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "decoded-frame-fixture.json")));
            long origin = metadata.RootElement.GetProperty("sourceOriginUs").GetInt64();
            using var intent = new global::Android.Content.Intent();
            intent.SetClassName(TargetContext, "uk.co.skyeye.companion.nativebridge.DecodedFrameProofActivity");
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            intent.PutExtra("proof-run", Guid.NewGuid().ToString("N"));
            TargetContext.StartActivity(intent);
            report = CheckAsync(origin).GetAwaiter().GetResult();
        }
        catch (Exception failure) { report = new { passed = false, error = failure.ToString() }; }
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "decoded-frame-binding.json"), json);
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", json); Finish(global::Android.App.Result.Ok, bundle);
    }
    static async Task<object> CheckAsync(long origin)
    {
        var camera = new CameraConnection(Guid.NewGuid(), new string('A', 64), CameraReachability.Reachable, true,
            CaptureState.Active, CameraOwnership.Owned, "synthetic-loopback", 1, "0123456789abcdef0123456789abcdef", 1);
        var source = new PlayerTableFrameSource(Native.DecodedFrameProofActivity.LiveProofPlayer, () => camera, () => true);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
        var samples = new List<object>(); long previous = -1;
        while (samples.Count < 10 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(40);
            try
            {
                using var frame = await source.ReadAsync(camera.Scope!, CancellationToken.None);
                if (frame.SourceTimeUs == previous) continue;
                var receipt = frame.DecodedReceipt ?? throw new Exception("Native identity was dropped by the C# adapter");
                if (frame.Jpeg.Length != 0 || frame.Image is not BitmapTableImage pixels || receipt.Width != 640 || receipt.Height != 360)
                    throw new Exception("Adapter copied/encoded the wrong pixels");
                int id = 0;
                for (int bit = 0; bit < 12; bit++)
                    if (global::Android.Graphics.Color.GetRedComponent(pixels.Bitmap.GetPixel(60 + 40 * bit, 180)) > 128) id |= 1 << bit;
                int expected = (int)Math.Round((frame.SourceTimeUs!.Value - origin) * 30 / 1_000_000d);
                if (id != expected) throw new Exception("C# pixels/source-PTS handoff mismatch");
                samples.Add(new { frame.SourceTimeUs, receipt, pixelFrameId = id, expectedFrameId = expected, noAnalysisJpeg = true });
                previous = frame.SourceTimeUs.Value;
            }
            catch (IOException) { } // Native copy rejection is permitted and retried only by this proof.
            catch (InvalidOperationException) when (Native.DecodedFrameProofActivity.LiveProofPlayer() is null) { }
        }
        if (samples.Count < 10) throw new Exception("Insufficient C# paired samples: " + samples.Count);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { using var unexpected = await source.ReadAsync(camera.Scope!, cancelled.Token); throw new Exception("Cancelled acquisition returned pixels"); }
        catch (OperationCanceledException) { }
        return new { passed = true, samples, cancelledAcquisitionRejected = true, source = "Synthetic loopback TS through production PlayerTableFrameSource; not real table accuracy" };
    }
}
