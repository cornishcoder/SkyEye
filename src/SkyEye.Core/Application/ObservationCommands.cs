using SkyEye.Core.Analysis;

namespace SkyEye.Core.Application;

public sealed record ObserveTable(CameraScope Source, long AnalysisGeneration, long MediaGeneration) : SkyEyeCommand;
public enum ObservationOutcome { Ready, Unsettled, Unclear }
public sealed record TableObservation(CameraScope Source, long AnalysisGeneration, long MediaGeneration, string Timeline,
    DateTimeOffset CapturedAt, long? SourceTimeUs, double FrameAgeMs, double AcquisitionMs, double VisionMs, double DurationMs,
    long TableVersion, ObservationOutcome Outcome, string FrameSource);
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
        await session().UpdateAsync(cancellation).WaitAsync(cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        if (!IsCurrent(request) || session().State.Observation is not { } observed) throw new InvalidOperationException("The source changed. Request a fresh observation.");
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
        if (observations is null || request.Source != state.Camera.Scope || !state.Camera.CanControl || state.Camera.Capture != CaptureState.Active
            || request.MediaGeneration <= 0 || !ObservationCurrent(request) || envelope.ExpectedVersion != state.Version || effectPending.Count >= 16)
        {
            var receipt = Receipt(envelope, CommandOutcome.Rejected, "Use the current active live source and calibration before observing.");
            Remember(envelope, receipt); return Task.FromResult(receipt);
        }
        var completion = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        effectPending.Add(envelope.OperationId, (envelope, completion.Task));
        _ = CompleteObservationAsync(envelope, request, completion, cancellation);
        return completion.Task;
    }
    async Task CompleteObservationAsync(CommandEnvelope envelope, ObserveTable request, TaskCompletionSource<CommandReceipt> completion, CancellationToken cancellation)
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
                Volatile.Write(ref state, state with { Observation = result, Version = checked(state.Version + 1) });
                Notify(state);
                receipt = Receipt(envelope, CommandOutcome.Applied, result.Outcome switch
                { ObservationOutcome.Ready => "Table observed.", ObservationOutcome.Unclear => "Table unclear. Review the frame before requesting advice.", _ => "Table observed; settlement is not established. Request another observation when the balls are still." }) with { Observation = result };
            }
            effectPending.Remove(envelope.OperationId); Remember(envelope, receipt); completion.TrySetResult(receipt);
        }
        finally { serial.Release(); }
    }
}
