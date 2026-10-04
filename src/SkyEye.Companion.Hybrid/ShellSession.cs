using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Companion.Hybrid.Probes;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion.Hybrid;

/// <summary>Renderable projection. Media addresses, read access and credentials stay native.</summary>
public sealed record ShellViewState(long Version, string Camera, bool Playing)
{
    public ShellViewState() : this(0, "Connect your camera", false) { }
}

public sealed class ShellSession : IDisposable
{
    readonly CameraClient client = new();
    readonly CameraControlEffect effects;
    readonly AppCoordinator app;
    readonly IDisposable subscription;
    readonly ProjectionClient agentClient = new();
    Bridge.PlayerBridge? player;
    Bridge.CastBridge? cast;
    CancellationTokenSource? foreground;
    string? source;
    bool liveSource, active, disposed, playbackNeedsStart;
    long sourceGeneration;
    public UIAgent<ShellViewState> Agent { get; }
    public bool SheetOpen { get; private set; }
    public string Message { get; private set; } = "Your table. Every shot.";
    public string Playback { get; private set; } = "Waiting for your table";
    public bool Playing { get; private set; }
    public bool HasSource => source is not null;
    public event Action? Changed;

    public ShellSession()
    {
        effects = new(client);
        app = new(cameraControls: effects);
        Agent = new(agentClient, _ => { }, new ShellViewState());
        subscription = app.Subscribe(_ => MainThread.BeginInvokeOnMainThread(Publish));
    }
    public void SetSheet(bool open) { SheetOpen = open; Changed?.Invoke(); }
    public void Attach(Bridge.PlayerBridge player, Bridge.CastBridge cast)
    {
        this.player = player; this.cast = cast; player.SetPortrait(true);
        if (active && source is not null && !liveSource) PlayCurrent();
        if (active && liveSource) _ = RefreshAsync(foreground?.Token ?? CancellationToken.None);
    }
    public void Detach(Bridge.PlayerBridge owner)
    {
        if (player != owner) return;
        player = null; cast = null; Playing = false;
    }
    public void ReportPlaying(bool playing)
    {
        Playing = playing; Playback = playing ? "Live table · silent" : "Waiting for your table";
        if (playing && !liveSource) Playback = "Table preview";
        Publish();
    }
    public void ReportError() { Playing = false; Playback = "Viewing interrupted"; Message = "The table view is reconnecting."; Publish(); }

    public Task ConnectAsync(string address) => MainThread.InvokeOnMainThreadAsync(() => ConnectCoreAsync(address));
    async Task ConnectCoreAsync(string address)
    {
        if (!active || foreground is null) return;
        var cancellation = foreground.Token;
        if (await Permissions.RequestAsync<LocalNetworkPermission>() != PermissionStatus.Granted)
        { Message = "Allow nearby devices to connect your camera."; Publish(); return; }
        cancellation.ThrowIfCancellationRequested();
        Stop(); var generation = sourceGeneration;
        Message = "Connecting to your camera…"; Publish();
        var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, new ConnectCamera(address)), cancellation);
        if (cancellation.IsCancellationRequested || generation != sourceGeneration || disposed) return;
        Message = result.Succeeded ? "Camera connected." : result.Message;
        if (result.Succeeded) Preferences.Set("shell-camera", client.Address);
        ApplyCameraView(generation); Publish();
    }
    public string SavedAddress => Preferences.Get("shell-camera", "http://192.168.1.124:8765");
    public Task FixtureAsync() => MainThread.InvokeOnMainThreadAsync(FixtureCoreAsync);
    async Task FixtureCoreAsync()
    {
        if (!active || foreground is null) return;
        var token = foreground.Token; Stop(); var generation = sourceGeneration;
        var path = Path.Combine(FileSystem.CacheDirectory, "skyeye-table-preview.mp4");
        await using (var input = await FileSystem.OpenAppPackageFileAsync("probe.mp4"))
        await using (var output = File.Create(path)) await input.CopyToAsync(output, token);
        if (token.IsCancellationRequested || generation != sourceGeneration || disposed) return;
        source = new Uri(path).AbsoluteUri; liveSource = false; Message = "Preview only · connect for your live table";
        PlayCurrent(); Publish();
    }
    void ApplyCameraView(long generation)
    {
        if (generation != sourceGeneration || !active) return;
        var state = app.State.Camera; var view = effects.Latest;
        if (state.Reachability != CameraReachability.Reachable || view?.Connection != state || state.Capture != CaptureState.Active)
        {
            if (liveSource) { player?.Stop(); source = null; cast?.Configure("", true); }
            Playing = false;
            Playback = state.Reachability == CameraReachability.Unreachable ? "Camera unreachable" : "Waiting for your table";
            return;
        }
        // Repeated telemetry/sheet renders must not reload ExoPlayer.
        if (source != view.Stream || !liveSource || playbackNeedsStart) { source = view.Stream; liveSource = true; PlayCurrent(); }
    }
    void PlayCurrent()
    {
        if (!active || source is null || player is null) return;
        player.SetPortrait(true); player.SetMuted(true); player.Play(source);
        playbackNeedsStart = false;
        cast?.Configure(liveSource ? source : "", true);
    }
    public void Stop()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(Stop); return; }
        ++sourceGeneration; source = null; liveSource = false; player?.Stop(); cast?.Configure("", true);
        Playing = false; Playback = "Waiting for your table"; Publish();
    }
    public void Suspend()
    {
        active = false; foreground?.Cancel(); foreground?.Dispose(); foreground = null;
        player?.Stop(); Playing = false; playbackNeedsStart = true; Publish();
    }
    public void Resume()
    {
        if (active || disposed) return;
        active = true; foreground = new();
        if (source is not null && !liveSource) PlayCurrent();
        _ = PollAsync(foreground.Token);
    }
    async Task PollAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            do { await RefreshAsync(token); } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) { }
    }
    async Task RefreshAsync(CancellationToken token)
    {
        var state = app.State; if (state.Camera.ConnectionId == Guid.Empty || state.Camera.Reachability == CameraReachability.Connecting) return;
        var generation = sourceGeneration;
        try
        {
            await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.System, state.Version, new RefreshCamera(state.Camera.ConnectionId)), token);
            if (token.IsCancellationRequested || disposed) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Only a live selection may resume automatically; stopping/previewing overrides a late refresh.
                if (generation == sourceGeneration && liveSource) ApplyCameraView(generation);
                Publish();
            });
        }
        catch (OperationCanceledException) { }
    }
    void Publish()
    {
        if (disposed) return;
        var state = app.State.Camera;
        var camera = state.Reachability switch
        {
            CameraReachability.Connecting => "Connecting…",
            CameraReachability.Unreachable => "Camera unreachable",
            CameraReachability.Reachable when state.Capture == CaptureState.Active => "Camera active",
            CameraReachability.Reachable => "Camera ready",
            _ => "Connect your camera"
        };
        Agent.State.Value = new(app.State.Version, camera, Playing); Changed?.Invoke();
    }
    public void Dispose()
    {
        if (disposed) return;
        Suspend(); disposed = true; subscription.Dispose(); client.Dispose(); Agent.Dispose(); agentClient.Dispose();
    }
    // H01 only projects canonical state. Voice/tool transport is connected in V03.
    sealed class ProjectionClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Voice is not connected in this shell yet.");
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Voice is not connected in this shell yet.");
        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
        public void Dispose() { }
    }
}
