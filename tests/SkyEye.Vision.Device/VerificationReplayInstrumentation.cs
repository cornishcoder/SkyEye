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
    bool refinement, dump, boundaryAudit; string run = "";
    public override void OnCreate(global::Android.OS.Bundle? args) { refinement = args?.GetString("refine") == "1"; dump = args?.GetString("dump") == "1"; boundaryAudit = args?.GetString("audit") == "1"; run = args?.GetString("run") ?? ""; if (run.Length > 32 || run.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Invalid replay run name."); base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart(); object report;
        try
        {
            var root = Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, run.Length == 0 ? "o08-replay" : "o08-replay-" + run);
            var paths = Directory.GetFiles(root, "*.json").OrderBy(p => long.Parse(Path.GetFileNameWithoutExtension(p))).ToArray();
            if (paths.Length is < 3 or > 6) throw new IOException("Replay 3 to 6 captured development snapshots.");
            using var first = JsonDocument.Parse(File.ReadAllBytes(paths[0]));
            var corners = first.RootElement.GetProperty("calibrationCorners").EnumerateArray().Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray();
            var calibration = new TableCalibration(corners);
            var analyzer800 = new VisionAnalyzer(new VisionSettings(Width: 800) { VerificationBodyRefinement = refinement }, boundaryAudit, boundaryAudit);
            var analyzer1000 = new VisionAnalyzer(new VisionSettings(Width: 1000) { VerificationBodyRefinement = refinement }, boundaryAudit, boundaryAudit);
            using var worker800 = analyzer800.CreateVerification(calibration);
            using var worker1000 = analyzer1000.CreateVerification(calibration);
            var samples = new List<object>();
            var analyses800 = new List<VisionResult>(); var analyses1000 = new List<VisionResult>();
            var measurements800 = new List<VerificationMeasurement>(); var measurements1000 = new List<VerificationMeasurement>();
            foreach (var path in paths)
            {
                using var metadata = JsonDocument.Parse(File.ReadAllBytes(path)); var receipt = metadata.RootElement.GetProperty("indexedFrame");
                var file = receipt.GetProperty("pixelFile").GetString()!;
                if (file != Path.GetFileName(file)) throw new IOException("Replay asset leaves its directory.");
                var bytes = File.ReadAllBytes(Path.Combine(root, file));
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                if (sha != receipt.GetProperty("pixelSha256").GetString()) throw new IOException("Captured pixel asset hash mismatch.");
                var baseline800 = analyzer800.Analyze(bytes, calibration);
                var baseline1000 = analyzer1000.Analyze(bytes, calibration);
                analyses800.Add(baseline800); analyses1000.Add(baseline1000);
                if (dump) DumpPixels(bytes, calibration, root, Path.GetFileNameWithoutExtension(path));
                using var frame = new TableFrame(bytes, DateTimeOffset.UtcNow, receipt.GetProperty("scope").GetProperty("timeline").GetString()!) { SourceTimeUs = receipt.GetProperty("sourceTimeUs").GetInt64() };
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var verification800 = worker800.Measure(frame, true, CancellationToken.None); measurements800.Add(verification800);
                var verification1000 = worker1000.Measure(frame, true, CancellationToken.None); measurements1000.Add(verification1000);
                samples.Add(new
                {
                    frameId = receipt.GetProperty("frameId").GetInt64(), pixelSha256 = sha, sourceTimeUs = frame.SourceTimeUs,
                    width800 = new { analysis = Summarize(baseline800, 800), verification = Summarize(verification800, 800) },
                    width1000 = new { analysis = Summarize(baseline1000, 1000), verification = Summarize(verification1000, 1000) },
                    analysisWidthComparison = Compare(baseline800, baseline1000),
                    widthComparison = Compare(verification800.Vision!, verification1000.Vision!),
                    processingMs = timer.Elapsed.TotalMilliseconds
                });
            }
            var consensus800 = VerificationConsensus.Resolve(measurements800.Select(m => m.Vision!).ToArray(), out var conflict800);
            var consensus1000 = VerificationConsensus.Resolve(measurements1000.Select(m => m.Vision!).ToArray(), out var conflict1000);
            var analysisConsensus800 = VerificationConsensus.Resolve(analyses800, out var analysisConflict800);
            var analysisConsensus1000 = VerificationConsensus.Resolve(analyses1000, out var analysisConflict1000);
            report = new { completed = true, configuration = ObservationPolicy.VerificationConfiguration, bodyRefinement = refinement, boundaryAudit,
                fullAnalysis800 = new { consensus = analysisConsensus800, conflict = analysisConflict800, observations = analyses800.Select(v => Summarize(v, 800)) },
                fullAnalysis1000 = new { consensus = analysisConsensus1000, conflict = analysisConflict1000, observations = analyses1000.Select(v => Summarize(v, 1000)) },
                width800 = new { consensus = consensus800, conflict = conflict800, allMotionSamplesStill = measurements800.Skip(1).All(m => m.Motion == TableMotion.Still && m.CoverageValid), measurements = measurements800.Select(SummarizeMotion) },
                width1000 = new { consensus = consensus1000, conflict = conflict1000, allMotionSamplesStill = measurements1000.Skip(1).All(m => m.Motion == TableMotion.Still && m.CoverageValid), measurements = measurements1000.Select(SummarizeMotion) },
                scope = "Same captured pixel assets replayed through 800 and 1000 analysis and verification workers. Development evidence only; not blind holdout, live pixel pairing or latency acceptance.", samples };
        }
        catch (Exception error) { report = new { completed = false, error = error.ToString() }; }
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "o08-replay-proof.json"), json);
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", "O08 development replay written"); Finish(global::Android.App.Result.Ok, bundle);
    }
    static object Summarize(VisionResult vision, int width) => new
    {
        configuredWidth = width, geometryWidth = width,
        vision.Uncertain, vision.Obstructed, vision.CircleCount,
        counts = vision.Balls.GroupBy(b => b.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
        vision.Recognition,
        vision.PackedCluster, vision.PackedClusterFallback,
        boundaryAudits = vision.BoundaryAudits,
        candidateAudits = vision.CandidateAudits,
        circles = vision.Circles.Select(c => new
        {
            c.BallId, kind = Kind(c.BallId), x = c.Centre.Y / width, y = 1 - c.Centre.X / width,
            radius = c.Radius / width, c.BoundarySupport, c.CandidateBoundarySupport
        })
    };
    static object Summarize(VerificationMeasurement measurement, int width) => new
    {
        measurement.Motion, measurement.Reason, measurement.CoverageValid, measurement.MaximumBodyDisplacement,
        measurement.MaximumDetectedBodyDisplacement, measurement.SupportedBodies,
        vision = measurement.Vision is null ? null : Summarize(measurement.Vision, width)
    };
    static object SummarizeMotion(VerificationMeasurement measurement) => new
    {
        measurement.Motion, measurement.Reason, measurement.CoverageValid, measurement.MaximumBodyDisplacement,
        measurement.MaximumDetectedBodyDisplacement, measurement.SupportedBodies
    };
    static object Compare(VisionResult low, VisionResult high)
    {
        const int leftWidth = 800, rightWidth = 1000;
        var left = low.Circles.Select(c => new Located(Kind(c.BallId), c.BallId, c.Centre.Y / leftWidth, 1 - c.Centre.X / leftWidth)).ToArray();
        var right = high.Circles.Select(c => new Located(Kind(c.BallId), c.BallId, c.Centre.Y / rightWidth, 1 - c.Centre.X / rightWidth)).ToArray();
        var pairs = new List<(Located Left, Located Right, double Distance)>(); var usedLeft = new HashSet<Located>(); var usedRight = new HashSet<Located>();
        foreach (var pair in (from a in left from b in right where a.Kind == b.Kind
                              let distance = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2))
                              orderby distance select (Left: a, Right: b, Distance: distance)))
        {
            if (usedLeft.Contains(pair.Left) || usedRight.Contains(pair.Right)) continue;
            usedLeft.Add(pair.Left); usedRight.Add(pair.Right); pairs.Add(pair);
        }
        return new
        {
            configuredWidth800 = 800, effectiveWidth800 = leftWidth, configuredWidth1000 = 1000, effectiveWidth1000 = rightWidth,
            matchedSameColour = pairs.Count, unmatched800 = left.Length - pairs.Count, unmatched1000 = right.Length - pairs.Count,
            maximumTableDisplacement = pairs.Count == 0 ? (double?)null : pairs.Max(p => p.Distance),
            meanTableDisplacement = pairs.Count == 0 ? (double?)null : pairs.Average(p => p.Distance),
            idSuffixAgreement = pairs.Count == 0 ? (double?)null : pairs.Count(p => p.Left.Id == p.Right.Id) / (double)pairs.Count,
            pairs = pairs.Select(p => new { kind = p.Left.Kind, id800 = p.Left.Id, id1000 = p.Right.Id, p.Distance })
        };
    }
    readonly record struct Located(string Kind, string Id, double X, double Y);
    static string Kind(string id) { int separator = id.IndexOf('-'); return separator > 0 ? id[..separator] : id; }
    static void DumpPixels(byte[] encodedImage, TableCalibration calibration, string root, string id)
    {
        // Explicit test-only export, at most six inputs / 42 MB, no app/game state.
        using var encoded = new Org.Opencv.Core.MatOfByte(encodedImage);
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
