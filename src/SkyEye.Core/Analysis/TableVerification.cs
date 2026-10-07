using SkyEye.Core.Application;

namespace SkyEye.Core.Analysis;

public enum ObservationMode { Single, VerifyForAdvice }
public sealed record VerificationMeasurement(TableMotion Motion, ObservationReason Reason, bool CoverageValid,
    double MaximumTileChange, double PerimeterChange, double IlluminationChange, VisionResult? Vision);
public interface ITableVerificationAnalyzer
{
    ITableVerificationWorker CreateVerification(TableCalibration calibration);
}
public interface ITableVerificationFrameSource : ITableFrameSource
{
    Task RefreshVerificationClockAsync(CancellationToken cancellation);
    Task<bool> IsVerificationSourceCurrentAsync(CameraScope source, long mediaGeneration, string timeline, CancellationToken cancellation);
}
public interface ITableVerificationWorker : IDisposable
{
    VerificationMeasurement Measure(TableFrame frame, bool detect, CancellationToken cancellation);
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
    public VerificationRejection? RejectedSample { get; init; }
}
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
    public static Ball[]? Resolve(IReadOnlyList<VisionResult> observations)
    {
        if (observations.Count < 3 || observations.Any(o => o.Obstructed || o.Balls.Length > 16)) return null;
        var first = observations[0].Balls; var final = observations[^1];
        if (final.Uncertain || first.Length != final.Balls.Length || first.Length == 0) return null;
        var histories = first.OrderBy(b => b.Position.X).ThenBy(b => b.Position.Y).Select(b => new List<Ball> { b }).ToArray();
        foreach (var observation in observations.Skip(1))
        {
            var used = new HashSet<int>();
            foreach (var history in histories)
            {
                var candidates = observation.Balls.Select((b, i) => (Ball: b, Index: i))
                    .Where(p => (p.Ball.Position - history[0].Position).Length <= .015).ToArray();
                // A middle dropout is a hypothesis only; the final sample must recover it.
                if (candidates.Length == 0 && !ReferenceEquals(observation, final)) continue;
                if (candidates.Length != 1 || !used.Add(candidates[0].Index)) return null;
                var anchorCircle = observations[0].Circles.SingleOrDefault(c => c.BallId == history[0].Id);
                var currentCircle = observation.Circles.SingleOrDefault(c => c.BallId == candidates[0].Ball.Id);
                if (anchorCircle is not null && currentCircle is not null && (anchorCircle.Radius <= 0 || currentCircle.Radius <= 0
                    || Math.Max(anchorCircle.Radius, currentCircle.Radius) / Math.Min(anchorCircle.Radius, currentCircle.Radius) > 1.25)) return null;
                history.Add(candidates[0].Ball);
            }
            if (used.Count != observation.Balls.Length) return null; // unexplained extra/merged body
        }
        var result = new List<Ball>();
        foreach (var history in histories)
        {
            var current = history[^1];
            if (history.Count < 3 || current.Kind == "unknown" || current.Confidence < .7
                || history.Count(b => b.Kind == current.Kind && b.Confidence >= .7) < 2) return null;
            var x = history.Select(b => b.Position.X).Order().ToArray();
            var y = history.Select(b => b.Position.Y).Order().ToArray();
            result.Add(current with { Id = "burst-" + result.Count, Position = new(x[x.Length / 2], y[y.Length / 2]) });
        }
        return result.ToArray();
    }
}
