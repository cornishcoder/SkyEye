using System.Text.Json;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using Point = SkyEye.Core.Point;
using CvPoint = Org.Opencv.Core.Point;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.CueBallRecoveryInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class CueBallRecoveryInstrumentation : global::Android.App.Instrumentation
{
    readonly List<object> rows = [];
    bool includeSaved;
    public CueBallRecoveryInstrumentation() { }
    public CueBallRecoveryInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args)
    { includeSaved = args?.GetString("saved") == "1"; base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart(); object report; bool passed = false;
        try { report = Check(); passed = true; }
        catch (Exception error) { report = new { passed = false, error = error.ToString(), rows }; }
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "cue-ball-recovery-proof.json"), json);
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", passed ? "Cue ball recovery checks passed" : json);
        Finish(passed ? global::Android.App.Result.Ok : global::Android.App.Result.Canceled, bundle);
    }

    object Check()
    {
        var syntheticCalibration = new TableCalibration([new(1, 0), new(1, 1), new(0, 1), new(0, 0)]);
        VisionResult? lastBaseline = null;
        VisionResult Pair(string name, byte[] jpeg, TableCalibration calibration, VisionSettings settings)
        {
            var before = new VisionAnalyzer(settings with { FreshCueBallRecovery = false }).Analyze(jpeg, calibration);
            lastBaseline = before;
            var after = new VisionAnalyzer(settings with { FreshCueBallRecovery = true }).Analyze(jpeg, calibration);
            rows.Add(new { name, settings, pixelSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(jpeg)).ToLowerInvariant(),
                before, after, cueImageCentres = after.Balls.Where(b => b.Kind == "cue").Select(b => calibration.Unmap(b.Position)).ToArray() });
            return after;
        }
        int CueCount(VisionResult result) => result.Balls.Count(b => b.Kind == "cue");
        foreach (int level in new[] { 96, 160, 230 })
        {
            var result = Pair("synthetic-neutral-body-" + level, Synthetic(level), syntheticCalibration,
                new VisionSettings { InitialVotes = 80 });
            Require(CueCount(result) == 1, "Neutral-body recovery missed a supported synthetic cue ball at level " + level);
            Require(result.Recognition!.CueBallRecovery is { InitialCueBallCount: 0, NeutralBodiesRecovered: 1 },
                "Synthetic initial-pass dropout did not exercise neutral-body recovery");
        }
        var retained = Pair("synthetic-normalized-proposal-loss", Synthetic(160), syntheticCalibration,
            new VisionSettings { NormalizedVotes = 80 });
        Require(CueCount(retained) == 1 && retained.Recognition!.CueBallRecovery is
            { BeforeNormalizationCueBallCount: 1, GeometryRetained: true },
            "A cue ball observed before colour correction disappeared when normalized Hough was forced to miss it");
        foreach (string scene in new[] { "empty", "stripes", "number-patch" })
        {
            var result = Pair("synthetic-negative-" + scene, Synthetic(160, scene), syntheticCalibration, new VisionSettings());
            Require(CueCount(result) == 0 && result.Uncertain, "False cue ball on synthetic " + scene);
        }
        var weak = Pair("synthetic-unsupported-boundary", Synthetic(160, "soft-glare"), syntheticCalibration,
            new VisionSettings { InitialVotes = 80, NormalizedVotes = 80 });
        Require(CueCount(weak) == 0, "Soft neutral glare passed the current-image boundary gate");
        var competing = Pair("synthetic-two-neutral-bodies", Synthetic(160, "two"), syntheticCalibration, new VisionSettings { InitialVotes = 80 });
        Require(CueCount(competing) == 0 && competing.Uncertain && competing.Recognition!.CueBallRecovery!.BeforeNormalizationCueBallCount == 2,
            "Competing neutral bodies were resolved using inventory rather than left uncertain");
        // Reuse one analyzer across movement and disappearance: it must find the
        // new centre from this image and never carry its previous cue forward.
        var reused = new VisionAnalyzer();
        var first = reused.Analyze(Synthetic(160), syntheticCalibration);
        var moved = reused.Analyze(Synthetic(160, "moved"), syntheticCalibration);
        var missing = reused.Analyze(Synthetic(160, "empty"), syntheticCalibration);
        Require(CueCount(first) == 1 && CueCount(moved) == 1
            && (first.Balls.Single(b => b.Kind == "cue").Position - moved.Balls.Single(b => b.Kind == "cue").Position).Length > .1
            && CueCount(missing) == 0, "Recovery retained stale cue ball geometry after movement/removal");
        rows.Add(new { name = "synthetic-freshness-sequence", first, moved, missing });

        using var ordinaryLabels = Labels("pixel-table");
        using var spreadLabels = Labels("pixel-spread-dim");
        foreach (var fixture in new[] {
            ("pixel-table", ordinaryLabels), ("synthetic-dim", ordinaryLabels), ("pixel-later-light", ordinaryLabels),
            ("pixel-cushion-shadow", ordinaryLabels), ("pixel-spread-dim", spreadLabels), ("pixel-spread-snapshot", spreadLabels) })
        {
            var calibration = new TableCalibration(fixture.Item2.RootElement.GetProperty("calibration").EnumerateArray()
                .Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
            var bytes = Asset(fixture.Item1 + ".jpg");
            var result = Pair(fixture.Item1, bytes, calibration, new VisionSettings());
            var old = lastBaseline!;
            if (CueCount(old) == 1) Require(CueCount(result) == 1, "Cue ball presence regressed on saved fixture " + fixture.Item1);
            int Matched(VisionResult vision, string kind)
            {
                var available = vision.Balls.Where(b => b.Kind == kind).ToList(); int matched = 0;
                foreach (var expected in fixture.Item2.RootElement.GetProperty("expected").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == kind))
                {
                    var image = expected.GetProperty("image");
                    var target = calibration.Map(new(image.GetProperty("x").GetDouble(), image.GetProperty("y").GetDouble()));
                    var nearest = available.MinBy(b => (b.Position - target).Length);
                    if (nearest is not null && (nearest.Position - target).Length <= .027) { available.Remove(nearest); matched++; }
                }
                return matched;
            }
            var correspondence = new[] { "red", "yellow", "black" }.Select(kind => new { kind, before = Matched(old, kind), after = Matched(result, kind) }).ToArray();
            rows.Add(new { name = fixture.Item1 + "-label-correspondence", correspondence });
            Require(correspondence.All(c => c.after >= c.before), "True non-cue detections regressed on saved fixture " + fixture.Item1);
            if (CueCount(result) == 1)
            {
                var label = fixture.Item2.RootElement.GetProperty("expected").EnumerateArray().Single(p => p.GetProperty("kind").GetString() == "cue");
                var image = label.GetProperty("image");
                double error = (result.Balls.Single(b => b.Kind == "cue").Position
                    - calibration.Map(new(image.GetProperty("x").GetDouble(), image.GetProperty("y").GetDouble()))).Length;
                Require(error <= .025, "Recovered cue ball centre exceeds saved fixture tolerance: " + fixture.Item1 + " " + error);
            }
        }
        if (includeSaved)
        {
            string root = Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, "cue-ball-development");
            var paths = Directory.EnumerateFiles(root, "*.label.json").Order().Take(3).ToArray();
            Require(paths.Length is >= 1 and <= 2, "Saved cue development replay accepts at most two explicit captures");
            foreach (string path in paths)
            {
                using var receipt = JsonDocument.Parse(File.ReadAllBytes(path)); var label = receipt.RootElement;
                var frame = label.GetProperty("indexedFrame"); string file = frame.GetProperty("pixelFile").GetString()!;
                Require(file == Path.GetFileName(file), "Saved cue image leaves the capture directory");
                string imagePath = Path.Combine(root, file);
                Require(new FileInfo(imagePath).Length <= 4 * 1024 * 1024, "Saved cue image exceeds capture size bound");
                var bytes = File.ReadAllBytes(imagePath);
                Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(
                    frame.GetProperty("pixelSha256").GetString(), StringComparison.OrdinalIgnoreCase), "Saved cue image hash mismatch");
                var calibration = new TableCalibration(label.GetProperty("calibrationCorners").EnumerateArray()
                    .Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
                var result = Pair("partial-label-development-" + frame.GetProperty("frameId").GetInt64(), bytes, calibration, new VisionSettings());
                var cueLabels = label.GetProperty("balls").EnumerateArray().Where(b => b.GetProperty("colour").GetString() == "cue").ToArray();
                if (cueLabels.Length == 1)
                {
                    Require(CueCount(result) == 1, "Manually labelled saved cue ball missing in development replay");
                    // These old, incomplete labels identify the intended ball;
                    // their loose/off-centre box is not precise position truth.
                    if (frame.GetProperty("pixelSha256").GetString() == "e59b05d620bf1459a49a38c7356be1f8cf64eb8476f2b90ee183f71215957c2d")
                    {
                        // Manually inspected source-image ROI around the clearly
                        // visible cue ball, separate from the inaccurate saved box.
                        var source = calibration.Unmap(result.Balls.Single(b => b.Kind == "cue").Position);
                        Require(source.X is > .61 and < .645 && source.Y is > .65 and < .70,
                            "Saved replay recovered a cue candidate outside the visually reviewed cue ball region");
                    }
                }
            }
        }
        return new { passed = true, model = global::Android.OS.Build.Model,
            scope = "Controlled native synthetic failures/negatives and saved development replays. Partial labels validate only a marked cue; they are excluded from O09 full-table training/comparison. Not a lighting holdout or current-room accuracy result",
            configuration = new VisionAnalyzer().ConfigurationIdentity, rows };
    }

    JsonDocument Labels(string name) => JsonDocument.Parse(Asset(name + ".json"));
    byte[] Asset(string name)
    {
        using var stream = TargetContext!.Assets!.Open(name); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    static byte[] Synthetic(int cueBrightness, string scene = "cue")
    {
        if (!Org.Opencv.Android.OpenCVLoader.InitLocal()) throw new IOException("OpenCV not available");
        using var cloth = new Scalar(70, 65, 60); using var frame = new Mat(2000, 1000, CvType.Cv8uc3, cloth);
        using var white = new Scalar(cueBrightness, cueBrightness, cueBrightness);
        if (scene == "stripes")
        {
            foreach (int x in new[] { 240, 500, 730 })
            {
                using var a = new CvPoint(x, 0); using var b = new CvPoint(x + 45, 1999); Imgproc.Rectangle(frame, a, b, white, -1);
            }
        }
        else if (scene == "number-patch")
        {
            using var centre = new CvPoint(500, 700); using var black = new Scalar(25, 25, 25);
            Imgproc.Circle(frame, centre, 27, black, -1); Imgproc.Circle(frame, centre, 12, white, -1);
        }
        else if (scene != "empty")
        {
            using var centre = new CvPoint(scene == "moved" ? 650 : 500, 700);
            Imgproc.Circle(frame, centre, 27, white, -1);
            if (scene == "two") { using var other = new CvPoint(500, 1300); Imgproc.Circle(frame, other, 27, white, -1); }
        }
        if (scene == "soft-glare")
        {
            using var blur = new Size(101, 101); Imgproc.GaussianBlur(frame, frame, blur, 25);
        }
        using var encoded = new MatOfByte(); Imgcodecs.Imencode(".jpg", frame, encoded); return encoded.ToArray();
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
