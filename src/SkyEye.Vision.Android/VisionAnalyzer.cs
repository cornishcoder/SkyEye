using System.Diagnostics;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using Cv = Org.Opencv.Core.Core;
using Point = SkyEye.Core.Point;
using CvPoint = Org.Opencv.Core.Point;

namespace SkyEye.Vision.Android;

/// <summary>C# port of the upstream HSV/morphology/Hough pipeline with calibrated
/// pocket masks, cue-reference normalization and bounded C# colour clustering.</summary>
public sealed partial class VisionAnalyzer : ITableAnalyzer, ITableVerificationAnalyzer, ITableAnalyzerMetadata
{
    const int MinimumObstructionNeutralValueCeiling = 94;
    static readonly object loadGate = new(); static bool loaded;
    readonly AnalysisCoordinates coordinates;
    readonly VisionSettings settings;
    readonly bool boundaryDiagnostics;
    readonly bool candidateDiagnostics;
    readonly bool obstructionMaskDiagnostics;
    readonly Point? diagnosticNeutralCueProbeAt800;
    readonly (int Hue, int SaturationFloor, int ValueFloor, int MedianValue)? diagnosticObstructionClothRangeOverride;
    readonly (int Minimum, int Maximum)? diagnosticObstructionNeutralValueRangeOverride;
    byte[]? lastObstructionMaskDiagnosticPng;
    readonly record struct Circle(double X, double Y, double Radius);
    readonly record struct PackedClusterEvidence(bool Detected, int RedCentres, int YellowCentres, bool BlackCentre);
    public VisionAnalyzer(int width = 1000) : this(new VisionSettings(Width: width)) { }
    public VisionAnalyzer(VisionSettings settings, bool includeBoundaryDiagnostics = false, bool includeCandidateDiagnostics = false,
        bool includeObstructionMaskDiagnostics = false,
        (int Hue, int SaturationFloor, int ValueFloor, int MedianValue)? diagnosticObstructionClothRangeOverride = null,
        (int Minimum, int Maximum)? diagnosticObstructionNeutralValueRangeOverride = null,
        Point? diagnosticNeutralCueProbeAt800 = null)
    {
        settings.Validate(); this.settings = settings; boundaryDiagnostics = includeBoundaryDiagnostics;
        candidateDiagnostics = includeCandidateDiagnostics; obstructionMaskDiagnostics = includeObstructionMaskDiagnostics;
        this.diagnosticObstructionClothRangeOverride = diagnosticObstructionClothRangeOverride;
        this.diagnosticObstructionNeutralValueRangeOverride = diagnosticObstructionNeutralValueRangeOverride;
        this.diagnosticNeutralCueProbeAt800 = diagnosticNeutralCueProbeAt800;
        coordinates = new(settings.Width);
    }
    public byte[]? TakeObstructionMaskDiagnosticPng()
    {
        var result = lastObstructionMaskDiagnosticPng;
        lastObstructionMaskDiagnosticPng = null;
        return result;
    }
    public string ConfigurationIdentity
    {
        get
        {
            // Keep experiment-only knobs out of the production configuration
            // identity so default replay still matches historical receipts.
            // When an ablation is enabled, identify it explicitly as diagnostic.
            var productionSettings = new
            {
                settings.Width, settings.MinimumRadius, settings.MaximumRadius, settings.Separation,
                settings.MorphologyKernel, settings.HoughDp, settings.EdgeThreshold, settings.InitialVotes,
                settings.NormalizedVotes, settings.HueBand, settings.PocketExclusionRadius,
                settings.LocalIlluminationNormalization, settings.IlluminationKernelWidth,
                settings.FreshCueBallRecovery, settings.BodyProposalPriority, settings.MinimumHoughBoundarySupport,
                settings.MinimumSparseColourBoundarySupport,
                settings.SparseBlackBoundarySearchExtentFraction, settings.MinimumSparseBlackBoundarySupport,
                settings.SparseBlackRankByRadius,
                settings.DarkBodyValueCeiling, settings.VerificationBodyRefinement, settings.PackedClusterImageEdgeMargin,
                settings.PackedColourBoundarySearchExtentFraction
            };
            const string algorithm = "body-priority-packed-colour-peaks-v25-neutralceiling94-edge23-pocket48-sparse20-review75-redyellow-black-radius13-support21";
            bool normalizedProposalAblation = settings.UseNormalizedGeometryForDetection || settings.UseNormalizedPackedColourProposalMask;
            bool edgeMarginAblation = settings.PackedClusterImageEdgeMargin != 23;
            bool sparseColourSupportAblation = settings.MinimumSparseColourBoundarySupport
                != VisionSettings.DefaultMinimumSparseColourBoundarySupport;
            var diagnosticAblations = new Dictionary<string, object>();
            if (normalizedProposalAblation)
            {
                diagnosticAblations["UseNormalizedGeometryForDetection"] = settings.UseNormalizedGeometryForDetection;
                diagnosticAblations["UseNormalizedPackedColourProposalMask"] = settings.UseNormalizedPackedColourProposalMask;
            }
            if (edgeMarginAblation)
                diagnosticAblations["packedClusterImageEdgeMargin"] = settings.PackedClusterImageEdgeMargin;
            if (sparseColourSupportAblation)
                diagnosticAblations["minimumSparseColourBoundarySupport"] = settings.MinimumSparseColourBoundarySupport;
            if (settings.SparseBlackCandidateLimit != 12)
                diagnosticAblations["sparseBlackCandidateLimit"] = settings.SparseBlackCandidateLimit;
            if (diagnosticObstructionClothRangeOverride is { } clothRange)
                diagnosticAblations["obstructionClothRangeOverride"] = new
                {
                    clothRange.Hue, clothRange.SaturationFloor, clothRange.ValueFloor, clothRange.MedianValue
                };
            if (diagnosticObstructionNeutralValueRangeOverride is { } neutralRange)
                diagnosticAblations["obstructionNeutralValueRangeOverride"] = new
                {
                    neutralRange.Minimum, neutralRange.Maximum
                };
            object identity = diagnosticAblations.Count > 0
                ? new
                {
                    Algorithm = algorithm,
                    Settings = productionSettings,
                    DiagnosticAblations = diagnosticAblations
                }
                : new { Algorithm = algorithm, Settings = productionSettings };
            return System.Text.Json.JsonSerializer.Serialize(identity);
        }
    }
    static void Load()
    {
        lock (loadGate)
        {
            if (loaded) return;
            if (!OpenCVLoader.InitLocal()) throw new IOException("Table analysis could not load OpenCV.");
            // Leave mobile CPU capacity for playback, voice and Android's UI thread.
            Cv.NumThreads = Math.Min(4, Environment.ProcessorCount); loaded = true;
        }
    }
    internal static void EnsureNativeLoaded() => Load();
    public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default)
    {
        Load(); using var source = Decode(jpeg); return AnalyzePixels(source.Value, calibration, cancellation);
    }
    public VisionResult Analyze(TableFrame frame, TableCalibration calibration, CancellationToken cancellation = default)
    {
        if (frame.Image is not BitmapTableImage pixels) return Analyze(frame.Jpeg, calibration, cancellation);
        Load(); cancellation.ThrowIfCancellationRequested();
        using var rgba = new MatLease(new Mat()); using var bgr = new MatLease(new Mat());
        using (PipelineDiagnostics.Measure("bitmapConversionMs"))
        { Utils.BitmapToMat(pixels.Bitmap, rgba.Value); Imgproc.CvtColor(rgba.Value, bgr.Value, Imgproc.ColorRgba2bgr); }
        return AnalyzePixels(bgr.Value, calibration, cancellation);
    }
    VisionResult AnalyzePixels(Mat source, TableCalibration calibration, CancellationToken cancellation, BurstAppearance? appearance = null)
    {
        var primary = AnalyzePixelsCore(source, calibration, cancellation, appearance);
        if (settings.Width >= 1000 ||
            primary.PackedCluster is not { Detected: true, Complete: false }) return primary;

        cancellation.ThrowIfCancellationRequested();
        var fallbackAnalyzer = new VisionAnalyzer(settings with { Width = 1000 }, boundaryDiagnostics, candidateDiagnostics,
            obstructionMaskDiagnostics, diagnosticNeutralCueProbeAt800: diagnosticNeutralCueProbeAt800);
        var fallback = fallbackAnalyzer.AnalyzePixelsCore(source, calibration, cancellation, appearance);
        bool fallbackComplete = fallback.PackedCluster?.Complete == true;
        bool useFallback = fallbackComplete || fallback.CircleCount > primary.CircleCount;
        var chosen = useFallback ? fallback : primary;
        if (useFallback && obstructionMaskDiagnostics)
            lastObstructionMaskDiagnosticPng = fallbackAnalyzer.TakeObstructionMaskDiagnosticPng();
        bool remainsUnresolved = !fallbackComplete && primary.PackedCluster is { Detected: true, Complete: false };
        string[] warnings = remainsUnresolved
            ? primary.Warnings.Concat(fallback.Warnings).Append("Packed colour cluster still has unresolved visible balls; review it before using advice.").Distinct().ToArray()
            : chosen.Warnings;
        return chosen with
        {
            ElapsedMs = primary.ElapsedMs + fallback.ElapsedMs,
            // The fallback detector works at 1000px, but its observations are
            // published alongside results from the configured analysis grid.
            // Keep CircleObservation geometry in one coordinate space so later
            // motion/consensus checks do not compare mixed-width radii.
            Circles = useFallback && settings.Width != 1000
                ? RescaleCircleObservations(chosen.Circles, settings.Width / 1000d)
                : chosen.Circles,
            Uncertain = chosen.Uncertain || remainsUnresolved,
            Obstructed = chosen.Obstructed || (remainsUnresolved && primary.Obstructed),
            Warnings = warnings,
            PackedCluster = remainsUnresolved ? primary.PackedCluster : chosen.PackedCluster,
            PackedClusterFallback = new(true, settings.Width, 1000,
                primary.PackedCluster?.Detected == true, primary.PackedCluster?.Complete == true,
                primary.ElapsedMs, fallback.ElapsedMs,
                primary.Balls.Length, fallback.Balls.Length, chosen.PackedCluster?.Complete == true)
        };
    }

    static CircleObservation[] RescaleCircleObservations(CircleObservation[] observations, double scale)
        => observations.Select(circle => circle with
        {
            Centre = circle.Centre * scale,
            Radius = circle.Radius * scale,
            ProposalCentre = circle.ProposalCentre is { } proposal ? proposal * scale : null,
            ProposalRadius = circle.ProposalRadius * scale
        }).ToArray();

    VisionResult AnalyzePixelsCore(Mat source, TableCalibration calibration, CancellationToken cancellation, BurstAppearance? appearance = null)
    {
        cancellation.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew(); var warnings = new List<string>();
        using var rawWarped = Warp(source, calibration);
        using var rawHsv = new MatLease(new Mat()); Imgproc.CvtColor(rawWarped.Value, rawHsv.Value, Imgproc.ColorBgr2hsv);
        byte[] rawHsvPixels = Read(rawHsv.Value);
        var boundaryColourMask = BoundaryColourMask.FromHsvPixels(rawHsvPixels);
        using var warped = new MatLease(new Mat()); rawWarped.Value.CopyTo(warped.Value);
        IlluminationTelemetry illumination;
        if (settings.LocalIlluminationNormalization)
        {
            using var correction = PipelineDiagnostics.Measure("illuminationNormalizationMs");
            illumination = NormalizeIllumination(warped.Value, cancellation);
        }
        else illumination = new(false, 0, 1, 1, 0);
        using var hsv = new MatLease(new Mat()); Imgproc.CvtColor(warped.Value, hsv.Value, Imgproc.ColorBgr2hsv);
        byte[] pixels = Read(hsv.Value); var packedClassifications = new Dictionary<Circle, BallClassification>();
        Mat geometryImage = settings.UseNormalizedGeometryForDetection ? warped.Value : rawWarped.Value;
        Mat geometryHsv = settings.UseNormalizedGeometryForDetection ? hsv.Value : rawHsv.Value;
        byte[] geometryHsvPixels = settings.UseNormalizedGeometryForDetection ? pixels : rawHsvPixels;
        Mat packedColourProposalHsv = settings.UseNormalizedPackedColourProposalMask ? hsv.Value : geometryHsv;
        BoundaryColourMask geometryBoundaryColourMask = settings.UseNormalizedGeometryForDetection
            ? BoundaryColourMask.FromHsvPixels(geometryHsvPixels) : boundaryColourMask;
        var candidateAudits = candidateDiagnostics ? new List<VisionCandidateAudit>() : null;
        var acceptedPackedBlackBoundaries = new Dictionary<Circle, BallBoundary>();
        var circles = Detect(hsv.Value, pixels, settings.InitialVotes, out var packedCluster,
            out bool conflictingCoincidentCandidate,
            appearance is not null && settings.VerificationBodyRefinement,
            settings.FreshCueBallRecovery && settings.BodyProposalPriority ? geometryImage : null,
            geometryHsv, geometryHsvPixels, geometryBoundaryColourMask, packedClassifications,
            candidateAudits, "initial", acceptedPackedBlackBoundaries, packedColourProposalHsv);
        bool inspectRim = appearance is not null || settings.FreshCueBallRecovery;
        var initialLabels = circles.Select(c => Classify(c, pixels, inspectRim)).ToArray();
        int initialCueCount = initialLabels.Count(l => l.Kind == "cue");
        int neutralCandidates = 0, neutralRecovered = 0;
        NeutralCueCandidateTelemetry[] neutralCandidateAudits = [];
        NeutralCueMaskTelemetry? neutralMaskTelemetry = null;
        var recoveredBoundaries = new Dictionary<Circle, BallBoundary>();
        var recoveredClassifications = new Dictionary<Circle, BallClassification>();
        int rejectedCueBodies = 0, rejectedNormalizedCueBodies = 0;
        if (settings.FreshCueBallRecovery)
            ValidateCueBodies(rawWarped.Value, circles, initialLabels, recoveredBoundaries,
                recoveredClassifications, cancellation, out rejectedCueBodies);
        var initialClassificationCache = new Dictionary<Circle, BallClassification>();
        for (int i = 0; i < circles.Length; i++) initialClassificationCache[circles[i]] = initialLabels[i];
        if (settings.FreshCueBallRecovery && initialLabels.All(l => l.Kind != "cue"))
            circles = RecoverNeutralCueBodies(rawWarped.Value, circles, recoveredBoundaries, recoveredClassifications,
                cancellation, out neutralCandidates, out neutralRecovered, out neutralCandidateAudits,
                out neutralMaskTelemetry, candidateDiagnostics);
        var proposals = circles.ToArray();
        var boundaries = new Dictionary<Circle, BallBoundary>(recoveredBoundaries);
        var candidateBoundaries = new Dictionary<Circle, BallBoundary>();
        bool boundaryUncertain = false;
        if (appearance is not null)
        {
            var raw = Read(rawWarped.Value);
            for (int i = 0; i < circles.Length; i++)
            {
                bool packedOcclusionValidated = acceptedPackedBlackBoundaries.TryGetValue(circles[i], out var packedBlackBoundary);
                var fit = recoveredBoundaries.GetValueOrDefault(circles[i]) ?? packedBlackBoundary
                    ?? BallBoundaryRefiner.Refine(raw, coordinates.Width, coordinates.Height, new(circles[i].X, circles[i].Y),
                        PoolGeometry.Radius * coordinates.Width, collectSectorEvidence: boundaryDiagnostics,
                        expectedKind: initialLabels[i].Kind, colourMask: boundaryColourMask);
                if (fit is null) { boundaryUncertain = true; continue; }
                candidateBoundaries[circles[i]] = fit;
                // Support is sampled at 32 fixed angles. The 18-sector minimum
                // recovers a clear mounted ball with 18-20 supported sectors;
                // repeated current-frame consensus still gates publication.
                if (fit.SupportedSectors < ObservationPolicy.MinimumSupportedBoundaryFraction && !packedOcclusionValidated)
                { boundaryUncertain = true; continue; }
                var refined = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
                boundaries[refined] = fit; candidateBoundaries[refined] = fit; circles[i] = refined;
            }
        }
        var first = circles.Select(c => initialClassificationCache.TryGetValue(c, out var cached)
            ? cached : Classify(c, pixels, inspectRim)).ToArray();
        // A supported neutral body is evidence from this frame's raw colour
        // image. Illumination smoothing must not erase it before white balancing.
        for (int i = 0; i < circles.Length; i++)
            if (recoveredClassifications.TryGetValue(circles[i], out var rawCue)) first[i] = rawCue;
        var classificationCache = new Dictionary<Circle, BallClassification>();
        for (int i = 0; i < circles.Length; i++) classificationCache[circles[i]] = first[i];
        Circle? referenceCue = null;
        int? normalizedCueCount = null;
        bool cueGeometryRetained = false;
        double? retainedBoundarySupport = null;
        string cueRecoveryReason = settings.FreshCueBallRecovery ? "no-unique-first-pass-cue" : "disabled";
        int rawCircleCount = circles.Length;
        bool normalized = false, referenceValid = false; double[] colourGains = [1, 1, 1]; var cueIndices = Enumerable.Range(0, first.Length).Where(i => first[i].Kind == "cue").ToArray();
        if (cueIndices.Length == 1)
        {
            referenceCue = circles[cueIndices[0]];
            if (settings.FreshCueBallRecovery)
                cueRecoveryReason = neutralRecovered > 0 ? "recovered-neutral-cue-midrange-reference"
                    : appearance is null ? "colour-reference-unavailable" : "verification-preserves-geometry";
            byte[] bgr = Read(warped.Value); var cue = circles[cueIndices[0]];
            var channels = new[] { new List<byte>(), new List<byte>(), new List<byte>() };
            var luminanceValues = new List<byte>();
            ForPixels(cue, .7, (x, y) =>
            {
                int offset = (y * coordinates.Width + x) * 3;
                for (int k = 0; k < 3; k++) channels[k].Add(bgr[offset + k]);
                luminanceValues.Add((byte)Math.Clamp((int)Math.Round(.114 * bgr[offset] + .587 * bgr[offset + 1] + .299 * bgr[offset + 2]), 0, 255));
            });
            double[] gains;
            if (neutralRecovered > 0)
            {
                // Mixed-neutral recovery supports cue geometry, but its channel
                // ratios are not a trustworthy white reference. Lift brightness
                // from sampled luma only so red/yellow chroma stays unchanged.
                luminanceValues.Sort();
                double p95 = luminanceValues.Count == 0 ? 0 : luminanceValues[(int)((luminanceValues.Count - 1) * .95)];
                double gain = p95 < 10 ? 0 : Math.Min(3, 128.0 / p95);
                gains = [gain, gain, gain];
                cueRecoveryReason = "recovered-neutral-cue-luma-reference";
            }
            else
            {
                double[] reference = channels.Select(c => { c.Sort(); return c.Count == 0 ? 0.0 : (double)c[(int)((c.Count - 1) * .95)]; }).ToArray();
                gains = reference.Select(value => value < 10 ? 0 : Math.Min(3, 255.0 / value)).ToArray();
            }
            if (gains.All(g => g > 0)) { colourGains = gains; referenceValid = true; }
        }
        if (appearance is not null)
        {
            if (referenceValid)
            {
                // Re-estimate the cue-white balance from each detected frame. A
                // changed exposure is a normal input condition, not a conflict;
                // the verifier still demands stable ball geometry and motion.
                appearance.Gains = colourGains.ToArray(); appearance.ReferenceValid = true; appearance.HasReference = true;
            }
            else if (appearance.HasReference && appearance.ReferenceValid)
            {
                // A brief cue dropout may reuse the last validated reference.
                colourGains = appearance.Gains.ToArray(); referenceValid = true;
            }
        }
        if (referenceValid)
        {
            byte[] bgr = Read(warped.Value);
            {
                for (int i = 0; i < bgr.Length; i++) bgr[i] = (byte)Math.Clamp((int)(bgr[i] * colourGains[i % 3]), 0, 255);
                warped.Value.Put(0, 0, bgr); Imgproc.CvtColor(warped.Value, hsv.Value, Imgproc.ColorBgr2hsv); pixels = Read(hsv.Value);
                classificationCache.Clear(); // Cached labels belong to the previous colour image.
                // Colour correction classifies this frame's geometry. Replacing
                // all proposals here can lose an already observed ball or add
                // clipped-cloth circles. Preserve the original branch for ablation.
                if (appearance is null)
                {
                    if (!settings.FreshCueBallRecovery)
                    {
                        circles = Detect(hsv.Value, pixels, settings.NormalizedVotes, out _,
                            out bool normalizedCoincidentConflict,
                            packedClassifications: packedClassifications, candidateAudits: candidateAudits, pass: "normalized");
                        conflictingCoincidentCandidate |= normalizedCoincidentConflict;
                    }
                    var normalizedLabels = circles.Select(c => Classify(c, pixels, inspectRim)).ToArray();
                    normalizedCueCount = normalizedLabels.Count(l => l.Kind == "cue");
                    if (settings.FreshCueBallRecovery)
                    {
                        ValidateCueBodies(rawWarped.Value, circles, normalizedLabels, recoveredBoundaries,
                            recoveredClassifications, cancellation, out rejectedNormalizedCueBodies);
                        foreach (var pair in recoveredBoundaries) boundaries[pair.Key] = pair.Value;
                    }
                    for (int i = 0; i < circles.Length; i++) classificationCache[circles[i]] =
                        packedClassifications.TryGetValue(circles[i], out var packedLabel) ? packedLabel : normalizedLabels[i];
                    if (settings.FreshCueBallRecovery && referenceCue is { } currentCue)
                    {
                        cueRecoveryReason = "preserved-current-frame-geometry";
                        cueGeometryRetained = true;
                        retainedBoundarySupport = recoveredBoundaries.GetValueOrDefault(currentCue)?.SupportedSectors;
                        // The raw, full-body cue observation remains fresh evidence
                        // even if colour correction clips or shifts its appearance.
                        for (int i = 0; i < circles.Length; i++)
                            if (recoveredClassifications.TryGetValue(circles[i], out var supportedLabel))
                                classificationCache[circles[i]] = supportedLabel;
                    }
                }
                else if (settings.FreshCueBallRecovery && referenceCue is { } liveCurrentCue)
                {
                    // The live-appearance path keeps detector geometry instead
                    // of rerunning normalized proposals. Preserve the same
                    // validated raw cue label used by the offline path.
                    cueRecoveryReason = "preserved-current-frame-geometry";
                    cueGeometryRetained = true;
                    retainedBoundarySupport = recoveredBoundaries.GetValueOrDefault(liveCurrentCue)?.SupportedSectors;
                    for (int i = 0; i < circles.Length; i++)
                        if (recoveredClassifications.TryGetValue(circles[i], out var supportedLabel))
                            classificationCache[circles[i]] = supportedLabel;
                }
                normalized = true;
            }
        }
        if (!normalized) warnings.Add("No unique cue ball reference; colours were analyzed without normalization.");
        cancellation.ThrowIfCancellationRequested(); var balls = new List<Ball>(); var observations = new List<CircleObservation>();
        var boundaryAudits = new List<CircleBoundaryAudit>();
        var ordered = circles.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        var rawLabels = ordered.Select(c => classificationCache.TryGetValue(c, out var cached)
            ? cached : Classify(c, pixels, inspectRim)).ToArray();
        var labels = BallClassifier.ConstrainInventory(rawLabels);
        for (int i = 0; i < ordered.Length; i++)
        {
            var circle = ordered[i]; var label = labels[i]; var position = coordinates.ToTable(new(circle.X, circle.Y));
            balls.Add(new(label.Kind + "-" + balls.Count, label.Kind, position, label.Quality));
            observations.Add(new(balls[^1].Id, new(circle.X, circle.Y), circle.Radius, label.Pattern, label.Clusters)
            { BodyDarkFraction = label.BodyDarkFraction, RimDarkFraction = label.RimDarkFraction,
                ProposalCentre = appearance is null ? null : new(proposals[Array.IndexOf(circles, circle)].X, proposals[Array.IndexOf(circles, circle)].Y),
                ProposalRadius = appearance is null ? null : proposals[Array.IndexOf(circles, circle)].Radius,
                BoundarySupport = boundaries.GetValueOrDefault(circle)?.SupportedSectors,
                CandidateBoundarySupport = candidateBoundaries.GetValueOrDefault(circle)?.SupportedSectors });
            int proposalIndex = Array.IndexOf(circles, circle);
            if (boundaryDiagnostics && proposalIndex >= 0 && proposalIndex < proposals.Length
                && candidateBoundaries.GetValueOrDefault(circle) is { } candidate
                && candidate.SectorEvidence.Length > 0)
            {
                var proposal = proposals[proposalIndex]; double expectedRadius = PoolGeometry.Radius * coordinates.Width;
                bool fitDrifted = (candidate.Centre - new Point(proposal.X, proposal.Y)).Length > expectedRadius * .25
                    || Math.Abs(candidate.Radius - expectedRadius) > expectedRadius * .15;
                if (candidate.SupportedSectors < ObservationPolicy.MinimumSupportedBoundaryFraction || fitDrifted)
                    boundaryAudits.Add(new(balls[^1].Id, new(proposal.X, proposal.Y), proposal.Radius, expectedRadius,
                        expectedRadius * .4, Math.Max(1.5, expectedRadius / 9), expectedRadius * .75,
                        expectedRadius * 1.25, ObservationPolicy.MinimumSupportedBoundaryFraction, candidate));
            }
        }
        bool layoutUncertain = balls.Any(b => b.Kind == "unknown") || balls.Count > 16
            || balls.Count(b => b.Kind == "cue") > 1 || balls.Count(b => b.Kind == "black") > 1
            || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7;
        bool uncertain = layoutUncertain || balls.Count(b => b.Kind == "cue") != 1
            || balls.Count(b => b.Kind == "black") != 1;
        if (conflictingCoincidentCandidate)
        {
            uncertain = layoutUncertain = true;
            warnings.Add("Conflicting ball candidates share nearly the same centre; review before using table state.");
        }
        bool packedClusterUnresolved = packedCluster.Detected &&
            (balls.Any(b => b.Kind == "unknown") || balls.Count(b => b.Kind == "black") != 1 ||
                balls.Count(b => b.Kind == "red") != 7 || balls.Count(b => b.Kind == "yellow") != 7);
        if (packedClusterUnresolved)
        { uncertain = layoutUncertain = true; warnings.Add("Packed colour cluster still has unresolved visible balls; review it before using advice."); }
        if (boundaryUncertain) { uncertain = layoutUncertain = true; warnings.Add("Current ball boundary lacks sufficient edge support."); }
        if (appearance is not null && !referenceValid)
        {
            uncertain = true;
            if (balls.Count(b => b.Kind == "cue") != 0) layoutUncertain = true;
            warnings.Add("No validated burst colour reference; raw colour labels are retained for layout review, not advice.");
        }
        if (uncertain) warnings.Add("Check uncertain colours or ball counts before using advice.");
        if (PocketColourNeedsReview(pixels, rawWarped.Value, ordered, labels, boundaryColourMask, out var pocketColourAudit))
        { uncertain = layoutUncertain = true; warnings.Add("Check a coloured ball at a pocket opening; the pocket mask may hide part of it."); }
        Circle[] recognizedBallCircles = ordered.Where((_, index) => labels[index].Kind != "unknown").ToArray();
        bool completePackedCluster = packedCluster.Detected && !packedClusterUnresolved;
        bool obstructed = LargeObstruction(rawHsv.Value, rawHsvPixels, recognizedBallCircles, completePackedCluster, out var obstructionAudit);
        if (obstructed) { uncertain = layoutUncertain = true; warnings.Add("A large object covers part of the playable surface."); }
        for (int i = 0; i < balls.Count; i++) for (int j = i + 1; j < balls.Count; j++) if ((balls[i].Position - balls[j].Position).Length < .040) { uncertain = layoutUncertain = true; warnings.Add("Overlapping circle candidates need checking."); }
        return new(balls.ToArray(), uncertain, warnings.Distinct().ToArray(), watch.Elapsed.TotalMilliseconds, circles.Length, normalized, Cv.Version, colourGains, ClothHue(pixels), obstructed, observations.ToArray())
        { Recognition = new(normalized ? "cue-normalized" : "raw", rawCircleCount, circles.Length, rawLabels.Select(l => l.Kind).ToArray(), colourGains, ClothHue(pixels), warnings.Distinct().ToArray())
            { CueBallRecovery = new(initialCueCount, cueIndices.Length, normalizedCueCount,
                labels.Count(l => l.Kind == "cue"), neutralCandidates, neutralRecovered,
                cueGeometryRetained, cueRecoveryReason, retainedBoundarySupport)
                {
                    RejectedInitialCueBodies = rejectedCueBodies,
                    RejectedNormalizedCueBodies = rejectedNormalizedCueBodies,
                    NeutralCandidateAudits = neutralCandidateAudits,
                    NeutralMaskTelemetry = neutralMaskTelemetry
                } },
            Illumination = illumination,
            PackedCluster = new(packedCluster.Detected, packedCluster.Detected && !packedClusterUnresolved,
                packedCluster.RedCentres, packedCluster.YellowCentres, packedCluster.BlackCentre),
            LayoutUncertain = layoutUncertain,
            ObstructionAudit = obstructionAudit,
            PocketColourAudit = pocketColourAudit,
            BoundaryAudits = boundaryAudits.ToArray(), CandidateAudits = candidateAudits?.ToArray() ?? [] };
    }
    IlluminationTelemetry NormalizeIllumination(Mat image, CancellationToken cancellation)
    {
        int width = image.Cols(), height = image.Rows();
        using var gray = new MatLease(new Mat()); using var field = new MatLease(new Mat());
        using (PipelineDiagnostics.Measure("illuminationGrayMs"))
            Imgproc.CvtColor(image, gray.Value, Imgproc.ColorBgr2gray);
        int maximumKernel = Math.Min(width, height);
        if (maximumKernel % 2 == 0) maximumKernel--;
        int kernelWidth = Math.Min(maximumKernel, Math.Max(3, (int)Math.Round(settings.IlluminationKernelWidth * width / 1000.0) | 1));
        if (kernelWidth < 3) return new(false, 0, 1, 1, 0);
        using (PipelineDiagnostics.Measure("illuminationBlurMs"))
        using (var size = new Size(kernelWidth, kernelWidth)) Imgproc.GaussianBlur(gray.Value, field.Value, size, 0);
        byte[] illumination = Read(field.Value);
        var tableSamples = new List<byte>();
        for (int y = height / 10; y < height * 9 / 10; y += 10)
            for (int x = width / 10; x < width * 9 / 10; x += 10)
            {
                byte value = illumination[y * width + x];
                if (value > 12) tableSamples.Add(value);
            }
        if (tableSamples.Count < 20) return new(false, 0, 1, 1, 0);
        tableSamples.Sort();
        double reference = tableSamples[tableSamples.Count / 2];
        // If the whole playable area is underexposed, using its low median as
        // the target makes every dark pixel hit the minimum 0.65 gain. That
        // attenuates the entire scene and can erase otherwise visible ball
        // colour. Keep the measured reference for diagnostics, but floor the
        // normalization target so low-light pixels are preserved rather than
        // darkened again.
        double targetBrightness = Math.Max(55, reference);
        byte[] bgr = Read(image);
        // The gain depends only on the local brightness byte. Precompute the
        // exact per-channel rounded output so the two-million-pixel pass does
        // table lookups instead of repeating division, rounding and clamping
        // for every colour channel.
        var gainsByBrightness = new double[256];
        var adjustedByBrightness = new bool[256];
        var correctedChannelByBrightness = new byte[256 * 256];
        for (int brightness = 0; brightness < 256; brightness++)
        {
            double gain = Math.Clamp(targetBrightness / Math.Max(55, brightness), .65, 1.55);
            gainsByBrightness[brightness] = gain;
            adjustedByBrightness[brightness] = Math.Abs(gain - 1) > .05;
            int lookupBase = brightness << 8;
            for (int channel = 0; channel < 256; channel++)
                correctedChannelByBrightness[lookupBase + channel] =
                    (byte)Math.Clamp((int)Math.Round(channel * gain), 0, 255);
        }

        var brightnessCounts = new int[256];
        for (int y = 0; y < height; y++)
        {
            if ((y & 31) == 0) cancellation.ThrowIfCancellationRequested();
            int row = y * width;
            int rowOffset = row * 3;
            for (int x = 0; x < width; x++)
            {
                int brightness = illumination[row + x];
                brightnessCounts[brightness]++;
                int lookupBase = brightness << 8;
                int offset = rowOffset + x * 3;
                bgr[offset] = correctedChannelByBrightness[lookupBase + bgr[offset]];
                bgr[offset + 1] = correctedChannelByBrightness[lookupBase + bgr[offset + 1]];
                bgr[offset + 2] = correctedChannelByBrightness[lookupBase + bgr[offset + 2]];
            }
        }
        cancellation.ThrowIfCancellationRequested();
        image.Put(0, 0, bgr);
        double minimumGain = double.MaxValue, maximumGain = 0;
        long adjusted = 0;
        for (int brightness = 0; brightness < brightnessCounts.Length; brightness++)
        {
            int count = brightnessCounts[brightness];
            if (count == 0) continue;
            double gain = gainsByBrightness[brightness];
            minimumGain = Math.Min(minimumGain, gain);
            maximumGain = Math.Max(maximumGain, gain);
            if (adjustedByBrightness[brightness]) adjusted += count;
        }
        return new(true, reference, minimumGain, maximumGain, adjusted * 100d / (width * (double)height));
    }
    MatLease Warp(Mat source, TableCalibration calibration)
    {
        CvPoint[] original = calibration.Corners.Select(p => new CvPoint(p.X * source.Cols(), p.Y * source.Rows())).ToArray();
        CvPoint[] portrait = [new(coordinates.Width, 0), new(coordinates.Width, coordinates.Height), new(0, coordinates.Height), new(0, 0)];
        try
        {
            using var src = new MatLease(new MatOfPoint2f(original)); using var dst = new MatLease(new MatOfPoint2f(portrait));
            using var transform = new MatLease(Imgproc.GetPerspectiveTransform(src.Value, dst.Value)); var output = new MatLease(new Mat());
            try { using var size = new Size(coordinates.Width, coordinates.Height); Imgproc.WarpPerspective(source, output.Value, transform.Value, size); return output; }
            catch { output.Dispose(); throw; }
        }
        finally { foreach (var point in original.Concat(portrait)) point.Dispose(); }
    }
    Circle[] Detect(Mat hsv, byte[] pixels, double votes, out PackedClusterEvidence packedCluster,
        out bool conflictingCoincidentCandidate,
        bool refineGeometry = false, Mat? rawImage = null, Mat? rawHsv = null, byte[]? rawHsvPixels = null,
        BoundaryColourMask? boundaryColourMask = null,
        Dictionary<Circle, BallClassification>? packedClassifications = null,
        List<VisionCandidateAudit>? candidateAudits = null, string pass = "initial",
        Dictionary<Circle, BallBoundary>? acceptedPackedBlackBoundaries = null,
        Mat? packedColourProposalHsv = null)
    {
        using var timing = PipelineDiagnostics.Measure("circleDetectionMs");
        packedCluster = default;
        conflictingCoincidentCandidate = false;
        bool conflictingCoincidentCandidateDetected = false;
        using var mask = ClothMask(hsv, pixels); using var circles = new MatLease(new Mat());
        double scale = coordinates.Width / 1000.0;
        using (PipelineDiagnostics.Measure("houghCirclesMs"))
            Imgproc.HoughCircles(mask.Value, circles.Value, Imgproc.HoughGradient, settings.HoughDp, settings.Separation * scale, settings.EdgeThreshold, votes * scale, (int)(settings.MinimumRadius * scale), (int)(settings.MaximumRadius * scale));
        var result = new List<Circle>(); int count = Math.Max(circles.Value.Cols(), circles.Value.Rows()); var clothRange = ClothRange(pixels); int clothHue = clothRange.Hue;
        PipelineDiagnostics.Sample("houghCircleCandidateCount", count);
        // Strong compact paint bodies get a bounded proposal budget before
        // binary-mask texture can fill the Hough list with cloth holes.
        bool prioritizeBodies = settings.FreshCueBallRecovery && settings.BodyProposalPriority;
        byte[]? rawBoundaryPixels = null;
        if (prioritizeBodies && rawImage is not null)
        {
            using var packedTiming = PipelineDiagnostics.Measure("packedClusterBodyProposalsMs");
            packedCluster = AddPackedClusterBodies(rawImage, rawHsv!, packedColourProposalHsv ?? rawHsv!, rawHsvPixels!, boundaryColourMask!, result, scale,
                packedClassifications, candidateAudits, pass, acceptedPackedBlackBoundaries);
        }
        if (prioritizeBodies)
        {
            using var colourBodyTiming = PipelineDiagnostics.Measure("colourBodyProposalsMs");
            AddColourBodies(hsv, pixels, result, scale);
        }
        if (prioritizeBodies && rawImage is not null)
        {
            using var darkBodyTiming = PipelineDiagnostics.Measure("darkBodyProposalsMs");
            AddDarkBodies(rawImage, result, scale, packedClassifications, candidateAudits, pass);
        }
        for (int i = 0; i < count && result.Count <= 32; i++)
        {
            var value = circles.Value.Get(circles.Value.Rows() == 1 ? 0 : i, circles.Value.Rows() == 1 ? i : 0);
            if (value is null || value.Length < 3) continue;
            var c = new Circle(value[0], value[1], value[2]); double margin = 23 * scale;
            string? fittedKind = null;
            double? fittedSupport = null;
            if (c.X < margin || c.Y < margin || c.X > coordinates.Width - margin || c.Y > coordinates.Height - margin)
            { Audit("hough", "unknown", i, c, "rejected-margin"); continue; }
            // An opening is not a ball; ball-adjacent uncertain cases remain visible through review.
            if (PocketCentres().Any(p => (new Point(c.X, c.Y) - p).Length < settings.PocketExclusionRadius * scale))
            { Audit("hough", "unknown", i, c, "rejected-pocket-exclusion"); continue; }
            if (prioritizeBodies)
            {
                string proposedKind = Classify(c, pixels, inspectRim: true).Kind;
                if (HasNearbyDuplicate(c, proposedKind))
                { Audit("hough", "unknown", i, c, "rejected-duplicate-before-fit"); continue; }
            }
            // Cloth shadows can produce circle edges anywhere. Keep the sampled
            // cloth rejection bounded, while rescuing a low-light neutral circle
            // only when its body contrasts with the immediately surrounding cloth.
            {
                int sampled = 0, cloth = 0;
                ForPixels(c, .85, (x, y) =>
                {
                    int offset = (y * coordinates.Width + x) * 3; int difference = Math.Abs(pixels[offset] - clothHue); sampled++;
                    if ((Math.Min(difference, 180 - difference) <= Math.Max(12, settings.HueBand) || pixels[offset + 1] < 45) &&
                        pixels[offset + 1] < Math.Max(60, clothRange.SaturationFloor / .45 * 1.5) &&
                        pixels[offset + 2] > 20 && (prioritizeBodies || pixels[offset + 2] < NeutralClothValueCeiling(clothRange.ValueFloor))) cloth++;
                });
                if (sampled > 0 && cloth / (double)sampled > .9
                    && !(prioritizeBodies ? HasLocalBodyValueContrast(c, pixels) : HasLowLightNeutralBallContrast(c, pixels)))
                { Audit("hough", "unknown", i, c, "rejected-as-cloth"); continue; }
            }
            if (prioritizeBodies && rawImage is not null)
            {
                rawBoundaryPixels ??= Read(rawImage);
                var fit = BallBoundaryRefiner.Refine(rawBoundaryPixels, coordinates.Width, coordinates.Height,
                    new(c.X, c.Y), PoolGeometry.Radius * coordinates.Width, settings.MinimumHoughBoundarySupport,
                    boundaryDiagnostics);
                if (fit is null || fit.SupportedSectors < settings.MinimumHoughBoundarySupport)
                { Audit("hough", "unknown", i, c, fit is null ? "rejected-no-boundary-fit" : "rejected-insufficient-boundary-support", fit?.Centre, fit?.Radius, fit?.SupportedSectors); continue; }
                c = new(fit.Centre.X, fit.Centre.Y, fit.Radius);
                fittedSupport = fit.SupportedSectors;
                // The proposal can be outside a pocket's exclusion radius while
                // its fitted centre drifts into the opening. Recheck the fitted
                // geometry so a pocket edge cannot become a coloured ball.
                if (PocketCentres().Any(p => (new Point(c.X, c.Y) - p).Length < settings.PocketExclusionRadius * scale))
                { Audit("hough", "unknown", i, c, "rejected-pocket-exclusion-after-fit", new(c.X, c.Y), c.Radius, fit.SupportedSectors); continue; }
                fittedKind = Classify(c, pixels, inspectRim: true).Kind;
                if (HasNearbyDuplicate(c, fittedKind))
                { Audit("hough", "unknown", i, c, "rejected-duplicate-after-fit", new(c.X, c.Y), c.Radius, fit.SupportedSectors); continue; }
                // A fit near a neighbouring edge can move off the initial
                // contrast patch. Its centre must still belong to a body.
                if (IsClothBody(c, pixels, clothRange) && !HasLocalBodyValueContrast(c, pixels))
                { Audit("hough", "unknown", i, c, "rejected-fitted-cloth-body", new(c.X, c.Y), c.Radius, fit.SupportedSectors); continue; }
            }
            result.Add(c);
            if (fittedKind is null && candidateAudits is not null)
                fittedKind = Classify(c, pixels, inspectRim: true).Kind;
            Audit("hough", "unknown", i, c, "accepted", new(c.X, c.Y), c.Radius, fittedSupport, fittedKind);
        }
        if (!prioritizeBodies) AddColourBodies(hsv, pixels, result, scale);
        if (refineGeometry)
        {
            using var refinementTiming = PipelineDiagnostics.Measure("bodyGeometryRefinementMs");
            RefineBodyGeometry(mask.Value, result, scale);
        }
        conflictingCoincidentCandidate = conflictingCoincidentCandidateDetected;
        return result.ToArray();

        void Audit(string stage, string kind, int index, Circle proposal, string decision,
            Point? fitCentre = null, double? fitRadius = null, double? support = null, string? classifiedKind = null)
        {
            candidateAudits?.Add(new(pass, stage, kind, index, new(proposal.X, proposal.Y), proposal.Radius,
                fitCentre, fitRadius, support, classifiedKind, decision));
        }

        bool HasNearbyDuplicate(Circle candidate, string candidateKind)
        {
            double coincidentCentreThreshold = settings.Separation * scale * .25;
            foreach (var existing in result)
            {
                double distance = Distance(existing, candidate);
                // Independently classified balls can touch, but they cannot
                // occupy the same centre. Reject near-coincident proposals
                // across colours before applying the wider same-colour gate.
                if (distance < coincidentCentreThreshold)
                {
                    string coincidentKind = packedClassifications is not null && packedClassifications.TryGetValue(existing, out var coincidentKnown)
                        ? coincidentKnown.Kind : Classify(existing, pixels, inspectRim: true).Kind;
                    if (candidateKind == "unknown" || coincidentKind == "unknown" || candidateKind != coincidentKind)
                        conflictingCoincidentCandidateDetected = true;
                    return true;
                }
                if (distance >= settings.Separation * scale) continue;
                string existingKind = packedClassifications is not null && packedClassifications.TryGetValue(existing, out var nearbyKnown)
                    ? nearbyKnown.Kind : Classify(existing, pixels, inspectRim: true).Kind;
                // Distinct, independently classified touching balls are valid.
                // Preserve the old broad rejection when either body is unknown.
                if (candidateKind == "unknown" || existingKind == "unknown" || candidateKind == existingKind)
                    return true;
            }
            return false;
        }
    }
    void RefineBodyGeometry(Mat cloth, List<Circle> circles, double scale)
    {
        // A fresh connected body can refine a jittering Hough edge. Require a
        // bounded round component containing exactly one current candidate;
        // merged/touching bodies retain their unresolved original geometry.
        using var bodies = new MatLease(new Mat()); Cv.Bitwise_not(cloth, bodies.Value);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(bodies.Value, labels.Value, stats.Value, centres.Value);
        for (int i = 1; i < count; i++)
        {
            double w = stats.Value.Get(i, Imgproc.CcStatWidth)![0], h = stats.Value.Get(i, Imgproc.CcStatHeight)![0], area = stats.Value.Get(i, Imgproc.CcStatArea)![0];
            double radius = (w + h) / 4;
            if (radius < settings.MinimumRadius * scale || radius > settings.MaximumRadius * scale || w / h is < .75 or > 1.33 || area / (w * h) < .65) continue;
            var included = Enumerable.Range(0, circles.Count).Where(n => labels.Value.Get((int)circles[n].Y, (int)circles[n].X)![0] == i).ToArray();
            if (included.Length != 1) continue;
            var observed = new Circle(centres.Value.Get(i, 0)![0], centres.Value.Get(i, 1)![0], radius);
            var old = circles[included[0]];
            if (Math.Sqrt(Math.Pow(old.X - observed.X, 2) + Math.Pow(old.Y - observed.Y, 2)) <= radius * .5)
                circles[included[0]] = observed;
        }
    }
    void AddColourBodies(Mat hsv, byte[] pixels, List<Circle> circles, double scale)
    {
        // Recover isolated painted bodies that the cloth mask loses beside a
        // cushion or warm shadow. Size, fill and aspect bounds exclude streaks,
        // rails and merged groups; the normal uncertainty gates still apply.
        using var mask = new MatLease(new Mat());
        using var low = new Scalar(0, 160, 40); using var high = new Scalar(179, 255, 255);
        Cv.InRange(hsv, low, high, mask.Value);
        byte[] body = Read(mask.Value);
        for (int i = 0; i < body.Length; i++)
            if (pixels[i * 3] > 37 && pixels[i * 3] < (settings.FreshCueBallRecovery && settings.BodyProposalPriority ? 150 : 163)) body[i] = 0;
        mask.Value.Put(0, 0, body);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(mask.Value, labels.Value, stats.Value, centres.Value);
        for (int i = 1; i < count && circles.Count <= 32; i++)
        {
            double width = stats.Value.Get(i, Imgproc.CcStatWidth)![0], height = stats.Value.Get(i, Imgproc.CcStatHeight)![0], area = stats.Value.Get(i, Imgproc.CcStatArea)![0];
            double radius = (width + height) / 4;
            if (radius < settings.MinimumRadius * scale || radius > settings.MaximumRadius * scale || width / height is < .6 or > 1.6 || area / (width * height) < .55) continue;
            var c = new Circle(centres.Value.Get(i, 0)![0], centres.Value.Get(i, 1)![0], radius);
            double margin = 23 * scale;
            if (c.X < margin || c.Y < margin || c.X > coordinates.Width - margin || c.Y > coordinates.Height - margin ||
                PocketCentres().Any(p => (new Point(c.X, c.Y) - p).Length < settings.PocketExclusionRadius * scale) ||
                circles.Any(p => Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y)) < settings.Separation * scale)) continue;
            circles.Add(c);
        }
    }
    int ClothHue(byte[] pixels)
        => ClothRange(pixels).Hue;
    (int Hue, int SaturationFloor, int ValueFloor, int MedianValue) ClothRange(byte[] pixels)
    {
        // Low-saturation blue/grey cloth is the normal mounted-camera lighting.
        // Estimate both its hue and usable S/V floors from many interior samples.
        int[] histogram = new int[60]; var samples = new List<HsvPixel>(); int w = coordinates.Width, h = coordinates.Height;
        for (int y = h / 10; y < h * 9 / 10; y += 10) for (int x = w / 10; x < w * 9 / 10; x += 10)
        {
            int offset = (y * w + x) * 3;
            if (pixels[offset + 1] > 20 && pixels[offset + 2] > 35) { histogram[pixels[offset] / 3]++; samples.Add(new(pixels[offset], pixels[offset + 1], pixels[offset + 2])); }
        }
        int hue = Array.IndexOf(histogram, histogram.Max()) * 3 + 1;
        var cloth = samples.Where(p => Math.Min(Math.Abs(p.H - hue), 180 - Math.Abs(p.H - hue)) <= settings.HueBand).ToArray();
        if (cloth.Length < 20) return (hue, 60, 60, 0);
        double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
        double medianValue = Median(cloth.Select(p => p.V));
        return (hue, (int)Math.Clamp(Median(cloth.Select(p => p.S)) * .45, 8, 60),
            (int)Math.Clamp(medianValue * .45, 15, 60), (int)Math.Round(medianValue));
    }
    MatLease ClothMask(Mat hsv, byte[] pixels, bool separateBalls = true,
        (int Hue, int SaturationFloor, int ValueFloor, int MedianValue)? rangeOverride = null)
    {
        int w = coordinates.Width; var range = rangeOverride ?? ClothRange(pixels); int hue = range.Hue;
        int band = separateBalls ? settings.HueBand : Math.Max(12, settings.HueBand);
        var mask = new MatLease(new Mat());
        try
        {
            // Warm cloth and the cue/yellows can share a hue. Bound saturation
            // by the sampled cloth rather than treating every matching hue as cloth.
            double saturationCeiling = separateBalls ? Math.Clamp(range.SaturationFloor / .45 * 1.8, 90, 255) : 255;
            using var low = new Scalar(Math.Max(0, hue - band), range.SaturationFloor, range.ValueFloor); using var high = new Scalar(Math.Min(179, hue + band), saturationCeiling, 255);
            Cv.InRange(hsv, low, high, mask.Value);
            if (hue < band || hue > 179 - band)
            {
                using var wrapped = new MatLease(new Mat()); using var l = new Scalar(hue < band ? 180 + hue - band : 0, range.SaturationFloor, range.ValueFloor); using var u = new Scalar(hue < band ? 179 : hue + band - 180, saturationCeiling, 255);
                Cv.InRange(hsv, l, u, wrapped.Value); Cv.Bitwise_or(mask.Value, wrapped.Value, mask.Value);
            }
            // Video snapshot decoders quantize dim neutral cloth: its hue can
            // swing wildly while saturation stays low. Keep the fallback below
            // a typical white cue ball so its circular edge remains available.
            using var neutral = new MatLease(new Mat()); using var neutralLow = new Scalar(0, 0, range.ValueFloor);
            using var neutralHigh = new Scalar(179, 45, NeutralClothValueCeiling(range.ValueFloor));
            Cv.InRange(hsv, neutralLow, neutralHigh, neutral.Value); Cv.Bitwise_or(mask.Value, neutral.Value, mask.Value);
            int kernelWidth = Math.Max(3, (int)Math.Round(settings.MorphologyKernel * w / 1000.0) | 1); using var size = new Size(kernelWidth, kernelWidth);
            using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphRect, size));
            Imgproc.MorphologyEx(mask.Value, mask.Value, Imgproc.MorphClose, kernel.Value); Imgproc.MorphologyEx(mask.Value, mask.Value, Imgproc.MorphOpen, kernel.Value);
            using var white = new Scalar(255);
            foreach (var pocket in PocketCentres()) { using var point = new CvPoint(pocket.X, pocket.Y); Imgproc.Circle(mask.Value, point, (int)(settings.PocketExclusionRadius * w / 1000.0), white, -1); }
            return mask;
        }
        catch { mask.Dispose(); throw; }
    }
    bool LargeObstruction(Mat hsv, byte[] pixels, IReadOnlyCollection<Circle> balls, bool completePackedCluster,
        out ObstructionAuditTelemetry audit)
    {
        // Obstruction checks retain the broad cloth range: local saturation and
        // value shifts are not themselves evidence of a large object. The
        // ordinary neutral fallback has a fixed upper ceiling to protect white
        // ball classification; use a cloth-relative range here so dim or bright
        // neutral felt is not mistaken for one connected obstruction.
        var sampledClothRange = ObstructionClothRange(pixels, balls);
        var clothRange = diagnosticObstructionClothRangeOverride ?? sampledClothRange;
        using var mask = ClothMask(hsv, pixels, separateBalls: false, rangeOverride: clothRange);
        (int Minimum, int Maximum)? sampledNeutralValueRange = sampledClothRange.MedianValue > 0
            ? ((int)Math.Floor(sampledClothRange.MedianValue * .25), Math.Min(255,
                Math.Max(MinimumObstructionNeutralValueCeiling, (int)Math.Ceiling(sampledClothRange.MedianValue * 1.8))))
            : null;
        (int Minimum, int Maximum)? clothRangeNeutralValueRange = clothRange.MedianValue > 0
            ? ((int)Math.Floor(clothRange.MedianValue * .25), Math.Min(255,
                Math.Max(MinimumObstructionNeutralValueCeiling, (int)Math.Ceiling(clothRange.MedianValue * 1.8))))
            : null;
        var appliedNeutralValueRange = diagnosticObstructionNeutralValueRangeOverride ?? clothRangeNeutralValueRange;
        if (appliedNeutralValueRange is { } neutralRange)
        {
            using var neutral = new MatLease(new Mat());
            using var low = new Scalar(0, 0, neutralRange.Minimum);
            using var high = new Scalar(179, 45, Math.Min(255, neutralRange.Maximum));
            Cv.InRange(hsv, low, high, neutral.Value);
            Cv.Bitwise_or(mask.Value, neutral.Value, mask.Value);
        }
        Cv.Bitwise_not(mask.Value, mask.Value);
        // Crop only the uncertain calibration border; a hand connected to an image
        // edge must still count as an obstruction inside the playable surface.
        int border = (int)(65 * coordinates.Width / 1000.0);
        byte[] interior = Read(mask.Value);
        for (int y = 0; y < coordinates.Height; y++) for (int x = 0; x < coordinates.Width; x++)
            if (x < border || y < border || x >= coordinates.Width - border || y >= coordinates.Height - border) interior[y * coordinates.Width + x] = 0;
        mask.Value.Put(0, 0, interior);
        if (obstructionMaskDiagnostics)
        {
            using var encodedMask = new MatOfByte();
            if (!Imgcodecs.Imencode(".png", mask.Value, encodedMask))
                throw new IOException("Could not encode the opt-in obstruction mask diagnostic.");
            lastObstructionMaskDiagnosticPng = encodedMask.ToArray();
        }
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(mask.Value, labels.Value, stats.Value, centres.Value);
        using var ballBodyMask = new MatLease(new Mat(coordinates.Height, coordinates.Width, CvType.Cv8uc1));
        using var maskBlack = new Scalar(0); using var maskWhite = new Scalar(255);
        ballBodyMask.Value.SetTo(maskBlack);
        foreach (var ball in balls)
        {
            using var centre = new CvPoint(ball.X, ball.Y);
            Imgproc.Circle(ballBodyMask.Value, centre, Math.Max(1, (int)Math.Round(ball.Radius * .8)), maskWhite, -1);
        }
        double scale = coordinates.Width / 1000.0;
        double minimumWidth = 120 * scale, minimumHeight = 120 * scale, minimumArea = 5000 * scale * scale;
        double packedMaximumWidth = 400 * scale, packedMaximumHeight = 400 * scale;
        int packedClusterMinimumBallCentres = Math.Min(12, Math.Max(8, (int)Math.Ceiling(balls.Count * .5)));
        const double packedClusterMinimumBallBodyCoverageFraction = .36;
        const int ballGroupExemptionMinimumCentres = 3;
        const double ballGroupExemptionMinimumBodyCoverageFraction = .52;
        var candidates = new List<ObstructionComponentAudit>();
        int triggeringComponents = 0;
        for (int i = 1; i < count; i++)
        {
            double x = stats.Value.Get(i, Imgproc.CcStatLeft)![0], y = stats.Value.Get(i, Imgproc.CcStatTop)![0];
            double width = stats.Value.Get(i, Imgproc.CcStatWidth)![0], height = stats.Value.Get(i, Imgproc.CcStatHeight)![0], area = stats.Value.Get(i, Imgproc.CcStatArea)![0];
            if (width <= minimumWidth || height <= minimumHeight || area <= minimumArea) continue;
            // A component that occupies nearly the whole table is a failed cloth
            // segmentation, not evidence of a localized object covering it.
            bool wholeTableSegmentation = width >= coordinates.Width * .75 && height >= coordinates.Height * .75 &&
                area >= coordinates.Width * coordinates.Height * .55;
            bool packedClusterSizeEligible = width <= packedMaximumWidth && height <= packedMaximumHeight;
            int ballCentres = 0, ballCentresInBounds = 0;
            foreach (var ball in balls)
            {
                int bx = Math.Clamp((int)Math.Round(ball.X), 0, coordinates.Width - 1);
                int by = Math.Clamp((int)Math.Round(ball.Y), 0, coordinates.Height - 1);
                if ((int)labels.Value.Get(by, bx)![0] == i) ballCentres++;
                if (bx >= x && bx < x + width && by >= y && by < y + height) ballCentresInBounds++;
            }
            int ballBodyPixels = 0;
            if (packedClusterSizeEligible && balls.Count > 0)
            {
                using var componentMask = new MatLease(new Mat()); using var ballBodyOverlap = new MatLease(new Mat());
                using var bounds = new Rect((int)x, (int)y, (int)width, (int)height);
                using var componentLabels = new MatLease(labels.Value.Submat(bounds));
                using var componentBallBodies = new MatLease(ballBodyMask.Value.Submat(bounds));
                using var label = new Scalar(i);
                Cv.Compare(componentLabels.Value, label, componentMask.Value, Cv.CmpEq);
                Cv.Bitwise_and(componentMask.Value, componentBallBodies.Value, ballBodyOverlap.Value);
                ballBodyPixels = Cv.CountNonZero(ballBodyOverlap.Value);
            }
            double ballBodyCoverage = area > 0 ? ballBodyPixels / area : 0;
            // Ball shadows can join a compact group of recognized balls into one
            // non-cloth component even when the whole table is not a packed rack.
            // Keep the stricter full-rack rule, but also allow a localized group
            // when at least three supported ball centres belong to the component
            // and supported ball bodies explain at least half its pixels. The live
            // three-ball spread group was falsely blocked by the old five-centre
            // floor. Rectangle membership alone is insufficient: a hand's bounds
            // can include visible balls outside its actual connected component.
            // Broad hand/object regions still fail size or body-coverage gates.
            bool completePackedClusterExempted = completePackedCluster && packedClusterSizeEligible &&
                ballCentresInBounds >= packedClusterMinimumBallCentres &&
                ballBodyCoverage >= packedClusterMinimumBallBodyCoverageFraction;
            bool localizedBallGroupExempted = packedClusterSizeEligible &&
                ballCentres >= ballGroupExemptionMinimumCentres &&
                ballBodyCoverage >= ballGroupExemptionMinimumBodyCoverageFraction;
            bool packedClusterExempted = completePackedClusterExempted || localizedBallGroupExempted;
            bool triggersObstruction = !wholeTableSegmentation && !packedClusterExempted;
            if (triggersObstruction) triggeringComponents++;
            candidates.Add(new(i, (int)x, (int)y, (int)width, (int)height, (int)area, ballCentres,
                wholeTableSegmentation, packedClusterSizeEligible, packedClusterExempted, triggersObstruction)
            {
                BallCentresInBounds = ballCentresInBounds,
                BallBodyPixels = ballBodyPixels,
                BallBodyCoverageFraction = ballBodyCoverage,
            });
        }
        const int maximumReportedComponents = 64;
        var reported = candidates.Where(component => component.TriggersObstruction).OrderByDescending(component => component.Area)
            .Take(maximumReportedComponents / 2)
            .Concat(candidates.Where(component => !component.TriggersObstruction).OrderByDescending(component => component.Area)
                .Take(maximumReportedComponents / 2))
            .OrderByDescending(component => component.Area).ThenBy(component => component.Label).ToArray();
        int omittedComponents = Math.Max(0, candidates.Count - reported.Length);
        int reportedTriggering = reported.Count(component => component.TriggersObstruction);
        audit = new(coordinates.Width, coordinates.Height, border, minimumWidth, minimumHeight, minimumArea,
            packedMaximumWidth, packedMaximumHeight, packedClusterMinimumBallCentres, count - 1, candidates.Count, reported.Length,
            omittedComponents, triggeringComponents, Math.Max(0, triggeringComponents - reportedTriggering), reported)
        {
            PackedClusterMinimumBallBodyCoverageFraction = packedClusterMinimumBallBodyCoverageFraction,
            BallGroupExemptionMinimumCentres = ballGroupExemptionMinimumCentres,
            BallGroupExemptionMinimumBallBodyCoverageFraction = ballGroupExemptionMinimumBodyCoverageFraction,
            DiagnosticClothHue = obstructionMaskDiagnostics ? sampledClothRange.Hue : null,
            DiagnosticClothSaturationFloor = obstructionMaskDiagnostics ? sampledClothRange.SaturationFloor : null,
            DiagnosticClothValueFloor = obstructionMaskDiagnostics ? sampledClothRange.ValueFloor : null,
            DiagnosticClothMedianValue = obstructionMaskDiagnostics ? sampledClothRange.MedianValue : null,
            DiagnosticAppliedClothHue = obstructionMaskDiagnostics ? clothRange.Hue : null,
            DiagnosticAppliedClothSaturationFloor = obstructionMaskDiagnostics ? clothRange.SaturationFloor : null,
            DiagnosticAppliedClothValueFloor = obstructionMaskDiagnostics ? clothRange.ValueFloor : null,
            DiagnosticAppliedClothMedianValue = obstructionMaskDiagnostics ? clothRange.MedianValue : null,
            DiagnosticSampledNeutralMinimumValue = obstructionMaskDiagnostics ? sampledNeutralValueRange?.Minimum : null,
            DiagnosticSampledNeutralMaximumValue = obstructionMaskDiagnostics ? sampledNeutralValueRange?.Maximum : null,
            DiagnosticAppliedNeutralMinimumValue = obstructionMaskDiagnostics ? appliedNeutralValueRange?.Minimum : null,
            DiagnosticAppliedNeutralMaximumValue = obstructionMaskDiagnostics ? appliedNeutralValueRange?.Maximum : null,
        };
        return triggeringComponents > 0;
    }
    (int Hue, int SaturationFloor, int ValueFloor, int MedianValue) ObstructionClothRange(
        byte[] pixels, IReadOnlyCollection<Circle> balls)
    {
        // The cloth is low saturation. A hue histogram of saturated pixels is
        // dominated by balls and shadows, so its winning hue can jump between
        // otherwise identical live frames and turn a large patch of felt into
        // an apparent obstruction. Estimate this mask's range only from low-
        // saturation pixels, excluding the recognized ball bodies.
        int w = coordinates.Width, h = coordinates.Height;
        int step = Math.Max(4, (int)Math.Round(10 * w / 1000d));
        var cloth = new List<HsvPixel>();
        foreach (var (x, y) in Grid(w / 10, w * 9 / 10, step, h / 10, h * 9 / 10, step))
        {
            bool onBall = false;
            foreach (var ball in balls)
            {
                double radius = Math.Max(ball.Radius * 1.5, PoolGeometry.Radius * w * 1.2);
                double dx = x - ball.X, dy = y - ball.Y;
                if (dx * dx + dy * dy <= radius * radius) { onBall = true; break; }
            }
            if (onBall) continue;
            int offset = (y * w + x) * 3;
            var pixel = new HsvPixel(pixels[offset], pixels[offset + 1], pixels[offset + 2]);
            if (pixel.S <= 70 && pixel.V > 15) cloth.Add(pixel);
        }
        if (cloth.Count < 20) return ClothRange(pixels);

        double Median(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();
            return sorted[sorted.Length / 2];
        }
        double medianSaturation = Median(cloth.Select(pixel => pixel.S));
        double medianValue = Median(cloth.Select(pixel => pixel.V));
        double hueX = 0, hueY = 0;
        foreach (var pixel in cloth.Where(pixel => pixel.S >= 8))
        {
            double angle = pixel.H * Math.PI / 90;
            hueX += Math.Cos(angle) * pixel.S;
            hueY += Math.Sin(angle) * pixel.S;
        }
        int hue = Math.Abs(hueX) + Math.Abs(hueY) < 1
            ? ClothRange(pixels).Hue
            : (int)Math.Round(Math.Atan2(hueY, hueX) * 90 / Math.PI + 180) % 180;
        return (hue, (int)Math.Clamp(medianSaturation * .45, 8, 60),
            (int)Math.Clamp(medianValue * .45, 15, 60), (int)Math.Round(medianValue));
    }
    static IEnumerable<(int X, int Y)> Grid(int startX, int endX, int stepX, int startY, int endY, int stepY)
    {
        for (int y = startY; y < endY; y += stepY)
            for (int x = startX; x < endX; x += stepX)
                yield return (x, y);
    }
    Point[] PocketCentres() => [new(0, 0), new(coordinates.Width, 0), new(0, coordinates.Width), new(coordinates.Width, coordinates.Width), new(0, coordinates.Height), new(coordinates.Width, coordinates.Height)];
    bool PocketColourNeedsReview(byte[] pixels, Mat rawImage, Circle[] circles, BallClassification[] labels,
        BoundaryColourMask colourMask, out PocketColourAuditTelemetry audit)
    {
        double scale = coordinates.Width / 1000.0, radius = settings.PocketExclusionRadius * scale;
        double threshold = 100 * scale * scale, expectedRadius = PoolGeometry.Radius * coordinates.Width;
        byte[]? raw = null;
        var pockets = new List<PocketColourRegionAudit>();
        bool IsPocketColour(int offset) => pixels[offset + 1] > 120 && pixels[offset + 2] > 100
            && (pixels[offset] <= 37 || pixels[offset] >= 163);
        foreach (var pocket in PocketCentres())
        {
            int coloured = 0;
            ForPixels(new(pocket.X, pocket.Y, radius), 1, (x, y) =>
            {
                int i = (y * coordinates.Width + x) * 3;
                if (IsPocketColour(i)) coloured++;
            });
            if (coloured <= threshold)
            {
                pockets.Add(new(pocket, coloured, 0, coloured, 0, false));
                continue;
            }
            // A detected red or yellow outside the exclusion can still paint
            // pixels inside it. Explain only its inner body after independent
            // raw-image boundary support, including the arc facing the opening.
            // Hidden/partial bodies and unexplained colour retain the old guard.
            var supported = new List<Circle>();
            var supportAudits = new List<PocketColourBallSupportAudit>();
            for (int n = 0; n < circles.Length; n++)
            {
                var circle = circles[n]; string kind = labels[n].Kind;
                if (kind is not "red" and not "yellow") continue;
                var centre = new Point(circle.X, circle.Y); double distance = (centre - pocket).Length;
                if (distance >= radius + circle.Radius) continue;
                if (distance < radius)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "centre-inside-pocket-exclusion"));
                    continue;
                }
                raw ??= Read(rawImage);
                var fit = BallBoundaryRefiner.Refine(raw, coordinates.Width, coordinates.Height, centre, expectedRadius,
                    minimumSupportedFraction: .75, collectSectorEvidence: true, expectedKind: kind, colourMask: colourMask);
                if (fit is null)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "no-boundary-fit"));
                    continue;
                }
                double centreShift = (fit.Centre - centre).Length;
                double radiusShift = Math.Abs(fit.Radius - circle.Radius);
                double boundarySupport = fit.SupportedSectors;
                if (boundarySupport < .75)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "weak-boundary-support",
                        boundarySupport, centreShift, radiusShift));
                    continue;
                }
                if (fit.SectorEvidence.Length != 32)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "incomplete-sector-evidence",
                        boundarySupport, centreShift, radiusShift));
                    continue;
                }
                if (centreShift > expectedRadius * .25)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "centre-drift",
                        boundarySupport, centreShift, radiusShift));
                    continue;
                }
                if (radiusShift > expectedRadius * .25)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "radius-drift",
                        boundarySupport, centreShift, radiusShift));
                    continue;
                }
                if (fit.Centre.X < fit.Radius || fit.Centre.Y < fit.Radius ||
                    fit.Centre.X > coordinates.Width - fit.Radius || fit.Centre.Y > coordinates.Height - fit.Radius)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "fit-crosses-image-edge",
                        boundarySupport, centreShift, radiusShift));
                    continue;
                }
                if (PocketCentres().Any(p => (fit.Centre - p).Length < radius))
                {
                    supportAudits.Add(new(n, kind, centre, distance, "fit-centre-inside-pocket-exclusion",
                        boundarySupport, centreShift, radiusShift));
                    continue;
                }
                var towardPocket = pocket - fit.Centre; double pocketDistance = towardPocket.Length;
                var pocketFacing = fit.SectorEvidence.Where(sector =>
                {
                    var direction = sector.OuterPoint - fit.Centre;
                    return (direction.X * towardPocket.X + direction.Y * towardPocket.Y)
                        / (direction.Length * pocketDistance) >= Math.Sqrt(.5);
                }).ToArray();
                int supportedPocketFacing = pocketFacing.Count(sector => sector.Supported);
                bool pocketArcSupported = pocketFacing.All(sector => sector.Supported);
                if (!pocketArcSupported)
                {
                    supportAudits.Add(new(n, kind, centre, distance, "pocket-facing-arc-unsupported",
                        boundarySupport, centreShift, radiusShift, pocketFacing.Length, supportedPocketFacing));
                    continue;
                }
                supportAudits.Add(new(n, kind, centre, distance, "supported",
                    boundarySupport, centreShift, radiusShift, pocketFacing.Length, supportedPocketFacing));
                supported.Add(new(fit.Centre.X, fit.Centre.Y, fit.Radius * .95));
            }
            int explained = 0;
            ForPixels(new(pocket.X, pocket.Y, radius), 1, (x, y) =>
            {
                int i = (y * coordinates.Width + x) * 3;
                if (!IsPocketColour(i)) return;
                if (supported.Any(ball =>
                    (x - ball.X) * (x - ball.X) + (y - ball.Y) * (y - ball.Y)
                        <= ball.Radius * ball.Radius)) explained++;
            });
            int unexplained = coloured - explained;
            pockets.Add(new(pocket, coloured, explained, unexplained, supported.Count, unexplained > threshold,
                supportAudits.Count == 0 ? null : supportAudits.ToArray()));
        }
        audit = new(coordinates.Width, coordinates.Height, radius, threshold, pockets.ToArray());
        return pockets.Any(pocket => pocket.NeedsReview);
    }
    BallClassification Classify(Circle circle, byte[] hsv, bool inspectRim = false,
        bool allowWarmNeutralCueCandidate = false)
    {
        var samples = new List<HsvPixel>();
        bool lowLightNeutralCueBallContrast = HasLowLightNeutralBallContrast(circle, hsv, allowWarmNeutralCueCandidate);
        // Include enough of the ball body to distinguish the black from its large
        // cream number patch when the fitted circle centre lands on a highlight.
        ForPixels(circle, .85, (x, y) => { int offset = (y * coordinates.Width + x) * 3; samples.Add(new(hsv[offset], hsv[offset + 1], hsv[offset + 2])); });
        if (!inspectRim) return BallClassifier.Classify(samples.ToArray(), lowLightNeutralCueBallContrast);
        var rim = new List<HsvPixel>();
        ForPixels(circle, .95, (x, y) =>
        {
            if ((x - circle.X) * (x - circle.X) + (y - circle.Y) * (y - circle.Y) < circle.Radius * circle.Radius * .8 * .8) return;
            int offset = (y * coordinates.Width + x) * 3; rim.Add(new(hsv[offset], hsv[offset + 1], hsv[offset + 2]));
        });
        return allowWarmNeutralCueCandidate
            ? BallClassifier.ClassifyWarmNeutralCueCandidate(samples.ToArray(), rim.ToArray(), lowLightNeutralCueBallContrast)
            : BallClassifier.ClassifyWithRim(samples.ToArray(), rim.ToArray(), lowLightNeutralCueBallContrast);
    }
    double NeutralClothValueCeiling(int clothValueFloor)
        // The fixed 90 ceiling makes holes in bright neutral cloth. Follow the
        // measured cloth value; dim scenes keep their original lower ceiling.
        // Fresh raw-body recovery supplies an independent check for the cue ball.
        => Math.Clamp(clothValueFloor / .45 * .9, 45, settings.FreshCueBallRecovery ? 120 : 90);
    bool HasLocalBodyValueContrast(Circle circle, byte[] hsv)
    {
        var body = new List<byte>(); var ring = new List<byte>();
        ForPixels(circle, .75, (x, y) => body.Add(hsv[(y * coordinates.Width + x) * 3 + 2]));
        double inner = circle.Radius * 1.08, outer = circle.Radius * 1.4;
        for (int y = Math.Max(0, (int)(circle.Y - outer)); y <= Math.Min(coordinates.Height - 1, (int)(circle.Y + outer)); y++)
        for (int x = Math.Max(0, (int)(circle.X - outer)); x <= Math.Min(coordinates.Width - 1, (int)(circle.X + outer)); x++)
        {
            double distance = Math.Pow(x - circle.X, 2) + Math.Pow(y - circle.Y, 2);
            if (distance >= inner * inner && distance <= outer * outer) ring.Add(hsv[(y * coordinates.Width + x) * 3 + 2]);
        }
        if (body.Count < 8 || ring.Count < 12) return false;
        body.Sort(); ring.Sort();
        double centre = body[body.Count / 2], surrounding = ring[ring.Count / 2];
        // Positive white-body or negative dark-body contrast is current evidence.
        // Texture holes with matching cloth brightness are not ball proposals.
        return Math.Abs(centre - surrounding) >= Math.Max(8, surrounding * .1);
    }
    bool IsClothBody(Circle circle, byte[] hsv, (int Hue, int SaturationFloor, int ValueFloor, int MedianValue) cloth)
    {
        int samples = 0, matching = 0;
        ForPixels(circle, .85, (x, y) =>
        {
            int offset = (y * coordinates.Width + x) * 3;
            int difference = Math.Abs(hsv[offset] - cloth.Hue); samples++;
            if ((Math.Min(difference, 180 - difference) <= Math.Max(12, settings.HueBand) || hsv[offset + 1] < 45)
                && hsv[offset + 1] < Math.Max(60, cloth.SaturationFloor / .45 * 1.5)) matching++;
        });
        return samples > 0 && matching / (double)samples > .9;
    }
    bool HasLowLightNeutralBallContrast(Circle circle, byte[] hsv, bool includeWarmCream = false)
    {
        var bodyValues = new List<byte>(); int bodySamples = 0;
        ForPixels(circle, .75, (x, y) =>
        {
            int offset = (y * coordinates.Width + x) * 3; bodySamples++;
            if (hsv[offset + 1] < 85 && hsv[offset + 2] >= 35
                || includeWarmCream && hsv[offset] is >= 8 and <= 30 && hsv[offset + 1] < 160 && hsv[offset + 2] > 130)
                bodyValues.Add(hsv[offset + 2]);
        });
        if (bodySamples < 8 || bodyValues.Count / (double)bodySamples < .65) return false;

        var surroundingValues = new List<byte>();
        double innerRadius = circle.Radius * 1.08, outerRadius = circle.Radius * 1.4;
        double innerSquared = innerRadius * innerRadius, outerSquared = outerRadius * outerRadius;
        for (int y = Math.Max(0, (int)(circle.Y - outerRadius)); y <= Math.Min(coordinates.Height - 1, (int)(circle.Y + outerRadius)); y++)
        for (int x = Math.Max(0, (int)(circle.X - outerRadius)); x <= Math.Min(coordinates.Width - 1, (int)(circle.X + outerRadius)); x++)
        {
            double dx = x - circle.X, dy = y - circle.Y, distance = dx * dx + dy * dy;
            if (distance < innerSquared || distance > outerSquared) continue;
            surroundingValues.Add(hsv[(y * coordinates.Width + x) * 3 + 2]);
        }
        if (surroundingValues.Count < 12) return false;
        bodyValues.Sort(); surroundingValues.Sort();
        double bodyMedian = bodyValues[bodyValues.Count / 2], surroundingMedian = surroundingValues[surroundingValues.Count / 2];
        return bodyMedian >= Math.Max(48, Math.Max(surroundingMedian + 8, surroundingMedian * 1.1));
    }
    void ForPixels(Circle circle, double fraction, Action<int, int> action)
    {
        double radius = circle.Radius * fraction;
        for (int y = Math.Max(0, (int)(circle.Y - radius)); y <= Math.Min(coordinates.Height - 1, (int)(circle.Y + radius)); y++)
        for (int x = Math.Max(0, (int)(circle.X - radius)); x <= Math.Min(coordinates.Width - 1, (int)(circle.X + radius)); x++)
            if ((x - circle.X) * (x - circle.X) + (y - circle.Y) * (y - circle.Y) <= radius * radius) action(x, y);
    }
    static byte[] Read(Mat mat) { byte[] data = new byte[mat.Rows() * mat.Cols() * mat.Channels()]; mat.Get(0, 0, data); return data; }
    static MatLease Decode(byte[] jpeg)
    {
        if (jpeg.Length is < 4 or > 16 * 1024 * 1024) throw new IOException("Invalid analysis image size.");
        using var encoded = new MatLease(new MatOfByte(jpeg)); var decoded = new MatLease(Imgcodecs.Imdecode(encoded.Value, Imgcodecs.ImreadColor));
        if (decoded.Value.Empty() || decoded.Value.Rows() > 6000 || decoded.Value.Cols() > 6000) { decoded.Dispose(); throw new IOException("Snapshot could not be decoded."); }
        return decoded;
    }
    public Point[]? FindCorners(byte[] jpeg) => FindCorners(jpeg, CancellationToken.None);
    public Point[]? FindCorners(byte[] jpeg, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); PipelineDiagnostics.Count("calibrationDetections");
        var pockets = FindPockets(jpeg, cancellation);
        if (pockets is not null) return pockets.Corners.ToArray();
        Load(); using var frame = Decode(jpeg); using var gray = new MatLease(new Mat()); using var edges = new MatLease(new Mat()); using var circles = new MatLease(new Mat()); using var size = new Size(7, 7);
        Imgproc.CvtColor(frame.Value, gray.Value, Imgproc.ColorBgr2gray); Imgproc.GaussianBlur(gray.Value, gray.Value, size, 0); Imgproc.Canny(gray.Value, edges.Value, 170, 200);
        double scale = Math.Min(frame.Value.Rows(), frame.Value.Cols()) / 1000.0;
        Imgproc.HoughCircles(edges.Value, circles.Value, Imgproc.HoughGradient, 1.6, 300 * scale, 30, 30, (int)(35 * scale), (int)(100 * scale));
        cancellation.ThrowIfCancellationRequested();
        var candidates = new List<Point>();
        for (int i = 0; i < circles.Value.Cols(); i++) { var c = circles.Value.Get(0, i); if (c is { Length: >= 3 }) candidates.Add(new(c[0] / frame.Value.Cols(), c[1] / frame.Value.Rows())); }
        if (candidates.Count < 4) return null;
        Point[] corners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        var picks = corners.Select(p => candidates.MinBy(c => (c - p).Length)).ToArray();
        if (picks.Distinct().Count() != 4 || picks.Where((p, i) => (p - corners[i]).Length > .35).Any()) return null;
        try { _ = new TableCalibration(picks); return picks; } catch (ArgumentException) { return null; }
    }
    sealed class MatLease(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() { Value.Release(); Value.Dispose(); } }
}

