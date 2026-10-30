using System.Text.Json;
using System.Globalization;
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
    bool refinement, dump, boundaryAudit, traceDarkMask, normalizedGeometry, normalizedPackedProposalMask, boundaryExtentAblation,
        edgeMarginAblation, continueCalibrationMismatch, obstructionMaskTrace;
    int fastWidth, darkBodyCeiling = 40, packedClusterImageEdgeMargin = 65, manualBlackImageX = 1282, manualBlackImageY = 536;
    double boundaryExtentFraction = .65, pocketExclusionRadius = 55, minimumBoundarySupport = .75,
        minimumSparseColourBoundarySupport = .75, sparseBlackSearchExtentFraction = .4,
        minimumSparseBlackBoundarySupport = .7;
    int sparseBlackCandidateLimit = 12; bool sparseBlackRankByRadius; string run = "";
    public override void OnCreate(global::Android.OS.Bundle? args)
    {
        refinement = args?.GetString("refine") == "1"; dump = args?.GetString("dump") == "1";
        boundaryAudit = args?.GetString("audit") == "1"; traceDarkMask = args?.GetString("trace") == "1";
        obstructionMaskTrace = args?.GetString("obstructionMaskTrace") == "1";
        var requestedBlackLabelX = args?.GetString("blackLabelX");
        var requestedBlackLabelY = args?.GetString("blackLabelY");
        if (!string.IsNullOrWhiteSpace(requestedBlackLabelX) || !string.IsNullOrWhiteSpace(requestedBlackLabelY))
        {
            if (!int.TryParse(requestedBlackLabelX, NumberStyles.Integer, CultureInfo.InvariantCulture, out manualBlackImageX)
                || !int.TryParse(requestedBlackLabelY, NumberStyles.Integer, CultureInfo.InvariantCulture, out manualBlackImageY)
                || manualBlackImageX < 0 || manualBlackImageY < 0)
                throw new ArgumentOutOfRangeException(nameof(manualBlackImageX), "Manual black label requires non-negative source-image X and Y pixel coordinates.");
        }
        normalizedGeometry = args?.GetString("normGeometry") == "1";
        normalizedPackedProposalMask = args?.GetString("normPackedProposalMask") == "1";
        continueCalibrationMismatch = args?.GetString("continueCalibrationMismatch") == "1";
        var requestedBoundaryExtent = args?.GetString("boundaryExtent");
        if (!string.IsNullOrWhiteSpace(requestedBoundaryExtent))
        {
            if (!double.TryParse(requestedBoundaryExtent, NumberStyles.Float, CultureInfo.InvariantCulture, out boundaryExtentFraction)
                || !double.IsFinite(boundaryExtentFraction) || boundaryExtentFraction is < .2 or > .8)
                throw new ArgumentOutOfRangeException(nameof(boundaryExtentFraction), "Packed-colour boundary extent must be between 0.2 and 0.8.");
            boundaryExtentAblation = boundaryExtentFraction != .65;
        }
        var requestedEdgeMargin = args?.GetString("edgeMargin");
        if (!string.IsNullOrWhiteSpace(requestedEdgeMargin))
        {
            if (!int.TryParse(requestedEdgeMargin, NumberStyles.Integer, CultureInfo.InvariantCulture, out packedClusterImageEdgeMargin)
                || packedClusterImageEdgeMargin is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(packedClusterImageEdgeMargin), "Packed-cluster image edge margin must be between 0 and 100 pixels at width 1000.");
            edgeMarginAblation = packedClusterImageEdgeMargin != 65;
        }
        var requestedPocketRadius = args?.GetString("pocketRadius");
        if (!string.IsNullOrWhiteSpace(requestedPocketRadius)
            && (!double.TryParse(requestedPocketRadius, NumberStyles.Float, CultureInfo.InvariantCulture, out pocketExclusionRadius)
                || !double.IsFinite(pocketExclusionRadius) || pocketExclusionRadius is <= 0 or > 80))
            throw new ArgumentOutOfRangeException(nameof(pocketExclusionRadius), "Pocket exclusion radius must be greater than 0 and at most 80 pixels at width 1000.");
        var requestedBoundarySupport = args?.GetString("boundarySupport");
        if (!string.IsNullOrWhiteSpace(requestedBoundarySupport)
            && (!double.TryParse(requestedBoundarySupport, NumberStyles.Float, CultureInfo.InvariantCulture, out minimumBoundarySupport)
                || !double.IsFinite(minimumBoundarySupport) || minimumBoundarySupport is < .5625 or > 1))
            throw new ArgumentOutOfRangeException(nameof(minimumBoundarySupport), "Minimum boundary support must be between 0.5625 and 1.");
        var requestedSparseColourSupport = args?.GetString("sparseColourSupport");
        if (!string.IsNullOrWhiteSpace(requestedSparseColourSupport)
            && (!double.TryParse(requestedSparseColourSupport, NumberStyles.Float, CultureInfo.InvariantCulture, out minimumSparseColourBoundarySupport)
                || !double.IsFinite(minimumSparseColourBoundarySupport) || minimumSparseColourBoundarySupport is < .5625 or > 1))
            throw new ArgumentOutOfRangeException(nameof(minimumSparseColourBoundarySupport), "Minimum sparse-colour boundary support must be between 0.5625 and 1.");
        var requestedSparseBlackExtent = args?.GetString("sparseBlackExtent");
        if (!string.IsNullOrWhiteSpace(requestedSparseBlackExtent)
            && (!double.TryParse(requestedSparseBlackExtent, NumberStyles.Float, CultureInfo.InvariantCulture, out sparseBlackSearchExtentFraction)
                || !double.IsFinite(sparseBlackSearchExtentFraction) || sparseBlackSearchExtentFraction is < .2 or > 1.5))
            throw new ArgumentOutOfRangeException(nameof(sparseBlackSearchExtentFraction), "Sparse-black search extent must be between 0.2 and 1.5 ball radii.");
        var requestedSparseBlackSupport = args?.GetString("sparseBlackSupport");
        if (!string.IsNullOrWhiteSpace(requestedSparseBlackSupport)
            && (!double.TryParse(requestedSparseBlackSupport, NumberStyles.Float, CultureInfo.InvariantCulture, out minimumSparseBlackBoundarySupport)
                || !double.IsFinite(minimumSparseBlackBoundarySupport) || minimumSparseBlackBoundarySupport is < .5625 or > 1))
            throw new ArgumentOutOfRangeException(nameof(minimumSparseBlackBoundarySupport), "Minimum sparse-black boundary support must be between 0.5625 and 1.");
        var requestedSparseBlackLimit = args?.GetString("sparseBlackLimit");
        if (!string.IsNullOrWhiteSpace(requestedSparseBlackLimit)
            && (!int.TryParse(requestedSparseBlackLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out sparseBlackCandidateLimit)
                || sparseBlackCandidateLimit is < 1 or > 64))
            throw new ArgumentOutOfRangeException(nameof(sparseBlackCandidateLimit), "Sparse-black candidate limit must be between 1 and 64.");
        sparseBlackRankByRadius = args?.GetString("sparseBlackRadiusRank") is "1" or "true";
        run = args?.GetString("run") ?? "";
        fastWidth = int.TryParse(args?.GetString("fastWidth"), out var requestedWidth) ? requestedWidth : 0;
        if (int.TryParse(args?.GetString("darkCeiling"), out var requestedDarkCeiling)) darkBodyCeiling = requestedDarkCeiling;
        if (fastWidth != 0 && fastWidth is < 320 or > 800) throw new ArgumentOutOfRangeException(nameof(fastWidth), "Fast-width probe must be between 320 and 800 pixels.");
        if (darkBodyCeiling is < 10 or > 55) throw new ArgumentOutOfRangeException(nameof(darkBodyCeiling), "Dark-body ceiling must be between 10 and 55.");
        if (run.Length > 32 || run.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new ArgumentException("Invalid replay run name.");
        base.OnCreate(args); Start();
    }
    public override void OnStart()
    {
        base.OnStart(); object report; PipelineDiagnostics.Enabled = true;
        try
        {
            var root = Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath, run.Length == 0 ? "o08-replay" : "o08-replay-" + run);
            var paths = Directory.GetFiles(root, "*.json")
                .Where(path => long.TryParse(Path.GetFileNameWithoutExtension(path), out _))
                .OrderBy(path => long.Parse(Path.GetFileNameWithoutExtension(path))).ToArray();
            if (paths.Length is < 1 or > 6) throw new IOException("Replay 1 to 6 captured development snapshots.");
            using var first = JsonDocument.Parse(File.ReadAllBytes(paths[0]));
            var firstMetadata = first.RootElement;
            var corners = firstMetadata.GetProperty("calibrationCorners").EnumerateArray().Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray();
            var recordedConfiguration = firstMetadata.TryGetProperty("analyzerConfigurationIdentity", out var configurationElement)
                ? configurationElement.GetString() : null;
            var calibration = new TableCalibration(corners);
            VisionSettings ReplaySettings(int width, int edgeMargin = 65, double boundaryExtent = .65,
                bool useNormalizedGeometry = false, bool useNormalizedProposalMask = false)
                => new(Width: width, PocketExclusionRadius: pocketExclusionRadius)
                {
                    VerificationBodyRefinement = refinement,
                    DarkBodyValueCeiling = darkBodyCeiling,
                    MinimumHoughBoundarySupport = minimumBoundarySupport,
                    MinimumSparseColourBoundarySupport = minimumSparseColourBoundarySupport,
                    SparseBlackBoundarySearchExtentFraction = sparseBlackSearchExtentFraction,
                    MinimumSparseBlackBoundarySupport = minimumSparseBlackBoundarySupport,
                    SparseBlackCandidateLimit = sparseBlackCandidateLimit,
                    SparseBlackRankByRadius = sparseBlackRankByRadius,
                    PackedClusterImageEdgeMargin = edgeMargin,
                    PackedColourBoundarySearchExtentFraction = boundaryExtent,
                    UseNormalizedGeometryForDetection = useNormalizedGeometry,
                    UseNormalizedPackedColourProposalMask = useNormalizedProposalMask
                };
            var analyzer800 = new VisionAnalyzer(ReplaySettings(800), boundaryAudit, boundaryAudit, obstructionMaskTrace);
            var analyzer1000 = new VisionAnalyzer(ReplaySettings(1000), boundaryAudit, boundaryAudit, obstructionMaskTrace);
            var fastAnalyzer = fastWidth > 0 ? new VisionAnalyzer(ReplaySettings(fastWidth)) : null;
            var normalizedGeometryAnalyzer800 = normalizedGeometry
                ? new VisionAnalyzer(ReplaySettings(800, useNormalizedGeometry: true), boundaryAudit, boundaryAudit)
                : null;
            var normalizedGeometryAnalyzer1000 = normalizedGeometry
                ? new VisionAnalyzer(ReplaySettings(1000, useNormalizedGeometry: true), boundaryAudit, boundaryAudit)
                : null;
            var normalizedPackedProposalMaskAnalyzer800 = normalizedPackedProposalMask
                ? new VisionAnalyzer(ReplaySettings(800, useNormalizedProposalMask: true), boundaryAudit, boundaryAudit)
                : null;
            var normalizedPackedProposalMaskAnalyzer1000 = normalizedPackedProposalMask
                ? new VisionAnalyzer(ReplaySettings(1000, useNormalizedProposalMask: true), boundaryAudit, boundaryAudit)
                : null;
            var boundaryExtentAnalyzer800 = boundaryExtentAblation
                ? new VisionAnalyzer(ReplaySettings(800, boundaryExtent: boundaryExtentFraction), boundaryAudit, boundaryAudit)
                : null;
            var boundaryExtentAnalyzer1000 = boundaryExtentAblation
                ? new VisionAnalyzer(ReplaySettings(1000, boundaryExtent: boundaryExtentFraction), boundaryAudit, boundaryAudit)
                : null;
            var edgeMarginAnalyzer800 = edgeMarginAblation
                ? new VisionAnalyzer(ReplaySettings(800, edgeMargin: packedClusterImageEdgeMargin), boundaryAudit, boundaryAudit)
                : null;
            var edgeMarginAnalyzer1000 = edgeMarginAblation
                ? new VisionAnalyzer(ReplaySettings(1000, edgeMargin: packedClusterImageEdgeMargin), boundaryAudit, boundaryAudit)
                : null;
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
            using var cached15Worker = analyzer800.CreateVerification(calibration);
            using var cachedCompleteWorker = analyzer800.CreateVerification(calibration);
            JsonDocument? cacheSeeds = File.Exists(Path.Combine(root, "cache-seeds.json"))
                ? JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "cache-seeds.json"))) : null;
            if (cacheSeeds is not null)
            {
                var seed = cacheSeeds.RootElement;
                SeedVerificationWorker(cached15Worker, seed.GetProperty("actual15BodyCache"), seed.GetProperty("gains"), 800);
                SeedVerificationWorker(cachedCompleteWorker, seed.GetProperty("priorCompleteCache"), seed.GetProperty("gains"), 800);
            }
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
                var baseline800 = Measure(() => analyzer800.Analyze(bytes, calibration), out var analysis800Metrics);
                var baseline1000 = Measure(() => analyzer1000.Analyze(bytes, calibration), out var analysis1000Metrics);
                string? obstructionMask800 = null, obstructionMask1000 = null;
                if (obstructionMaskTrace)
                {
                    var mask800 = analyzer800.TakeObstructionMaskDiagnosticPng()
                        ?? throw new IOException("The width-800 obstruction mask diagnostic was not captured.");
                    var mask1000 = analyzer1000.TakeObstructionMaskDiagnosticPng()
                        ?? throw new IOException("The width-1000 obstruction mask diagnostic was not captured.");
                    obstructionMask800 = $"{frameId}-config800-obstruction-mask.png";
                    obstructionMask1000 = $"{frameId}-config1000-obstruction-mask.png";
                    File.WriteAllBytes(Path.Combine(root, obstructionMask800), mask800);
                    File.WriteAllBytes(Path.Combine(root, obstructionMask1000), mask1000);
                }
                VisionResult? normalizedGeometry800 = null, normalizedGeometry1000 = null;
                object? normalizedGeometry800Metrics = null, normalizedGeometry1000Metrics = null;
                if (normalizedGeometryAnalyzer800 is not null)
                    normalizedGeometry800 = Measure(() => normalizedGeometryAnalyzer800.Analyze(bytes, calibration), out normalizedGeometry800Metrics);
                if (normalizedGeometryAnalyzer1000 is not null)
                    normalizedGeometry1000 = Measure(() => normalizedGeometryAnalyzer1000.Analyze(bytes, calibration), out normalizedGeometry1000Metrics);
                VisionResult? normalizedPackedProposalMask800 = null, normalizedPackedProposalMask1000 = null;
                object? normalizedPackedProposalMask800Metrics = null, normalizedPackedProposalMask1000Metrics = null;
                if (normalizedPackedProposalMaskAnalyzer800 is not null)
                    normalizedPackedProposalMask800 = Measure(() => normalizedPackedProposalMaskAnalyzer800.Analyze(bytes, calibration), out normalizedPackedProposalMask800Metrics);
                if (normalizedPackedProposalMaskAnalyzer1000 is not null)
                    normalizedPackedProposalMask1000 = Measure(() => normalizedPackedProposalMaskAnalyzer1000.Analyze(bytes, calibration), out normalizedPackedProposalMask1000Metrics);
                VisionResult? boundaryExtent800 = null, boundaryExtent1000 = null;
                object? boundaryExtent800Metrics = null, boundaryExtent1000Metrics = null;
                if (boundaryExtentAnalyzer800 is not null)
                    boundaryExtent800 = Measure(() => boundaryExtentAnalyzer800.Analyze(bytes, calibration), out boundaryExtent800Metrics);
                if (boundaryExtentAnalyzer1000 is not null)
                    boundaryExtent1000 = Measure(() => boundaryExtentAnalyzer1000.Analyze(bytes, calibration), out boundaryExtent1000Metrics);
                VisionResult? edgeMargin800 = null, edgeMargin1000 = null;
                object? edgeMargin800Metrics = null, edgeMargin1000Metrics = null;
                if (edgeMarginAnalyzer800 is not null)
                    edgeMargin800 = Measure(() => edgeMarginAnalyzer800.Analyze(bytes, calibration), out edgeMargin800Metrics);
                if (edgeMarginAnalyzer1000 is not null)
                    edgeMargin1000 = Measure(() => edgeMarginAnalyzer1000.Analyze(bytes, calibration), out edgeMargin1000Metrics);
                VisionResult? fastAnalysis = null; object? fastAnalysisMetrics = null;
                if (fastAnalyzer is not null)
                    fastAnalysis = Measure(() => fastAnalyzer.Analyze(bytes, calibration), out fastAnalysisMetrics);
                var detectedPocketLayout = analyzer800.FindPockets(bytes);
                var detectedPocketCorners = detectedPocketLayout?.Corners.ToArray();
                var pocketCornerDeltas = detectedPocketCorners?.Select(corner =>
                    calibration.Corners.Min(saved => (saved - corner).Length)).ToArray();
                var fallbackCorners = analyzer800.FindCorners(bytes);
                var fallbackCornerDeltas = fallbackCorners?.Select(corner =>
                    calibration.Corners.Min(saved => (saved - corner).Length)).ToArray();
                object? detectedPocketGeometry = null;
                if (detectedPocketCorners is { Length: > 0 } pocketCorners && pocketCornerDeltas is { Length: > 0 } deltas)
                    detectedPocketGeometry = new
                    {
                        corners = pocketCorners,
                        nearestSavedCornerDeltas = deltas,
                        maximumNearestSavedCornerDelta = deltas.Max(),
                        withinSavedCalibrationTolerance = deltas.All(delta => delta <= .035),
                        sidePocketCount = detectedPocketLayout!.SidePockets.Length,
                        sidePocketCentres = detectedPocketLayout.SidePockets,
                        minimumContrast = detectedPocketLayout.Contrast
                    };
                object? fallbackCornerGeometry = null;
                if (fallbackCorners is { Length: > 0 } fallbackCornerPoints && fallbackCornerDeltas is { Length: > 0 } fallbackDeltas)
                    fallbackCornerGeometry = new
                    {
                        corners = fallbackCornerPoints,
                        nearestSavedCornerDeltas = fallbackDeltas,
                        maximumNearestSavedCornerDelta = fallbackDeltas.Max(),
                        withinSavedCalibrationTolerance = fallbackDeltas.All(delta => delta <= .035)
                    };
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
                var verification800 = Measure(() => worker800.Measure(frame, detectionRequested, CancellationToken.None), out var verification800Metrics); measurements800.Add(verification800);
                var verification1000 = Measure(() => worker1000.Measure(frame, detectionRequested, CancellationToken.None), out var verification1000Metrics); measurements1000.Add(verification1000);
                bool fallbackMatchesStoredCalibration = fallbackCornerDeltas is { Length: > 0 }
                    && fallbackCornerDeltas.All(delta => delta <= .035);
                bool calibrationGeometryContradiction = fallbackMatchesStoredCalibration
                    && (verification800.Reason == ObservationReason.CalibrationChanged || verification1000.Reason == ObservationReason.CalibrationChanged);
                if (calibrationGeometryContradiction && !continueCalibrationMismatch)
                    throw new InvalidDataException($"Verification rejected a view whose detected corners match the saved calibration. frame={frameId}; " +
                        $"width800={verification800.Reason},coverage={verification800.CoverageValid},rail={verification800.PerimeterChange:0.000},lighting={verification800.IlluminationChange:0.0}; " +
                        $"width1000={verification1000.Reason},coverage={verification1000.CoverageValid},rail={verification1000.PerimeterChange:0.000},lighting={verification1000.IlluminationChange:0.0}; " +
                        $"residual800={verification800.PhotometricResidualTileChange:0.000},weak800={verification800.SensitiveTileChange:0.000}," +
                        $"bodies800={verification800.SupportedBodies},templateMotion800={verification800.MaximumBodyDisplacement:0.000}," +
                        $"fallbackDelta={fallbackCornerDeltas?.Max():0.000}");
                object? cacheStateRegression = null;
                if (cacheSeeds is not null && samples.Count == 0 && detectionRequested)
                {
                    var seed = cacheSeeds.RootElement;
                    var cached15 = Measure(() => cached15Worker.Measure(frame, true, CancellationToken.None), out var cached15Metrics);
                    var cachedComplete = Measure(() => cachedCompleteWorker.Measure(frame, true, CancellationToken.None), out var cachedCompleteMetrics);
                    using var unmodifiedSyntheticControlWorker = analyzer800.CreateVerification(calibration);
                    SeedVerificationWorker(unmodifiedSyntheticControlWorker, seed.GetProperty("priorCompleteCache"), seed.GetProperty("gains"), 800, freezeAppearance: false);
                    var unmodifiedSyntheticControl = Measure(() => unmodifiedSyntheticControlWorker.Measure(frame, true, CancellationToken.None), out var unmodifiedSyntheticControlMetrics);
                    using var absentBlackWorker = analyzer800.CreateVerification(calibration);
                    SeedVerificationWorker(absentBlackWorker, seed.GetProperty("priorCompleteCache"), seed.GetProperty("gains"), 800);
                    var blankBitmap = global::Android.Graphics.Bitmap.CreateBitmap(800, 1600,
                        global::Android.Graphics.Bitmap.Config.Argb8888!);
                    blankBitmap.EraseColor(global::Android.Graphics.Color.Rgb(35, 110, 45));
                    using var absentBlackFrame = new TableFrame([], DateTimeOffset.UtcNow, frameTimeline)
                    {
                        SourceTimeUs = sourceTimeUs + 1,
                        FrameSource = "synthetic-uniform-cloth-negative",
                        Image = new BitmapTableImage(blankBitmap)
                    };
                    var absentBlack = Measure(() => absentBlackWorker.Measure(absentBlackFrame, true, CancellationToken.None), out var absentBlackMetrics);
                    var occludedBlack = CreateBlackOccludedPng(bytes, calibration, baseline800.Circles, 800,
                        out var occludedPixelsHash, out var occludedPatchAudit);
                    File.WriteAllBytes(Path.Combine(root, "black-removed-variant.png"), occludedBlack);
                    using var occludedBlackFrame = new TableFrame(occludedBlack, DateTimeOffset.UtcNow, frameTimeline)
                    {
                        SourceTimeUs = sourceTimeUs + 2,
                        FrameSource = "synthetic-packed-rack-black-removed"
                    };
                    using var occluded800Worker = analyzer800.CreateVerification(calibration);
                    var analyzer1000WorkerOwner = new VisionAnalyzer(new VisionSettings(Width: 1000) { DarkBodyValueCeiling = darkBodyCeiling });
                    using var occluded1000Worker = analyzer1000WorkerOwner.CreateVerification(calibration);
                    SeedVerificationWorker(occluded800Worker, seed.GetProperty("priorCompleteCache"), seed.GetProperty("gains"), 800, freezeAppearance: false);
                    SeedVerificationWorker(occluded1000Worker, seed.GetProperty("priorCompleteCache"), seed.GetProperty("gains"), 1000, freezeAppearance: false);
                    var occluded800 = Measure(() => occluded800Worker.Measure(occludedBlackFrame, true, CancellationToken.None), out var occluded800Metrics);
                    var occluded1000 = Measure(() => occluded1000Worker.Measure(occludedBlackFrame, true, CancellationToken.None), out var occluded1000Metrics);
                    bool Recovered(VerificationMeasurement result) => result.Vision?.PackedClusterFallback?.Recovered == true
                        && result.Vision.Balls.Count(ball => ball.Kind == "black") == 1;
                    cacheStateRegression = new
                    {
                        samePixelSha256 = sha,
                        inputPixelSha256 = receipt.GetProperty("pixelSha256").GetString(),
                        freshNoCache = new { metrics = verification800Metrics, vision = Summarize(verification800, 800), recoveredBlackFromPixels = Recovered(verification800) },
                        actual15BodyCache = new { metrics = cached15Metrics, vision = Summarize(cached15, 800), recoveredBlackFromPixels = Recovered(cached15) },
                        priorCompleteCache = new { metrics = cachedCompleteMetrics, vision = Summarize(cachedComplete, 800), recoveredBlackFromPixels = Recovered(cachedComplete) },
                        controlPurpose = seed.TryGetProperty("controlPurpose", out var controlPurpose) ? controlPurpose.GetString() : "current-frame cache regression",
                        priorCompleteCacheControl = new
                        {
                            metrics = cachedCompleteMetrics,
                            vision = cachedComplete.Vision is null ? null : Summarize(cachedComplete, 800),
                            branchExecuted = cachedComplete.Vision?.Recognition?.Branch,
                            passed = cachedComplete.Vision is { Uncertain: true, Obstructed: true }
                        },
                        absentBlackNegative = new
                        {
                            input = "Synthetic uniform cloth frame with no visible balls; frozen cue gains and a prior 16-circle anchor are preloaded.",
                            metrics = absentBlackMetrics,
                            vision = absentBlack.Vision is null ? null : Summarize(absentBlack.Vision, 800),
                            detectedBlackCount = absentBlack.Vision?.Balls.Count(ball => ball.Kind == "black") ?? 0,
                            fallback = absentBlack.Vision?.PackedClusterFallback,
                            passed = absentBlack.Vision is { Balls.Length: 0, PackedClusterFallback: null }
                        },
                        packedRackBlackRemovalNegative = new
                        {
                            input = "Controlled removal of visible black pixels from the saved live frame; pixels inside the other 15 anchored ball disks are preserved.",
                            unmodifiedSourcePairedControl = new
                            {
                                metrics = unmodifiedSyntheticControlMetrics,
                                vision = unmodifiedSyntheticControl.Vision is null ? null : Summarize(unmodifiedSyntheticControl.Vision, 800),
                                passed = unmodifiedSyntheticControl.Vision is { Uncertain: false, CircleCount: 16 }
                                    && unmodifiedSyntheticControl.Vision.Balls.Count(ball => ball.Kind == "cue") == 1
                                    && unmodifiedSyntheticControl.Vision.Balls.Count(ball => ball.Kind == "red") == 7
                                    && unmodifiedSyntheticControl.Vision.Balls.Count(ball => ball.Kind == "yellow") == 7
                                    && unmodifiedSyntheticControl.Vision.Balls.Count(ball => ball.Kind == "black") == 1
                            },
                            pixelBytesSha256 = occludedPixelsHash,
                            pixelPatchAudit = occludedPatchAudit,
                            width800 = new
                            {
                                metrics = occluded800Metrics,
                                vision = occluded800.Vision is null ? null : Summarize(occluded800.Vision, 800),
                                detectedBlackCount = occluded800.Vision?.Balls.Count(ball => ball.Kind == "black") ?? 0,
                                fallback = occluded800.Vision?.PackedClusterFallback,
                                passed = occluded800.Vision is { Uncertain: true, PackedClusterFallback: { Attempted: true, Recovered: false } }
                                    && occluded800.Vision.Balls.All(ball => ball.Kind != "black")
                            },
                            width1000 = new
                            {
                                metrics = occluded1000Metrics,
                                vision = occluded1000.Vision is null ? null : Summarize(occluded1000.Vision, 1000),
                                detectedBlackCount = occluded1000.Vision?.Balls.Count(ball => ball.Kind == "black") ?? 0,
                                fallback = occluded1000.Vision?.PackedClusterFallback,
                                passed = occluded1000.Vision is { Uncertain: true, Obstructed: false, PackedClusterFallback: null }
                                    && occluded1000.Vision.Balls.Length == 15 && occluded1000.Vision.Balls.All(ball => ball.Kind != "black")
                            }
                        },
                        boundaryScoringCost = ProfileBoundaryScoring(bytes, calibration, seed.GetProperty("actual15BodyCache"), 800),
                        seedSource = "Retained v2 width800 worker result for this exact live frame; cached detections are anchor/motion history only, while cached gains exercise BurstAppearance.Frozen."
                    };
                }
                var auditComparison = CompareCapturedAudit(metadataRoot, verification800);
                if (verification800.Reason == ObservationReason.Obstructed) worker800.ResetAfterObstruction();
                if (verification1000.Reason == ObservationReason.Obstructed) worker1000.ResetAfterObstruction();
                samples.Add(new
                {
                    frameId, pixelSha256 = sha, sourceTimeUs = frame.SourceTimeUs, capturedAt,
                    detectionRequested, configurationMatchesPrimary,
                    detectedPocketGeometry,
                    fallbackCornerGeometry,
                    fallbackMatchesStoredCalibration,
                    calibrationGeometryContradiction = calibrationGeometryContradiction ? new
                    {
                        diagnosticOnly = continueCalibrationMismatch,
                        fallbackMaximumCornerDelta = fallbackCornerDeltas?.Max(),
                        width800 = new { reason = verification800.Reason, verification800.CoverageValid,
                            verification800.PerimeterChange, verification800.IlluminationChange,
                            verification800.PhotometricResidualTileChange, verification800.SensitiveTileChange,
                            verification800.SupportedBodies, verification800.MaximumBodyDisplacement },
                        width1000 = new { reason = verification1000.Reason, verification1000.CoverageValid,
                            verification1000.PerimeterChange, verification1000.IlluminationChange,
                            verification1000.PhotometricResidualTileChange, verification1000.SensitiveTileChange,
                            verification1000.SupportedBodies, verification1000.MaximumBodyDisplacement }
                    } : null,
                    analysis800Metrics, analysis1000Metrics, verification800Metrics, verification1000Metrics,
                    obstructionMaskTrace = obstructionMaskTrace ? new
                    {
                        configuredWidth800 = obstructionMask800,
                        actualWidth800 = baseline800.ObstructionAudit?.AnalysisWidth,
                        configuredWidth1000 = obstructionMask1000,
                        actualWidth1000 = baseline1000.ObstructionAudit?.AnalysisWidth
                    } : null,
                    normalizedGeometryAblation = normalizedGeometry ? new
                    {
                        width800 = new { metrics = normalizedGeometry800Metrics, analysis = Summarize(normalizedGeometry800!, 800),
                            comparison = Compare(baseline800, normalizedGeometry800!, 800, 800) },
                        width1000 = new { metrics = normalizedGeometry1000Metrics, analysis = Summarize(normalizedGeometry1000!, 1000),
                            comparison = Compare(baseline1000, normalizedGeometry1000!, 1000, 1000) }
                    } : null,
                    normalizedPackedProposalMaskAblation = normalizedPackedProposalMask ? new
                    {
                        width800 = new { metrics = normalizedPackedProposalMask800Metrics, analysis = Summarize(normalizedPackedProposalMask800!, 800),
                            comparison = Compare(baseline800, normalizedPackedProposalMask800!, 800, 800) },
                        width1000 = new { metrics = normalizedPackedProposalMask1000Metrics, analysis = Summarize(normalizedPackedProposalMask1000!, 1000),
                            comparison = Compare(baseline1000, normalizedPackedProposalMask1000!, 1000, 1000) }
                    } : null,
                    packedColourBoundarySearchAblation = boundaryExtentAblation ? new
                    {
                        extentFraction = boundaryExtentFraction,
                        width800 = new { metrics = boundaryExtent800Metrics, analysis = Summarize(boundaryExtent800!, 800),
                            comparison = Compare(baseline800, boundaryExtent800!, 800, 800) },
                        width1000 = new { metrics = boundaryExtent1000Metrics, analysis = Summarize(boundaryExtent1000!, 1000),
                            comparison = Compare(baseline1000, boundaryExtent1000!, 1000, 1000) }
                    } : null,
                    packedClusterImageEdgeMarginAblation = edgeMarginAblation ? new
                    {
                        edgeMarginAtWidth1000 = packedClusterImageEdgeMargin,
                        width800 = new { metrics = edgeMargin800Metrics, analysis = Summarize(edgeMargin800!, 800),
                            comparison = Compare(baseline800, edgeMargin800!, 800, 800) },
                        width1000 = new { metrics = edgeMargin1000Metrics, analysis = Summarize(edgeMargin1000!, 1000),
                            comparison = Compare(baseline1000, edgeMargin1000!, 1000, 1000) }
                    } : null,
                    fastWidth = fastAnalyzer is null ? null : new
                    {
                        configuredWidth = fastWidth,
                        metrics = fastAnalysisMetrics,
                        analysis = Summarize(fastAnalysis!, fastWidth),
                    },
                    cacheStateRegression,
                    replayObstructionGeometryMatchesCapture = auditComparison.ExactGeometryMatches,
                    replayObstructionNormalizedGeometryMatchesCapture = auditComparison.NormalizedGeometryMatches,
                    replayObstructionMaximumNormalizedBoundsDelta = auditComparison.MaximumNormalizedBoundsDelta,
                    replayObstructionDecisionMatchesCapture = auditComparison.DecisionMatches,
                    width800 = new { blackSeedRecovered = blackSeedRecovered800, analysis = Summarize(baseline800, 800), verification = Summarize(verification800, 800),
                        darkMaskTrace = traceDarkMask ? PackedBlackMaskTrace.Capture(bytes, calibration, 800, baseline800, Path.Combine(root, "o08-black-trace-" + run), receipt.GetProperty("frameId").GetInt64().ToString(), manualBlackImageX, manualBlackImageY) : null,
                        packedBlackBoundaryTrace = boundaryAudit ? AuditPackedBlackBoundaries(bytes, calibration, 800, baseline800) : null },
                    width1000 = new { blackSeedRecovered = blackSeedRecovered1000, analysis = Summarize(baseline1000, 1000), verification = Summarize(verification1000, 1000),
                        darkMaskTrace = traceDarkMask ? PackedBlackMaskTrace.Capture(bytes, calibration, 1000, baseline1000, Path.Combine(root, "o08-black-trace-" + run), frameId.ToString(), manualBlackImageX, manualBlackImageY) : null,
                        packedBlackBoundaryTrace = boundaryAudit ? AuditPackedBlackBoundaries(bytes, calibration, 1000, baseline1000) : null },
                    analysisWidthComparison = Compare(baseline800, baseline1000, 800, 1000),
                    widthComparison = verification800.Vision is null || verification1000.Vision is null
                        ? null : Compare(verification800.Vision, verification1000.Vision, 800, 1000),
                });
            }
            var consensus800 = VerificationConsensus.Resolve(measurements800.Where(m => m.Vision is not null).Select(m => m.Vision!).ToArray(), out var conflict800);
            var consensus1000 = VerificationConsensus.Resolve(measurements1000.Where(m => m.Vision is not null).Select(m => m.Vision!).ToArray(), out var conflict1000);
            var analysisConsensus800 = VerificationConsensus.Resolve(analyses800, out var analysisConflict800);
            var analysisConsensus1000 = VerificationConsensus.Resolve(analyses1000, out var analysisConflict1000);
            report = new { completed = true, configuration = ObservationPolicy.VerificationConfiguration, bodyRefinement = refinement, boundaryAudit,
                obstructionMaskTrace, darkBodyValueCeiling = darkBodyCeiling,
                pocketExclusionRadius, minimumHoughBoundarySupport = minimumBoundarySupport,
                minimumSparseColourBoundarySupport,
                sparseBlackSearchExtentFraction, minimumSparseBlackBoundarySupport,
                sparseBlackCandidateLimit, sparseBlackRankByRadius,
                normalizedGeometryAblation = normalizedGeometry,
                normalizedPackedProposalMaskAblation = normalizedPackedProposalMask,
                packedColourBoundarySearchExtentFraction = boundaryExtentAblation ? boundaryExtentFraction : (double?)null,
                packedClusterImageEdgeMargin = edgeMarginAblation ? packedClusterImageEdgeMargin : (int?)null,
                fastWidth = fastWidth > 0 ? fastWidth : (int?)null,
                pipelineDiagnostics = PipelineDiagnostics.Snapshot(),
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
    static void SeedVerificationWorker(ITableVerificationWorker worker, JsonElement circlesElement, JsonElement gainsElement, int width,
        bool freezeAppearance = true)
    {
        const System.Reflection.BindingFlags fields = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var workerType = worker.GetType();
        var appearance = workerType.GetField("appearance", fields)?.GetValue(worker)
            ?? throw new InvalidOperationException("Verification appearance cache field is unavailable.");
        var appearanceType = appearance.GetType();
        if (freezeAppearance)
        {
            appearanceType.GetField("Gains", fields)?.SetValue(appearance, gainsElement.EnumerateArray().Select(value => value.GetDouble()).ToArray());
            appearanceType.GetField("ReferenceValid", fields)?.SetValue(appearance, true);
            appearanceType.GetField("HasReference", fields)?.SetValue(appearance, true);
        }
        var circles = circlesElement.EnumerateArray().Select(circle =>
        {
            string id = circle.GetProperty("ballId").GetString()!;
            double x = circle.GetProperty("x").GetDouble(), y = circle.GetProperty("y").GetDouble();
            double radius = circle.GetProperty("radius").GetDouble();
            return new CircleObservation(id, new((1 - y) * width, x * width), radius * width,
                id.Contains('-') ? id[..id.IndexOf('-')] : id, []);
        }).ToArray();
        workerType.GetField("anchorDetections", fields)?.SetValue(worker, circles);
    }
    static object ProfileBoundaryScoring(byte[] encodedImage, TableCalibration calibration, JsonElement circlesElement, int width)
    {
        var bgr = WarpRawPixels(encodedImage, calibration, width);
        var hsv = WarpRawPixels(encodedImage, calibration, width, toHsv: true);
        var mask = BoundaryColourMask.FromHsvPixels(hsv);
        var proposals = circlesElement.EnumerateArray().Select(circle =>
        {
            string id = circle.GetProperty("ballId").GetString()!;
            double x = circle.GetProperty("x").GetDouble(), y = circle.GetProperty("y").GetDouble();
            return (Id: id, Kind: id[..id.IndexOf('-')], Centre: new Point((1 - y) * width, x * width));
        }).ToArray();
        BallBoundary?[] Run(BoundaryColourMask? boundaryColourMask) => proposals.Select(proposal =>
            BallBoundaryRefiner.Refine(bgr, width, width * 2, proposal.Centre, PoolGeometry.Radius * width,
                expectedKind: proposal.Kind, colourMask: boundaryColourMask)).ToArray();
        _ = Run(null); _ = Run(mask); // Warm both JIT and native paths before timing.
        PipelineDiagnostics.Enabled = false;
        var legacy = Measure(() => Run(null), out var legacyMetrics);
        var cached = Measure(() => Run(mask), out var cachedMetrics);
        PipelineDiagnostics.Enabled = true;
        int comparable = 0, changedSupport = 0, changedGeometry = 0;
        double maximumCentreDelta = 0, maximumRadiusDelta = 0, maximumScoreDelta = 0;
        for (int i = 0; i < proposals.Length; i++)
        {
            if (legacy[i] is null || cached[i] is null) { if (legacy[i] is not null || cached[i] is not null) changedGeometry++; continue; }
            comparable++;
            maximumCentreDelta = Math.Max(maximumCentreDelta, (legacy[i]!.Centre - cached[i]!.Centre).Length);
            maximumRadiusDelta = Math.Max(maximumRadiusDelta, Math.Abs(legacy[i]!.Radius - cached[i]!.Radius));
            maximumScoreDelta = Math.Max(maximumScoreDelta, Math.Abs(legacy[i]!.Score - cached[i]!.Score));
            if (legacy[i]!.SupportedSectors != cached[i]!.SupportedSectors) changedSupport++;
        }
        return new { candidateCount = proposals.Length, comparable, legacyMetrics, cachedMetrics,
            maximumCentreDelta, maximumRadiusDelta, maximumScoreDelta, changedSupport, changedGeometry };
    }
    static byte[] CreateBlackOccludedPng(byte[] encodedImage, TableCalibration calibration,
        IReadOnlyCollection<CircleObservation> circles, int width, out string pixelBytesSha256,
        out object pixelPatchAudit)
    {
        using var encoded = new Org.Opencv.Core.MatOfByte(encodedImage);
        using var source = Org.Opencv.Imgcodecs.Imgcodecs.Imdecode(encoded, Org.Opencv.Imgcodecs.Imgcodecs.ImreadColor);
        if (source.Empty()) throw new IOException("Could not decode the saved rack frame for controlled black removal.");
        int height = width * 2;
        var original = calibration.Corners.Select(point => new Org.Opencv.Core.Point(point.X * source.Cols(), point.Y * source.Rows())).ToArray();
        Org.Opencv.Core.Point[] target = [new(width, 0), new(width, height), new(0, height), new(0, 0)];
        using var from = new Org.Opencv.Core.MatOfPoint2f(original);
        using var to = new Org.Opencv.Core.MatOfPoint2f(target);
        using var transform = Org.Opencv.Imgproc.Imgproc.GetPerspectiveTransform(from, to);
        using var warped = new Org.Opencv.Core.Mat();
        using var warpedSize = new Org.Opencv.Core.Size(width, height);
        Org.Opencv.Imgproc.Imgproc.WarpPerspective(source, warped, transform, warpedSize);
        var bgrPixels = new byte[width * height * 3]; warped.Get(0, 0, bgrPixels);
        var black = circles.Single(circle => circle.BallId.StartsWith("black-", StringComparison.Ordinal));
        double centreX = black.Centre.X, centreY = black.Centre.Y;
        double radius = PoolGeometry.Radius * width;
        var neighbours = circles.Where(circle => !string.Equals(circle.BallId, black.BallId, StringComparison.Ordinal))
            .Select(circle => (X: circle.Centre.X, Y: circle.Centre.Y, Radius: circle.Radius)).ToArray();

        // Estimate the local cloth colour from an annulus around the removed
        // ball. A single global cloth sample creates a hard-edged disk that
        // the detector quite reasonably accepts as another ball.
        var blueSamples = new List<byte>();
        var greenSamples = new List<byte>();
        var redSamples = new List<byte>();
        double sampleInnerRadius = radius * 3.0, sampleOuterRadius = radius * 5.0;
        for (int py = Math.Max(0, (int)(centreY - sampleOuterRadius)); py <= Math.Min(height - 1, (int)(centreY + sampleOuterRadius)); py++)
        for (int px = Math.Max(0, (int)(centreX - sampleOuterRadius)); px <= Math.Min(width - 1, (int)(centreX + sampleOuterRadius)); px++)
        {
            double dx = px - centreX, dy = py - centreY;
            double distanceSquared = dx * dx + dy * dy;
            if (distanceSquared < sampleInnerRadius * sampleInnerRadius || distanceSquared > sampleOuterRadius * sampleOuterRadius) continue;
            if (neighbours.Any(neighbour =>
            {
                double nx = px - neighbour.X, ny = py - neighbour.Y;
                double exclusionRadius = Math.Max(radius * 1.12, neighbour.Radius * 1.2);
                return nx * nx + ny * ny <= exclusionRadius * exclusionRadius;
            })) continue;
            int offset = (py * width + px) * 3;
            blueSamples.Add(bgrPixels[offset]);
            greenSamples.Add(bgrPixels[offset + 1]);
            redSamples.Add(bgrPixels[offset + 2]);
        }
        if (blueSamples.Count < 64) throw new IOException("Not enough visible local cloth to synthesize the black-removal control.");
        static byte Median(List<byte> samples)
        {
            samples.Sort();
            return samples[samples.Count / 2];
        }
        byte blue = Median(blueSamples), green = Median(greenSamples), red = Median(redSamples);
        int centrePx = Math.Clamp((int)Math.Round(centreX), 0, width - 1);
        int centrePy = Math.Clamp((int)Math.Round(centreY), 0, height - 1);
        int centreOffset = (centrePy * width + centrePx) * 3;
        string centreBefore = $"{bgrPixels[centreOffset]},{bgrPixels[centreOffset + 1]},{bgrPixels[centreOffset + 2]}";
        bool centreBlockedByNeighbour = neighbours.Any(neighbour =>
        {
            double nx = centrePx - neighbour.X, ny = centrePy - neighbour.Y;
            double exclusionRadius = neighbour.Radius * 1.05;
            return nx * nx + ny * ny <= exclusionRadius * exclusionRadius;
        });

        // Remove the ball and nearby shadow, then feather the patch into the
        // untouched cloth. Preserve neighboring ball bodies throughout.
        double fillRadius = radius * 1.5, featherStart = radius * .92;
        int changedPixels = 0;
        bool centreProcessed = false, centreSkippedByNeighbour = false;
        double centreBlend = double.NaN;
        for (int py = Math.Max(0, (int)(centreY - fillRadius)); py <= Math.Min(height - 1, (int)(centreY + fillRadius)); py++)
        for (int px = Math.Max(0, (int)(centreX - fillRadius)); px <= Math.Min(width - 1, (int)(centreX + fillRadius)); px++)
        {
            bool isCentre = px == centrePx && py == centrePy;
            double dx = px - centreX, dy = py - centreY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            bool blockedByNeighbour = neighbours.Any(neighbour =>
            {
                double nx = px - neighbour.X, ny = py - neighbour.Y;
                double bodyRadius = neighbour.Radius * 1.05;
                return nx * nx + ny * ny <= bodyRadius * bodyRadius;
            });
            if (isCentre && blockedByNeighbour) centreSkippedByNeighbour = true;
            if (distance > fillRadius || blockedByNeighbour) continue;
            double feather = distance <= featherStart ? 0 : (distance - featherStart) / (fillRadius - featherStart);
            double blend = 1 - feather * feather * (3 - 2 * feather);
            if (isCentre) { centreProcessed = true; centreBlend = blend; }
            int offset = (py * width + px) * 3;
            bgrPixels[offset] = (byte)Math.Clamp(Math.Round(bgrPixels[offset] * (1 - blend) + blue * blend), 0, 255);
            bgrPixels[offset + 1] = (byte)Math.Clamp(Math.Round(bgrPixels[offset + 1] * (1 - blend) + green * blend), 0, 255);
            bgrPixels[offset + 2] = (byte)Math.Clamp(Math.Round(bgrPixels[offset + 2] * (1 - blend) + red * blend), 0, 255);
            changedPixels++;
        }
        string centreAfter = $"{bgrPixels[centreOffset]},{bgrPixels[centreOffset + 1]},{bgrPixels[centreOffset + 2]}";
        pixelPatchAudit = new
        {
            centreX, centreY, radius,
            localClothBgr = new { blue, green, red },
            centreBefore, centreAfter, centreBlockedByNeighbour, centreProcessed, centreSkippedByNeighbour,
            centreBlend, changedPixels
        };
        pixelBytesSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bgrPixels)).ToLowerInvariant();
        using var patched = new Org.Opencv.Core.Mat(height, width, Org.Opencv.Core.CvType.Cv8uc3);
        patched.Put(0, 0, bgrPixels);
        using var inverse = new Org.Opencv.Core.Mat();
        if (Org.Opencv.Core.Core.Invert(transform, inverse) == 0) throw new IOException("Could not invert the calibration transform.");
        using var restored = new Org.Opencv.Core.Mat();
        using var sourceSize = new Org.Opencv.Core.Size(source.Cols(), source.Rows());
        Org.Opencv.Imgproc.Imgproc.WarpPerspective(patched, restored, inverse, sourceSize);
        using var tableMask = new Org.Opencv.Core.Mat(source.Rows(), source.Cols(), Org.Opencv.Core.CvType.Cv8uc1);
        tableMask.SetTo(new Org.Opencv.Core.Scalar(0));
        using var tablePolygon = new Org.Opencv.Core.MatOfPoint(original);
        using var white = new Org.Opencv.Core.Scalar(255);
        Org.Opencv.Imgproc.Imgproc.FillConvexPoly(tableMask, tablePolygon, white);
        restored.CopyTo(source, tableMask);
        foreach (var point in original.Concat(target)) point.Dispose();
        using var encodedVariant = new Org.Opencv.Core.MatOfByte();
        if (!Org.Opencv.Imgcodecs.Imgcodecs.Imencode(".png", source, encodedVariant))
            throw new IOException("Could not encode the controlled black-removal replay frame.");
        return encodedVariant.ToArray();
    }
    static T Measure<T>(Func<T> action, out object metrics)
    {
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        T result = action();
        timer.Stop();
        metrics = new { wallMs = timer.Elapsed.TotalMilliseconds,
            managedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore };
        return result;
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
        vision.Uncertain, layoutUncertain = vision.HasUncertainLayout, vision.Obstructed, vision.CircleCount,
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
    static (bool? ExactGeometryMatches, bool? NormalizedGeometryMatches, double? MaximumNormalizedBoundsDelta, bool? DecisionMatches)
        CompareCapturedAudit(JsonElement metadata, VerificationMeasurement replay)
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
        if (captured is null || actual is null) return (captured is null && actual is null, captured is null && actual is null, null, null);
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
        var capturedComponents = captured.Components.OrderBy(component => component.Top / (double)captured.AnalysisHeight)
            .ThenBy(component => component.Left / (double)captured.AnalysisWidth).ToArray();
        var replayComponents = actual.Components.OrderBy(component => component.Top / (double)actual.AnalysisHeight)
            .ThenBy(component => component.Left / (double)actual.AnalysisWidth).ToArray();
        double? maximumNormalizedBoundsDelta = null;
        bool? normalizedGeometryMatches = null;
        if (capturedComponents.Length == replayComponents.Length)
        {
            var deltas = capturedComponents.Zip(replayComponents).Select(pair => new[]
            {
                Math.Abs(pair.First.Left / (double)captured.AnalysisWidth - pair.Second.Left / (double)actual.AnalysisWidth),
                Math.Abs(pair.First.Top / (double)captured.AnalysisHeight - pair.Second.Top / (double)actual.AnalysisHeight),
                Math.Abs(pair.First.Width / (double)captured.AnalysisWidth - pair.Second.Width / (double)actual.AnalysisWidth),
                Math.Abs(pair.First.Height / (double)captured.AnalysisHeight - pair.Second.Height / (double)actual.AnalysisHeight)
            }).SelectMany(delta => delta).ToArray();
            maximumNormalizedBoundsDelta = deltas.Length == 0 ? 0 : deltas.Max();
            normalizedGeometryMatches = maximumNormalizedBoundsDelta <= .003;
        }
        bool? decisionMatches = capturedVision.TryGetProperty("obstructed", out var capturedObstructed)
            && capturedObstructed.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? capturedObstructed.GetBoolean() == replay.Vision?.Obstructed : null;
        return (geometryMatches, normalizedGeometryMatches, maximumNormalizedBoundsDelta, decisionMatches);
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
    static object Compare(VisionResult low, VisionResult high, int lowWidth, int highWidth)
    {
        int leftWidth = lowWidth, rightWidth = highWidth;
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
            configuredWidthLeft = lowWidth, effectiveWidthLeft = leftWidth,
            configuredWidthRight = highWidth, effectiveWidthRight = rightWidth,
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
