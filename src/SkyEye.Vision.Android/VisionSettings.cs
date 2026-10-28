namespace SkyEye.Vision.Android;

/// <summary>Detection dimensions are expressed at the upstream 1000-pixel width;
/// the analyzer scales spatial parameters together for other resolutions.</summary>
public sealed record VisionSettings(int Width = 1000, double MinimumRadius = 18, double MaximumRadius = 35,
    double Separation = 45, int MorphologyKernel = 5, double HoughDp = 1.3, double EdgeThreshold = 150,
    double InitialVotes = 15, double NormalizedVotes = 14, int HueBand = 7, double PocketExclusionRadius = 55)
{
    public bool LocalIlluminationNormalization { get; init; } = true;
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
    // Controlled replay ablation for fresh neutral-body / geometry recovery.
    public bool FreshCueBallRecovery { get; init; } = true;
    // Separately replay the whole-table proposal fix against the v2 cue branch.
    public bool BodyProposalPriority { get; init; } = true;
    public double MinimumHoughBoundarySupport { get; init; } = .75;
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
        if (!double.IsFinite(PackedColourBoundarySearchExtentFraction) || PackedColourBoundarySearchExtentFraction is < .2 or > .8)
            throw new ArgumentException("Invalid packed-colour boundary search extent.");
        if (DarkBodyValueCeiling is < 10 or > 55) throw new ArgumentException("Invalid dark body ceiling.");
    }
}
