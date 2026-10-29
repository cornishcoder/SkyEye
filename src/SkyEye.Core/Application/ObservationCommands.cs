using SkyEye.Core.Analysis;
using System.Collections.Immutable;

namespace SkyEye.Core.Application;

public sealed record ObserveTable(CameraScope Source, long AnalysisGeneration, long MediaGeneration, bool ReviewFrame = false, bool IncludeChoices = false,
    ObservationMode Mode = ObservationMode.Single) : SkyEyeCommand;
public enum ObservationOutcome { Ready, Unsettled, Unclear }
public sealed record TableObservation(CameraScope Source, long AnalysisGeneration, long MediaGeneration, string Timeline,
    DateTimeOffset CapturedAt, long? SourceTimeUs, double FrameAgeMs, double AcquisitionMs, double VisionMs, double DurationMs,
    long TableVersion, ObservationOutcome Outcome, string FrameSource)
{
    public ShotChoiceCatalog? Choices { get; init; }
    public GameObservationEvidence? GameEvidence { get; init; }
    public DecodedFrameReceipt? DecodedReceipt { get; init; }
    public TableMotion Motion { get; init; }
    public LayoutQuality Quality { get; init; }
    public ObservationReason Reason { get; init; }
    public RecognitionTelemetry? Recognition { get; init; }
    public LayoutChangeTelemetry? Change { get; init; }
    public VerificationEvidence? Verification { get; init; }
    public DetectionEvidenceAge? DetectionEvidence { get; init; }
    public PlayerPlaybackTelemetry? PlayerPlayback { get; init; }
}
public interface ITableObservationEffect
{
    bool IsCurrent(ObserveTable request);
    Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation);
    Task<TableObservation> ExecuteAsync(ObserveTable request, Guid operationId, CancellationToken cancellation)
        => request.Mode == ObservationMode.Single ? ExecuteAsync(request, cancellation)
            : Task.FromException<TableObservation>(new NotSupportedException("This observation adapter does not support bounded verification."));
}

/// <summary>Single observations keep their contract; explicit verification uses the bounded session owner.</summary>
public sealed class TableObservationEffect(Func<TableAnalysisSession> session, Func<long> mediaGeneration, Func<bool> live) : ITableObservationEffect
{
    public bool IsCurrent(ObserveTable request) => live() && request.MediaGeneration == mediaGeneration()
        && request.Source == session().State.Source && request.AnalysisGeneration == session().State.Generation;
    public async Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation)
        => await ExecuteAsync(request, Guid.NewGuid(), cancellation).ConfigureAwait(false);
    public async Task<TableObservation> ExecuteAsync(ObserveTable request, Guid operationId, CancellationToken cancellation)
    {
        if (!IsCurrent(request) || session().State.Calibration is null) throw new InvalidOperationException("Calibrate an active live table before observing it.");
        if (!Enum.IsDefined(request.Mode)) throw new ArgumentException("Unknown observation mode.");
        if (request.Mode == ObservationMode.VerifyForAdvice)
            await session().VerifyAsync(operationId, request.MediaGeneration, cancellation, request.ReviewFrame).WaitAsync(cancellation).ConfigureAwait(false);
        else await session().UpdateAsync(cancellation, request.ReviewFrame).WaitAsync(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        var snapshot = session().State;
        if (!IsCurrent(request) || snapshot.Observation is not { } observed || snapshot.Table is not { } layout
            || layout.Version != observed.TableVersion || layout.CapturedAt > observed.CapturedAt
            || observed.Outcome != ObservationOutcome.Unclear && layout.CapturedAt != observed.CapturedAt
            || observed.DecodedReceipt is { } decoded && decoded.MediaGeneration != request.MediaGeneration)
            throw new InvalidOperationException("The source changed. Request a fresh observation.");
        // An unclear view retains the last clear layout for recovery. Its old
        // positions are not fresh sightings; publish the current detector evidence.
        var observedBalls = observed.Outcome == ObservationOutcome.Unclear
            ? snapshot.Vision?.Balls ?? throw new InvalidOperationException("The unclear frame has no detector evidence.")
            : layout.Balls;
        observed = observed with { GameEvidence = new(new(snapshot.CameraIdentity!, observed.Timeline, request.AnalysisGeneration + 1, snapshot.CalibrationId, request.Source.ConnectionId.ToString("N")),
            observedBalls.Select(b => new SkyEye.GameState.ObservedBall(b.Id, Enum.TryParse<SkyEye.GameState.BallColour>(b.Kind, true, out var colour) ? colour : SkyEye.GameState.BallColour.Unknown,
                new(b.Position.X / 2, b.Position.Y), b.Confidence)).ToImmutableArray()) };
        return observed with { MediaGeneration = request.MediaGeneration };
    }
}

public sealed partial class AppCoordinator
{
    readonly ITableObservationEffect? observations;
    HistoricalVerificationReport? latestVerificationReport;
    /// <summary>The most recent verifier result as historical diagnostics. It is deliberately
    /// separate from SkyEyeState.Observation and cannot make a stopped table current.</summary>
    public HistoricalVerificationReport? LatestVerificationReport => Volatile.Read(ref latestVerificationReport);
    bool ObservationCurrent(ObserveTable request)
    { try { return observations?.IsCurrent(request) == true; } catch (Exception) { return false; } }
    Task<CommandReceipt> BeginObservation(CommandEnvelope envelope, CancellationToken cancellation)
    {
        var request = (ObserveTable)envelope.Command;
        if (request.IncludeChoices && game.State.Frame?.Rules?.Pending == true)
        {
            var rejected = Receipt(envelope, CommandOutcome.Rejected, "Resolve the pending shot decision before requesting legal choices.");
            Remember(envelope, rejected); return Task.FromResult(rejected);
        }
        if (!Enum.IsDefined(request.Mode) || observations is null || request.Source != state.Camera.Scope || !state.Camera.CanControl || state.Camera.Capture != CaptureState.Active
            || request.MediaGeneration <= 0 || !ObservationCurrent(request) || envelope.ExpectedVersion != state.Version || effectPending.Count >= 16)
        {
            var receipt = Receipt(envelope, CommandOutcome.Rejected, "Use the current active live source and calibration before observing.");
            Remember(envelope, receipt); return Task.FromResult(receipt);
        }
        var completion = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (request.Mode == ObservationMode.VerifyForAdvice && effectPending.Values.Any(p => p.Envelope.Command is ObserveTable))
        {
            var busy = Receipt(envelope, CommandOutcome.Rejected, "A table observation is already running. Cancel it or wait for its result.");
            Remember(envelope, busy); return Task.FromResult(busy);
        }
        effectPending.Add(envelope.OperationId, (envelope, completion.Task));
        if (request.Mode == ObservationMode.VerifyForAdvice)
        {
            Volatile.Write(ref state, ClearShotCatalog(state) with { Observation = null, Version = checked(state.Version + 1) }); Notify(state);
        }
        _ = CompleteObservationAsync(envelope, request, completion, cancellation, game.State.Match?.Id, game.State.Frame?.Id, game.State.Version, state.Advice.TargetGroup);
        return completion.Task;
    }
    async Task CompleteObservationAsync(CommandEnvelope envelope, ObserveTable request, TaskCompletionSource<CommandReceipt> completion, CancellationToken cancellation,
        string? admittedMatch, string? admittedFrame, long admittedGameVersion, AdviceTargetGroup admittedGroup)
    {
        TableObservation? result = null;
        string? error = null;
        long resultAt = sceneClock.GetTimestamp();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(TimeSpan.FromSeconds(10));
        try { result = await observations!.ExecuteAsync(request, envelope.OperationId, bounded.Token).WaitAsync(bounded.Token).ConfigureAwait(false); resultAt = sceneClock.GetTimestamp(); }
        catch (OperationCanceledException) { error = "Observation cancelled or timed out. Request again when useful."; }
        catch (Exception) { error = "A fresh table frame is unavailable. Request again when useful."; }
        await serial.WaitAsync().ConfigureAwait(false);
        try
        {
            CommandReceipt receipt;
            if (request.Mode == ObservationMode.VerifyForAdvice && result is not null)
            {
                var publicationDelay = sceneClock.GetElapsedTime(resultAt).TotalMilliseconds;
                result = result with { FrameAgeMs = result.FrameAgeMs + publicationDelay, DurationMs = result.DurationMs + publicationDelay,
                    DetectionEvidence = result.DetectionEvidence is { } support ? support with { AgeMs = support.AgeMs + publicationDelay } : null };
                if (result.Outcome == ObservationOutcome.Ready && result.FrameAgeMs > ObservationPolicy.MaximumFinalAgeMs)
                    result = result with { Outcome = ObservationOutcome.Unclear, Reason = ObservationReason.FrameTooOld, Quality = LayoutQuality.Unknown, Choices = ShotChoiceCatalog.Empty, GameEvidence = null };
            }
            if (bounded.IsCancellationRequested || !ObservationCurrent(request) || state.Camera.Scope != request.Source || !state.Camera.CanControl || state.Camera.Capture != CaptureState.Active
                || request.Mode == ObservationMode.VerifyForAdvice && (state.Advice.TargetGroup != admittedGroup || game.State.Version != admittedGameVersion))
            { PipelineDiagnostics.Count("discardedObservations"); receipt = Receipt(envelope, CommandOutcome.Stale, error ?? "The source changed during observation."); }
            else if (result is null || result.Source != request.Source || result.AnalysisGeneration != request.AnalysisGeneration || result.MediaGeneration != request.MediaGeneration
                || !double.IsFinite(result.DurationMs) || result.DurationMs < 0 || !double.IsFinite(result.FrameAgeMs) || result.FrameAgeMs is < -2000 or > 6000)
                receipt = Receipt(envelope, CommandOutcome.Failed, error ?? "The displayed frame is too old for current advice.");
            else if (request.Mode == ObservationMode.VerifyForAdvice && result.Outcome == ObservationOutcome.Ready && !HasVerificationProof(result, envelope.OperationId, admittedGroup))
                receipt = Receipt(envelope, CommandOutcome.Failed, "The table check did not provide complete current verification evidence.");
            else
            {
                var next = state;
                if (result.Choices is { } catalog)
                {
                    var published = SceneCommand(new(envelope.OperationId, CommandOrigin.System, state.Version, new PublishShotCatalog(catalog)));
                    if (published is not null) next = published;
                }
                if (game.State.Match?.Id == admittedMatch && game.State.Frame?.Id == admittedFrame && (request.Mode == ObservationMode.Single || result.Outcome == ObservationOutcome.Ready))
                    next = AcceptGameObservation(next, result, envelope.OperationId, request.Mode == ObservationMode.VerifyForAdvice);
                Volatile.Write(ref state, next with { Observation = result, Version = checked(state.Version + 1) });
                Notify(state);
                receipt = Receipt(envelope, CommandOutcome.Applied, result.Outcome switch
                { ObservationOutcome.Ready => "Table observed.", ObservationOutcome.Unclear => "Table unclear. Review the frame before requesting advice.", _ => "Table observed; settlement is not established. Request another observation when the balls are still." }) with { Observation = result };
                if (request.Mode == ObservationMode.VerifyForAdvice) receipt = receipt with { Message = VerificationMessage(result) };
            }
            if (request.Mode == ObservationMode.VerifyForAdvice && result is not null)
                Volatile.Write(ref latestVerificationReport, HistoricalVerificationReport.From(result,
                    sceneClock.GetUtcNow(), receipt.Observation is not null, receipt.Outcome));
            effectPending.Remove(envelope.OperationId); Remember(envelope, receipt); completion.TrySetResult(receipt);
        }
        finally { serial.Release(); }
    }
    public static bool HasVerificationProof(TableObservation result, Guid operationId, AdviceTargetGroup group)
    {
        var evidence = result.Verification;
        if (evidence is null || evidence.OperationId != operationId || evidence.MediaGeneration != result.MediaGeneration
            || evidence.Configuration != ObservationPolicy.VerificationConfiguration || string.IsNullOrWhiteSpace(evidence.CalibrationId)
            || evidence.TargetGroup != group.ToString().ToLowerInvariant() || result.Motion != TableMotion.Still || result.Quality != LayoutQuality.Clear || result.Reason != ObservationReason.None
            || result.FrameAgeMs > ObservationPolicy.MaximumFinalAgeMs || evidence.IndependentFrames is < ObservationPolicy.MinimumVerificationSamples or > ObservationPolicy.MaximumVerificationSamples
            || evidence.Samples is null || evidence.Samples.Length != evidence.IndependentFrames || evidence.StartSourceUs is null || evidence.EndSourceUs is null
            || evidence.EndSourceUs - evidence.StartSourceUs < ObservationPolicy.MinimumStillSeconds * 1000000
            || !double.IsFinite(evidence.MaximumGapMs) || evidence.MaximumGapMs < 0 || evidence.MaximumGapMs > ObservationPolicy.MaximumVerificationGapSeconds * 1000
            || !double.IsFinite(evidence.FinalClockSampleAgeMs) || evidence.FinalClockSampleAgeMs is < 0 or > ObservationPolicy.MaximumClockSampleAgeMs
            || result.DecodedReceipt?.MediaGeneration != result.MediaGeneration || result.DecodedReceipt?.FrameId != evidence.Samples[^1].FrameId
            || result.SourceTimeUs != evidence.EndSourceUs || result.DetectionEvidence is not { } detection || detection.FrameId != evidence.Samples[^1].FrameId
            || detection.SourceTimeUs != evidence.EndSourceUs || !double.IsFinite(detection.AgeMs) || detection.AgeMs is < 0 or > ObservationPolicy.MaximumFinalAgeMs) return false;
        return evidence.Samples[0].SourceTimeUs == evidence.StartSourceUs && evidence.Samples[^1].SourceTimeUs == evidence.EndSourceUs
            && evidence.Samples.All(s => s.Measurement.CoverageValid && double.IsFinite(s.ClockSampleAgeMs) && s.ClockSampleAgeMs is >= 0 and <= ObservationPolicy.MaximumClockSampleAgeMs
                && double.IsFinite(s.ClockUncertaintyMs) && s.ClockUncertaintyMs is >= 0 and <= 250)
            && evidence.Samples.Skip(1).Select((s, i) => s.FrameId > evidence.Samples[i].FrameId && s.SourceTimeUs > evidence.Samples[i].SourceTimeUs
                && s.SourceTimeUs - evidence.Samples[i].SourceTimeUs <= ObservationPolicy.MaximumVerificationGapSeconds * 1000000 && s.Measurement.Motion == TableMotion.Still).All(ok => ok);
    }
    public static string VerificationMessage(ObservationReason reason) => reason switch
    {
        ObservationReason.None => "Clear, still table verified. Shot choices are checked separately against safe geometry.",
        ObservationReason.Moving => "Balls moving. Let the table settle, then retry.",
        ObservationReason.Obstructed => "Table obstructed. Clear the camera view, then retry.",
        ObservationReason.DetectionAmbiguous => "Table detection is unclear. Review the balls or retry.",
        ObservationReason.LightingChanged => "Lighting changed. Let exposure settle, then retry.",
        ObservationReason.CalibrationChanged => "Camera/calibration changed. Review the table calibration.",
        ObservationReason.FrameTooOld or ObservationReason.ClockUntrusted => "Live frame too old or its clock is uncertain. Resume live video and retry.",
        ObservationReason.SourceChanged or ObservationReason.SampleGap => "Live source interrupted. Resume live video and retry.",
        ObservationReason.TimedOut => "Table check timed out. Check the live view, then retry.",
        ObservationReason.FrameUnavailable => "Live frame unavailable. Resume live video and retry.",
        ObservationReason.AnalysisFailed => "Table analysis failed. Retry or review the table.",
        ObservationReason.CaptureFailed => "Diagnostic capture failed. Disable capture and retry.",
        _ => "Stillness could not be verified. Check the live view or review the table."
    };

    public static string VerificationMessage(TableObservation observation)
    {
        if (!HasFreshStillSamples(observation.Motion, observation.Outcome, observation.Quality, observation.Reason,
                observation.FrameAgeMs, observation.Verification?.FinalClockSampleAgeMs ?? double.NaN,
                observation.Verification?.IndependentFrames ?? 0,
                observation.Verification?.StartSourceUs, observation.Verification?.EndSourceUs,
                observation.Verification?.CandidateSpanStartSampleIndex ?? 0,
                observation.Verification?.CandidateSpanIndependentFrames ?? 0,
                observation.Verification?.Samples.Select(sample =>
                    (sample.SourceTimeUs, sample.AgeMs, sample.ClockSampleAgeMs, sample.ClockUncertaintyMs,
                        sample.Measurement.Motion, sample.Measurement.CoverageValid,
                        sample.Measurement.Vision?.Obstructed == true)).ToArray() ?? []))
            return VerificationMessage(observation.Reason);

        return StillButDetectionFailedMessage(observation.Verification?.ConsensusConflict);
    }

    public static string VerificationMessage(HistoricalVerificationReport report)
    {
        if (!HasFreshStillSamples(report.Motion, report.Outcome, report.Quality, report.Reason,
                report.FinalFrameAgeMs, report.FinalClockSampleAgeMs, report.IndependentFrames,
                report.StartSourceTimeUs, report.EndSourceTimeUs,
                report.CandidateSpanStartSampleIndex, report.CandidateSpanIndependentFrames,
                report.Samples.Select(sample =>
                    (sample.SourceTimeUs, sample.AgeMs, sample.ClockSampleAgeMs, sample.ClockUncertaintyMs,
                        sample.Motion, sample.CoverageValid, sample.Obstructed == true)).ToArray()))
            return VerificationMessage(report.Reason);

        return StillButDetectionFailedMessage(report.ConsensusConflict);
    }

    static string StillButDetectionFailedMessage(ConsensusConflict? conflict)
    {
        string issue = conflict?.Code switch
        {
            "radius-inconsistent" => "ball boundary radius evidence disagreed",
            "final-quality-or-count" => "the final ball detection was uncertain or its count differed",
            "insufficient-current-support" => "a ball lacked enough current boundary support",
            "unexplained-body" => "detected ball identities or counts did not agree",
            _ => "ball detection did not produce a consistent layout"
        };
        return $"Motion stayed still in the valid samples, but {issue}. No shot choices were returned; review the detections or retry.";
    }

    static bool HasFreshStillSamples(TableMotion motion, ObservationOutcome outcome, LayoutQuality quality,
        ObservationReason reason, double finalFrameAgeMs, double finalClockSampleAgeMs, int independentFrames,
        long? startSourceUs, long? endSourceUs, int candidateStart, int candidateCount,
        IReadOnlyList<(long SourceTimeUs, double AgeMs, double ClockSampleAgeMs, double ClockUncertaintyMs,
            TableMotion Motion, bool CoverageValid, bool Obstructed)> samples)
    {
        if (motion != TableMotion.Still || outcome != ObservationOutcome.Unclear || quality != LayoutQuality.Ambiguous
            || reason != ObservationReason.DetectionAmbiguous
            || !double.IsFinite(finalFrameAgeMs) || finalFrameAgeMs is < -200 or > ObservationPolicy.MaximumFinalAgeMs
            || !double.IsFinite(finalClockSampleAgeMs) || finalClockSampleAgeMs is < 0 or > ObservationPolicy.MaximumClockSampleAgeMs
            || independentFrames is < ObservationPolicy.MinimumVerificationSamples or > ObservationPolicy.MaximumVerificationSamples
            || samples.Count != independentFrames || candidateStart < 0 || candidateCount < ObservationPolicy.MinimumVerificationSamples
            || candidateCount > ObservationPolicy.MaximumVerificationSamples || candidateStart + candidateCount != samples.Count
            || startSourceUs is null || endSourceUs is null
            || samples.Count == 0 || samples[0].SourceTimeUs != startSourceUs || samples[^1].SourceTimeUs != endSourceUs)
            return false;

        var span = samples.Skip(candidateStart).Take(candidateCount).ToArray();
        if (span.Length != candidateCount || span[^1].SourceTimeUs - span[0].SourceTimeUs
            < ObservationPolicy.MinimumStillSeconds * 1_000_000) return false;
        for (int i = 0; i < span.Length; i++)
        {
            var sample = span[i];
            bool motionValid = i == 0
                ? sample.Motion is TableMotion.Unknown or TableMotion.Still
                : sample.Motion == TableMotion.Still;
            if (!sample.CoverageValid || !motionValid || sample.Obstructed
                || !double.IsFinite(sample.AgeMs) || sample.AgeMs is < -200 or > ObservationPolicy.MaximumFinalAgeMs
                || !double.IsFinite(sample.ClockSampleAgeMs) || sample.ClockSampleAgeMs is < 0 or > ObservationPolicy.MaximumClockSampleAgeMs
                || !double.IsFinite(sample.ClockUncertaintyMs) || sample.ClockUncertaintyMs is < 0 or > 250)
                return false;
            if (i > 0 && (sample.SourceTimeUs <= span[i - 1].SourceTimeUs
                || sample.SourceTimeUs - span[i - 1].SourceTimeUs > ObservationPolicy.MaximumVerificationGapSeconds * 1_000_000))
                return false;
        }
        return true;
    }
}
