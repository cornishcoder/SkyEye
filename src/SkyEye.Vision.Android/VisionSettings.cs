namespace SkyEye.Vision.Android;

/// <summary>Detection dimensions are expressed at the upstream 1000-pixel width;
/// the analyzer scales spatial parameters together for other resolutions.</summary>
public sealed record VisionSettings(int Width = 1000, double MinimumRadius = 18, double MaximumRadius = 35,
    double Separation = 45, int MorphologyKernel = 5, double HoughDp = 1.3, double EdgeThreshold = 150,
    double InitialVotes = 15, double NormalizedVotes = 14, int HueBand = 7,
    double PocketExclusionRadius = 48)
{
    // The labelled spread table and two separate pocket/cushion captures
    // recovered edge balls at 48px while clear-rack and absent-black controls
    // stayed clean. The independent pocket-colour review still reports risk.
    public const double DefaultPocketExclusionRadius = 48;
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
    // Receipt-checked pocket/cushion replays showed the 65px margin rejected
    // real edge-ball proposals; 23px recovered four more labelled balls with
    // no extras in the pocket layout and retained a clear 16-ball rack control.
    // Incomplete edge layouts remain uncertain and cannot establish readiness.
    public int PackedClusterImageEdgeMargin { get; init; } = 23;
    // Controlled replay ablation for fresh neutral-body / geometry recovery.
    public bool FreshCueBallRecovery { get; init; } = true;
    // Separately replay the whole-table proposal fix against the v2 cue branch.
    public bool BodyProposalPriority { get; init; } = true;
    // T5: more than seven reds or yellows demotes only a clearly less saturated surplus to unknown
    // (BallClassifier.ConstrainInventory); without a clear gap the whole colour is demoted as before.
    // False replays the earlier whole-colour demotion and its historical algorithm identity.
    public bool SurplusOnlyInventoryDemotion { get; init; } = true;
    // A warm cue can fall inside the yellow colour mask. The packed-colour stage keeps only its
    // own colour, so a well-fitted body there that classifies as the cue was discarded. When no
    // other cue is found, admit exactly one such body (fit at least MinimumHoughBoundarySupport).
    // False replays the earlier behaviour and identity (Layout03 frames 382/1274/3360 diagnosis).
    public bool PackedCueHandover { get; init; } = true;
    // Obstruction mask only: a cast shadow under mixed daylight and warm room light keeps the cloth's
    // brightness but turns warm in hue (afternoon device run 7c151fba: H 17, S 53 against cloth H 102),
    // so the hue band and the S<=45 neutral fallback reject it and it joins balls into a false
    // "large object". Also treat S 46..60 at 0.6..1.15x cloth median brightness as cloth, any hue.
    // Skin measured S p50 93..94 and brighter than the cloth. False replays the earlier behaviour.
    public bool ObstructionShadowTolerance { get; init; } = true;
    // A packed cluster was marked unresolved by an unknown ball anywhere on the table, so a cue read as
    // unknown at the other end (afternoon run 7c151fba) denied a complete 7/7/1 rack its obstruction
    // exemption. Count only unknown balls within 2.5 ball radii of a red, yellow or black ball. The
    // whole-table colour counts still apply. False replays the earlier behaviour.
    public bool PackedClusterLocalUnknowns { get; init; } = true;
    public double MinimumHoughBoundarySupport { get; init; } = .75;
    // A fresh six-frame pocket-lip holdout recovered a fitted top-left red with
    // 20/32 supported sectors; the separate pocket-colour review retains its
    // stricter support gate. Clear-rack, black-absent and obstruction controls
    // remained clean at this sparse-colour-only threshold.
    // This gate applies to isolated sparse colour bodies only. A matched red
    // at a pocket lip can lose twelve sectors to the pocket while retaining a
    // fitted, correctly classified ball body; boundary, colour and separation
    // checks remain required.
    public const double DefaultMinimumSparseColourBoundarySupport = 20d / 32;
    public double MinimumSparseColourBoundarySupport { get; init; } = DefaultMinimumSparseColourBoundarySupport;
    // The labelled spread/pocket/cushion holdout hid the black among many
    // cloth-mask peaks. Radius-first ranking recovered it within the existing
    // 12-fit cap at both widths. A 21/32 boundary fit recovered every sampled
    // black; the clear-rack positive and synthetic black-absent negative stayed
    // correct. Incomplete colour layouts still remain uncertain.
    public const double DefaultSparseBlackBoundarySearchExtentFraction = 1.3;
    public const double DefaultMinimumSparseBlackBoundarySupport = 21d / 32;
    public const bool DefaultSparseBlackRankByRadius = true;
    // Ball-sized dark peaks are normally required before a seed enters the
    // expensive sparse-black fit. Keep the cutoff configurable for exact-frame
    // replay: a daylight pocket-positive sample had a nearby 6.14px peak just
    // below the default 6.4px width-800 cutoff.
    public const double DefaultSparseBlackMinimumPeakRadiusFactor = 8;
    public double SparseBlackBoundarySearchExtentFraction { get; init; } = DefaultSparseBlackBoundarySearchExtentFraction;
    public double MinimumSparseBlackBoundarySupport { get; init; } = DefaultMinimumSparseBlackBoundarySupport;
    // Evaluation-only: enumerate the full sparse-black mask before bounded fit ranking.
    public bool SparseBlackEnumerateAllSeeds { get; init; }
    public int SparseBlackCandidateLimit { get; init; } = 12;
    public bool SparseBlackRankByRadius { get; init; } = DefaultSparseBlackRankByRadius;
    public double SparseBlackMinimumPeakRadiusFactor { get; init; } = DefaultSparseBlackMinimumPeakRadiusFactor;
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
        if (!double.IsFinite(SparseBlackMinimumPeakRadiusFactor) || SparseBlackMinimumPeakRadiusFactor is < 1 or > 12)
            throw new ArgumentException("Invalid sparse-black minimum seed radius factor.");
        if (SparseBlackCandidateLimit is < 1 or > 64)
            throw new ArgumentException("Invalid sparse-black candidate limit.");
        if (!double.IsFinite(PackedColourBoundarySearchExtentFraction) || PackedColourBoundarySearchExtentFraction is < .2 or > .8)
            throw new ArgumentException("Invalid packed-colour boundary search extent.");
        if (PackedClusterImageEdgeMargin is < 0 or > 100)
            throw new ArgumentException("Invalid packed-cluster image edge margin.");
        if (DarkBodyValueCeiling is < 10 or > 55) throw new ArgumentException("Invalid dark body ceiling.");
    }
}
