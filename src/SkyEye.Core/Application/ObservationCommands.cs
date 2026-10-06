using SkyEye.Core.Analysis;
using System.Collections.Immutable;

namespace SkyEye.Core.Application;

public sealed record ObserveTable(CameraScope Source, long AnalysisGeneration, long MediaGeneration, bool ReviewFrame = false, bool IncludeChoices = false) : SkyEyeCommand;
public enum ObservationOutcome { Ready, Unsettled, Unclear }
public sealed record TableObservation(CameraScope Source, long AnalysisGeneration, long MediaGeneration, string Timeline,
    DateTimeOffset CapturedAt, long? SourceTimeUs, double FrameAgeMs, double AcquisitionMs, double VisionMs, double DurationMs,
    long TableVersion, ObservationOutcome Outcome, string FrameSource)
{
    public ShotChoiceCatalog? Choices { get; init; }
    public GameObservationEvidence? GameEvidence { get; init; }
    public DecodedFrameReceipt? DecodedReceipt { get; init; }
}
public interface ITableObservationEffect
{
    bool IsCurrent(ObserveTable request);
    Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation);
}

/// <summary>Coalesces identical active observations in TableAnalysisSession. Never polls or retries.</summary>
public sealed class TableObservationEffect(Func<TableAnalysisSession> session, Func<long> mediaGeneration, Func<bool> live) : ITableObservationEffect
{
    public bool IsCurrent(ObserveTable request) => live() && request.MediaGeneration == mediaGeneration()
        && request.Source == session().State.Source && request.AnalysisGeneration == session().State.Generation;
    public async Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation)
    {
        if (!IsCurrent(request) || session().State.Calibration is null) throw new InvalidOperationException("Calibrate an active live table before observing it.");
        await session().UpdateAsync(cancellation, request.ReviewFrame).WaitAsync(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        var snapshot = session().State;
        if (!IsCurrent(request) || snapshot.Observation is not { } observed || snapshot.Table is not { } layout
            || layout.Version != observed.TableVersion || layout.CapturedAt != observed.CapturedAt
            || observed.DecodedReceipt is { } decoded && decoded.MediaGeneration != request.MediaGeneration)
            throw new InvalidOperationException("The source changed. Request a fresh observation.");
        observed = observed with { GameEvidence = new(new(snapshot.CameraIdentity!, observed.Timeline, request.AnalysisGeneration + 1, snapshot.CalibrationId, request.Source.ConnectionId.ToString("N")),
            layout.Balls.Select(b => new SkyEye.GameState.ObservedBall(b.Id, Enum.TryParse<SkyEye.GameState.BallColour>(b.Kind, true, out var colour) ? colour : SkyEye.GameState.BallColour.Unknown,
                new(b.Position.X / 2, b.Position.Y), b.Confidence)).ToImmutableArray()) };
        if (request.IncludeChoices)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var shots = observed.Outcome == ObservationOutcome.Ready ? session().ComputeChoices(request.AnalysisGeneration, observed.TableVersion) : [];
            var table = session().State.Table!;
            var group = Enum.Parse<AdviceTargetGroup>(table.Group, ignoreCase: true);
            observed = observed with { Choices = new(request.Source, table.Version, group, table.CapturedAt, table.CapturedAt.AddSeconds(15),
                shots.Select(s => new ShotChoice(s.Id, s.Type, s.Pocket)).ToImmutableArray()), DurationMs = observed.DurationMs + timer.Elapsed.TotalMilliseconds };
        }
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
        if (observations is null || request.Source != state.Camera.Scope || !state.Camera.CanControl || state.Camera.Capture != CaptureState.Active
            || request.MediaGeneration <= 0 || !ObservationCurrent(request) || envelope.ExpectedVersion != state.Version || effectPending.Count >= 16)
        {
            var receipt = Receipt(envelope, CommandOutcome.Rejected, "Use the current active live source and calibration before observing.");
            Remember(envelope, receipt); return Task.FromResult(receipt);
        }
        var completion = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        effectPending.Add(envelope.OperationId, (envelope, completion.Task));
        _ = CompleteObservationAsync(envelope, request, completion, cancellation, game.State.Match?.Id, game.State.Frame?.Id);
        return completion.Task;
    }
    async Task CompleteObservationAsync(CommandEnvelope envelope, ObserveTable request, TaskCompletionSource<CommandReceipt> completion, CancellationToken cancellation,
        string? admittedMatch, string? admittedFrame)
    {
        TableObservation? result = null;
        string? error = null;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(TimeSpan.FromSeconds(10));
        try { result = await observations!.ExecuteAsync(request, bounded.Token).WaitAsync(bounded.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { error = "Observation cancelled or timed out. Request again when useful."; }
        catch (Exception) { error = "A fresh table frame is unavailable. Request again when useful."; }
        await serial.WaitAsync().ConfigureAwait(false);
        try
        {
            CommandReceipt receipt;
            if (bounded.IsCancellationRequested || !ObservationCurrent(request) || state.Camera.Scope != request.Source)
            { PipelineDiagnostics.Count("discardedObservations"); receipt = Receipt(envelope, CommandOutcome.Stale, error ?? "The source changed during observation."); }
            else if (result is null || result.Source != request.Source || result.AnalysisGeneration != request.AnalysisGeneration || result.MediaGeneration != request.MediaGeneration
                || !double.IsFinite(result.DurationMs) || result.DurationMs < 0 || !double.IsFinite(result.FrameAgeMs) || result.FrameAgeMs is < -2000 or > 6000)
                receipt = Receipt(envelope, CommandOutcome.Failed, error ?? "The displayed frame is too old for current advice.");
            else
            {
                var next = state;
                if (result.Choices is { } catalog)
                {
                    var published = SceneCommand(new(envelope.OperationId, CommandOrigin.System, state.Version, new PublishShotCatalog(catalog)));
                    if (published is not null) next = published;
                }
                if (game.State.Match?.Id == admittedMatch && game.State.Frame?.Id == admittedFrame)
                    next = AcceptGameObservation(next, result, envelope.OperationId);
                Volatile.Write(ref state, next with { Observation = result, Version = checked(state.Version + 1) });
                Notify(state);
                receipt = Receipt(envelope, CommandOutcome.Applied, result.Outcome switch
                { ObservationOutcome.Ready => "Table observed.", ObservationOutcome.Unclear => "Table unclear. Review the frame before requesting advice.", _ => "Table observed; settlement is not established. Request another observation when the balls are still." }) with { Observation = result };
            }
            effectPending.Remove(envelope.OperationId); Remember(envelope, receipt); completion.TrySetResult(receipt);
        }
        finally { serial.Release(); }
    }
}
