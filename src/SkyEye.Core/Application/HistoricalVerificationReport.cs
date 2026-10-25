using SkyEye.Core.Analysis;

namespace SkyEye.Core.Application;

/// <summary>A bounded, metadata-only copy of the most recently completed table verification.
/// It is diagnostic history; it never establishes current table readiness.</summary>
public sealed record HistoricalVerificationBallCounts(int Total, int CueBalls, int Reds, int Yellows, int BlackBalls, int Unknown);

public sealed record HistoricalVerificationSample(
    int Index, long FrameId, long SourceTimeUs, DateTimeOffset CapturedAt,
    double AgeMs, double ClockSampleAgeMs, double ClockUncertaintyMs,
    double AcquisitionMs, double ProcessingMs,
    TableMotion Motion, ObservationReason Reason, bool CoverageValid,
    double MaximumTileChange, double SensitiveTileChange, double PhotometricResidualTileChange,
    double PerimeterChange, double IlluminationChange,
    double? MaximumBodyDisplacement, double? MaximumDetectedBodyDisplacement,
    double? MinimumTemplateMotionPixels, int SupportedBodies,
    HistoricalVerificationBallCounts? BallCounts, bool? DetectionUncertain, bool? Obstructed);

/// <summary>Compact historical evidence for one completed bounded check. The per-frame
/// list is capped at the verifier's six-sample limit and contains no image pixels or
/// ball coordinates.</summary>
public sealed record HistoricalVerificationReport(
    DateTimeOffset RecordedAt, Guid OperationId, bool PublishedAsCurrent, string PublicationOutcome,
    string CameraIdentity, string ConnectionId, long AnalysisGeneration, long MediaGeneration,
    string Timeline, string CalibrationId, string TargetGroup, string Configuration,
    string FrameSource, long TableVersion, DateTimeOffset CapturedAt, double FinalFrameAgeMs, double DurationMs,
    ObservationOutcome Outcome, TableMotion Motion, LayoutQuality Quality, ObservationReason Reason,
    long? SourceTimeUs, DecodedFrameReceipt? DecodedReceipt, DetectionEvidenceAge? DetectionEvidence,
    long? StartSourceTimeUs, long? EndSourceTimeUs, int IndependentFrames, double MaximumGapMs,
    double FinalClockSampleAgeMs, int CandidateSpanStartSampleIndex, int CandidateSpanIndependentFrames,
    HistoricalVerificationBallCounts? FinalDetection, HistoricalVerificationSample[] Samples,
    VerificationFailure? Failure, VerificationRejection? RejectedSample, ConsensusConflict? ConsensusConflict)
{
    public bool Historical => true;
    public bool IsCurrent => false;

    public static HistoricalVerificationReport From(TableObservation observation, DateTimeOffset recordedAt,
        bool publishedAsCurrent, CommandOutcome publicationOutcome)
    {
        var evidence = observation.Verification;
        var sourceSamples = evidence?.Samples ?? [];
        var samples = sourceSamples.Take(ObservationPolicy.MaximumVerificationSamples)
            .Select((sample, index) => new HistoricalVerificationSample(
                index, sample.FrameId, sample.SourceTimeUs, sample.CapturedAt,
                sample.AgeMs, sample.ClockSampleAgeMs, sample.ClockUncertaintyMs,
                sample.AcquisitionMs, sample.ProcessingMs,
                sample.Measurement.Motion, sample.Measurement.Reason, sample.Measurement.CoverageValid,
                sample.Measurement.MaximumTileChange, sample.Measurement.SensitiveTileChange,
                sample.Measurement.PhotometricResidualTileChange, sample.Measurement.PerimeterChange,
                sample.Measurement.IlluminationChange, sample.Measurement.MaximumBodyDisplacement,
                sample.Measurement.MaximumDetectedBodyDisplacement, sample.Measurement.MinimumTemplateMotionPixels,
                sample.Measurement.SupportedBodies, Counts(sample.Measurement.Vision),
                sample.Measurement.Vision?.Uncertain, sample.Measurement.Vision?.Obstructed))
            .ToArray();

        return new(recordedAt, evidence?.OperationId ?? Guid.Empty, publishedAsCurrent, publicationOutcome.ToString(),
            observation.Source.CameraIdentity, observation.Source.ConnectionId.ToString("N"), observation.AnalysisGeneration,
            observation.MediaGeneration, observation.Timeline, evidence?.CalibrationId ?? "", evidence?.TargetGroup ?? "",
            evidence?.Configuration ?? "", observation.FrameSource, observation.TableVersion, observation.CapturedAt,
            observation.FrameAgeMs, observation.DurationMs, observation.Outcome, observation.Motion, observation.Quality,
            observation.Reason, observation.SourceTimeUs, observation.DecodedReceipt, observation.DetectionEvidence,
            evidence?.StartSourceUs, evidence?.EndSourceUs, evidence?.IndependentFrames ?? 0,
            evidence?.MaximumGapMs ?? 0, evidence?.FinalClockSampleAgeMs ?? -1,
            evidence?.CandidateSpanStartSampleIndex ?? 0, evidence?.CandidateSpanIndependentFrames ?? 0,
            samples.LastOrDefault()?.BallCounts, samples, evidence?.Failure, evidence?.RejectedSample,
            evidence?.ConsensusConflict);
    }

    static HistoricalVerificationBallCounts? Counts(VisionResult? vision)
    {
        if (vision is null) return null;
        var balls = vision.Balls;
        int Count(string kind) => balls.Count(ball => string.Equals(ball.Kind, kind, StringComparison.OrdinalIgnoreCase));
        int known = Count("cue") + Count("red") + Count("yellow") + Count("black");
        return new(balls.Length, Count("cue"), Count("red"), Count("yellow"), Count("black"), balls.Length - known);
    }
}
