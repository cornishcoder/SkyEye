using System.Collections.Immutable;

namespace SkyEye.Core.Analysis;

// Display-only landmarks in the original image. These are not cloth calibration,
// ball observations, or authoritative game facts. Contrast is a feature score.
public sealed record PocketLayout(ImmutableArray<Point> Corners, ImmutableArray<Point> SidePockets, double Contrast);
