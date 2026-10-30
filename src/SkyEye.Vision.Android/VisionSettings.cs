namespace SkyEye.Vision.Android;

/// <summary>Detection dimensions are expressed at the upstream 1000-pixel width;
/// the analyzer scales spatial parameters together for other resolutions.</summary>
public sealed record VisionSettings(int Width = 1000, double MinimumRadius = 18, double MaximumRadius = 35,
    double Separation = 45, int MorphologyKernel = 5, double HoughDp = 1.3, double EdgeThreshold = 150,
    double InitialVotes = 15, double NormalizedVotes = 14, int HueBand = 7, double PocketExclusionRadius = 55)
{
    // Default to source pixels. A retained all-lights black-removal control
    // produced a false neutral Hough body after normalization; disabling the
    // correction removed it while preserving all 11 replayed rack frames across
    // daylight/all-lights/mid/lights-off and the physical hand obstruction control.
    // Keep normalization available for explicit replay comparison.
    public bool LocalIlluminationNormalization { get; init; }
    public int IlluminationKernelWidth { get; init; } = 151;
    // Negative diagnostic ablation: code34 four-light replay reduced clear
    // full-rack reads from 23/24 to 20/24. Keep disabled unless new evidence
    // overturns the result; production continues using raw geometry inputs.
    public bool UseNormalizedGeometryForDetection { get; init; }
    // Diagnostic-only isolation: use the illumination-normalized HSV image only
    // to seed packed red/yellow distance-transform peaks. Boundary fitting,
    // colour classification, black recovery and all acceptance gates stay raw.
    public bool UseNormalizedPackedColourProposalMask { get; init; }
    // Wider bounded packed-colour fitting recovered the daylight rack black,
    // preserved 38 positive/negative controls, and improved one reviewed spread frame.
    public double PackedColourBoundarySearchExtentFraction { get; init; } = .65;
    // Diagnostic ablation for packed-colour and sparse-black proposal centres near
    // the analysis image edge. The default preserves the existing detector.
    public int PackedClusterImageEdgeMargin { get; init; } = 65;
    // Controlled replay ablation for fresh neutral-body / geometry recovery.
    public bool FreshCueBallRecovery { get; init; } = true;
    // Separately replay the whole-table proposal fix against the v2 cue branch.
    public bool BodyProposalPriority { get; init; } = true;
    public double MinimumHoughBoundarySupport { get; init; } = .75;
    // Diagnostic-only threshold for admitting isolated packed-colour proposals;
    // the production default remains the conservative 24/32-sector gate.
    public double MinimumSparseColourBoundarySupport { get; init; } = .75;
    // Keep isolated dark-body recovery conservative unless replay evidence
    // supports a wider seed search or lower support gate.
    public double SparseBlackBoundarySearchExtentFraction { get; init; } = .4;
    public double MinimumSparseBlackBoundarySupport { get; init; } = .7;
    public int SparseBlackCandidateLimit { get; init; } = 12;
    // Diagnostic ranking experiment: consider larger dark interiors before
    // painted-ball proximity; production keeps the current order by default.
    public bool SparseBlackRankByRadius { get; init; }
    // Keep the conservative default: source-time-paired brighter footage replayed
    // at 55 lost the black at 1000px in three of six frames; 40 retained all 16.
    public int DarkBodyValueCeiling { get; init; } = 40;
    // Development-only branch experiment. O08 replay showed poorer colour
    // availability with this refinement; normal verification leaves it disabled.
    public bool VerificationBodyRefinement { get; init; }
    public void Validate()
    {
        if (Width is < 100 or > 2000 || new[] { MinimumRadius, MaximumRadius, Separation, HoughDp, EdgeThreshold, InitialVotes, NormalizedVotes, PocketExclusionRadius }.Any(v => !double.IsFinite(v) || v <= 0) ||
            MinimumRadius < 5 || MaximumRadius > 60 || MaximumRadius <= MinimumRadius || Separation < 2 * MinimumRadius || Separation > 100 || MorphologyKernel is < 3 or > 15 || MorphologyKernel % 2 == 0 ||
            HueBand is < 2 or > 20 || HoughDp is < 1 or > 3 || PocketExclusionRadius > 80 ||
            IlluminationKernelWidth is < 31 or > 301 || IlluminationKernelWidth % 2 == 0)
            throw new ArgumentException("Invalid table vision settings.");
        if (!double.IsFinite(MinimumHoughBoundarySupport) || MinimumHoughBoundarySupport is < .5625 or > 1)
            throw new ArgumentException("Invalid Hough body boundary support.");
        if (!double.IsFinite(MinimumSparseColourBoundarySupport) || MinimumSparseColourBoundarySupport is < .5625 or > 1)
            throw new ArgumentException("Invalid sparse-colour body boundary support.");
        if (!double.IsFinite(SparseBlackBoundarySearchExtentFraction) || SparseBlackBoundarySearchExtentFraction is < .2 or > 1.5)
            throw new ArgumentException("Invalid sparse-black boundary search extent.");
        if (!double.IsFinite(MinimumSparseBlackBoundarySupport) || MinimumSparseBlackBoundarySupport is < .5625 or > 1)
            throw new ArgumentException("Invalid sparse-black body boundary support.");
        if (SparseBlackCandidateLimit is < 1 or > 64)
            throw new ArgumentException("Invalid sparse-black candidate limit.");
        if (!double.IsFinite(PackedColourBoundarySearchExtentFraction) || PackedColourBoundarySearchExtentFraction is < .2 or > .8)
            throw new ArgumentException("Invalid packed-colour boundary search extent.");
        if (PackedClusterImageEdgeMargin is < 0 or > 100)
            throw new ArgumentException("Invalid packed-cluster image edge margin.");
        if (DarkBodyValueCeiling is < 10 or > 55) throw new ArgumentException("Invalid dark body ceiling.");
    }
}
