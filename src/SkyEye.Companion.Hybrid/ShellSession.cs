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
    public CameraConnection Connection { get; init; } = CameraConnection.Empty;
}

public enum TableSessionMode { None, Live, Dark }

public sealed partial class ShellSession : IDisposable
{
    readonly CameraClient client = new();
    readonly CameraControlEffect effects;
    readonly AppCoordinator app;
    readonly IDisposable subscription;
    readonly SkyVoiceProjectionClient agentClient = new();
    Bridge.PlayerBridge? player;
    Bridge.CastBridge? cast;
    CancellationTokenSource? foreground;
    readonly PlaybackSelection selection = new();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    readonly ControlChrome chrome;
    bool active, disposed;
    TableSessionMode sessionMode;
    Guid? dashboardAttemptedConnection;
    int dashboardStartPending;
    public UIAgent<ShellViewState> Agent { get; }
    public bool SheetOpen { get; private set; }
    public TableSessionMode SessionMode => sessionMode;
    public bool SessionActive => sessionMode != TableSessionMode.None;
    public string UiScreen { get; private set; } = "home";
    public bool MenuPanelPinned => chrome.Pinned;
    public bool PhysicsAvailable => CanAnalyzeTable && HasTableCalibration;
    public string PhysicsStatus => sessionMode == TableSessionMode.Dark ? "Unavailable · dark session has no image analysis"
        : !CanAnalyzeTable ? sessionMode == TableSessionMode.Live
            ? selection.Kind != ViewingSource.Live ? "Unavailable · open the live camera view to enable image analysis"
                : "Unavailable · active host capture is required for image analysis"
            : "Unavailable · start a live session to enable image analysis"
        : !HasTableCalibration ? "Live image analysis is on · calibrate the table for shot choices"
        : "Available · fresh, settled table evidence is still required";
    public bool ControlsVisible => chrome.Visible;
    public bool NeedsAttention => chrome.Attention;
    public double FontScale { get; private set; } = 1;
    public int? CameraBatteryPercent
    {
        get
        {
            var state = app.State.Camera; var view = effects.Latest;
            if (state.Reachability != CameraReachability.Reachable || view?.Connection != state
                || view.StatusReceiptTimestamp == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(view.StatusReceiptTimestamp) > TimeSpan.FromSeconds(15)
                || view.Status.ValueKind != System.Text.Json.JsonValueKind.Object
                || !view.Status.TryGetProperty("battery", out var battery) || battery.ValueKind != System.Text.Json.JsonValueKind.Number
                || !battery.TryGetInt32(out var percent) || percent is < 0 or > 100) return null;
            return percent;
        }
    }
    public double DockHeight => (ControlsVisible ? NeedsAttention ? 360 : 320 : 80) * FontScale;
    public string Message { get; private set; } = "Your table. Every shot.";
    public string Playback { get; private set; } = "Waiting for your table";
    public bool Playing { get; private set; }
    public bool HasSource => selection.Source is not null;
    public event Action? Changed;
    public event Action<string>? NavigationRequested;

    public Task<bool> NavigateFromControlAsync(string screen) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var normalized = (screen ?? "").Trim().ToLowerInvariant();
        if (normalized is not ("home" or "settings" or "clips" or "match")) throw new ArgumentException("Choose home, settings, clips, or match.");
        if (NavigationRequested is null) return false;
        NavigationRequested.Invoke(normalized);
        return true;
    });

    public void SetUiScreen(string screen)
    {
        if (screen is "home" or "settings" or "clips" or "match") UiScreen = screen;
    }

    public ShellSession()
    {
        chrome = new(() => clock.Elapsed);
        var settings = Android.App.Application.Context.GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!;
        chrome.SetPinned(settings.GetBoolean("menu-panel-pinned", true));
        try
        {
            var savedCamera = settings.GetString("camera", "");
            if (!string.IsNullOrWhiteSpace(savedCamera)) { client.Connect(savedCamera); client.HostToken = settings.GetString("host", null); }
        }
        catch (ArgumentException) { settings.Edit()!.Remove("host")!.Apply(); }
        effects = new(client, token =>
        {
            var edit = settings.Edit()!;
            if (token is null) edit.Remove("host"); else edit.PutString("host", token);
            edit.Apply();
        });
        var savedPhoneCorrection = settings.GetBoolean("phone-corrected", true);
        var saveOperations = new ClipSaveExecutor(Path.Combine(FileSystem.AppDataDirectory, "save-operations"),
            new CameraClipSaveEffect(client, clip => { try { annotations.Preserve(clip); } catch (Exception) { } }));
        app = new(clipSaves: saveOperations, cameraControls: effects,
            observations: new TableObservationEffect(() => table, () => player is null ? 0 : MediaGeneration(), () => active && sessionMode == TableSessionMode.Live && selection.Kind == ViewingSource.Live),
            gameJournal: new GameJournalStore(Path.Combine(FileSystem.AppDataDirectory, "game-state")), phoneCorrected: savedPhoneCorrection);
        dashboardScenario = new DashboardScenarioWorkspace(Path.Combine(FileSystem.AppDataDirectory, "vision-dashboard-scenarios"));
        InitializeAnnotations();
        InitializeClips();
        InitializeTable();
        Agent = new(agentClient, options => options.AddGeneratedToolBlocks(), new ShellViewState());
        subscription = app.Subscribe(state => {
            if (savedPhoneCorrection != state.Presentation.PhoneCorrected)
            {
                savedPhoneCorrection = state.Presentation.PhoneCorrected;
                settings.Edit()!.PutBoolean("phone-corrected", savedPhoneCorrection)!.Apply();
            }
            BindTable(state); MainThread.BeginInvokeOnMainThread(Publish);
        });
    }
    public void SetSheet(bool open) { SheetOpen = open; chrome.SetEditing(open); Changed?.Invoke(); }
    public void ToggleMenuPanelPinned()
    {
        chrome.SetPinned(!chrome.Pinned);
        Android.App.Application.Context.GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!
            .Edit()!.PutBoolean("menu-panel-pinned", chrome.Pinned)!.Apply();
        Changed?.Invoke();
    }
    public void RevealControls() { chrome.Reveal(); Changed?.Invoke(); }
    public void DismissNotice() { chrome.SetAttention(false); chrome.Reveal(); Changed?.Invoke(); }
    public void ShowError(string message) { Message = message; chrome.SetAttention(true); Publish(); }
    public void SetAccessibility(double fontScale, bool touchExploration)
    {
        FontScale = Math.Clamp(fontScale, 1, 2); chrome.SetAccessibility(touchExploration); Changed?.Invoke();
    }
    public void Attach(Bridge.PlayerBridge player, Bridge.CastBridge cast)
    {
        this.player = player; this.cast = cast; player.SetPortrait(true);
        player.SetDiagnostics(SkyEye.Core.Analysis.PipelineDiagnostics.Enabled);
        lastScene = null;
        Android.Util.Log.Info("SkyEyeShell", "Native media handler attached");
        selection.Suspend();
        if (active && selection.Kind is ViewingSource.Preview or ViewingSource.LocalClip) PlayCurrent();
        if (active && selection.Kind == ViewingSource.Live) _ = RefreshAsync(foreground?.Token ?? CancellationToken.None);
    }
    public void Detach(Bridge.PlayerBridge owner)
    {
        if (player != owner) return;
        CheckpointAnnotations();
        StopPocketFraming();
        StopShotScenes();
        ClearSceneCheck();
        player = null; cast = null; Playing = false; selection.Suspend();
        lastScene = null;
    }
    public void ReportPlaying(bool playing)
    {
        if (player is not null) MediaGeneration();
        if (playing && player is not null) Android.Util.Log.Info("SkyEyeShell", "Native playback observed: " + player.PlaybackMetrics());
        if (playing && !Playing) chrome.Reveal();
        Playing = playing;
        Playback = selection.Kind switch
        {
            ViewingSource.LocalClip => playing ? "Offline clip · room audio" : "Offline clip · not playing",
            ViewingSource.Preview => playing ? "Test video" : "Test video · not playing",
            _ => playing ? "Live table · silent" : "Waiting for your table"
        };
        Publish();
    }
    public void ReportError()
    {
        Playing = false; Playback = "Viewing interrupted";
        ShowError(selection.Kind == ViewingSource.Live ? "The table view is reconnecting." : "This clip could not play. Try opening it again.");
    }

    public Task ConnectAsync(string address) => MainThread.InvokeOnMainThreadAsync(async () =>
    { await ConnectCoreAsync(address, CommandOrigin.Touch, Guid.NewGuid(), CancellationToken.None); });
    public Task<CommandReceipt?> ConnectCameraFromVoiceAsync(string address, string callId, CancellationToken cancellation) =>
        MainThread.InvokeOnMainThreadAsync(() => ConnectCoreAsync(address, CommandOrigin.Sky, VoiceOperationId(callId, "connect-camera"), cancellation));

    async Task<CommandReceipt?> ConnectCoreAsync(string address, CommandOrigin origin, Guid operationId, CancellationToken operationCancellation)
    {
        if (!active || foreground is null) return null;
        DismissNotice();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, operationCancellation);
        var cancellation = linked.Token;
        if (await Permissions.RequestAsync<LocalNetworkPermission>() != PermissionStatus.Granted)
        { ShowError("Allow nearby devices to connect your camera."); return null; }
        cancellation.ThrowIfCancellationRequested();
        sessionMode = TableSessionMode.None; Stop(); var generation = selection.Begin(ViewingSource.Live);
        Message = "Connecting to your camera…"; Publish();
        var result = await app.DispatchAsync(new(operationId, origin, app.State.Version, new ConnectCamera(address)), cancellation);
        if (cancellation.IsCancellationRequested || generation != selection.Generation || disposed) return null;
        Message = result.Succeeded ? "Camera connected." : result.Message;
        chrome.SetAttention(!result.Succeeded);
        if (result.Succeeded)
        {
            Preferences.Set("shell-camera", client.Address);
            Android.App.Application.Context.GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!.Edit()!.PutString("camera", client.Address)!.Apply();
        }
        ApplyCameraView(generation); Publish();
        return result;
    }
    public string SavedAddress => Android.App.Application.Context.GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!
        .GetString("camera", Preferences.Get("shell-camera", "http://192.168.1.124:8765"))!;
    public Task FixtureAsync() => MainThread.InvokeOnMainThreadAsync(FixtureCoreAsync);
    async Task FixtureCoreAsync()
    {
        if (!active || foreground is null) return;
        var token = foreground.Token; Stop(); var generation = selection.Begin(ViewingSource.Preview);
        var path = Path.Combine(FileSystem.CacheDirectory, "skyeye-table-preview.mp4");
        await using (var input = await FileSystem.OpenAppPackageFileAsync("probe.mp4"))
        await using (var output = File.Create(path)) await input.CopyToAsync(output, token);
        if (token.IsCancellationRequested || generation != selection.Generation || disposed) return;
        selection.Observe(generation, ViewingSource.Preview, new Uri(path).AbsoluteUri); Message = "Preview only · connect for your live table";
        PlayCurrent(); Publish();
    }
    void ApplyCameraView(long generation)
    {
        if (generation != selection.Generation || selection.Kind != ViewingSource.Live || !active) return;
        var state = app.State.Camera; var view = effects.Latest;
        if (state.Reachability != CameraReachability.Reachable || view?.Connection != state || state.Capture != CaptureState.Active)
        {
            if (selection.Source is not null) { StopPocketFraming(); StopShotScenes(); ClearSceneCheck(); player?.Stop(); if (!TvReplaySelected) cast?.Configure("", true); }
            selection.Observe(generation, ViewingSource.Live, null);
            Playing = false;
            Playback = state.Reachability == CameraReachability.Unreachable ? "Camera unreachable" : "Waiting for your table";
            return;
        }
        // Repeated telemetry/sheet renders must not reload ExoPlayer.
        if (selection.Observe(generation, ViewingSource.Live, view.PhoneStream)) PlayCurrent();
        BindAnnotationClock();
    }
    void PlayCurrent()
    {
        if (!active || selection.Source is not { } source || player is null) return;
        StopPocketFraming();
        StopShotScenes();
        ClearSceneCheck();
        player.SetPortrait(selection.Kind != ViewingSource.LocalClip);
        player.SetMuted(selection.Kind != ViewingSource.LocalClip); player.Play(source);
        Android.Util.Log.Info("SkyEyeShell", "Native playback started: " + selection.Kind);
        selection.Started();
        if (!TvReplaySelected && BackgroundTvService.Instance is null) cast?.Configure("", true);
        EnsureShotLoop();
    }
    public void Stop()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(Stop); return; }
        StopMotion();
        CollectAnnotations();
        StopPocketFraming(); StopShotScenes(); ClearSceneCheck(); selection.Stop(); player?.Stop(); if (!TvReplaySelected && BackgroundTvService.Instance is null) cast?.Configure("", true);
        Playing = false; Playback = "Waiting for your table"; Publish();
    }
    public void Suspend()
    {
        if (VisionDashboardActive) VisionDashboardStatus = "Dashboard is available; waiting for SkyEye to resume fresh sampling.";
        ReleaseMotion();
        CheckpointAnnotations();
        StopPocketFraming();
        StopShotScenes();
        ClearSceneCheck();
        active = false; foreground?.Cancel(); foreground?.Dispose(); foreground = null; StopDiscovery(); CloseKeyEditor();
        CloseTableEdit(invalidatePicking: true); table.Invalidate();
        player?.Stop(); Playing = false; selection.Suspend(); Publish();
    }
    public void Resume()
    {
        if (active || disposed) return;
        active = true; foreground = new();
        _ = StartVisionDashboardAsync();
        chrome.Reveal();
        if (selection.Kind is ViewingSource.Preview or ViewingSource.LocalClip) PlayCurrent();
        RefreshLocalClips(); RefreshKeyStatus();
        _ = ResumeCameraAsync(foreground.Token);
        _ = FadeAsync(foreground.Token);
    }
    async Task FadeAsync(CancellationToken token)
    {
        var nextCastCheck = TimeSpan.Zero;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
            while (await timer.WaitForNextTickAsync(token))
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!active || token.IsCancellationRequested) return;
                    var manager = Android.App.Application.Context.GetSystemService(Android.Content.Context.AccessibilityService) as Android.Views.Accessibility.AccessibilityManager;
                    bool wasVisible = chrome.Visible, wasAccessible = chrome.Accessible;
                    chrome.SetAccessibility(manager?.IsTouchExplorationEnabled == true);
                    chrome.CanFade = HasSource && Playing;
                    if (clock.Elapsed >= nextCastCheck)
                    {
                        nextCastCheck = clock.Elapsed + TimeSpan.FromSeconds(1);
                        var wasConnected = TvConnected; var replayChanged = ObserveCast();
                        var commandExpired = ExpireTvCommands();
                        if (wasConnected != TvConnected || replayChanged || commandExpired) Publish();
                    }
                    CollectAnnotations();
                    bool tickChanged = chrome.Tick();
                    if (tickChanged || wasVisible != chrome.Visible || wasAccessible != chrome.Accessible) Changed?.Invoke();
                });
        }
        catch (OperationCanceledException) { }
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
        var generation = selection.Generation;
        try
        {
            await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.System, state.Version, new RefreshCamera(state.Camera.ConnectionId)), token);
            if (token.IsCancellationRequested || disposed) return;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // Only a live selection may resume automatically; stopping/previewing overrides a late refresh.
                if (generation == selection.Generation && selection.Kind == ViewingSource.Live) ApplyCameraView(generation);
                ObserveCast();
                Publish();
            });
        }
        catch (OperationCanceledException) { }
    }
    void Publish()
    {
        if (disposed) return;
        var state = app.State.Camera;
        if (sessionMode != TableSessionMode.None && state.Reachability == CameraReachability.Reachable
            && state.Capture is not (CaptureState.Active or CaptureState.Paused)) sessionMode = TableSessionMode.None;
        if (TvReplaySelected && tvReplay.Connection != state.ConnectionId) { tvReplay.Clear(); cast?.Configure("", true); }
        if (tvLive.Request is not null && tvLive.Connection != state.ConnectionId) { tvLive.Clear(); cast?.Configure("", true); }
        InvalidateClipCatalog(state.ConnectionId);
        ObserveTable();
        TryStartDashboardWhenReady();
        EnsurePocketLoop();
        EnsureShotLoop();
        RenderShotScenes();
        if (active && tvDeliveryRequested) EnsureBackgroundTv();
        var camera = state.Reachability switch
        {
            CameraReachability.Connecting => "Connecting…",
            CameraReachability.Unreachable => "Camera unreachable",
            CameraReachability.Reachable when state.Capture == CaptureState.Active => "Camera active",
            CameraReachability.Reachable => "Camera ready",
            _ => "Connect your camera"
        };
        Agent.State.Value = new(app.State.Version, camera, Playing) { Connection = state }; Changed?.Invoke();
    }

    void TryStartDashboardWhenReady()
    {
        var connectionId = app.State.Camera.ConnectionId;
        if (connectionId == Guid.Empty || dashboardAttemptedConnection == connectionId
            || !CanOpenVisionDashboard || VisionDashboardActive
            || Interlocked.CompareExchange(ref dashboardStartPending, 1, 0) != 0) return;

        dashboardAttemptedConnection = connectionId;
        _ = StartAsync();

        async Task StartAsync()
        {
            try { await StartVisionDashboardAsync(); }
            finally { Interlocked.Exchange(ref dashboardStartPending, 0); }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        CloseVisionDashboard();
        if (sky is not null) _ = EndSkyAsync();
        StopBackgroundTv();
#if SKYEYE_PERFORMANCE
        StopPerformance();
#endif
        Suspend(); disposed = true; clipLifetime.Cancel(); CancelClipTransfer(); clipLifetime.Dispose();
        table.Dispose();correctedClips?.Dispose();
        subscription.Dispose(); client.Dispose(); Agent.Dispose(); agentClient.Dispose();
    }
}
