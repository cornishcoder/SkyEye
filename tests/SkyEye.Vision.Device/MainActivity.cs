using Android.App;
using Android.OS;
using Android.Widget;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using Stopwatch = System.Diagnostics.Stopwatch;
using Cv = Org.Opencv.Core.Core;
using SkyEye.Vision.Android;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Point = Org.Opencv.Core.Point;
using System.Text.Json;

namespace SkyEye.Vision.Device;

[Activity(Name = "uk.co.skyeye.visionchecks.MainActivity", Label = "SkyEye Vision Checks", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state); var text = new TextView(this) { Text = "Checking C# OpenCV image operations…", TextSize = 18 }; SetContentView(text);
        _ = Task.Run(() =>
        {
            object result;
            try
            {
                var watch = Stopwatch.StartNew();
                if (!OpenCVLoader.InitLocal()) throw new Exception("OpenCV library did not load");
                using var stream = Assets!.Open("pixel-table.jpg"); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                using var encoded = new MatLease(new MatOfByte(bytes.ToArray())); using var frame = new MatLease(Imgcodecs.Imdecode(encoded.Value, Imgcodecs.ImreadColor));
                using var hsv = new MatLease(new Mat()); using var mask = new MatLease(new Mat()); using var cleaned = new MatLease(new Mat()); using var circles = new MatLease(new Mat());
                Imgproc.CvtColor(frame.Value, hsv.Value, Imgproc.ColorBgr2hsv);
                using var low = new Scalar(0, 70, 70); using var high = new Scalar(179, 255, 255); Cv.InRange(hsv.Value, low, high, mask.Value);
                using var size = new Size(3, 3); using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, size));
                Imgproc.MorphologyEx(mask.Value, cleaned.Value, Imgproc.MorphOpen, kernel.Value);
                Imgproc.HoughCircles(cleaned.Value, circles.Value, Imgproc.HoughGradient, 1.3, 35, 150, 15, 15, 35);
                using var src = new MatLease(new MatOfPoint2f(new Point(150, 160), new Point(1725, 181), new Point(1702, 1046), new Point(124, 929)));
                using var dst = new MatLease(new MatOfPoint2f(new Point(1000, 0), new Point(1000, 2000), new Point(0, 2000), new Point(0, 0)));
                using var transform = new MatLease(Imgproc.GetPerspectiveTransform(src.Value, dst.Value)); using var warped = new MatLease(new Mat()); using var outputSize = new Size(1000, 2000);
                Imgproc.WarpPerspective(frame.Value, warped.Value, transform.Value, outputSize);
                if (warped.Value.Rows() != 2000 || warped.Value.Cols() != 1000 || frame.Value.Channels() != 3) throw new Exception("Image primitive shape mismatch");
                using var labelsReader = new StreamReader(Assets.Open("pixel-table.json")); using var labels = JsonDocument.Parse(labelsReader.ReadToEnd());
                var calibration = new TableCalibration(labels.RootElement.GetProperty("calibration").EnumerateArray().Select(p => new SkyEye.Core.Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
                var analyzer = new VisionAnalyzer(); var analyzed = analyzer.Analyze(bytes.ToArray(), calibration);
                if (analyzed.Uncertain || analyzed.Balls.Length != 16 || analyzed.Balls.Count(b => b.Kind == "red") != 7 || analyzed.Balls.Count(b => b.Kind == "yellow") != 7 || analyzed.Balls.Count(b => b.Kind == "cue") != 1 || analyzed.Balls.Count(b => b.Kind == "black") != 1) throw new Exception("Real-frame classification failed: " + JsonSerializer.Serialize(analyzed));
                double error = 0; var remaining = analyzed.Balls.ToList();
                foreach (var expected in labels.RootElement.GetProperty("expected").EnumerateArray())
                {
                    var p = expected.GetProperty("image"); var image = new SkyEye.Core.Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()); string kind = expected.GetProperty("kind").GetString()!;
                    var ball = remaining.Where(b => b.Kind == kind).MinBy(b => (calibration.Unmap(b.Position) - image).Length) ?? throw new Exception("Missing labelled ball");
                    error = Math.Max(error, (ball.Position - calibration.Map(image)).Length); remaining.Remove(ball);
                }
                if (error > .015) throw new Exception("Ball-centre error exceeds fixture tolerance: " + error);
                var negativeResults = new List<object>();
                using var spreadLabelsReader = new StreamReader(Assets.Open("pixel-spread-dim.json"));
                using var spreadLabels = JsonDocument.Parse(spreadLabelsReader.ReadToEnd());
                var spreadCalibration = new TableCalibration(spreadLabels.RootElement.GetProperty("calibration").EnumerateArray()
                    .Select(p => new SkyEye.Core.Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
                using var spreadInput = Assets.Open("pixel-spread-dim.jpg"); using var spreadBytes = new MemoryStream(); spreadInput.CopyTo(spreadBytes);
                var spread = analyzer.Analyze(spreadBytes.ToArray(), spreadCalibration);
                if (spread.Uncertain || spread.Balls.Length != 16 || spread.Balls.Count(b => b.Kind == "red") != 7 || spread.Balls.Count(b => b.Kind == "yellow") != 7 || spread.Balls.Count(b => b.Kind == "cue") != 1 || spread.Balls.Count(b => b.Kind == "black") != 1)
                    throw new Exception("Dim spread regression: " + JsonSerializer.Serialize(spread));
                double spreadError = 0; var spreadRemaining = spread.Balls.ToList();
                foreach (var expected in spreadLabels.RootElement.GetProperty("expected").EnumerateArray())
                {
                    var p = expected.GetProperty("image"); var image = new SkyEye.Core.Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble());
                    var ball = spreadRemaining.Where(b => b.Kind == expected.GetProperty("kind").GetString()).MinBy(b => (spreadCalibration.Unmap(b.Position) - image).Length) ?? throw new Exception("Missing dim spread ball");
                    spreadError = Math.Max(spreadError, (ball.Position - spreadCalibration.Map(image)).Length); spreadRemaining.Remove(ball);
                }
                if (spreadError > .025) throw new Exception("Dim spread centre outside tolerance: " + spreadError);
                negativeResults.Add(new { name = "pixel-spread-dim", spread, maximumCentreError = spreadError });
                var spreadNow = DateTimeOffset.UtcNow;
                var spreadTable = new TableState(1, spreadNow, spread.Balls, "open", true, false, true, "fixture-spread");
                var spreadShots = ShotPlanner.Suggest(spreadTable, spreadNow);
                if (spreadShots.Length == 0) throw new Exception("Clear dim spread rejected by planner");
                negativeResults.Add(new { name = "pixel-spread-candidates", eligible = ShotPlanner.Eligible(spreadTable, spreadNow), spreadShots, timestampMeaning = "Freshness and review are test input, not live approval." });
                using var snapshotInput = Assets.Open("pixel-spread-snapshot.jpg"); using var snapshotBytes = new MemoryStream(); snapshotInput.CopyTo(snapshotBytes);
                var spreadSnapshot = analyzer.Analyze(snapshotBytes.ToArray(), spreadCalibration);
                if (spreadSnapshot.Uncertain || spreadSnapshot.Balls.Length != 16 || spreadSnapshot.Balls.Count(b => b.Kind == "red") != 7 || spreadSnapshot.Balls.Count(b => b.Kind == "yellow") != 7 || spreadSnapshot.Balls.Count(b => b.Kind == "cue") != 1 || spreadSnapshot.Balls.Count(b => b.Kind == "black") != 1)
                    throw new Exception("Live snapshot regression: " + JsonSerializer.Serialize(spreadSnapshot));
                negativeResults.Add(new { name = "pixel-spread-snapshot", spread = spreadSnapshot });
                foreach (string name in new[] { "pixel-later-light", "pixel-cushion-shadow", "synthetic-empty", "synthetic-dim", "synthetic-dark", "synthetic-obstruction", "synthetic-edge-obstruction", "synthetic-pocket-ball" })
                {
                    using var input = Assets.Open(name + ".jpg"); using var buffer = new MemoryStream(); input.CopyTo(buffer);
                    var variant = analyzer.Analyze(buffer.ToArray(), calibration);
                    if (name is "synthetic-dim" or "pixel-later-light" or "pixel-cushion-shadow" && (variant.Uncertain || variant.Balls.Length != 16 || variant.Balls.Count(b => b.Kind == "red") != 7 || variant.Balls.Count(b => b.Kind == "yellow") != 7)) throw new Exception("Clear-frame regression: " + JsonSerializer.Serialize(variant));
                    if (name is not ("synthetic-dim" or "pixel-later-light" or "pixel-cushion-shadow") && !variant.Uncertain) throw new Exception("Negative frame was accepted: " + name);
                    if (name.Contains("obstruction") && !variant.Obstructed) throw new Exception("Obstruction not flagged: " + name);
                    if (name == "synthetic-empty" && analyzer.FindCorners(buffer.ToArray()) is not null) throw new Exception("Invented automatic corners on empty cloth");
                    negativeResults.Add(new { name, count = variant.Balls.Length, variant.Uncertain, variant.Obstructed, variant.Warnings, variant.ElapsedMs });
                }
                using var rackLabelsReader = new StreamReader(Assets.Open("pixel-racked-dim.json"));
                using var rackLabels = JsonDocument.Parse(rackLabelsReader.ReadToEnd());
                var rackCalibration = new TableCalibration(rackLabels.RootElement.GetProperty("calibration").EnumerateArray()
                    .Select(p => new SkyEye.Core.Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
                using var rackInput = Assets.Open("pixel-racked-dim.jpg"); using var rackBytes = new MemoryStream(); rackInput.CopyTo(rackBytes);
                var rack = analyzer.Analyze(rackBytes.ToArray(), rackCalibration);
                var rackPockets = analyzer.FindPockets(rackBytes.ToArray()) ?? throw new Exception("Automatic pockets missing on real dim rack");
                var expectedPockets = rackLabels.RootElement.GetProperty("pocketCentres").EnumerateArray()
                    .Select(p => new SkyEye.Core.Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray();
                double pocketError = rackPockets.Corners.Zip(expectedPockets).Max(p => (p.First - p.Second).Length);
                if (pocketError > .02) throw new Exception("Automatic pocket centre outside labelled tolerance: " + pocketError);
                using var emptyInput = Assets.Open("synthetic-empty.jpg"); using var emptyBytes = new MemoryStream(); emptyInput.CopyTo(emptyBytes);
                if (analyzer.FindPockets(emptyBytes.ToArray()) is not null) throw new Exception("Invented pockets on empty image");
                negativeResults.Add(new { name = "automatic-dim-rack-pockets", rackPockets, maximumCentreError = pocketError });
                foreach (string pocketName in new[] { "pixel-table", "pixel-later-light", "pixel-cushion-shadow", "synthetic-dim", "synthetic-dark" })
                {
                    using var pocketInput = Assets.Open(pocketName + ".jpg"); using var pocketBytes = new MemoryStream(); pocketInput.CopyTo(pocketBytes);
                    var pockets = analyzer.FindPockets(pocketBytes.ToArray()) ?? throw new Exception("Automatic pockets missing: " + pocketName);
                    negativeResults.Add(new { name = "automatic-pockets-" + pocketName, pockets });
                }
                using var obscuredEncoded = new MatLease(new MatOfByte(rackBytes.ToArray()));
                using var obscured = new MatLease(Imgcodecs.Imdecode(obscuredEncoded.Value, Imgcodecs.ImreadColor));
                using var coverA = new Point(0, 0); using var coverB = new Point(260, 260); using var coverInk = new Scalar(70, 70, 70);
                Imgproc.Rectangle(obscured.Value, coverA, coverB, coverInk, -1);
                using var obscuredJpeg = new MatOfByte(); Imgcodecs.Imencode(".jpg", obscured.Value, obscuredJpeg);
                if (analyzer.FindPockets(obscuredJpeg.ToArray()) is not null) throw new Exception("Missing corner pocket invented under obstruction");
                // Fresh/settled/confirmed is deliberately the strongest possible admission attempt.
                // This replay timestamp is test input, not a claim about the recorded frame's capture time.
                var rackNow = DateTimeOffset.UtcNow;
                var rackTable = new TableState(1, rackNow, rack.Balls, "open", true, rack.Uncertain || rack.Obstructed, true, "fixture-rack");
                bool rackEligible = ShotPlanner.Eligible(rackTable, rackNow);
                if (!rack.Uncertain && !rack.Obstructed || rackEligible) throw new Exception("Unresolved rack accepted for shot advice");
                negativeResults.Add(new { name = "pixel-racked-dim", count = rack.Balls.Length, rack.Uncertain, rack.Obstructed, rack.Warnings, rack.ElapsedMs, eligible = rackEligible });
                bool invalidRejected = false; try { analyzer.Analyze([1, 2, 3, 4], calibration); } catch (IOException) { invalidRejected = true; }
                if (!invalidRejected) throw new Exception("Invalid JPEG accepted");
                byte[] imageBytes = bytes.ToArray(); analyzer.Analyze(imageBytes, calibration); GC.Collect(); GC.WaitForPendingFinalizers();
                long heapBefore = global::Android.OS.Debug.NativeHeapAllocatedSize; var times = new List<double>();
                for (int i = 0; i < 12; i++)
                {
                    var repeated = analyzer.Analyze(imageBytes, calibration);
                    if (repeated.Uncertain || repeated.Balls.Length != 16 || repeated.Balls.Count(b => b.Kind == "red") != 7 || repeated.Balls.Count(b => b.Kind == "yellow") != 7) throw new Exception("Repeated detection drift: " + JsonSerializer.Serialize(repeated)); times.Add(repeated.ElapsedMs);
                    // Let normal allocation pressure drive GC during the timing sample.
                    // Forced collection belongs to the separate retained-memory check.
                }
                GC.Collect(); GC.WaitForPendingFinalizers();
                long heapAfter = global::Android.OS.Debug.NativeHeapAllocatedSize;
                var sorted = times.Order().ToArray(); double p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
                if (heapAfter - heapBefore > 12 * 1024 * 1024) throw new Exception($"Native allocation growth exceeded the memory budget: {heapAfter - heapBefore}");
                result = new { passed = true, version = Cv.Version, model = Build.Model, analyzed, maximumTableCentreError = error, negatives = negativeResults,
                    performance = new { analysisWidth = 1000, iterations = times.Count, samplesMs = times, targetMs = 1000, targetMet = p95 < 1000, p95Ms = p95, medianMs = sorted[sorted.Length / 2], heapBefore, heapAfter, nativeGrowthBytes = heapAfter - heapBefore }, elapsedMs = watch.Elapsed.TotalMilliseconds };
            }
            catch (Exception e) { result = new { passed = false, error = e.ToString() }; }
            string json = JsonSerializer.Serialize(result); File.WriteAllText(Path.Combine(FilesDir!.AbsolutePath, "vision-checks.json"), json);
            // Test-only report export also works for trimmed, non-debuggable Release APKs.
            string? external = GetExternalFilesDir(null)?.AbsolutePath;
            if (external is not null) File.WriteAllText(Path.Combine(external, "vision-checks.json"), json);
            RunOnUiThread(() => text.Text = json);
        });
    }
    sealed class MatLease(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() { Value.Release(); Value.Dispose(); } }
}
