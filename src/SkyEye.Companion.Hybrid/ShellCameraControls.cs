using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Companion.Hybrid.Probes;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion.Hybrid;

public sealed record CameraChoice(string Name, string Address);
public sealed record CameraControlRequest(long Version, CameraCommand Command);

public sealed partial class ShellSession
{
    Bridge.DiscoveryBridge? discovery;
    DiscoveryListener? discoveryListener;
    long discoveryGeneration;
    CameraChoice[] discoveries = [];
    public IReadOnlyList<CameraChoice> Cameras => discoveries;
    public bool FindingCamera { get; private set; }
    public string TvStatus { get; private set; } = "Choose your TV to watch together";
    public bool TvConnected { get; private set; }
    public bool CanCastLive => app.State.Camera is { Reachability: CameraReachability.Reachable, Capture: CaptureState.Active }
        && effects.Latest?.Connection == app.State.Camera;
    public string HostName => Android.OS.Build.Model ?? "SkyEye companion";

    public Task ControlCameraAsync(CameraControlRequest request) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || request.Command is not (ClaimCameraHost or ReleaseCameraHost or ChangeCapture)) return;
        var cancellation = foreground.Token;
        DismissNotice(); Message = "Requesting camera control…"; Publish();
        var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, request.Version, request.Command), cancellation);
        if (cancellation.IsCancellationRequested || disposed) return;
        Message = result.Message; chrome.SetAttention(!result.Succeeded);
        ApplyCameraView(selection.Generation); Publish();
    });

    public Task StartLiveSessionAsync() => StartSessionAsync(dark: false);
    public Task StartDarkSessionAsync() => StartSessionAsync(dark: true);

    Task StartSessionAsync(bool dark) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || foreground is null) return;
        if (sessionMode != TableSessionMode.None)
        { ShowError("End the current session before starting another."); return; }

        DismissNotice();
        Message = dark ? "Starting a dark recording session…" : "Starting your live session…";
        Publish();
        await HostLiveSessionFromVoiceAsync(Guid.NewGuid().ToString("N"), foreground.Token);
        var camera = app.State.Camera;
        if (camera is not { CanControl: true, Capture: CaptureState.Active })
        {
            ShowError("The camera is not ready to record. Check its connection and host status in Settings.");
            return;
        }

        sessionMode = dark ? TableSessionMode.Dark : TableSessionMode.Live;
        if (dark)
        {
            Stop();
            Playback = "Dark session · recording · physics unavailable";
            Message = "Dark session recording. Sky can save clips and manage the match; table physics is unavailable.";
            Publish();
            return;
        }

        await WatchLiveAsync(foreground.Token);
        if (!HasSource)
        {
            sessionMode = TableSessionMode.None;
            ShowError("Recording started, but the live camera view is unavailable. Check the connection in Settings.");
            return;
        }
        Message = "Live session ready. Table analysis is available.";
        Publish();
    });

    public Task EndSessionAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || foreground is null || sessionMode == TableSessionMode.None) return;
        var camera = app.State.Camera;
        if (camera.Capture is CaptureState.Active or CaptureState.Paused)
        {
            if (!camera.CanControl || camera.Scope is not { } scope)
            { ShowError("Host control was lost. The camera may still be recording; reconnect as host to stop it."); return; }
            var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
                new ChangeCapture(scope, CaptureAction.Stop)), foreground.Token);
            if (!result.Succeeded) { ShowError(result.Message); return; }
        }
        sessionMode = TableSessionMode.None;
        Stop();
        Playback = "Session ended";
        Message = "Session ended. Camera recording stopped.";
        Publish();
    });

    public Task WatchLiveAsync() => WatchLiveAsync(CancellationToken.None);
    public Task WatchLiveAsync(CancellationToken operationCancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, operationCancellation);
        var token = linked.Token;
        sessionMode = TableSessionMode.Live;
        Stop(); selection.Begin(ViewingSource.Live);
        await RefreshAsync(token);
        if (active && !HasSource) ShowError("Start recording on the camera before watching live.");
    });

    public Task FindCamerasAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null) return;
        var token = foreground.Token;
        if (await Permissions.RequestAsync<LocalNetworkPermission>() != PermissionStatus.Granted)
        { discoveries = []; ShowError("Allow nearby devices to find your camera."); return; }
        token.ThrowIfCancellationRequested(); StopDiscovery(); discoveries = [];
        var generation = ++discoveryGeneration;
        discoveryListener = new(this, generation);
        discovery = new(Android.App.Application.Context, discoveryListener);
        FindingCamera = true; discovery.Start(); Message = "Looking for cameras nearby…"; Publish();
        _ = EndDiscoveryAsync(generation, token);
    });
    public async Task<IReadOnlyList<CameraChoice>> FindCamerasFromVoiceAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        await FindCamerasAsync();
        try
        {
            for (int attempt = 0; attempt < 45; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                var snapshot = await MainThread.InvokeOnMainThreadAsync(() => (FindingCamera, Cameras.ToArray()));
                if (!snapshot.FindingCamera) return snapshot.Item2;
                await Task.Delay(250, cancellation);
            }
            await MainThread.InvokeOnMainThreadAsync(StopDiscovery);
            return Cameras.ToArray();
        }
        catch (OperationCanceledException)
        {
            await MainThread.InvokeOnMainThreadAsync(() => { if (FindingCamera) StopDiscovery(); });
            throw;
        }
    }
    async Task EndDiscoveryAsync(long generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation != discoveryGeneration) return;
                StopDiscovery(); Message = discoveries.Length > 0 ? "Choose your camera." : "No camera found. Enter its address."; Publish();
            });
        }
        catch (OperationCanceledException) { }
    }
    void StopDiscovery()
    {
        ++discoveryGeneration; FindingCamera = false;
        discovery?.Stop(); discovery?.Dispose(); discovery = null;
        discoveryListener?.Dispose(); discoveryListener = null;
    }
    sealed class DiscoveryListener(ShellSession owner, long generation) : Java.Lang.Object, Bridge.DiscoveryBridge.IListener
    {
        public void OnCamera(string? name, string? address)
        {
            if (!owner.active || owner.disposed || owner.discoveryGeneration != generation || address is null) return;
            try
            {
                var endpoint = CameraEndpoint.Parse(address);
                if (owner.discoveries.Any(c => c.Address == endpoint) || owner.discoveries.Length >= 8) return;
                var label = name is { Length: > 0 } ? name[..Math.Min(80, name.Length)] : "SkyEye camera";
                owner.discoveries = [..owner.discoveries, new(label, endpoint)]; owner.Publish();
            }
            catch (ArgumentException) { }
        }
        public void OnError(string? error)
        {
            if (!owner.active || owner.disposed || owner.discoveryGeneration != generation) return;
            owner.FindingCamera = false; owner.ShowError("Discovery unavailable. Enter your camera address.");
        }
    }
    public void ReportCast(string? status)
    {
        if (status == "TV connected") TvConnected = true;
        if (status == "TV disconnected") { TvConnected = false; tvReplay.Clear(); tvLive.Clear(); }
        if (tvReplay.Selected && status is "TV playing live camera" or "TV playing shared table scene" or "TV playing candidate overlay") return;
        if (!TvReplaySelected && status is "TV playing live camera" or "TV playing shared table scene" or "TV playing candidate overlay")
            Message = "Live table on TV · silent.";
        TvStatus = status switch { "Casting silently in landscape" => "TV accepted the live playback request",
            "TV playing saved clip" when TvReplayKind == "recent replay" => "TV playing recent replay",
            "TV playing shared table scene" => AutomaticPocketsCurrent && FrameTableRequested ? "TV playing rectangular table" : "TV playing table overlay", _ => status ?? "TV status changed" };
        Publish();
    }
    bool ObserveCast()
    {
        if (cast is null) { TvConnected = false; return false; }
        try
        {
            using var metrics = JsonDocument.Parse(cast.Metrics()!);
            TvConnected = metrics.RootElement.TryGetProperty("connected", out var value) && value.GetBoolean();
            if (!TvConnected && (tvReplay.Selected || tvLive.Request is not null)) { tvReplay.Clear(); tvLive.Clear(); return true; }
            return ObserveTvReplay(metrics.RootElement) | ObserveTvLive(metrics.RootElement);
        }
        catch { TvConnected = false; }
        return false;
    }
    public Task ChooseTvAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    { await ChooseTvCoreAsync(CancellationToken.None); });
    public Task<bool> ChooseTvFromVoiceAsync(CancellationToken cancellation) =>
        MainThread.InvokeOnMainThreadAsync(() => ChooseTvCoreAsync(cancellation));
    async Task<bool> ChooseTvCoreAsync(CancellationToken operationCancellation)
    {
        if (!active || disposed || foreground is null) return false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, operationCancellation);
        var token = linked.Token;
        if (await Permissions.RequestAsync<LocalNetworkPermission>() != PermissionStatus.Granted)
        { ShowError("Allow nearby devices to choose your TV."); return false; }
        token.ThrowIfCancellationRequested();
        if (!active || disposed || cast?.Button is null) { ShowError("Chromecast is unavailable on this phone."); return false; }
        RevealControls(); cast.Button.PerformClick();
        Message = "Choose a Chromecast in Android's Cast picker."; Publish();
        return true;
    }
    public void PlayTv()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(PlayTv); return; }
        if (!active || cast is null) return;
        ObserveCast();
        if (!CanCastLive || effects.Latest is not { } view) { ShowError("An active, reachable camera is needed for TV viewing."); return; }
        if (!TvConnected) { TvStatus = "Choose a TV before playing live"; Publish(); return; }
        tvReplay.Clear();
        correctedClips.ClearServing();
        var compose = TvCorrectionRequested || app.State.Presentation.Tv.Mode is SkyEye.Core.Application.ShotDisplayMode.Cycling or SkyEye.Core.Application.ShotDisplayMode.Pinned;
        EnsureBackgroundTv();
        var liveSource=sceneRelay is { Ready:true }?sceneRelay.Url:BackgroundTvUrl.Length > 0?BackgroundTvUrl:!compose?view.Stream:null;
        if(liveSource is null){TvStatus="Preparing the live table feed…";EnsureShotLoop();Publish();return;}
        var request = tvLive.Begin(app.State.Camera.ConnectionId, DateTimeOffset.UtcNow);
        Message = "Sending the live table to TV…";
        cast.PlayLive(liveSource, request); TvStatus = "Sending the live table to TV…"; Publish();
    }
    public void StopTv()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(StopTv); return; }
        if (!active) return;
        StopBackgroundTv(); tvReplay.Clear(); tvLive.Clear(); cast?.Stop(); TvStatus = "Disconnecting TV…"; Publish();
    }
}
