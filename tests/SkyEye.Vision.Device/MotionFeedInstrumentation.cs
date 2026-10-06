using System.Diagnostics;
using System.Text.Json;
using SkyEye.Companion;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Vision.Android;
using Native = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.MotionFeedInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class MotionFeedInstrumentation : global::Android.App.Instrumentation
{
    public MotionFeedInstrumentation() { }
    public MotionFeedInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart(); object result;
        try
        {
            using var intent = new global::Android.Content.Intent(); intent.SetClassName(TargetContext!, "uk.co.skyeye.companion.nativebridge.DecodedFrameProofActivity");
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask); intent.PutExtra("proof-run", Guid.NewGuid().ToString("N")); intent.PutExtra("feed-proof", true);
            TargetContext!.StartActivity(intent); result = RunAsync().GetAwaiter().GetResult();
        }
        catch (Exception error) { result = new { passed = false, error = error.ToString() }; }
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "motion-feed-proof.json"), json);
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", json); Finish(global::Android.App.Result.Ok, bundle);
    }
    static async Task<object> RunAsync()
    {
        var camera = new CameraConnection(Guid.NewGuid(), new string('A', 64), CameraReachability.Reachable, true, CaptureState.Active,
            CameraOwnership.Owned, "synthetic-loopback", 1, "0123456789abcdef0123456789abcdef", 1);
        var source = new PlayerTableFrameSource(Native.DecodedFrameProofActivity.LiveProofPlayer, () => camera, () => true);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10); TableFrame? initial = null;
        while (initial is null && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(20);
            try { initial = await source.ReadAsync(camera.Scope!, CancellationToken.None); }
            catch (Exception e) when (e is IOException or InvalidOperationException) { }
        }
        if (initial is null) throw new Exception("No paired synthetic live source");
        var scope = new MotionFeedScope(camera.Scope!, initial.DecodedReceipt!.MediaGeneration, "fixed-motion-texture-v1", initial.Timeline); initial.Dispose();
        var owner = Native.DecodedFrameProofActivity.LiveProofPlayer(); var runs = new List<object>();
        foreach (var hz in new[] { 15, 30, 60 })
        {
            using var roi = new RoiMotionProcessor([new("texture", 540, 40, 96, 96)]); var measured = new Measurements(roi);
            await using var feed = new TableMotionFeed(source, measured, scope, () => Native.DecodedFrameProofActivity.LiveProofPlayer() == owner, new(hz));
            var watch = Stopwatch.StartNew(); feed.Start(); await Task.Delay(1500); await feed.StopAsync(); watch.Stop();
            var state = feed.Snapshot;
            if (state.Processed < 8 || state.WorkerFailures != 0 || state.ScopeRejected != 0 || state.Active != 0 || state.Pending != 0)
                throw new Exception("Native feed did not drain correctly at " + hz + ": " + JsonSerializer.Serialize(state));
            runs.Add(new { requestedHz = hz, roiCount = 1, sourceNominalHz = 30, seconds = watch.Elapsed.TotalSeconds, processedHz = state.Processed / watch.Elapsed.TotalSeconds,
                state, acquisitionP50Ms = Percentile(measured.Acquisition, .5), acquisitionP95Ms = Percentile(measured.Acquisition, .95),
                workerP50Ms = Percentile(measured.Worker, .5), workerP95Ms = Percentile(measured.Worker, .95),
                conversionP95Ms = Percentile(measured.Conversion, .95), flowP95Ms = Percentile(measured.Flow, .95), measured.Supported, measured.Unresolved });
        }
        using (var many = new RoiMotionProcessor(Enumerable.Range(0, 16).Select(i => new PixelRoi("region-" + i, 540, 40, 96, 96))))
        {
            var measured = new Measurements(many);
            await using var feed = new TableMotionFeed(source, measured, scope, () => Native.DecodedFrameProofActivity.LiveProofPlayer() == owner, new(30));
            var watch = Stopwatch.StartNew(); feed.Start(); await Task.Delay(1000); await feed.StopAsync(); watch.Stop();
            if (feed.Snapshot.Processed < 2 || feed.Snapshot.WorkerFailures != 0 || feed.Snapshot.Active != 0 || feed.Snapshot.Pending != 0)
                throw new Exception("Sixteen-ROI workload failed to drain");
            runs.Add(new { requestedHz = 30, roiCount = 16, repeatedRoiWorkload = true, sourceNominalHz = 30,
                seconds = watch.Elapsed.TotalSeconds, processedHz = feed.Snapshot.Processed / watch.Elapsed.TotalSeconds, state = feed.Snapshot,
                workerP50Ms = Percentile(measured.Worker, .5), workerP95Ms = Percentile(measured.Worker, .95),
                acquisitionP95Ms = Percentile(measured.Acquisition, .95), measured.Supported, measured.Unresolved });
        }
        using var slowRoi = new RoiMotionProcessor([new("texture", 540, 40, 96, 96)]); var slow = new Measurements(slowRoi, 100);
        await using (var feed = new TableMotionFeed(source, slow, scope, () => Native.DecodedFrameProofActivity.LiveProofPlayer() == owner, new(60)))
        {
            feed.Start(); await Task.Delay(1000); await feed.StopAsync();
            if (feed.Snapshot.Replaced < 3 || feed.Snapshot.WorkerFailures != 0 || feed.Snapshot.Active != 0 || feed.Snapshot.Pending != 0)
                throw new Exception("Overload did not replace/drain latest frames");
            runs.Add(new { requestedHz = 60, injectedWorkerDelayMs = 100, state = feed.Snapshot });
        }
        return new { passed = true, source = "Controlled 640x360/30Hz loopback TS; not real 1080p/table accuracy or sustained performance", runs, explicitEnableAndStop = true, noFullDetectorOrGameMutation = true };
    }
    static double Percentile(List<double> values, double fraction) { var sorted = values.Order().ToArray(); return sorted[(int)Math.Ceiling(sorted.Length * fraction) - 1]; }
    sealed class Measurements(RoiMotionProcessor roi, int delayMs = 0) : ITableMotionProcessor
    {
        public readonly List<double> Acquisition = [], Worker = [], Conversion = [], Flow = [];
        public int Supported, Unresolved;
        public async Task<MotionMeasurement> MeasureAsync(TableFrame frame, MotionFrameContext context, CancellationToken cancellation)
        {
            var clock = Stopwatch.StartNew(); if (delayMs != 0) await Task.Delay(delayMs, cancellation);
            var result = await roi.MeasureAsync(frame, context, cancellation); clock.Stop();
            Acquisition.Add(context.AcquisitionMs); Worker.Add(clock.Elapsed.TotalMilliseconds); Conversion.Add(result.ConversionMs); Flow.Add(result.FlowMs);
            Supported += result.Regions.Count(r => r.Status == "supported"); Unresolved += result.Regions.Count(r => r.Status != "supported"); return result;
        }
    }
}
