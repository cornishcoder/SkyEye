using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Vision.Android;
using ABitmap = global::Android.Graphics.Bitmap;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.RoiMotionInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class RoiMotionInstrumentation : global::Android.App.Instrumentation
{
    public RoiMotionInstrumentation() { }
    public RoiMotionInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart(); object result;
        try { result = Check(); } catch (Exception error) { result = new { passed = false, error = error.ToString() }; }
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "roi-motion-proof.json"), json);
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", json); Finish(global::Android.App.Result.Ok, bundle);
    }
    static object Check()
    {
        var scope = new MotionFeedScope(new(Guid.NewGuid(), new string('A', 64), "synthetic", 1, 1), 7, "fixed-roi-v1", "synthetic-roi");
        using var processor = new RoiMotionProcessor([new("texture", 48, 24, 96, 96)]);
        MotionMeasurement Run(long id, long pts, int x, int y, bool reset = false, bool blank = false)
        {
            using var frame = Frame(id, pts, x, y, blank);
            return processor.MeasureAsync(frame, new(scope, id, pts, reset, reset ? null : .033333), CancellationToken.None).GetAwaiter().GetResult();
        }
        var seeded = Run(1, 0, 0, 0, true);
        var translated = Run(2, 33333, 2, 1);
        var still = Run(3, 66666, 2, 1);
        var lost = Run(4, 99999, 0, 0, blank: true);
        var reset = Run(5, 400000, 4, 2, true);
        var motion = translated.Regions[0];
        Require(motion.Status == "supported" && Math.Abs(motion.DxPixels!.Value - 2) < .25 && Math.Abs(motion.DyPixels!.Value - 1) < .25,
            "Known 2x1 pixel translation differs");
        Require(Math.Abs(motion.VelocityXPixelsPerSecond!.Value - 60) < 8, "Velocity did not use source delta time");
        Require(still.Regions[0].Status == "supported" && Math.Abs(still.Regions[0].DxPixels!.Value) < .1, "Stationary texture moved");
        Require(lost.Regions[0].Status != "supported" && lost.Regions[0].VelocityXPixelsPerSecond is null, "Occlusion produced confident flow");
        Require(reset.Regions[0].Status == "seed" && reset.Regions[0].DxPixels is null, "Reset retained old trajectories");
        using var whole = new RoiMotionProcessor([new("whole", 0, 0, 192, 144)]);
        using var borrowed = Frame(6, 500000, 0, 0, false);
        whole.MeasureAsync(borrowed, new(scope, 6, 500000, true, null), CancellationToken.None).GetAwaiter().GetResult();
        Require(!((BitmapTableImage)borrowed.Image!).Bitmap.IsRecycled, "Whole-image ROI disposed borrowed source pixels");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { whole.MeasureAsync(borrowed, new(scope, 6, 500000, false, .033), cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { }
        return new { passed = true, source = "Synthetic native bitmaps; not ball/real-table accuracy", seeded, translated, still, lost, reset,
            borrowedImageRetained = true, cancellationRejected = true, originalSize = new[] { 192, 144 }, roiSize = new[] { 96, 96 } };
    }
    static TableFrame Frame(long id, long pts, int dx, int dy, bool blank)
    {
        var bitmap = ABitmap.CreateBitmap(192, 144, ABitmap.Config.Argb8888!)!; var pixels = new int[192 * 144];
        for (int y = 0; y < 144; y++) for (int x = 0; x < 192; x++)
        {
            uint hash = unchecked((uint)((x - dx + 100) / 5 * 73856093) ^ (uint)((y - dy + 100) / 5 * 19349663));
            int v = blank ? 80 : 32 + (int)(hash % 192); pixels[y * 192 + x] = global::Android.Graphics.Color.Rgb(v, v, v);
        }
        bitmap.SetPixels(pixels, 0, 192, 0, 0, 192, 144);
        return new([], DateTimeOffset.UtcNow, "synthetic-roi") { Image = new BitmapTableImage(bitmap), SourceTimeUs = pts,
            DecodedReceipt = new(7, id, pts, 100, 192, 144) };
    }
    static void Require(bool yes, string reason) { if (!yes) throw new Exception(reason); }
}
