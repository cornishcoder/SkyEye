namespace SkyEye.Vision.Android;

/// <summary>Detection dimensions are expressed at the upstream 1000-pixel width;
/// the analyzer scales spatial parameters together for other resolutions.</summary>
public sealed record VisionSettings(int Width = 1000, double MinimumRadius = 18, double MaximumRadius = 35,
    double Separation = 45, int MorphologyKernel = 5, double HoughDp = 1.3, double EdgeThreshold = 150,
    double InitialVotes = 15, double NormalizedVotes = 14, int HueBand = 7, double PocketExclusionRadius = 55)
{
    // Development-only branch experiment. O08 replay showed poorer colour
    // availability with this refinement; normal verification leaves it disabled.
    public bool VerificationBodyRefinement { get; init; }
    public void Validate()
    {
        if (Width is < 100 or > 2000 || new[] { MinimumRadius, MaximumRadius, Separation, HoughDp, EdgeThreshold, InitialVotes, NormalizedVotes, PocketExclusionRadius }.Any(v => !double.IsFinite(v) || v <= 0) ||
            MinimumRadius < 5 || MaximumRadius > 60 || MaximumRadius <= MinimumRadius || Separation < 2 * MinimumRadius || Separation > 100 || MorphologyKernel is < 3 or > 15 || MorphologyKernel % 2 == 0 ||
            HueBand is < 2 or > 20 || HoughDp is < 1 or > 3 || PocketExclusionRadius > 80)
            throw new ArgumentException("Invalid table vision settings.");
    }
}
