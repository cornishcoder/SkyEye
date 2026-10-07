using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.VerificationReplayInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class VerificationReplayInstrumentation : global::Android.App.Instrumentation
{
    public VerificationReplayInstrumentation() { }
    public VerificationReplayInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    bool refinement, dump;
    public override void OnCreate(global::Android.OS.Bundle? args) { refinement = args?.GetString("refine") == "1"; dump = args?.GetString("dump") == "1"; base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart(); object report;
        try
        {
            var root = Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "o08-replay");
            var paths = Directory.GetFiles(root, "*.json").OrderBy(p => long.Parse(Path.GetFileNameWithoutExtension(p))).ToArray();
            if (paths.Length is < 3 or > 6) throw new IOException("Replay 3 to 6 captured development snapshots.");
            using var first = JsonDocument.Parse(File.ReadAllBytes(paths[0]));
            var corners = first.RootElement.GetProperty("calibrationCorners").EnumerateArray().Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray();
            var calibration = new TableCalibration(corners); var analyzer = new VisionAnalyzer(new VisionSettings { VerificationBodyRefinement = refinement }); using var worker = analyzer.CreateVerification(calibration);
            var samples = new List<object>();
            var measurements = new List<VerificationMeasurement>();
            foreach (var path in paths)
            {
                using var metadata = JsonDocument.Parse(File.ReadAllBytes(path)); var receipt = metadata.RootElement.GetProperty("indexedFrame");
                var file = receipt.GetProperty("pixelFile").GetString()!;
                if (file != Path.GetFileName(file)) throw new IOException("Replay asset leaves its directory.");
                var bytes = File.ReadAllBytes(Path.Combine(root, file));
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                if (sha != receipt.GetProperty("pixelSha256").GetString()) throw new IOException("Captured JPEG hash mismatch.");
                var baseline = analyzer.Analyze(bytes, calibration);
                if (dump) DumpPixels(bytes, calibration, root, Path.GetFileNameWithoutExtension(path));
                using var frame = new TableFrame(bytes, DateTimeOffset.UtcNow, receipt.GetProperty("scope").GetProperty("timeline").GetString()!) { SourceTimeUs = receipt.GetProperty("sourceTimeUs").GetInt64() };
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var enhanced = worker.Measure(frame, true, CancellationToken.None); measurements.Add(enhanced);
                samples.Add(new { pixelSha256 = sha, sourceTimeUs = frame.SourceTimeUs, baseline, enhanced, processingMs = timer.Elapsed.TotalMilliseconds });
            }
            report = new { completed = true, configuration = ObservationPolicy.VerificationConfiguration, bodyRefinement = refinement,
                consensus = VerificationConsensus.Resolve(measurements.Select(m => m.Vision!).ToArray(), out var conflict), conflict,
                allMotionSamplesStill = measurements.Skip(1).All(m => m.Motion == TableMotion.Still && m.CoverageValid),
                scope = "Native JPEG development replay/branch ablation, not blind holdout, live pixel pairing or latency acceptance", samples };
        }
        catch (Exception error) { report = new { completed = false, error = error.ToString() }; }
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "o08-replay-proof.json"), json);
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", "O08 development replay written"); Finish(global::Android.App.Result.Ok, bundle);
    }
    static void DumpPixels(byte[] jpeg, TableCalibration calibration, string root, string id)
    {
        // Explicit test-only export, at most six inputs / 42 MB, no app/game state.
        using var encoded = new Org.Opencv.Core.MatOfByte(jpeg);
        using var source = Org.Opencv.Imgcodecs.Imgcodecs.Imdecode(encoded, Org.Opencv.Imgcodecs.Imgcodecs.ImreadColor);
        foreach (int width in new[] { 1000, 400 })
        {
            var original = calibration.Corners.Select(p => new Org.Opencv.Core.Point(p.X * source.Cols(), p.Y * source.Rows())).ToArray();
            Org.Opencv.Core.Point[] target = [new(width, 0), new(width, width * 2), new(0, width * 2), new(0, 0)];
            try
            {
                using var from = new Org.Opencv.Core.MatOfPoint2f(original); using var to = new Org.Opencv.Core.MatOfPoint2f(target);
                using var transform = Org.Opencv.Imgproc.Imgproc.GetPerspectiveTransform(from, to);
                using var warped = new Org.Opencv.Core.Mat(); using var size = new Org.Opencv.Core.Size(width, width * 2);
                Org.Opencv.Imgproc.Imgproc.WarpPerspective(source, warped, transform, size);
                var pixels = new byte[width * width * 2 * 3]; warped.Get(0, 0, pixels);
                File.WriteAllBytes(Path.Combine(root, $"{id}-{width}.bgr"), pixels);
            }
            finally { foreach (var p in original.Concat(target)) p.Dispose(); }
        }
    }
}
