using SkyEye.Core.Application;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    readonly AppCoordinator app = new(e => Android.Util.Log.Warn("SkyEye", $"App state observer: {e.GetType().Name}"));
    IDisposable? appSubscription;
    SkyEyeState renderedAppState = SkyEyeState.Empty;
    bool overlayEnabled => app.State.Presentation.ShotChoicesEnabled;
    string group => app.State.Advice.TargetGroup.ToString().ToLowerInvariant();

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
