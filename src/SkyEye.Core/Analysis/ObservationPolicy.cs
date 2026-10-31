namespace SkyEye.Core.Analysis;

/// <summary>Different policies govern legacy observation, review, advice lifetime
/// and continuous evidence. Verification values are provisional until O08 holdout acceptance.</summary>
public static class ObservationPolicy
{
    public const double LegacyObservationGapSeconds = 15;
    public const double ReviewFreshnessSeconds = 6;
    public const double CatalogueLifetimeSeconds = 15;
    public const double MinimumStillSeconds = 2;
    public const double MaximumVerificationGapSeconds = .8;
    public const double VerificationTimeoutSeconds = 4;
    public const double MaximumFinalAgeMs = 1500;
    public const double MaximumClockSampleAgeMs = 2500;
    public const int MinimumVerificationSamples = 5;
    // Allow up to two unstable bootstrap frames to be excluded before a fresh
    // five-sample, two-second stationary span is established. Verification may
    // add one final frame only when its last clear-view detection is uncertain.
    // The ordinary verification burst captures up to seven independent samples.
    // One additional sample may be taken only to re-detect an uncertain final view.
    public const int StandardVerificationSamples = 7;
    public const int MaximumVerificationSamples = 8;
    public const int MaximumCapturedVerificationSamples = 7;
    public const double SampleIntervalSeconds = .5;
    public const double MinimumDetectedBallMotionFraction = .006;
    public const double MinimumTemplateBallMotionPixels = 1;
    public const double MinimumSupportedBoundaryFraction = 18 / 32d;
    public const string VerificationConfiguration = "o08-provisional-v21";
}

public enum TableMotion { Unknown, Still, Moving }
public enum LayoutQuality { Unknown, Clear, Ambiguous, Obstructed }
public enum ObservationReason
{
    None, Checking, InsufficientEvidence, DetectionAmbiguous, Obstructed,
    Moving, LightingChanged, CalibrationChanged, FrameUnavailable, FrameTooOld,
    ClockUntrusted, SourceChanged, SampleGap, Cancelled, TimedOut, AnalysisFailed, CaptureFailed
}

/// <summary>Positive motion evidence from repeated, edge-supported ball centres
/// corroborated by local image-template motion. Ambiguous associations may not establish
/// movement, and absence or weak support never establishes stillness.</summary>
public static class VerificationObjectMotion
{
    public static bool IsReliablePositiveEvidence(VisionResult? previous, VisionResult? current,
        double? displacement, double minimumDisplacement,
        double? templateDisplacement, double minimumTemplateDisplacement) =>
        previous is { Obstructed: false, HasUncertainLayout: false }
        && current is { Obstructed: false, HasUncertainLayout: false }
        && displacement is { } pixels
        && double.IsFinite(pixels)
        && double.IsFinite(minimumDisplacement)
        && minimumDisplacement >= 0
        && pixels >= minimumDisplacement
        && templateDisplacement is { } templatePixels
        && double.IsFinite(templatePixels)
        && double.IsFinite(minimumTemplateDisplacement)
        && minimumTemplateDisplacement >= 0
        && templatePixels > minimumTemplateDisplacement;

    public static double? MaximumSupportedDisplacement(IReadOnlyList<CircleObservation> before,
        IReadOnlyList<CircleObservation> after, double minimumBoundarySupport = ObservationPolicy.MinimumSupportedBoundaryFraction)
    {
        var left = before.Where(c => Support(c) >= minimumBoundarySupport).ToArray();
        var right = after.Where(c => Support(c) >= minimumBoundarySupport).ToArray();
        double maximum = 0; int matched = 0;
        foreach (var kind in left.Select(Kind).Intersect(right.Select(Kind), StringComparer.Ordinal))
        {
            var a = left.Where(c => Kind(c) == kind).ToArray();
            var b = right.Where(c => Kind(c) == kind).ToArray();
            var pairs = (from i in Enumerable.Range(0, a.Length)
                         from j in Enumerable.Range(0, b.Length)
                         let distance = (a[i].Centre - b[j].Centre).Length
                         let radiusRatio = a[i].Radius > 0 && b[j].Radius > 0
                             ? Math.Max(a[i].Radius, b[j].Radius) / Math.Min(a[i].Radius, b[j].Radius) : double.PositiveInfinity
                         where radiusRatio <= 1.4
                         orderby distance
                         select (I: i, J: j, Distance: distance)).ToArray();
            var usedA = new HashSet<int>(); var usedB = new HashSet<int>();
            foreach (var pair in pairs)
            {
                if (usedA.Contains(pair.I) || usedB.Contains(pair.J)) continue;
                usedA.Add(pair.I); usedB.Add(pair.J);
                matched++; maximum = Math.Max(maximum, pair.Distance);
            }
        }
        return matched == 0 ? null : maximum;

        static double Support(CircleObservation circle) => circle.CandidateBoundarySupport ?? circle.BoundarySupport ?? 0;
        static string Kind(CircleObservation circle)
        {
            int separator = circle.BallId.IndexOf('-');
            return separator > 0 ? circle.BallId[..separator] : circle.BallId;
        }
    }
}

public sealed record RecognitionTelemetry(string Branch, int RawCircleCount, int FinalCircleCount,
    string[] RawLabels, double[] Gains, int ClothHue, string[] Warnings)
{
    public CueBallRecoveryTelemetry? CueBallRecovery { get; init; }
}
public sealed record CueBallRecoveryTelemetry(int InitialCueBallCount, int BeforeNormalizationCueBallCount,
    int? AfterNormalizationCueBallCount, int FinalCueBallCount, int NeutralBodyCandidates,
    int NeutralBodiesRecovered, bool GeometryRetained, string Reason, double? BoundarySupport)
{
    public int RejectedInitialCueBodies { get; init; }
    public int RejectedNormalizedCueBodies { get; init; }
    public NeutralCueCandidateTelemetry[] NeutralCandidateAudits { get; init; } = [];
    public NeutralCueMaskTelemetry? NeutralMaskTelemetry { get; init; }
}
public sealed record NeutralCueMaskTelemetry(int MaskPixels, int ConnectedComponents,
    Dictionary<string, int> FilterCounts, NeutralCueMaskComponentTelemetry[] NearPocketComponents)
{
    public NeutralCueMaskProbeTelemetry? Probe { get; init; }
    public NeutralCueMaskBoundaryProbeTelemetry? BoundaryProbe { get; init; }
}
public sealed record NeutralCueMaskBoundaryProbeTelemetry(double? FittedCentreX, double? FittedCentreY,
    double? FittedRadius, double? BoundarySupport, string? Classification, bool? LocalContrast, string Decision)
{
    public BoundarySectorEvidence[] SectorEvidence { get; init; } = [];
}
public sealed record NeutralCueMaskProbeTelemetry(double CentreX, double CentreY, double Radius,
    int SamplePixels, int AppearancePassPixels, int AdaptiveValuePassPixels,
    int PreMorphMaskPixels, int PostMorphMaskPixels, double MeanValue, double MeanFieldValue,
    int CentreValue, int CentreFieldValue, bool CentreAppearancePass, bool CentreAdaptiveValuePass,
    bool CentrePreMorphMask, bool CentrePostMorphMask, int CentreComponentLabel);
public sealed record NeutralCueMaskComponentTelemetry(int Label, double CentreX, double CentreY,
    int Width, int Height, int Area, double Radius, double FillFraction,
    double NearestPocketDistance, string Decision);
public sealed record NeutralCueCandidateTelemetry(double ProposalX, double ProposalY, double ProposalRadius,
    double? FittedX, double? FittedY, double? FittedRadius, double? BoundarySupport,
    string? Classification, bool? LocalContrast, string Decision)
{
    public BallClassification? ClassificationEvidence { get; init; }
}
public sealed record LayoutChangeTelemetry(int PreviousCount, int CurrentCount, bool SpatiallyCompatible, bool LabelsCompatible);
