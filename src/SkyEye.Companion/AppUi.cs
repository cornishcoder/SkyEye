using SkyEye.Core.Application;
using SkyEye.Core;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    AppCoordinator app = null!;
    IDisposable? appSubscription;
    SkyEyeState renderedAppState = SkyEyeState.Empty;
    bool overlayEnabled => app.State.Presentation.ShotChoicesEnabled;
    string group => app.State.Advice.TargetGroup.ToString().ToLowerInvariant();

    void InitializeApp() => app = new(e => Android.Util.Log.Warn("SkyEye", $"App state observer: {e.GetType().Name}"),
        new ClipSaveExecutor(System.IO.Path.Combine(FilesDir!.AbsolutePath, "save-operations"), new CameraClipSaveEffect(client)));

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
        if (client.SaveScope is not { } scope || client.HostToken is null) { Say("Connect and become host before saving a clip."); return; }
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
        overlayToggle.Text = next.Presentation.ShotChoicesEnabled ? "Candidate overlay: on" : "Candidate overlay: off";
        if (previous.Advice != next.Advice)
        {
            if (groupSelector is not null && groupSelector.SelectedItemPosition != (int)next.Advice.TargetGroup)
                groupSelector.SetSelection((int)next.Advice.TargetGroup);
            if (tableState is not null)
            {
                tableState = tableTracker.SetGroup(next.Advice.TargetGroup.ToString().ToLowerInvariant());
                ClearCandidatePresentation("Checking your selected group…"); overlayRefreshAt = 0; ShowTable();
                if (maeve is not null && tableState is not null) _ = maeve.UpdateTableAsync(tableState);
            }
        }
        if (previous.Presentation != next.Presentation)
        {
            if (next.Presentation.ShotChoicesEnabled)
            {
                if (connected && currentState == "Active") { offline = false; player.SetMuted(true); player.Play(stream); }
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
