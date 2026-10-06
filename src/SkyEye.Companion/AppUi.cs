using SkyEye.Core.Application;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    AppCoordinator app = null!;
    CameraControlEffect cameraEffects = null!;
    TableAnalysisSession tableAnalysis = null!;
    CompanionClipService clipService = null!;
    CompanionCorrectedClips correctedMedia = null!;
    IDisposable? effectSubscription;
    IDisposable? appSubscription;
    SkyEyeState renderedAppState = SkyEyeState.Empty;
    bool overlayEnabled => app.State.Presentation.ShotChoicesEnabled;
    string group => app.State.Advice.TargetGroup.ToString().ToLowerInvariant();

    void InitializeApp()
    {
        cameraEffects = new(client, token =>
        {
            var edit = GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!.Edit()!;
            if (token is null) edit.Remove("host"); else edit.PutString("host", token);
            edit.Apply();
        });
        app = new(e => Android.Util.Log.Warn("SkyEye", $"App state observer: {e.GetType().Name}"),
            new ClipSaveExecutor(System.IO.Path.Combine(FilesDir!.AbsolutePath, "save-operations"), new CameraClipSaveEffect(client, clip =>
            {
                try { new AnnotationJournalStore(System.IO.Path.Combine(FilesDir!.AbsolutePath,"recorded-annotations")).Preserve(clip); }
                catch(Exception) { Android.Util.Log.Warn("SkyEyeAnnotations","Saved clip acknowledged; annotation retention unavailable."); }
            })), cameraEffects, observations: new TableObservationEffect(() => tableAnalysis, LegacyMediaGeneration, () => resumed && !offline));
        tableAnalysis = new(new PlayerTableFrameSource(() => player, () => app.State.Camera, () => resumed && !offline), new VisionAnalyzer(),
            new CameraCalibrationStore(ApplicationContext!, () => client.Address));
        // Service binding runs before UI observers; delayed UI notifications must
        // not leave a late frame/edit attached to an obsolete camera or group.
        effectSubscription = app.Subscribe(state => tableAnalysis.Bind(state.Camera, state.Advice.TargetGroup.ToString().ToLowerInvariant()));
        clipService = new(client, () => app.State.Camera,
            new ClipLibrary(System.IO.Path.Combine(FilesDir!.AbsolutePath, "clips")), new AndroidPortraitClipExporter(ApplicationContext!));
        correctedMedia=new(client,()=>app.State.Camera,clipService,System.IO.Path.Combine(FilesDir!.AbsolutePath,"corrected-legacy-clips"),
            new AndroidCorrectedClipRenderer(ApplicationContext!),new AnnotationJournalStore(System.IO.Path.Combine(FilesDir.AbsolutePath,"recorded-annotations")),port:8769);
    }
    long legacyMediaGeneration;
    long LegacyMediaGeneration()
    {
        if (Android.OS.Looper.MyLooper() != Android.OS.Looper.MainLooper) return Interlocked.Read(ref legacyMediaGeneration);
        if (player is null) return 0;
        using var metrics = System.Text.Json.JsonDocument.Parse(player.PlaybackMetrics()!);
        var value = metrics.RootElement.GetProperty("mediaGeneration").GetInt64(); Interlocked.Exchange(ref legacyMediaGeneration, value); return value;
    }

#if SKYEYE_COMMAND_LAB
    // Lab-only retry lets device checks submit exactly the same request, including
    // after reopening. It never sends anything automatically on activity creation.
    sealed record LabSaveRequest(Guid OperationId, CommandOrigin Origin, long ExpectedVersion, SaveClip Command);
    string LabSaveRequestPath => System.IO.Path.Combine(FilesDir!.AbsolutePath, "last-save-request.json");
    async Task RetryLabSaveAsync()
    {
        if (!File.Exists(LabSaveRequestPath)) { Say("Save a clip first."); return; }
        var saved = System.Text.Json.JsonSerializer.Deserialize<LabSaveRequest>(File.ReadAllText(LabSaveRequestPath), JsonDefaults.Options)
            ?? throw new IOException("The lab retry request could not be read.");
        var result = await app.DispatchAsync(new(saved.OperationId, saved.Origin, saved.ExpectedVersion, saved.Command), lifetime.Token);
        if (lifetime.IsCancellationRequested) return;
        Say((result.Duplicate ? "Original result: " : "") + result.Message);
        WriteSaveDiagnostic(result);
        if (result.Succeeded || result.Outcome == CommandOutcome.Uncertain) await LoadClipsAsync();
    }
#endif

    void BindAppState() => appSubscription = app.Subscribe(_ => RunOnUiThread(ApplyAppState));

    async Task DispatchAppAsync(SkyEyeCommand command)
    {
        try
        {
            var receipt = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, renderedAppState.Version, command), lifetime.Token);
            if (!receipt.Succeeded)
            {
                ApplyAppState();
                if (groupSelector is not null) groupSelector.SetSelection((int)app.State.Advice.TargetGroup);
                Say(receipt.Message);
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task SaveClipUiAsync()
    {
        await RefreshAsync();
        if (!app.State.Camera.CanControl || client.SaveScope is not { } scope || client.HostToken is null) { Say("Connect and become host before saving a clip."); return; }
        var envelope = new CommandEnvelope(Guid.NewGuid(), CommandOrigin.Touch, renderedAppState.Version,
            new SaveClip(ReplaySelection.Durations[duration.SelectedItemPosition], scope));
#if SKYEYE_COMMAND_LAB
        File.WriteAllText(LabSaveRequestPath, System.Text.Json.JsonSerializer.Serialize(
            new LabSaveRequest(envelope.OperationId, envelope.Origin, envelope.ExpectedVersion, (SaveClip)envelope.Command), JsonDefaults.Options));
#endif
        Say("Saving the camera's recent recording…");
        var receipt = await app.DispatchAsync(envelope, lifetime.Token);
        if (lifetime.IsCancellationRequested) return;
        Say(receipt.Message);
        WriteSaveDiagnostic(receipt);
        if (receipt.Succeeded || receipt.Outcome == CommandOutcome.Uncertain) await LoadClipsAsync();
    }

    void WriteSaveDiagnostic(CommandReceipt receipt)
    {
#if DEBUG
        File.WriteAllText(System.IO.Path.Combine(FilesDir!.AbsolutePath, "save-result.json"),
            System.Text.Json.JsonSerializer.Serialize(receipt, SkyEye.Core.JsonDefaults.Options));
#endif
    }

    void ApplyAppState()
    {
        if (lifetime.IsCancellationRequested) return;
        // A queued UI notification must never replay an older app snapshot.
        var next = app.State;
        var previous = renderedAppState;
        if (next.Version <= previous.Version) return;
        renderedAppState = next;
        if (previous.Camera != next.Camera) ApplyCameraState(previous.Camera, next.Camera);
        overlayToggle.Text = next.Presentation.ShotChoicesEnabled ? "Candidate overlay: on" : "Candidate overlay: off";
        if (previous.Advice != next.Advice)
        {
            if (groupSelector is not null && groupSelector.SelectedItemPosition != (int)next.Advice.TargetGroup)
                groupSelector.SetSelection((int)next.Advice.TargetGroup);
            if (tableState is not null)
            {
                ClearCandidatePresentation("Group changed. Request fresh choices."); ShowTable();
                if (sky is not null && tableState is not null) _ = sky.UpdateTableAsync(tableState);
            }
        }
        if (previous.Presentation != next.Presentation)
        {
            if (next.Presentation.ShotChoicesEnabled)
            {
                if (connected && currentState == "Active" && offline) { offline = false; player.SetMuted(true); player.Play(PhoneStream); }
                StartCandidateOverlay();
            }
            else { PauseCandidateOverlay(); ClearCandidatePresentation("Candidate overlay is off."); }
        }
#if DEBUG
        // Latest sanitized projection only; no host token, API key or media URL.
        File.WriteAllText(System.IO.Path.Combine(FilesDir!.AbsolutePath, "app-state.json"),
            System.Text.Json.JsonSerializer.Serialize(next, SkyEye.Core.JsonDefaults.Options));
#endif
    }
}
