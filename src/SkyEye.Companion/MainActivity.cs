using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using SkyEye.Core;
using SkyEye.Core.Application;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

[Activity(Name = "uk.co.skyeye.companion.MainActivity", Label = MainActivity.AppTitle, MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed partial class MainActivity : AndroidX.AppCompat.App.AppCompatActivity
{
#if SKYEYE_COMMAND_LAB
    internal const string AppTitle = "SkyEye Commands Lab";
#else
    internal const string AppTitle = "SkyEye";
#endif
    readonly CameraClient client = new();
    readonly CancellationTokenSource lifetime = new();
    TextView status = null!, message = null!, viewerStatus = null!;
    EditText address = null!;
    LinearLayout controls = null!, remoteClips = null!, localClips = null!, discoveries = null!;
    Spinner duration = null!;
    Button claim = null!, release = null!;
    Bridge.PlayerBridge player = null!;
    Bridge.CastBridge? cast;
    Bridge.DiscoveryBridge discovery = null!;
    PlayerListener playerListener = null!; CastListener? castListener; DiscoveryListener discoveryListener = null!;
    bool resumed, polling, busy, portrait, offline;
    bool connected => app.State.Camera.ConnectionId != Guid.Empty && app.State.Camera.Reachability is CameraReachability.Reachable or CameraReachability.Unreachable;
    string currentState => app.State.Camera.Capture?.ToString() ?? "";
    string stream = "";
    MaeveSession? maeve;
    public override void OnConfigurationChanged(Android.Content.Res.Configuration config) { base.OnConfigurationChanged(config); }
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState); InitializeApp();
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical }; root.SetPadding(20, 24, 20, 20); root.SetBackgroundColor(Color.Rgb(10, 19, 28));
        var scroll = new ScrollView(this); scroll.AddView(root); SetContentView(scroll);
        root.AddView(Label(AppTitle, 30)); root.AddView(Label("Your table. Every shot.", 15));
        var settings = GetSharedPreferences("skyeye", FileCreationMode.Private)!;
        address = new EditText(this) { Hint = "Camera address · IP:8765", Text = settings.GetString("camera", "") }; address.SetSingleLine(true); address.SetTextColor(Color.White); address.SetHintTextColor(Color.Gray); root.AddView(address);
        var connectRow = Row(root); Add(connectRow, "Connect", () => _ = ConnectAsync()); Add(connectRow, "Find camera", () => discovery.Start());
        discoveries = new LinearLayout(this) { Orientation = Orientation.Vertical }; root.AddView(discoveries);
        status = Label("Connect to your armed camera", 16); root.AddView(status);
        message = Label("", 14); root.AddView(message);
        var hostRow = Row(root); claim = Add(hostRow, "Become host", () => HostCommand(true));
        release = Add(hostRow, "Release control", () => HostCommand(false));
        controls = new LinearLayout(this) { Orientation = Orientation.Vertical }; root.AddView(controls);
        var sessionRow = Row(controls); Add(sessionRow, "Start", () => CaptureCommand(CaptureAction.Start)); Add(sessionRow, "Pause", () => CaptureCommand(CaptureAction.Pause)); Add(sessionRow, "Resume", () => CaptureCommand(CaptureAction.Resume)); Add(sessionRow, "Stop", () => CaptureCommand(CaptureAction.Stop));
        var replayRow = Row(controls);
        duration = new Spinner(this); duration.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, ["30 seconds", "1 minute", "2 minutes", "5 minutes", "10 minutes"]); replayRow.AddView(duration);
        Add(replayRow, "Save last…", () => _ = ActionAsync(SaveClipUiAsync));
        controls.AddView(Label("Save ends at the camera's current moment. Viewing is delayed.", 12));
#if SKYEYE_COMMAND_LAB
        Add(controls, "Retry last save", () => _ = ActionAsync(RetryLabSaveAsync));
#endif
        playerListener = new PlayerListener(this); player = new Bridge.PlayerBridge(this, playerListener);
        root.AddView(player.View, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)(260 * Resources!.DisplayMetrics!.Density)));
        viewerStatus = Label("Live viewing is silent. Saved clips include room sound.", 13); root.AddView(viewerStatus);
        BuildCandidateOverlayUi(root);
        var viewRow = Row(root); Add(viewRow, "Watch live", () => _ = ActionAsync(async () => { await RefreshAsync(); if (app.State.Camera.Reachability != CameraReachability.Reachable || app.State.Camera.Capture != CaptureState.Active) { Say("An active, reachable camera is needed for live viewing."); return; } offline = false; player.SetMuted(true); player.Play(stream); }));
        Add(viewRow, "Portrait / landscape", () => { portrait = !portrait; player.SetPortrait(portrait); });
        try { castListener = new CastListener(this); cast = new Bridge.CastBridge(this, castListener); viewRow.AddView(cast.Button, new LinearLayout.LayoutParams(56, 56)); }
        catch { root.AddView(Label("Cast is unavailable on this device.", 13)); }
        var tvRow = Row(controls); Add(tvRow, "Play on TV", () => cast?.Load()); Add(tvRow, "Stop TV", () => cast?.Stop());
        root.AddView(Label("Clips on camera", 22)); Add(root, "Refresh clips", () => _ = ActionAsync(LoadClipsAsync)); remoteClips = new LinearLayout(this) { Orientation = Orientation.Vertical }; root.AddView(remoteClips);
        root.AddView(Label("Your downloaded clips", 22)); localClips = new LinearLayout(this) { Orientation = Orientation.Vertical }; root.AddView(localClips); RefreshLocal();
        BuildTableUi(root);
        root.AddView(Label("Maeve · pool partner", 22));
        var keyRow = Row(root); Add(keyRow, "Set API key", SetupKey); Add(keyRow, "Talk to Maeve", StartMaeve); Add(keyRow, "End Maeve", () => _ = EndMaeveAsync());
        Add(root, "Mute / unmute Maeve microphone", () => { voiceMuted = !voiceMuted; maeve?.SetMuted(voiceMuted); Say(voiceMuted ? "Maeve microphone muted" : "Maeve microphone enabled"); });
        discoveryListener = new DiscoveryListener(this); discovery = new Bridge.DiscoveryBridge(this, discoveryListener);
        // Bind a restored native credential to its saved endpoint before a new
        // connect can compare identities. Authority is still unknown until proof.
        try { var savedCamera = settings.GetString("camera", ""); if (!string.IsNullOrWhiteSpace(savedCamera)) { client.Connect(savedCamera); client.HostToken = settings.GetString("host", null); } }
        catch (ArgumentException) { settings.Edit()!.Remove("host")!.Apply(); }
        controls.Visibility = ViewStates.Gone; release.Visibility = ViewStates.Gone; claim.Enabled = false;
        var initial = Intent?.GetStringExtra("camera"); if (!string.IsNullOrWhiteSpace(initial)) { address.Text = initial; _ = ConnectAsync(); }
    }
    TextView Label(string text, int size) { var view = new TextView(this) { Text = text, TextSize = size }; view.SetTextColor(Color.Rgb(205, 230, 235)); view.SetPadding(0, 7, 0, 7); return view; }
    LinearLayout Row(LinearLayout parent) { var row = new LinearLayout(this) { Orientation = Orientation.Horizontal }; parent.AddView(row); return row; }
    Button Add(LinearLayout parent, string title, Action action) { var button = new Button(this) { Text = title, TextSize = 12 }; button.Click += (_, _) => action(); parent.AddView(button, parent.Orientation == Orientation.Horizontal ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1) : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)); return button; }
    void Say(string text) { if (!lifetime.IsCancellationRequested) RunOnUiThread(() => { if (!lifetime.IsCancellationRequested) message.Text = text; }); }
    async Task ActionAsync(Func<Task> work)
    {
        if (busy) { Say("Please wait for the current operation."); return; } busy = true;
        try { await work(); }
        catch (System.OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { Say(e.Message); }
        finally { busy = false; }
    }
    void StartPolling()
    {
        if (polling || !connected || !resumed && maeve is null) return; polling = true;
        _ = PollAsync();
    }
    async Task PollAsync()
    {
        try
        {
            int delayMs = 2500;
            while ((resumed || maeve is not null) && connected && !lifetime.IsCancellationRequested)
            {
                await Task.Delay(delayMs, lifetime.Token);
                var cycle = System.Diagnostics.Stopwatch.StartNew();
                try { await RefreshAsync(); }
                catch { RunOnUiThread(() => { status.Text = "Camera unreachable. Recording may continue; reconnect to check."; if (!offline) player.Stop(); InvalidateTable("Camera unreachable; advice is waiting for a fresh view."); }); }
                // Downloads/export can occupy the action slot longer than a frame's
                // advice lifetime. Expire displayed paths even while analysis is skipped.
                if (tableState is not null) ShowTable();
                if ((resumed || maeve is not null) && !overlayEnabled && !busy && app.State.Camera.CanControl && currentState == "Active" && calibration is not null) try { await UpdateTableAsync(); } catch { InvalidateTable("Table analysis unavailable; wait for a fresh view and confirm it."); }
                // Aim for 2.5 seconds between starts, including network and analysis
                // time. Remain sequential even when a cycle exceeds that budget.
                delayMs = (int)Math.Max(250, 2500 - cycle.ElapsedMilliseconds);
            }
        }
        catch (System.OperationCanceledException) { }
        finally { polling = false; if ((resumed || maeve is not null) && connected && !lifetime.IsCancellationRequested) StartPolling(); }
    }
    void SetupKey()
    {
        var edit = new EditText(this) { Hint = "Personal OpenAI API key", InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword };
        new AlertDialog.Builder(this)!.SetTitle("Maeve voice setup")!.SetMessage("Stored encrypted on this host phone. Camera and guests never receive it.")!.SetView(edit)!.SetPositiveButton("Save", (_, _) => { SecretStore.Set(this, "openai", edit.Text?.Trim() ?? ""); edit.Text = ""; Say("API key saved on this phone."); })!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
    }
    void StartMaeve()
    {
        if (!app.State.Camera.CanControl) { Say("Claim host control before starting Maeve."); return; }
        if (CheckSelfPermission(Manifest.Permission.RecordAudio) != Permission.Granted) { RequestPermissions([Manifest.Permission.RecordAudio], 2); return; }
        if (maeve is not null) { Say("Maeve is already connected."); return; }
        var key = SecretStore.Get(this, "openai"); if (string.IsNullOrWhiteSpace(key)) { SetupKey(); return; }
        if (busy) { Say("Finish the current operation before starting Maeve."); return; }
        StartForegroundService(new Intent(this, typeof(VoiceService)));
        player.SetMuted(true); voiceMuted = false; var session = new MaeveSession(this, key, Say, ExecutePoolToolAsync); maeve = session;
        _ = ActionAsync(async () => { try { await session.StartAsync(); } catch { if (maeve == session) maeve = null; StopService(new Intent(this, typeof(VoiceService))); throw; } });
    }
    async Task EndMaeveAsync() { var session = maeve; maeve = null; try { if (session is not null) await session.CloseAsync(); } finally { StopService(new Intent(this, typeof(VoiceService))); } }
    protected override void OnResume() { base.OnResume(); resumed = true; if (connected) _ = RefreshOnResumeAsync(); StartPolling(); if (overlayEnabled) StartCandidateOverlay(); }
    protected override void OnPause() { resumed = false; PauseCandidateOverlay(); discovery.Stop(); if (maeve is null) InvalidateTable("Return for a fresh table view."); base.OnPause(); }
    protected override void OnDestroy() { appSubscription?.Dispose(); effectSubscription?.Dispose(); lifetime.Cancel(); tableAnalysis.Dispose(); PauseCandidateOverlay(); tableBitmap?.Dispose(); _ = EndMaeveAsync(); player.Release(); cast?.Release(); discovery.Stop(); client.Dispose(); base.OnDestroy(); }
    sealed class PlayerListener(MainActivity owner) : Java.Lang.Object, Bridge.PlayerBridge.IListener { public void OnError(string? error) => owner.Say(error ?? "Viewing interrupted"); public void OnPlaying(bool playing) => owner.RunOnUiThread(() => { if (!owner.lifetime.IsCancellationRequested) owner.viewerStatus.Text = playing ? owner.offline ? "Playing downloaded clip" : "Live · delayed · silent" : "Waiting for video…"; }); }
    sealed class CastListener(MainActivity owner) : Java.Lang.Object, Bridge.CastBridge.IListener { public void OnStatus(string? status) => owner.Say(status ?? "TV status changed"); }
    sealed class DiscoveryListener(MainActivity owner) : Java.Lang.Object, Bridge.DiscoveryBridge.IListener
    {
        public void OnError(string? error) => owner.Say(error ?? "Discovery unavailable");
        public void OnCamera(string? name, string? address) => owner.RunOnUiThread(() => owner.Add(owner.discoveries, name ?? "Camera", () => { owner.address.Text = address; _ = owner.ConnectAsync(); }));
    }
}
