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
                if (request.Mode == ObservationMode.VerifyForAdvice) receipt = receipt with { Message = VerificationMessage(result.Reason) };
            }
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
        ObservationReason.DetectionAmbiguous => "Table still; detection unclear. Review the balls or retry.",
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
}
