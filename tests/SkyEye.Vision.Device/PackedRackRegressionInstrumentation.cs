using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using Cv = Org.Opencv.Core.Core;
using CvPoint = Org.Opencv.Core.Point;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name = "uk.co.skyeye.visionchecks.PackedRackRegressionInstrumentation", TargetPackage = "uk.co.skyeye.visionchecks", FunctionalTest = true)]
[global::Android.Runtime.Preserve(AllMembers = true)]
public sealed class PackedRackRegressionInstrumentation : global::Android.App.Instrumentation
{
    int? darkBodyCeiling;
    double? packedColourBoundarySearchExtentFraction;
    sealed class ProbeMat(Mat value) : IDisposable
    {
        public Mat Value { get; } = value;
        public void Dispose() { Value.Release(); Value.Dispose(); }
    }
    sealed class WorkingSetProbe : IDisposable
    {
        readonly Process process = Process.GetCurrentProcess();
        readonly long baseline;
        readonly Timer timer;
        long peak;
        public WorkingSetProbe()
        {
            process.Refresh(); baseline = process.WorkingSet64; peak = baseline;
            timer = new Timer(Sample, null, 0, 10);
        }
        void Sample(object? state)
        {
            try
            {
                process.Refresh(); long value = process.WorkingSet64;
                long observed;
                do { observed = Interlocked.Read(ref peak); if (value <= observed) return; }
                while (Interlocked.CompareExchange(ref peak, value, observed) != observed);
            }
            catch (InvalidOperationException) { }
        }
        public long StopAndReadPeakDeltaBytes()
        {
            timer.Dispose(); Sample(null);
            return Math.Max(0, Interlocked.Read(ref peak) - baseline);
        }
        public void Dispose() { timer.Dispose(); process.Dispose(); }
    }
    sealed record PeakEvidence(double X, double Y, double InscribedRadius, int SupportArea);
    sealed record ComponentScan(string Name, int ComponentCount, object[] Regions, PeakEvidence[] CentresOfEvidence);
    sealed record Fixture(string Name, byte[] Image, JsonDocument Labels, TableCalibration Calibration) : IDisposable
    {
        public void Dispose() => Labels.Dispose();
    }

    public PackedRackRegressionInstrumentation() { }
    public PackedRackRegressionInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args)
    {
        if (int.TryParse(args?.GetString("darkCeiling"), out var requestedDarkCeiling)) darkBodyCeiling = requestedDarkCeiling;
        if (darkBodyCeiling is < 10 or > 55) throw new ArgumentOutOfRangeException(nameof(darkBodyCeiling), "Dark-body ceiling must be between 10 and 55.");
        if (double.TryParse(args?.GetString("boundaryExtent"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var requestedBoundaryExtent))
        {
            if (!double.IsFinite(requestedBoundaryExtent) || requestedBoundaryExtent is < .2 or > .8)
                throw new ArgumentOutOfRangeException(nameof(requestedBoundaryExtent), "Packed-colour boundary extent must be between 0.2 and 0.8.");
            packedColourBoundarySearchExtentFraction = requestedBoundaryExtent;
        }
        else if (args?.GetString("boundaryExtent") is { Length: > 0 })
            throw new ArgumentException("Packed-colour boundary extent must be an invariant-culture number.");
        base.OnCreate(args); Start();
    }

    Fixture ReadFixture(string name, string? labelsName = null, string extension = ".jpg")
    {
        using var image = TargetContext!.Assets!.Open(name + extension);
        using var bytes = new MemoryStream(); image.CopyTo(bytes);
        using var labels = TargetContext.Assets.Open((labelsName ?? name) + ".json");
        var document = JsonDocument.Parse(labels);
        if (document.RootElement.TryGetProperty("pixelSha256", out var expectedHash) &&
            Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant() != expectedHash.GetString())
        {
            document.Dispose();
            throw new IOException("Packed-rack fixture image does not match its reviewed receipt.");
        }
        var calibration = new TableCalibration(document.RootElement.GetProperty("calibration").EnumerateArray()
            .Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
        return new(name, bytes.ToArray(), document, calibration);
    }

    static object AnalyzeExpected(Fixture fixture, VisionResult result, double tolerance = .03, int analysisWidth = 1000)
    {
        var remaining = result.Balls.ToList(); var missing = new List<string>(); double maximumError = 0;
        foreach (var expected in fixture.Labels.RootElement.GetProperty("expected").EnumerateArray())
        {
            string kind = expected.GetProperty("kind").GetString()!;
            JsonElement image = expected.TryGetProperty("image", out var nested) ? nested : expected;
            var centre = new Point(image.GetProperty("x").GetDouble(), image.GetProperty("y").GetDouble());
            var match = remaining.Where(b => b.Kind == kind)
                .MinBy(b => (fixture.Calibration.Unmap(b.Position) - centre).Length);
            if (match is null) { missing.Add(kind); continue; }
            double error = (fixture.Calibration.Unmap(match.Position) - centre).Length;
            if (error > tolerance) { missing.Add(kind + "@" + error.ToString("F3")); continue; }
            maximumError = Math.Max(maximumError, error); remaining.Remove(match);
        }
        bool exact = missing.Count == 0 && remaining.Count == 0 && !result.Uncertain && !result.Obstructed;
        return new { exact, maximumImageCentreError = maximumError, missing, extras = remaining,
            result.Uncertain, result.Obstructed, ballCount = result.Balls.Length,
            counts = result.Balls.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count()),
            candidates = result.Circles.Select(c => new { c.Pattern,
                imageCentre = fixture.Calibration.Unmap(new Point(c.Centre.Y / analysisWidth, 1 - c.Centre.X / analysisWidth)), c.Radius }),
            result.CircleCount, result.Normalized, result.ClothHue, result.Recognition, result.Warnings, result.ElapsedMs,
            result.PackedCluster, result.PackedClusterFallback };
    }

    static bool RetryWasClusterGated(VisionResult result) => result.PackedClusterFallback is null ||
        result.PackedClusterFallback is { Attempted: true, PrimaryClusterDetected: true, PrimaryClusterComplete: false,
            PrimaryAnalysisWidth: < 1000, FallbackAnalysisWidth: 1000 };

    static byte[] CreatePackedRackObstruction(Fixture fixture)
    {
        using var encoded = new ProbeMat(new MatOfByte(fixture.Image));
        using var image = new ProbeMat(Imgcodecs.Imdecode(encoded.Value, Imgcodecs.ImreadColor));
        var rackCentres = fixture.Labels.RootElement.GetProperty("expected").EnumerateArray()
            .Where(ball => ball.GetProperty("kind").GetString() != "cue")
            .Select(ball => ball.TryGetProperty("image", out var nested) ? nested : ball)
            .ToArray();
        if (rackCentres.Length != 15) throw new IOException("Packed-rack obstruction control needs 15 labelled balls.");
        double x = rackCentres.Average(point => point.GetProperty("x").GetDouble()) * image.Value.Cols();
        double y = rackCentres.Average(point => point.GetProperty("y").GetDouble()) * image.Value.Rows();
        using var centre = new CvPoint(x, y); using var handColour = new Scalar(70, 130, 190);
        Imgproc.Circle(image.Value, centre, (int)Math.Round(image.Value.Rows() * .09), handColour, -1);
        using var result = new MatOfByte();
        if (!Imgcodecs.Imencode(".jpg", image.Value, result)) throw new IOException("Could not encode packed-rack obstruction control.");
        return result.ToArray();
    }

    static byte[] CreateLocalizedClusterObstruction(Fixture fixture)
    {
        using var encoded = new ProbeMat(new MatOfByte(fixture.Image));
        using var image = new ProbeMat(Imgcodecs.Imdecode(encoded.Value, Imgcodecs.ImreadColor));
        using var centre = new CvPoint(.65 * image.Value.Cols(), .50 * image.Value.Rows());
        using var handColour = new Scalar(70, 130, 190);
        Imgproc.Circle(image.Value, centre, (int)Math.Round(image.Value.Rows() * .15), handColour, -1);
        using var result = new MatOfByte();
        if (!Imgcodecs.Imencode(".jpg", image.Value, result)) throw new IOException("Could not encode localized cluster obstruction control.");
        return result.ToArray();
    }

    object InspectRackColourComponents(Fixture fixture)
    {
        using var encoded = new ProbeMat(new MatOfByte(fixture.Image));
        using var source = new ProbeMat(Imgcodecs.Imdecode(encoded.Value, Imgcodecs.ImreadColor));
        CvPoint[] original = fixture.Calibration.Corners.Select(p => new CvPoint(p.X * source.Value.Cols(), p.Y * source.Value.Rows())).ToArray();
        CvPoint[] portrait = [new(1000, 0), new(1000, 2000), new(0, 2000), new(0, 0)];
        try
        {
            using var src = new ProbeMat(new MatOfPoint2f(original)); using var dst = new ProbeMat(new MatOfPoint2f(portrait));
            using var transform = new ProbeMat(Imgproc.GetPerspectiveTransform(src.Value, dst.Value));
            using var warped = new ProbeMat(new Mat()); using var outputSize = new Size(1000, 2000);
            Imgproc.WarpPerspective(source.Value, warped.Value, transform.Value, outputSize);
            using var hsv = new ProbeMat(new Mat()); Imgproc.CvtColor(warped.Value, hsv.Value, Imgproc.ColorBgr2hsv);
            var expected = fixture.Labels.RootElement.GetProperty("expected").EnumerateArray()
                .Where(p => p.GetProperty("kind").GetString() != "cue").Select(p =>
            {
                var image = p.TryGetProperty("image", out var nested) ? nested : p;
                var table = fixture.Calibration.Map(new Point(image.GetProperty("x").GetDouble(), image.GetProperty("y").GetDouble()));
                return new Point((1 - table.Y) * 1000, table.X * 1000);
            }).ToArray();
            int left = Math.Max(0, (int)expected.Min(p => p.X) - 80), top = Math.Max(0, (int)expected.Min(p => p.Y) - 80);
            int right = Math.Min(1000, (int)expected.Max(p => p.X) + 81), bottom = Math.Min(2000, (int)expected.Max(p => p.Y) + 81);
            using var roi = new ProbeMat(hsv.Value.Submat(new Rect(left, top, right - left, bottom - top)));
            var coordinates = new AnalysisCoordinates(1000);
            var reviewed = fixture.Labels.RootElement.GetProperty("expected").EnumerateArray().Where(p => p.GetProperty("kind").GetString() != "cue")
                .Select(p =>
                {
                    var image = p.GetProperty("image");
                    var centre = new Point(image.GetProperty("x").GetDouble(), image.GetProperty("y").GetDouble());
                    return (kind: p.GetProperty("kind").GetString()!, imageCentre: centre,
                        analysis: coordinates.FromTable(fixture.Calibration.Map(centre)));
                }).ToArray();
            var red = Components(roi.Value, "red", [new Scalar(0, 100, 30), new Scalar(170, 100, 30)], [new Scalar(10, 255, 255), new Scalar(179, 255, 255)]);
            var yellow = Components(roi.Value, "yellow-orange", [new Scalar(10, 100, 30)], [new Scalar(40, 255, 255)]);
            var dark = Components(roi.Value, "dark-body", [new Scalar(0, 0, 0)], [new Scalar(179, 255, 40)], openMask: true);
            byte[] rawBgr = new byte[warped.Value.Cols() * warped.Value.Rows() * 3];
            if (warped.Value.Get(0, 0, rawBgr) != rawBgr.Length) throw new IOException("Could not read warped rack pixels for boundary checks.");
            Point ImageCentre(Point analysis) => fixture.Calibration.Unmap(coordinates.ToTable(analysis));
            object RefineEvidence(string kind, PeakEvidence peak)
            {
                var proposal = new Point(left + peak.X, top + peak.Y);
                var fit = BallBoundaryRefiner.Refine(rawBgr, 1000, 2000, proposal, PoolGeometry.Radius * 1000, .75);
                var expected = reviewed.Where(p => p.kind == kind).ToArray();
                Point? imageCentre = fit is null ? null : ImageCentre(fit.Centre);
                double? nearestCentreError = imageCentre is null || expected.Length == 0 ? null : expected.Min(p => (p.imageCentre - imageCentre.Value).Length);
                return new { kind, proposal, peak.InscribedRadius, imageCentre, fitRadius = fit?.Radius,
                    boundarySupport = fit?.SupportedSectors, nearestCentreError };
            }
            var supportedCentres = red.CentresOfEvidence.Select(p => RefineEvidence("red", p))
                .Concat(yellow.CentresOfEvidence.Select(p => RefineEvidence("yellow", p)))
                .Concat(dark.CentresOfEvidence.Select(p => RefineEvidence("black", p))).ToArray();
            return new
            {
                region = new { left, top, width = right - left, height = bottom - top },
                expectedCentres = reviewed.Select(p => new { p.kind, x = p.analysis.X - left, y = p.analysis.Y - top }),
                supportedCentres,
                components = new[]
                {
                    red,
                    yellow,
                    Components(roi.Value, "all-colour", [new Scalar(0, 100, 30)], [new Scalar(40, 255, 255)]),
                    dark
                }
            };
        }
        finally { foreach (var point in original.Concat(portrait)) point.Dispose(); }
    }

    static ComponentScan Components(Mat image, string name, Scalar[] lows, Scalar[] highs, bool openMask = false)
    {
        using var combined = new ProbeMat(new Mat());
        foreach (var (low, high) in lows.Zip(highs))
        {
            using var part = new ProbeMat(new Mat()); Cv.InRange(image, low, high, part.Value);
            if (combined.Value.Empty()) part.Value.CopyTo(combined.Value); else Cv.Bitwise_or(combined.Value, part.Value, combined.Value);
            low.Dispose(); high.Dispose();
        }
        if (openMask)
        {
            using var cleanupSize = new Size(3, 3); using var cleanupKernel = new ProbeMat(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, cleanupSize));
            Imgproc.MorphologyEx(combined.Value, combined.Value, Imgproc.MorphOpen, cleanupKernel.Value);
        }
        using var labels = new ProbeMat(new Mat()); using var stats = new ProbeMat(new Mat()); using var centres = new ProbeMat(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(combined.Value, labels.Value, stats.Value, centres.Value);
        var rows = new List<object>();
        for (int i = 1; i < count; i++)
        {
            int width = (int)stats.Value.Get(i, Imgproc.CcStatWidth)![0], height = (int)stats.Value.Get(i, Imgproc.CcStatHeight)![0];
            int area = (int)stats.Value.Get(i, Imgproc.CcStatArea)![0];
            if (area < 40) continue;
            rows.Add(new { x = centres.Value.Get(i, 0)![0], y = centres.Value.Get(i, 1)![0], width, height, area });
        }
        using var distance = new ProbeMat(new Mat()); Imgproc.DistanceTransform(combined.Value, distance.Value, Imgproc.DistL2, 3);
        using var dilated = new ProbeMat(new Mat()); using var kernelSize = new Size(17, 17);
        using var kernel = new ProbeMat(Imgproc.GetStructuringElement(Imgproc.MorphRect, kernelSize));
        Imgproc.Dilate(distance.Value, dilated.Value, kernel.Value);
        using var peaks = new ProbeMat(new Mat()); Cv.Compare(distance.Value, dilated.Value, peaks.Value, Cv.CmpEq);
        using var sufficientlyLarge = new ProbeMat(new Mat()); using var minimumRadius = new Scalar(12);
        Cv.Compare(distance.Value, minimumRadius, sufficientlyLarge.Value, Cv.CmpGt);
        Cv.Bitwise_and(peaks.Value, sufficientlyLarge.Value, peaks.Value);
        using var peakLabels = new ProbeMat(new Mat()); using var peakStats = new ProbeMat(new Mat()); using var peakCentres = new ProbeMat(new Mat());
        int peakCount = Imgproc.ConnectedComponentsWithStats(peaks.Value, peakLabels.Value, peakStats.Value, peakCentres.Value);
        var centresOfEvidence = new List<PeakEvidence>();
        for (int i = 1; i < peakCount; i++)
        {
            double x = peakCentres.Value.Get(i, 0)![0], y = peakCentres.Value.Get(i, 1)![0];
            double radius = distance.Value.Get((int)Math.Round(y), (int)Math.Round(x))![0];
            if (radius >= 12) centresOfEvidence.Add(new(x, y, radius,
                (int)peakStats.Value.Get(i, Imgproc.CcStatArea)![0]));
        }
        return new(name, rows.Count, rows.ToArray(), centresOfEvidence.ToArray());
    }

    public override void OnStart()
    {
        base.OnStart();
        bool passed = false; string? error = null; object? packedMaskDiagnostics = null; var rows = new List<object>();
        try
        {
            if (!OpenCVLoader.InitLocal()) throw new IOException("OpenCV library did not load for packed-rack pixel diagnostics.");
            using var packed = ReadFixture("pixel-packed-rack-2026-10-25");
            packedMaskDiagnostics = InspectRackColourComponents(packed);
            using var ordinary = ReadFixture("pixel-table");
            using var spread = ReadFixture("pixel-spread-dim");
            using var dimRack = ReadFixture("pixel-racked-dim");
            using var shadowedDarkRack = ReadFixture("pixel-packed-rack-shadowed-dark", extension: ".png");
            using var postShotSpread = ReadFixture("pixel-table-post-shot-spread-2026-10-27", extension: ".png");
            byte[] packedRackObstruction = CreatePackedRackObstruction(packed);
            using var empty = ReadFixture("synthetic-empty", "pixel-table");
            using var obstruction = ReadFixture("synthetic-obstruction", "pixel-table");
            var configurations = new (string Name, VisionSettings Settings)[]
            {
                ("production-width-800", new VisionSettings(Width: 800)),
                ("default-width-1000", new VisionSettings()),
                ("moderate-spacing-34", new VisionSettings(MinimumRadius: 16, Separation: 34, InitialVotes: 12, NormalizedVotes: 11)),
                ("tight-spacing-28", new VisionSettings(MinimumRadius: 14, MaximumRadius: 33, Separation: 28, InitialVotes: 10, NormalizedVotes: 9))
            };
            bool controlsPassed = true, packedRecovered = true, productionResolutionPassed = false;
            foreach (var configuration in configurations)
            {
                var settings = configuration.Settings with
                {
                    DarkBodyValueCeiling = darkBodyCeiling ?? configuration.Settings.DarkBodyValueCeiling,
                    PackedColourBoundarySearchExtentFraction = packedColourBoundarySearchExtentFraction ?? .65
                };
                var analyzer = new VisionAnalyzer(settings);
                using var workingSet = new WorkingSetProbe();
                var packedResult = analyzer.Analyze(packed.Image, packed.Calibration);
                long peakWorkingSetDeltaBytes = workingSet.StopAndReadPeakDeltaBytes();
                var packedComparison = AnalyzeExpected(packed, packedResult, analysisWidth: configuration.Settings.Width);
                bool recovered = JsonSerializer.SerializeToElement(packedComparison).GetProperty("exact").GetBoolean()
                    && packedResult.Balls.Length == 16 && !packedResult.Uncertain && !packedResult.Obstructed &&
                    packedResult.Balls.Count(b => b.Kind == "cue") == 1 && packedResult.Balls.Count(b => b.Kind == "red") == 7 &&
                    packedResult.Balls.Count(b => b.Kind == "yellow") == 7 && packedResult.Balls.Count(b => b.Kind == "black") == 1;
                packedRecovered &= recovered;
                var packedNow = DateTimeOffset.UtcNow;
                var packedTable = new TableState(1, packedNow, packedResult.Balls, "open", true,
                    packedResult.Uncertain || packedResult.Obstructed, true, "fixture-packed-rack");
                bool packedEligible = ShotPlanner.Eligible(packedTable, packedNow);
                bool safeFallback = packedResult.Uncertain && !packedEligible &&
                    packedResult.Warnings.Any(w => w.Contains("Packed colour cluster still has unresolved visible balls", StringComparison.Ordinal));
                bool fallbackRecovered = packedResult.PackedClusterFallback is
                    { Attempted: true, PrimaryAnalysisWidth: 800, FallbackAnalysisWidth: 1000,
                        PrimaryClusterDetected: true, PrimaryClusterComplete: false, Recovered: true };
                bool primaryComplete = packedResult.PackedCluster is { Detected: true, Complete: true }
                    && packedResult.PackedClusterFallback is null;
                bool retryGate = RetryWasClusterGated(packedResult);
                if (configuration.Name == "production-width-800")
                    productionResolutionPassed = recovered && (primaryComplete || fallbackRecovered) && retryGate;
                rows.Add(new { configuration = configuration.Name, role = "packed-rack-sweep", packed = packedComparison,
                    packedEligible, safeFallback, primaryComplete, fallbackRecovered, peakWorkingSetDeltaBytes,
                    packedResult.ObstructionAudit,
                    retryGate,
                    peakWorkingSetSampleIntervalMs = 10, analysisPixelAreas = new
                    {
                        primary = 2L * configuration.Settings.Width * configuration.Settings.Width,
                        fallback = packedResult.PackedClusterFallback is { } retry ? 2L * retry.FallbackAnalysisWidth * retry.FallbackAnalysisWidth : 0
                    } });

                foreach (var positive in new[] { ordinary, spread, postShotSpread })
                {
                    var result = analyzer.Analyze(positive.Image, positive.Calibration);
                    double tolerance = positive.Name switch
                    {
                        "pixel-table" => .015,
                        "pixel-table-post-shot-spread-2026-10-27" => .03,
                        _ => .025,
                    };
                    var comparison = AnalyzeExpected(positive, result, tolerance, configuration.Settings.Width);
                    bool good = JsonSerializer.SerializeToElement(comparison).GetProperty("exact").GetBoolean();
                    bool positiveRetryGate = RetryWasClusterGated(result);
                    controlsPassed &= good && positiveRetryGate;
                    rows.Add(new { configuration = configuration.Name, role = "clear-positive-control", fixture = positive.Name, comparison, retryGate = positiveRetryGate });
                    if (positive.Name == "pixel-table-post-shot-spread-2026-10-27")
                    {
                        byte[] localizedObstruction = CreateLocalizedClusterObstruction(positive);
                        var obstructedSpread = analyzer.Analyze(localizedObstruction, positive.Calibration);
                        bool obstructionWasRetained = obstructedSpread.Uncertain && obstructedSpread.Obstructed;
                        controlsPassed &= obstructionWasRetained;
                        rows.Add(new { configuration = configuration.Name, role = "localized-obstruction-over-ball-group-control",
                            passed = obstructionWasRetained, obstructedSpread.Uncertain, obstructedSpread.Obstructed,
                            ballCount = obstructedSpread.Balls.Length, obstructedSpread.ObstructionAudit,
                            obstructedSpread.Warnings, obstructedSpread.ElapsedMs });
                    }
                }
                var emptyResult = analyzer.Analyze(empty.Image, empty.Calibration);
                bool emptySafe = emptyResult.Balls.Length == 0 && emptyResult.Uncertain && !emptyResult.Obstructed;
                bool emptyRetryGate = RetryWasClusterGated(emptyResult);
                controlsPassed &= emptySafe && emptyRetryGate;
                rows.Add(new { configuration = configuration.Name, role = "empty-negative-control", passed = emptySafe,
                    emptyRetryGate, emptyResult.PackedCluster, emptyResult.PackedClusterFallback,
                    emptyResult.Uncertain, emptyResult.Obstructed, ballCount = emptyResult.Balls.Length, emptyResult.Warnings, emptyResult.ElapsedMs });
                var obstructionResult = analyzer.Analyze(obstruction.Image, obstruction.Calibration);
                bool obstructionSafe = obstructionResult.Uncertain && obstructionResult.Obstructed;
                bool obstructionRetryGate = RetryWasClusterGated(obstructionResult);
                controlsPassed &= obstructionSafe && obstructionRetryGate;
                rows.Add(new { configuration = configuration.Name, role = "true-obstruction-control", passed = obstructionSafe,
                    obstructionRetryGate, obstructionResult.PackedCluster, obstructionResult.PackedClusterFallback,
                    obstructionResult.Uncertain, obstructionResult.Obstructed, ballCount = obstructionResult.Balls.Length, obstructionResult.Warnings, obstructionResult.ElapsedMs });
                if (configuration.Name is "production-width-800" or "default-width-1000")
                {
                    var packedObstructionResult = analyzer.Analyze(packedRackObstruction, packed.Calibration);
                    bool packedObstructionSafe = packedObstructionResult.Uncertain && packedObstructionResult.Obstructed;
                    controlsPassed &= packedObstructionSafe;
                    rows.Add(new { configuration = configuration.Name, role = "true-obstruction-over-packed-rack-control", passed = packedObstructionSafe,
                        packedObstructionResult.PackedCluster, packedObstructionResult.PackedClusterFallback,
                        packedObstructionResult.Uncertain, packedObstructionResult.Obstructed,
                        ballCount = packedObstructionResult.Balls.Length, packedObstructionResult.ObstructionAudit,
                        packedObstructionResult.Warnings, packedObstructionResult.ElapsedMs });

                }
                var dimResult = analyzer.Analyze(dimRack.Image, dimRack.Calibration);
                var now = DateTimeOffset.UtcNow;
                var table = new TableState(1, now, dimResult.Balls, "open", true, dimResult.Uncertain || dimResult.Obstructed, true, "fixture-dim-rack");
                bool dimSafe = dimResult.PackedCluster is { Detected: true, Complete: true }
                    && !ShotPlanner.Eligible(table, now);
                bool dimRetryGate = RetryWasClusterGated(dimResult);
                controlsPassed &= dimSafe && dimRetryGate;
                rows.Add(new { configuration = configuration.Name, role = "touching-dim-rack-control", passed = dimSafe,
                    dimRetryGate, dimResult.PackedCluster, dimResult.PackedClusterFallback,
                    dimResult.Uncertain, dimResult.Obstructed, shotAdviceEligible = ShotPlanner.Eligible(table, now),
                    ballCount = dimResult.Balls.Length, dimResult.Warnings, dimResult.ElapsedMs });

                var shadowedDarkRackResult = analyzer.Analyze(shadowedDarkRack.Image, shadowedDarkRack.Calibration);
                bool shadowedDarkRackSafe = shadowedDarkRackResult.PackedCluster is { Detected: true, Complete: true,
                    RedCentres: 7, YellowCentres: 7, BlackCentre: true }
                    && shadowedDarkRackResult.Balls.Length == 16
                    && shadowedDarkRackResult.Balls.Count(b => b.Kind == "cue") == 1
                    && shadowedDarkRackResult.Balls.Count(b => b.Kind == "red") == 7
                    && shadowedDarkRackResult.Balls.Count(b => b.Kind == "yellow") == 7
                    && shadowedDarkRackResult.Balls.Count(b => b.Kind == "black") == 1
                    && !shadowedDarkRackResult.Uncertain && !shadowedDarkRackResult.Obstructed;
                bool shadowedDarkRetryGate = RetryWasClusterGated(shadowedDarkRackResult);
                controlsPassed &= shadowedDarkRackSafe && shadowedDarkRetryGate;
                rows.Add(new { configuration = configuration.Name, role = "shadowed-dark-packed-rack-control",
                    passed = shadowedDarkRackSafe, shadowedDarkRetryGate, shadowedDarkRackResult.PackedCluster,
                    shadowedDarkRackResult.PackedClusterFallback, shadowedDarkRackResult.Uncertain,
                    shadowedDarkRackResult.Obstructed, ballCount = shadowedDarkRackResult.Balls.Length,
                    counts = shadowedDarkRackResult.Balls.GroupBy(b => b.Kind).ToDictionary(g => g.Key, g => g.Count()),
                    shadowedDarkRackResult.Warnings, shadowedDarkRackResult.ElapsedMs });
            }
            passed = controlsPassed && packedRecovered && productionResolutionPassed;
        }
        catch (Exception failure) { error = failure.ToString(); }
        var report = new { passed, error, darkBodyValueCeiling = darkBodyCeiling ?? 40,
            packedColourBoundarySearchExtentFraction = packedColourBoundarySearchExtentFraction ?? .4, measuredAt = DateTimeOffset.UtcNow,
            scope = "On-device OpenCV replay of the exact manually reviewed bright rack and the shadowed-dark packed-rack regression, with a conditional same-frame 1000px retry, sampled process working-set deltas, a compact true-obstruction over the rack, and clear, empty and dim-rack controls. Working-set samples are coarse 10ms process measurements; the shadowed-dark fixture is tuning evidence, not a held-out accuracy claim.", packedMaskDiagnostics, rows };
        string output = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        string root = TargetContext!.GetExternalFilesDir(null)!.AbsolutePath;
        File.WriteAllText(Path.Combine(root, "packed-rack-regression-proof.json"), output);
        using var bundle = new global::Android.OS.Bundle();
        bundle.PutString("result", passed ? "Packed-rack and control replay passed" : "Packed-rack replay or a control failed; inspect report");
        Finish(passed ? global::Android.App.Result.Ok : global::Android.App.Result.Canceled, bundle);
    }
}
