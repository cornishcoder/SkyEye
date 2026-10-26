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
    bool refinement, dump, boundaryAudit, traceDarkMask; string run = "";
    public override void OnCreate(global::Android.OS.Bundle? args) { refinement = args?.GetString("refine") == "1"; dump = args?.GetString("dump") == "1"; boundaryAudit = args?.GetString("audit") == "1"; traceDarkMask = args?.GetString("trace") == "1"; run = args?.GetString("run") ?? ""; if (run.Length > 32 || run.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Invalid replay run name."); base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart(); object report;
        try
        {
            var root = Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, run.Length == 0 ? "o08-replay" : "o08-replay-" + run);
            var paths = Directory.GetFiles(root, "*.json").OrderBy(p => long.Parse(Path.GetFileNameWithoutExtension(p))).ToArray();
            if (paths.Length is < 1 or > 6) throw new IOException("Replay 1 to 6 captured development snapshots.");
            using var first = JsonDocument.Parse(File.ReadAllBytes(paths[0]));
            var firstMetadata = first.RootElement;
            var corners = firstMetadata.GetProperty("calibrationCorners").EnumerateArray().Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray();
            var recordedConfiguration = firstMetadata.TryGetProperty("analyzerConfigurationIdentity", out var configurationElement)
                ? configurationElement.GetString() : null;
            var calibration = new TableCalibration(corners);
            var analyzer800 = new VisionAnalyzer(new VisionSettings(Width: 800) { VerificationBodyRefinement = refinement }, boundaryAudit, boundaryAudit);
            var analyzer1000 = new VisionAnalyzer(new VisionSettings(Width: 1000) { VerificationBodyRefinement = refinement }, boundaryAudit, boundaryAudit);
            string replayConfiguration = analyzer800.ConfigurationIdentity;
            bool? configurationMatchesPrimary = recordedConfiguration is null
                ? null : string.Equals(recordedConfiguration, replayConfiguration, StringComparison.Ordinal);
            bool? configurationSettingsMatchPrimary = recordedConfiguration is null
                ? null : string.Equals(AnalyzerSettings(recordedConfiguration), AnalyzerSettings(replayConfiguration), StringComparison.Ordinal);
            string? recordedAlgorithm = AnalyzerAlgorithm(recordedConfiguration);
            string replayAlgorithm = AnalyzerAlgorithm(replayConfiguration)!;
            bool? configurationAlgorithmChangedSinceCapture = recordedAlgorithm is null
                ? null : !string.Equals(recordedAlgorithm, replayAlgorithm, StringComparison.Ordinal);
            using var worker800 = analyzer800.CreateVerification(calibration);
            using var worker1000 = analyzer1000.CreateVerification(calibration);
            var samples = new List<object>();
            var analyses800 = new List<VisionResult>(); var analyses1000 = new List<VisionResult>();
            var measurements800 = new List<VerificationMeasurement>(); var measurements1000 = new List<VerificationMeasurement>();
            long priorFrameId = long.MinValue, priorSourceTimeUs = long.MinValue;
            string? timeline = null;
            foreach (var path in paths)
            {
                using var metadata = JsonDocument.Parse(File.ReadAllBytes(path)); var metadataRoot = metadata.RootElement;
                var receipt = metadataRoot.GetProperty("indexedFrame");
                var file = receipt.GetProperty("pixelFile").GetString()!;
                if (file != Path.GetFileName(file)) throw new IOException("Replay asset leaves its directory.");
                var bytes = File.ReadAllBytes(Path.Combine(root, file));
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                if (sha != receipt.GetProperty("pixelSha256").GetString()) throw new IOException("Captured pixel asset hash mismatch.");
                var frameId = receipt.GetProperty("frameId").GetInt64();
                var sourceTimeUs = receipt.GetProperty("sourceTimeUs").GetInt64();
                var frameTimeline = receipt.GetProperty("scope").GetProperty("timeline").GetString()!;
                if (frameId <= priorFrameId || sourceTimeUs <= priorSourceTimeUs)
                    throw new IOException("Replay frames must preserve strictly increasing frame IDs and source PTS.");
                if (timeline is not null && !string.Equals(timeline, frameTimeline, StringComparison.Ordinal))
                    throw new IOException("Replay frames cross a captured source timeline.");
                timeline = frameTimeline; priorFrameId = frameId; priorSourceTimeUs = sourceTimeUs;
                var recordedCorners = metadataRoot.GetProperty("calibrationCorners").EnumerateArray()
                    .Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray();
                if (!corners.SequenceEqual(recordedCorners)) throw new IOException("Replay frames do not share one calibration corner set.");
                var frameConfiguration = metadataRoot.TryGetProperty("analyzerConfigurationIdentity", out var frameConfigurationElement)
                    ? frameConfigurationElement.GetString() : null;
                if (!string.Equals(recordedConfiguration, frameConfiguration, StringComparison.Ordinal))
                    throw new IOException("Replay frames do not share one analyzer configuration.");
                var detectionRequested = metadataRoot.TryGetProperty("detectionRequested", out var detectionElement)
                    ? detectionElement.GetBoolean() : true;
                var baseline800 = analyzer800.Analyze(bytes, calibration);
                var baseline1000 = analyzer1000.Analyze(bytes, calibration);
                analyses800.Add(baseline800); analyses1000.Add(baseline1000);
                bool blackSeedRecovered800 = baseline800.CandidateAudits.Any(a => a.Pass == "initial" && a.Stage == "packed-black" && a.Decision == "added-black-body");
                bool blackSeedRecovered1000 = baseline1000.CandidateAudits.Any(a => a.Pass == "initial" && a.Stage == "packed-black" && a.Decision == "added-black-body");
                if (dump) DumpPixels(bytes, calibration, root, Path.GetFileNameWithoutExtension(path));
                var capturedAt = metadataRoot.TryGetProperty("capturedAt", out var capturedAtElement)
                    ? capturedAtElement.GetDateTimeOffset() : DateTimeOffset.UtcNow;
                DecodedFrameReceipt? decodedReceipt = null;
                if (metadataRoot.TryGetProperty("decoder", out var decoder) && decoder.ValueKind == JsonValueKind.Object)
                    decodedReceipt = new(decoder.GetProperty("mediaGeneration").GetInt64(), decoder.GetProperty("frameId").GetInt64(),
                        decoder.GetProperty("presentationTimeUs").GetInt64(), decoder.GetProperty("surfaceTimestampNs").GetInt64(),
                        decoder.GetProperty("width").GetInt32(), decoder.GetProperty("height").GetInt32());
                var frameSource = metadataRoot.TryGetProperty("frameSource", out var sourceElement)
                    ? sourceElement.GetString() ?? "captured-verification" : "captured-verification";
                using var frame = new TableFrame(bytes, capturedAt, frameTimeline)
                {
                    SourceTimeUs = sourceTimeUs,
                    FrameSource = frameSource,
                    DecodedReceipt = decodedReceipt,
                };
                var timer = System.Diagnostics.Stopwatch.StartNew();
                var verification800 = worker800.Measure(frame, detectionRequested, CancellationToken.None); measurements800.Add(verification800);
                var verification1000 = worker1000.Measure(frame, detectionRequested, CancellationToken.None); measurements1000.Add(verification1000);
                var auditComparison = CompareCapturedAudit(metadataRoot, verification800);
                if (verification800.Reason == ObservationReason.Obstructed) worker800.ResetAfterObstruction();
                if (verification1000.Reason == ObservationReason.Obstructed) worker1000.ResetAfterObstruction();
                samples.Add(new
                {
                    frameId, pixelSha256 = sha, sourceTimeUs = frame.SourceTimeUs, capturedAt,
                    detectionRequested, configurationMatchesPrimary,
                    replayObstructionGeometryMatchesCapture = auditComparison.GeometryMatches,
                    replayObstructionDecisionMatchesCapture = auditComparison.DecisionMatches,
                    width800 = new { blackSeedRecovered = blackSeedRecovered800, analysis = Summarize(baseline800, 800), verification = Summarize(verification800, 800),
                        darkMaskTrace = traceDarkMask ? PackedBlackMaskTrace.Capture(bytes, calibration, 800, baseline800, Path.Combine(root, "o08-black-trace-" + run), receipt.GetProperty("frameId").GetInt64().ToString()) : null,
                        packedBlackBoundaryTrace = boundaryAudit ? AuditPackedBlackBoundaries(bytes, calibration, 800, baseline800) : null },
                    width1000 = new { blackSeedRecovered = blackSeedRecovered1000, analysis = Summarize(baseline1000, 1000), verification = Summarize(verification1000, 1000),
                        darkMaskTrace = traceDarkMask ? PackedBlackMaskTrace.Capture(bytes, calibration, 1000, baseline1000, Path.Combine(root, "o08-black-trace-" + run), frameId.ToString()) : null,
                        packedBlackBoundaryTrace = boundaryAudit ? AuditPackedBlackBoundaries(bytes, calibration, 1000, baseline1000) : null },
                    analysisWidthComparison = Compare(baseline800, baseline1000),
                    widthComparison = verification800.Vision is null || verification1000.Vision is null
                        ? null : Compare(verification800.Vision, verification1000.Vision),
                    processingMs = timer.Elapsed.TotalMilliseconds
                });
            }
            var consensus800 = VerificationConsensus.Resolve(measurements800.Where(m => m.Vision is not null).Select(m => m.Vision!).ToArray(), out var conflict800);
            var consensus1000 = VerificationConsensus.Resolve(measurements1000.Where(m => m.Vision is not null).Select(m => m.Vision!).ToArray(), out var conflict1000);
            var analysisConsensus800 = VerificationConsensus.Resolve(analyses800, out var analysisConflict800);
            var analysisConsensus1000 = VerificationConsensus.Resolve(analyses1000, out var analysisConflict1000);
            report = new { completed = true, configuration = ObservationPolicy.VerificationConfiguration, bodyRefinement = refinement, boundaryAudit,
                recordedAnalyzerConfigurationIdentity = recordedConfiguration,
                replayAnalyzerConfigurationIdentity = replayConfiguration,
                configurationMatchesPrimaryWidth = configurationMatchesPrimary,
                configurationSettingsMatchPrimaryWidth = configurationSettingsMatchPrimary,
                recordedAlgorithmIdentity = recordedAlgorithm,
                replayAlgorithmIdentity = replayAlgorithm,
                configurationAlgorithmChangedSinceCapture,
                configurationComparison = configurationMatchesPrimary == true ? "exact-match"
                    : configurationSettingsMatchPrimary == true && configurationAlgorithmChangedSinceCapture == true
                        ? "settings-match; algorithm-revision-changed"
                        : recordedConfiguration is null ? "recorded-identity-unavailable" : "configuration-mismatch",
                fullAnalysis800 = new { consensus = analysisConsensus800, conflict = analysisConflict800, observations = analyses800.Select(v => Summarize(v, 800)) },
                fullAnalysis1000 = new { consensus = analysisConsensus1000, conflict = analysisConflict1000, observations = analyses1000.Select(v => Summarize(v, 1000)) },
                width800 = new { consensus = consensus800, conflict = conflict800, allMotionSamplesStill = measurements800.Count > 1 && measurements800.Skip(1).All(m => m.Motion == TableMotion.Still && m.CoverageValid), measurements = measurements800.Select(SummarizeMotion) },
                width1000 = new { consensus = consensus1000, conflict = conflict1000, allMotionSamplesStill = measurements1000.Count > 1 && measurements1000.Skip(1).All(m => m.Motion == TableMotion.Still && m.CoverageValid), measurements = measurements1000.Select(SummarizeMotion) },
                scope = samples.Count == 1
                    ? "Single captured pixel asset replayed through 800 and 1000 analysis and verification workers. The single frame has no paired motion history; use only per-frame detection audits. Development evidence only; not blind holdout, live pixel pairing or latency acceptance."
                    : "Same captured pixel assets replayed through 800 and 1000 analysis and verification workers. Development evidence only; not blind holdout, live pixel pairing or latency acceptance.", samples };
        }
        catch (Exception error) { report = new { completed = false, error = error.ToString() }; }
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        var reportName = run.Length > 0
            ? $"o08-replay-proof-{run}{(traceDarkMask ? "-blacktrace" : "")}.json"
            : "o08-replay-proof.json";
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, reportName), json);
        using var bundle = new global::Android.OS.Bundle(); bundle.PutString("result", "O08 development replay written"); Finish(global::Android.App.Result.Ok, bundle);
    }
    static string? AnalyzerAlgorithm(string? identity)
    {
        if (identity is null) return null;
        using var document = JsonDocument.Parse(identity);
        return document.RootElement.TryGetProperty("Algorithm", out var algorithm) ? algorithm.GetString() : null;
    }
    static string? AnalyzerSettings(string identity)
    {
        using var document = JsonDocument.Parse(identity);
        return document.RootElement.TryGetProperty("Settings", out var settings) ? settings.GetRawText() : null;
    }
    static object Summarize(VisionResult vision, int width) => new
    {
        configuredWidth = width, geometryWidth = width,
        vision.Uncertain, vision.Obstructed, vision.CircleCount,
        counts = vision.Balls.GroupBy(b => b.Kind).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count()),
        vision.Recognition,
        vision.PackedCluster, vision.PackedClusterFallback,
        vision.ObstructionAudit,
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
        measurement.MaximumDetectedBodyDisplacement, measurement.MinimumTemplateMotionPixels, measurement.SupportedBodies,
        measurement.MaximumTileChange, measurement.SensitiveTileChange, measurement.PhotometricResidualTileChange,
        measurement.PerimeterChange, measurement.IlluminationChange, measurement.MaximumTileOrigin,
        measurement.SensitiveTileOrigin, measurement.PhotometricResidualTileOrigin,
        vision = measurement.Vision is null ? null : Summarize(measurement.Vision, width)
    };
    static object SummarizeMotion(VerificationMeasurement measurement) => new
    {
        measurement.Motion, measurement.Reason, measurement.CoverageValid, measurement.MaximumBodyDisplacement,
        measurement.MaximumDetectedBodyDisplacement, measurement.SupportedBodies
    };
    static (bool? GeometryMatches, bool? DecisionMatches) CompareCapturedAudit(JsonElement metadata, VerificationMeasurement replay)
    {
        ObstructionAuditTelemetry? captured = null;
        JsonElement capturedVision = default;
        if (metadata.TryGetProperty("measurement", out var measurement)
            && measurement.ValueKind == JsonValueKind.Object
            && measurement.TryGetProperty("vision", out var vision)
            && vision.ValueKind == JsonValueKind.Object)
        {
            capturedVision = vision;
            if (vision.TryGetProperty("obstructionAudit", out var audit)
            && audit.ValueKind == JsonValueKind.Object)
                captured = JsonSerializer.Deserialize<ObstructionAuditTelemetry>(audit.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        var actual = replay.Vision?.ObstructionAudit;
        if (captured is null || actual is null) return (captured is null && actual is null, null);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        static object Geometry(ObstructionAuditTelemetry audit) => new
        {
            audit.AnalysisWidth, audit.AnalysisHeight,
            Components = audit.Components.Select(component => new
            {
                component.Label, component.Left, component.Top, component.Width, component.Height, component.Area,
                component.BallCentres, component.BallCentresInBounds,
                component.WholeTableSegmentation, component.PackedClusterSizeEligible,
            }).OrderBy(component => component.Label).ToArray()
        };
        bool geometryMatches = JsonSerializer.Serialize(Geometry(captured), options)
            == JsonSerializer.Serialize(Geometry(actual), options);
        bool? decisionMatches = capturedVision.TryGetProperty("obstructed", out var capturedObstructed)
            && capturedObstructed.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? capturedObstructed.GetBoolean() == replay.Vision?.Obstructed : null;
        return (geometryMatches, decisionMatches);
    }
    static object[] AuditPackedBlackBoundaries(byte[] encodedImage, TableCalibration calibration, int width, VisionResult vision)
    {
        var seeds = vision.CandidateAudits.Where(a => a.Pass == "initial" && a.Stage == "packed-black").ToArray();
        if (seeds.Length == 0) return [];
        var pixels = WarpRawPixels(encodedImage, calibration, width);
        var hsvPixels = WarpRawPixels(encodedImage, calibration, width, toHsv: true);
        return seeds.Select(seed =>
        {
            var fit = BallBoundaryRefiner.Refine(pixels, width, width * 2, seed.ProposalCentre,
                PoolGeometry.Radius * width, .7, collectSectorEvidence: true);
            return (object)new
            {
                seed.Index,
                seed.ProposalCentre,
                seed.ProposalRadius,
                seed.Decision,
                recordedSupport = seed.BoundarySupport,
                fit = fit is null ? null : new
                {
                    fit.Centre,
                    fit.Radius,
                    fit.SupportedSectors,
                    fit.Score,
                    proposalSupport = fit.ProposalFit?.SupportedSectors,
                    sectors = fit.SectorEvidence.Select(sector =>
                    {
                        int x = Math.Clamp((int)Math.Round(sector.OuterPoint.X), 0, width - 1);
                        int y = Math.Clamp((int)Math.Round(sector.OuterPoint.Y), 0, width * 2 - 1);
                        int offset = (y * width + x) * 3;
                        int hue = hsvPixels[offset], saturation = hsvPixels[offset + 1], value = hsvPixels[offset + 2];
                        bool red = (hue <= 10 || hue >= 170) && saturation >= 100 && value >= 30;
                        bool yellow = hue >= 10 && hue <= 40 && saturation >= 100 && value >= 30;
                        return new { sector.Index, sector.Supported, sector.ExteriorRejected, sector.EdgeContrast,
                            sector.ExteriorContrast, sector.OuterPoint, hue, saturation, value, red, yellow };
                    })
                }
            };
        }).ToArray();
    }
    static byte[] WarpRawPixels(byte[] encodedImage, TableCalibration calibration, int width, bool toHsv = false)
    {
        using var encoded = new Org.Opencv.Core.MatOfByte(encodedImage);
        using var source = Org.Opencv.Imgcodecs.Imgcodecs.Imdecode(encoded, Org.Opencv.Imgcodecs.Imgcodecs.ImreadColor);
        var original = calibration.Corners.Select(p => new Org.Opencv.Core.Point(p.X * source.Cols(), p.Y * source.Rows())).ToArray();
        Org.Opencv.Core.Point[] target = [new(width, 0), new(width, width * 2), new(0, width * 2), new(0, 0)];
        try
        {
            using var from = new Org.Opencv.Core.MatOfPoint2f(original);
            using var to = new Org.Opencv.Core.MatOfPoint2f(target);
            using var transform = Org.Opencv.Imgproc.Imgproc.GetPerspectiveTransform(from, to);
            using var warped = new Org.Opencv.Core.Mat();
            Org.Opencv.Imgproc.Imgproc.WarpPerspective(source, warped, transform, new Org.Opencv.Core.Size(width, width * 2));
            if (toHsv)
            {
                using var hsv = new Org.Opencv.Core.Mat();
                Org.Opencv.Imgproc.Imgproc.CvtColor(warped, hsv, Org.Opencv.Imgproc.Imgproc.ColorBgr2hsv);
                var hsvPixels = new byte[width * width * 2 * 3];
                hsv.Get(0, 0, hsvPixels);
                return hsvPixels;
            }
            var pixels = new byte[width * width * 2 * 3];
            warped.Get(0, 0, pixels);
            return pixels;
        }
        finally { foreach (var point in original.Concat(target)) point.Dispose(); }
    }
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
