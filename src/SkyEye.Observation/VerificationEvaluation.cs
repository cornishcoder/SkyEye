using System.Collections.Immutable;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Observation;

public enum VerificationTimingKind { OfflineReplay, LiveRecorded }
public sealed record VerificationWindow(string Id, string SequenceId, string FinalSampleId, Guid OperationId,
    long FromUs, long ToUs, AdviceTargetGroup Group, VerificationTimingKind TimingKind, TableObservation Result);
public sealed record VerificationWindowScore(string Id, string SequenceId, string Session, CorpusSplit Split,
    string[] Strata, bool Unsafe, bool PublishedReady, bool ValidProof, bool? CorrectLayout, bool? Timely,
    bool FalseReady, ObservationReason Reason, double DurationMs, BaselineScore? Geometry);

/// <summary>Score complete recorded request outcomes against independently reviewed
/// intervals and final geometry. Uses the product publication-proof validator.
/// Offline replay never establishes live latency or resource acceptance.</summary>
public static class VerificationEvaluation
{
    static readonly string[] unsafeKinds = ["moving", "obstructed", "lighting-changed", "camera-changed", "source-invalid"];
    public static VerificationWindowScore[] Score(CorpusManifest manifest, CorpusFiles files, VerificationWindow[] windows, double tolerance = .027)
    {
        if (windows.Length is 0 or > 10000 || windows.Select(w => w.Id).Distinct().Count() != windows.Length
            || windows.Select(w => w.OperationId).Distinct().Count() != windows.Length) throw new InvalidDataException("Unique bounded request windows required.");
        CorpusValidation.Validate(manifest, files);
        var scores = new List<VerificationWindowScore>();
        foreach (var w in windows)
        {
            var sequence = manifest.Sequences.Single(s => s.Id == w.SequenceId);
            if (sequence.Purpose is not (SequencePurpose.StillVerification or SequencePurpose.NegativeVerification)
                || w.OperationId == Guid.Empty || w.FromUs < 0 || w.ToUs < w.FromUs || !Enum.IsDefined(w.TimingKind)
                || !double.IsFinite(w.Result.DurationMs) || w.Result.DurationMs < 0)
                throw new InvalidDataException("Invalid verification request provenance.");
            var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(sequence.LabelsFile));
            if (!labels.Reviewed) throw new InvalidDataException("Request scoring requires reviewed labels.");
            var intervals = labels.Intervals.Where(i => i.Certainty == "confirmed" && i.FromUs <= w.ToUs && i.ToUs >= w.FromUs).ToArray();
            bool unsafeWindow = intervals.Any(i => unsafeKinds.Contains(i.Kind));
            if (!unsafeWindow && !intervals.Any(i => i.Kind == "still" && i.FromUs <= w.FromUs && i.ToUs >= w.ToUs))
                throw new InvalidDataException("A safe request must have a fully reviewed still interval.");
            var result = w.Result;
            if (result.Verification is { } proof && (proof.OperationId != w.OperationId
                || proof.StartSourceUs < w.FromUs || proof.EndSourceUs > w.ToUs)) throw new InvalidDataException("Request/source interval mismatch.");
            bool ready = result.Outcome == ObservationOutcome.Ready;
            bool valid = ready && AppCoordinator.HasVerificationProof(result, w.OperationId, w.Group);
            BaselineScore? geometry = null; bool? correct = unsafeWindow ? null : false;
            if (ready && !unsafeWindow && valid)
            {
                var frame = labels.Frames.Single(f => f.SampleId == w.FinalSampleId);
                var indexed = files.Index(sequence.IndexFile).Single(f => f.SampleId == w.FinalSampleId);
                if (frame.CoordinateSpace != "table-2x1" || indexed.SourceTimeUs != result.SourceTimeUs
                    || indexed.FrameId != result.DecodedReceipt?.FrameId || indexed.Scope.Timeline != result.Timeline
                    || indexed.Scope.Calibration != result.Verification?.CalibrationId)
                    throw new InvalidDataException("Final truth must match the published calibrated receipt.");
                var balls = result.GameEvidence?.Balls;
                if (balls is not null && balls.Value.All(b => b.Position is not null))
                {
                    var detections = balls.Value.Select(b => new DetectedBall(b.Id, b.Colour.ToString().ToLowerInvariant(), b.Position!.X * 2, b.Position.Y, b.Confidence)).ToImmutableArray();
                    geometry = DetectorBaseline.Score(frame, new(frame.SampleId, frame.PixelSha256, frame.CoordinateSpace,
                        result.Verification!.Configuration, "Recorded published verification result", false, detections), tolerance);
                    correct = valid && frame.Balls.All(b => b.Visibility is LabelVisibility.Visible or LabelVisibility.Absent)
                        && geometry.FalsePositives == 0 && geometry.MissedVisible == 0 && geometry.ColourCorrect == geometry.VisibleLabels
                        && geometry.MatchesWithReviewedRadii == geometry.VisibleLabels && geometry.CentreErrorInRadiiP95 <= .25;
                }
            }
            scores.Add(new(w.Id, sequence.Id, sequence.Session, sequence.Split, sequence.Tags.ToArray(), unsafeWindow, ready, valid, correct,
                w.TimingKind == VerificationTimingKind.LiveRecorded ? result.DurationMs <= 3000 : null,
                ready && (unsafeWindow || !valid || correct != true), result.Reason, result.DurationMs, geometry));
        }
        return scores.ToArray();
    }
}
