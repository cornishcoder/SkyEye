using SkyEye.Core.Application;

namespace SkyEye.Core.Analysis;

public enum ObservationMode { Single, VerifyForAdvice }
public sealed record VerificationMeasurement(TableMotion Motion, ObservationReason Reason, bool CoverageValid,
    double MaximumTileChange, double PerimeterChange, double IlluminationChange, VisionResult? Vision)
{
    public double SensitiveTileChange { get; init; }
    /// <summary>Strong changed-pixel fraction after removing the tile's per-channel exposure offset.</summary>
    public double PhotometricResidualTileChange { get; init; }
    public double? MaximumBodyDisplacement { get; init; }
    public double? MaximumDetectedBodyDisplacement { get; init; }
    public double? MinimumTemplateMotionPixels { get; init; }
    public int SupportedBodies { get; init; }
    public Point? MaximumTileOrigin { get; init; }
    public Point? SensitiveTileOrigin { get; init; }
    public Point? PhotometricResidualTileOrigin { get; init; }
}

/// <summary>Lets complete, stable ball-template evidence absorb weak surface-pixel noise.
/// Recognition uncertainty remains a layout concern; obstruction, strong motion,
/// lighting and calibration signals still block a stillness result.</summary>
public static class VerificationStillnessPolicy
{
    public const double MaximumSupportedWeakEdgeChange = .30;
    public const double MaximumSupportedPhotometricResidual = .03;
    public const double MaximumSupportedPerimeterChange = .02;

    public static bool CanResolveAsStill(VerificationMeasurement measurement, bool allTrackedBodiesSupported)
    {
        var vision = measurement.Vision;
        if (!allTrackedBodiesSupported || !measurement.CoverageValid
            || measurement.Motion != TableMotion.Unknown || measurement.Reason != ObservationReason.InsufficientEvidence
            || vision is null || vision.Obstructed
            || vision.Balls.Length is < 1 or > 16)
            return false;

        return measurement.SensitiveTileChange <= MaximumSupportedWeakEdgeChange
            && measurement.PhotometricResidualTileChange <= MaximumSupportedPhotometricResidual
            && measurement.PerimeterChange <= MaximumSupportedPerimeterChange
            && measurement.IlluminationChange <= 8;
    }
}

public interface ITableVerificationAnalyzer
{
    ITableVerificationWorker CreateVerification(TableCalibration calibration);
}
public interface ITableAnalyzerMetadata
{
    string ConfigurationIdentity { get; }
}
public interface ITableVerificationFrameSource : ITableFrameSource
{
    Task RefreshVerificationClockAsync(CancellationToken cancellation);
    Task<bool> IsVerificationSourceCurrentAsync(CameraScope source, long mediaGeneration, string timeline, CancellationToken cancellation);
}
public interface ITableVerificationWorker : IDisposable
{
    VerificationMeasurement Measure(TableFrame frame, bool detect, CancellationToken cancellation);
    void ResetAfterObstruction();
}
public sealed record VerificationSample(long FrameId, long SourceTimeUs, double AgeMs, double ClockSampleAgeMs, double ClockUncertaintyMs,
    double AcquisitionMs, double ProcessingMs, VerificationMeasurement Measurement)
{
    public DateTimeOffset CapturedAt { get; init; }
}
public sealed record DetectionEvidenceAge(long FrameId, long SourceTimeUs, double AgeMs);
public sealed record VerificationEvidence(Guid OperationId, string Configuration, string CalibrationId, string TargetGroup,
    long MediaGeneration, long? StartSourceUs, long? EndSourceUs, int IndependentFrames, double MaximumGapMs,
    double FinalClockSampleAgeMs, VerificationSample[] Samples)
{
    public int CandidateSpanStartSampleIndex { get; init; }
    public int CandidateSpanIndependentFrames { get; init; }
    public VerificationRejection? RejectedSample { get; init; }
    public VerificationFailure? Failure { get; init; }
    public ConsensusConflict? ConsensusConflict { get; init; }
}
public sealed record VerificationFailure(string Stage, string Code)
{
    public string? SourceStatus { get; init; }
}
public sealed class FrameAcquisitionException(ObservationReason reason, string code, string? sourceStatus = null) : IOException(code)
{
    public ObservationReason Reason { get; } = reason;
    public string Code { get; } = code;
    public string? SourceStatus { get; } = sourceStatus;
}
public sealed record ConsensusConflict(string Code, int Sample, string? BallId);
public sealed record VerificationRejection(ObservationReason Reason, DecodedFrameReceipt? Receipt, string Timeline,
    long? SourceTimeUs, double AgeMs, double? ClockSampleAgeMs, double? ClockUncertaintyMs);
public sealed record VerificationProgress(int IndependentFrames, ObservationReason Reason);
// Opt-in native evidence callback; TableFrame is borrowed only until the callback returns.
// Pixels never enter TableObservation, agent projections or durable game state.
public sealed record VerificationCapture(Guid OperationId, long AnalysisGeneration, string CalibrationId,
    Point[] CalibrationCorners, TableFrame Frame, VerificationMeasurement Measurement);

/// <summary>Conservative transient association. Never carries a missing final body
/// forward or creates durable identities. Conflicting one-to-one assignments are unresolved.</summary>
public static class VerificationConsensus
{
    public static Ball[]? Resolve(IReadOnlyList<VisionResult> observations) => Resolve(observations, out _);
    public static Ball[]? Resolve(IReadOnlyList<VisionResult> observations, out ConsensusConflict? conflict)
    {
        conflict = new("insufficient-or-obstructed", -1, null);
        if (observations.Count < 3 || observations.Any(o => o.Obstructed || o.Balls.Length > 16)) return null;
        var first = observations[0].Balls; var final = observations[^1];
        conflict = new("final-quality-or-count", observations.Count - 1, null);
        if (final.Uncertain || first.Length != final.Balls.Length || first.Length == 0) return null;
        var histories = first.OrderBy(b => b.Position.X).ThenBy(b => b.Position.Y).Select(b => new List<Ball> { b }).ToArray();
        var toleratedPartialRadiusOutliers = new HashSet<string>(StringComparer.Ordinal);
        for (int sample = 1; sample < observations.Count; sample++)
        {
            var observation = observations[sample];
            var used = new HashSet<int>();
            foreach (var history in histories)
            {
                var candidates = observation.Balls.Select((b, i) => (Ball: b, Index: i))
                    .Where(p => (p.Ball.Position - history[0].Position).Length <= .015).ToArray();
                // A middle dropout is a hypothesis only; the final sample must recover it.
                if (candidates.Length == 0 && !ReferenceEquals(observation, final)) continue;
                conflict = new("ambiguous-association", sample, history[0].Id);
                if (candidates.Length != 1 || !used.Add(candidates[0].Index)) return null;
                var anchorCircle = observations[0].Circles.SingleOrDefault(c => c.BallId == history[0].Id);
                var currentCircle = observation.Circles.SingleOrDefault(c => c.BallId == candidates[0].Ball.Id);
                if (anchorCircle is not null && currentCircle is not null)
                {
                    // An explicitly attempted but unsupported middle fit can be
                    // re-observed. It consumes no geometry/colour vote. Start/end
                    // and three independently supported appearances remain required.
                    if (currentCircle.ProposalRadius is not null && currentCircle.BoundarySupport is null && !ReferenceEquals(observation, final)) continue;
                    // Partial boundary arcs are enough to support a ball centre
                    // in a touching rack, but one isolated fit can move as the
                    // visible arc changes. Keep the 25% radius gate unless two
                    // adjacent, clear, supported fits corroborate the anchor.
                    double radiusRatio = anchorCircle.Radius > 0 && currentCircle.Radius > 0
                        ? Math.Max(anchorCircle.Radius, currentCircle.Radius) / Math.Min(anchorCircle.Radius, currentCircle.Radius)
                        : double.PositiveInfinity;
                    bool partialMiddleOutlier = !ReferenceEquals(observation, final)
                        && currentCircle.BoundarySupport is >= ObservationPolicy.MinimumSupportedBoundaryFraction and < 1
                        && sample > 0 && sample < observations.Count - 1
                        && HasCompatibleRadiusNeighbours(observations, sample, history[0], anchorCircle.Radius);
                    if (radiusRatio > 1.25
                        && (!partialMiddleOutlier || !toleratedPartialRadiusOutliers.Add(history[0].Id)))
                    {
                        conflict = new("radius-inconsistent", sample, history[0].Id);
                        return null;
                    }
                }
                history.Add(candidates[0].Ball);
            }
            conflict = new("unexplained-body", sample, null);
            if (used.Count != observation.Balls.Length) return null; // unexplained extra/merged body
        }
        var result = new List<Ball>();
        foreach (var history in histories)
        {
            var current = history[^1];
            conflict = new("insufficient-current-support", observations.Count - 1, current.Id);
            if (history.Count < 3 || current.Kind == "unknown" || current.Confidence < .7
                || history.Count(b => b.Kind == current.Kind && b.Confidence >= .7) < 2) return null;
            var x = history.Select(b => b.Position.X).Order().ToArray();
            var y = history.Select(b => b.Position.Y).Order().ToArray();
            result.Add(current with { Id = "burst-" + result.Count, Position = new(x[x.Length / 2], y[y.Length / 2]) });
        }
        conflict = null; return result.ToArray();
    }

    static bool HasCompatibleRadiusNeighbours(IReadOnlyList<VisionResult> observations, int sample,
        Ball anchor, double anchorRadius)
    {
        var before = observations[sample - 1]; var after = observations[sample + 1];
        if (before.Obstructed || after.Obstructed) return false;
        var beforeCircle = FindCircleAt(before, anchor);
        var afterCircle = FindCircleAt(after, anchor);
        return beforeCircle is { BoundarySupport: >= ObservationPolicy.MinimumSupportedBoundaryFraction } beforeFit
            && afterCircle is { BoundarySupport: >= ObservationPolicy.MinimumSupportedBoundaryFraction } afterFit
            && RadiusCompatible(anchorRadius, beforeFit.Radius)
            && RadiusCompatible(anchorRadius, afterFit.Radius)
            && RadiusCompatible(beforeFit.Radius, afterFit.Radius);
    }

    static CircleObservation? FindCircleAt(VisionResult observation, Ball anchor)
    {
        var matches = observation.Balls.Where(ball => (ball.Position - anchor.Position).Length <= .015).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].Kind != anchor.Kind || matches[0].Confidence < .7) return null;
        return observation.Circles.SingleOrDefault(circle => circle.BallId == matches[0].Id);
    }

    static bool RadiusCompatible(double first, double second) => first > 0 && second > 0
        && Math.Max(first, second) / Math.Min(first, second) <= 1.25;
}
