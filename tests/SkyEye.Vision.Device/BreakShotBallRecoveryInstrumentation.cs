using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using Org.Opencv.Android;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.BreakShotBallRecoveryInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class BreakShotBallRecoveryInstrumentation : global::Android.App.Instrumentation
{
    const string FixtureName = "break-shot-red-potted-2026-10-26";

    public BreakShotBallRecoveryInstrumentation() { }
    public BreakShotBallRecoveryInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args); Start(); }

    public override void OnStart()
    {
        base.OnStart();
        bool passed = false; string? error = null;
        var widths = new List<object>();
        try
        {
            if (!OpenCVLoader.InitLocal()) throw new IOException("OpenCV could not load for the break-shot replay.");
            using var imageStream = TargetContext!.Assets!.Open(FixtureName + ".png");
            using var imageBytes = new MemoryStream(); imageStream.CopyTo(imageBytes);
            using var labelsStream = TargetContext.Assets.Open(FixtureName + ".json");
            using var labels = JsonDocument.Parse(labelsStream);
            byte[] image = imageBytes.ToArray();
            string sha = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
            if (!string.Equals(sha, labels.RootElement.GetProperty("pixelSha256").GetString(), StringComparison.Ordinal))
                throw new IOException("Break-shot fixture image does not match its reviewed receipt.");
            var calibration = new TableCalibration(labels.RootElement.GetProperty("calibration").EnumerateArray()
                .Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
            var expected = labels.RootElement.GetProperty("expected").EnumerateArray()
                .Select(p =>
                {
                    string kind = p.GetProperty("kind").GetString()!;
                    var imagePoint = p.GetProperty("image");
                    return (Kind: kind, Centre: new Point(imagePoint.GetProperty("x").GetDouble(),
                        imagePoint.GetProperty("y").GetDouble()));
                }).ToArray();

            bool allWidthsRecovered = true;
            bool fallbackLocatedReviewedRed = false;
            foreach (int width in new[] { 800, 1000 })
            {
                var analyzer = new VisionAnalyzer(new VisionSettings(Width: width), includeCandidateDiagnostics: true);
                var result = analyzer.Analyze(image, calibration);
                var detectedByKind = result.Balls.GroupBy(b => b.Kind).ToDictionary(g => g.Key,
                    g => g.Select(b => calibration.Unmap(b.Position)).ToList());
                var classChecks = new List<object>();
                bool exactInventory = result.Balls.Length == expected.Length;
                foreach (var expectedGroup in expected.GroupBy(ball => ball.Kind))
                {
                    var remaining = detectedByKind.TryGetValue(expectedGroup.Key, out var detected)
                        ? detected.ToList() : new List<Point>();
                    var missing = new List<string>(); double maximumError = 0;
                    foreach (var expectedBall in expectedGroup)
                    {
                        int nearest = -1; double distance = double.PositiveInfinity;
                        for (int i = 0; i < remaining.Count; i++)
                        {
                            double candidate = (remaining[i] - expectedBall.Centre).Length;
                            if (candidate < distance) { nearest = i; distance = candidate; }
                        }
                        if (nearest < 0 || distance > .03)
                        { missing.Add($"({expectedBall.Centre.X:F4},{expectedBall.Centre.Y:F4})"); continue; }
                        maximumError = Math.Max(maximumError, distance); remaining.RemoveAt(nearest);
                    }
                    bool exactClass = missing.Count == 0 && remaining.Count == 0;
                    exactInventory &= exactClass;
                    classChecks.Add(new
                    {
                        kind = expectedGroup.Key, exact = exactClass,
                        expectedCount = expectedGroup.Count(),
                        detectedCount = detected?.Count ?? 0,
                        maximumImageCentreError = maximumError, missing,
                        extraCount = remaining.Count
                    });
                }
                var extraKinds = detectedByKind.Keys.Except(expected.Select(ball => ball.Kind)).ToArray();
                exactInventory &= extraKinds.Length == 0;
                allWidthsRecovered &= exactInventory && !result.Uncertain && !result.Obstructed;
                var targetTable = calibration.Map(new(.6589, .5454));
                var targetAnalysis = new AnalysisCoordinates(width).FromTable(targetTable);
                bool targetAdmittedBySparseFallback = result.CandidateAudits?.Any(a =>
                    a.Stage == "sparse-colour-add" && a.Decision == "added-strong-isolated-body"
                    && a.Kind == "red" && (a.ProposalCentre - targetAnalysis).Length <= width * .03) == true;
                fallbackLocatedReviewedRed |= width == 1000 && targetAdmittedBySparseFallback;
                widths.Add(new
                {
                    width, exactInventory, expectedTotal = expected.Length,
                    detectedTotal = result.Balls.Length, classes = classChecks, extraKinds,
                    targetAdmittedBySparseFallback,
                    counts = result.Balls.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count()),
                    result.Uncertain, result.Obstructed, result.Warnings
                });
            }
            passed = allWidthsRecovered && fallbackLocatedReviewedRed;
        }
        catch (Exception failure) { error = failure.ToString(); }

        var report = new
        {
            passed, error, measuredAt = DateTimeOffset.UtcNow,
            scope = "Offline on-device analyzer replay of one manually reviewed post-break development frame. The check requires all 15 visible ball classes and centres at 800 and 1000 width with no uncertainty or obstruction. This single frame is not live readiness or held-out acceptance evidence.",
            widths
        };
        string output = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        string root = TargetContext!.GetExternalFilesDir(null)!.AbsolutePath;
        File.WriteAllText(Path.Combine(root, "break-shot-ball-recovery-proof.json"), output);
        using var bundle = new global::Android.OS.Bundle();
        bundle.PutString("result", passed ? "Break-shot inventory recovery passed" : "Break-shot inventory recovery failed; inspect report");
        Finish(passed ? global::Android.App.Result.Ok : global::Android.App.Result.Canceled, bundle);
    }
}
