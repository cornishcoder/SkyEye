namespace SkyEye.Core.Application;

/// <summary>Single serialized writer for preferences and effect admission. Long
/// saves run outside the preference writer and retain their own durable outcomes.</summary>
public sealed partial class AppCoordinator
{
    readonly SemaphoreSlim serial = new(1);
    readonly object subscriptionGate = new();
    readonly Dictionary<long, Action<SkyEyeState>> subscribers = [];
    readonly Dictionary<Guid, (CommandEnvelope Envelope, CommandReceipt Receipt)> recent = [];
    readonly Queue<Guid> order = [];
    readonly Action<Exception>? observerError;
    readonly ClipSaveExecutor? clipSaves;
    readonly TimeProvider sceneClock;
    long nextSubscription;
    SkyEyeState state = SkyEyeState.Empty;
    public AppCoordinator(Action<Exception>? observerError = null, ClipSaveExecutor? clipSaves = null, ICameraControlEffect? cameraControls = null, TimeProvider? clock = null)
    { this.observerError = observerError; this.clipSaves = clipSaves; this.cameraControls = cameraControls; sceneClock = clock ?? TimeProvider.System; }
    public SkyEyeState State => Volatile.Read(ref state);

    // Subscribe first, then read State for initial rendering. Observers must return
    // promptly and post further commands/UI work; synchronous reentrant dispatch blocks.
    public IDisposable Subscribe(Action<SkyEyeState> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        long id;
        lock (subscriptionGate) subscribers.Add(id = ++nextSubscription, observer);
        return new Subscription(() => { lock (subscriptionGate) subscribers.Remove(id); });
    }

    public async Task<CommandReceipt> DispatchAsync(CommandEnvelope envelope, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        Task<CommandReceipt>? pendingEffect = null;
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (envelope.OperationId == Guid.Empty || envelope.ExpectedVersion < 0 || !Enum.IsDefined(envelope.Origin) || envelope.Command is null)
                return Receipt(envelope, CommandOutcome.Rejected, "Invalid command envelope.");
            if (effectPending.TryGetValue(envelope.OperationId, out var running))
                pendingEffect = running.Envelope == envelope ? DuplicateAsync(running.Pending)
                    : Task.FromResult(Receipt(envelope, CommandOutcome.Conflict, "Operation ID was already used for a different request."));
            else if (recent.TryGetValue(envelope.OperationId, out var previous))
                return previous.Envelope == envelope ? previous.Receipt with { Duplicate = true }
                    : Receipt(envelope, CommandOutcome.Conflict, "Operation ID was already used for a different request.");
            else if (envelope.Command is CameraCommand)
            {
                var durable = clipSaves?.FindPrevious(envelope, state.Version);
                if (durable is not null) return durable;
                pendingEffect = BeginCameraCommand(envelope, cancellation);
            }
            else if (envelope.Command is SaveClip)
            {
                if (clipSaves is null) return Receipt(envelope, CommandOutcome.Rejected, "Clip saving is unavailable.");
                pendingEffect = BeginSaveCommand(envelope, cancellation);
            }
            else
            {
                var durable = clipSaves?.FindPrevious(envelope, state.Version);
                if (durable is not null) return durable;
                CommandReceipt result;
                if (envelope.ExpectedVersion != state.Version)
                    result = Receipt(envelope, CommandOutcome.Stale, "The app changed. Use its current state before trying again.");
                else
                {
                    SkyEyeState? updated = envelope.Command switch
                    {
                        ShowShotChoices show => state with { Presentation = new(show.Enabled) },
                        SetTargetGroup group when Enum.IsDefined(group.Group) => group.Group == state.Advice.TargetGroup ? state : ClearShotCatalog(state with { Advice = new(group.Group) }),
                        _ => SceneCommand(envelope)
                    };
                    if (updated is null) result = Receipt(envelope, CommandOutcome.Rejected, envelope.Command is PinShot ? "That shot is no longer current. Check the latest choices." : envelope.Command is SetShotDisplay { Mode: ShotDisplayMode.Score } ? "Start a supported match before showing its score." : "This command is not supported.");
                    else if (updated == state) result = Receipt(envelope, CommandOutcome.Unchanged, "Already set.");
                    else
                    {
                        Volatile.Write(ref state, updated with { Version = checked(state.Version + 1) });
                        result = Receipt(envelope, CommandOutcome.Applied, "Updated.");
                        Notify(state);
                    }
                }
                // Bounded in-memory replay protection for preference commands only.
                Remember(envelope, result);
                return result;
            }
        }
        finally { serial.Release(); }
        return await pendingEffect!.ConfigureAwait(false);
    }
    CommandReceipt Receipt(CommandEnvelope envelope, CommandOutcome outcome, string message)
        => new(envelope.OperationId, outcome, state.Version, message);
    void Remember(CommandEnvelope envelope, CommandReceipt result)
    {
        recent.Add(envelope.OperationId, (envelope, result)); order.Enqueue(envelope.OperationId);
        while (order.Count > 256) recent.Remove(order.Dequeue());
    }
    void Notify(SkyEyeState snapshot)
    {
        Action<SkyEyeState>[] listeners;
        lock (subscriptionGate) listeners = subscribers.Values.ToArray();
        foreach (var observer in listeners)
            try { observer(snapshot); }
            catch (Exception e) { try { observerError?.Invoke(e); } catch { /* Reporting cannot roll back a commit. */ } }
    }
    sealed class Subscription(Action remove) : IDisposable
    {
        Action? unsubscribe = remove;
        public void Dispose() => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
    }
}
