using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.LiveTableRegressionInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class LiveTableRegressionInstrumentation : global::Android.App.Instrumentation
{
    public LiveTableRegressionInstrumentation() { }
    public LiveTableRegressionInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart(); var rows = new List<object>(); bool passed = true; string? error = null;
        try
        {
            string root = Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "live-table-regression");
            var paths = Directory.GetFiles(root, "*.json").Order().Take(13).ToArray();
            if (paths.Length is < 2 or > 12) throw new IOException("Replay two to twelve explicitly captured dashboard pairs");
            foreach (string path in paths)
            {
                using var receipt = JsonDocument.Parse(File.ReadAllBytes(path)); var metadata = receipt.RootElement;
                string image = metadata.GetProperty("pixelFile").GetString()!;
                if (image != Path.GetFileName(image)) throw new IOException("Image leaves replay root");
                var bytes = File.ReadAllBytes(Path.Combine(root, image));
                if (bytes.Length > 4 * 1024 * 1024 || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
                    != metadata.GetProperty("pixelSha256").GetString()) throw new IOException("Image receipt mismatch");
                var calibration = new TableCalibration(metadata.GetProperty("calibrationCorners").EnumerateArray()
                    .Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
                var before = new VisionAnalyzer(new VisionSettings { BodyProposalPriority = false }).Analyze(bytes, calibration);
                var after = new VisionAnalyzer().Analyze(bytes, calibration);
                var remaining = after.Balls.ToList(); var missing = new List<string>();
                foreach (var expected in metadata.GetProperty("reviewedVisibleBalls").EnumerateArray())
                {
                    string kind = expected.GetProperty("kind").GetString()!;
                    var centre = calibration.Map(new(expected.GetProperty("x").GetDouble(), expected.GetProperty("y").GetDouble()));
                    var match = remaining.Where(b => b.Kind == kind).MinBy(b => (b.Position - centre).Length);
                    if (match is null || (match.Position - centre).Length > .03) missing.Add(expected.GetProperty("id").GetString()!);
                    else remaining.Remove(match);
                }
                bool exact = missing.Count == 0 && remaining.Count == 0 && !after.Uncertain;
                passed &= exact;
                rows.Add(new { sample = metadata.GetProperty("sample").GetInt32(), pixelSha256 = metadata.GetProperty("pixelSha256").GetString(),
                    before, after, missing, extras = remaining, exact });
            }
        }
        catch (Exception failure) { passed = false; error = failure.ToString(); }
        var report = new { passed, error, measuredAt = DateTimeOffset.UtcNow,
            scope = "Development replay of the user's stationary-table regression with manually reviewed visible ball regions; not a blind holdout or live-pixel proof", rows };
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "live-table-regression-proof.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", passed ? "Whole-table replay passed" : "Whole-table replay has failures; inspect report");
        Finish(passed ? global::Android.App.Result.Ok : global::Android.App.Result.Canceled, bundle);
    }
}
