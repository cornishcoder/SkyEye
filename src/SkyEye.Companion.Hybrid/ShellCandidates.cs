using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    readonly CandidateCarousel shotCarousel = new();
    SkyEye.Companion.OverlayRelay? shotRelay;
    ShotRelayListener? shotRelayListener;
    CancellationTokenSource? shotLifetime;
    long relayRetryAt;
    bool suppressShotLoop, shotLoopFaulted;
    Task sceneRelayClosing = Task.CompletedTask;
    string lastPhoneShot = "", lastTvShot = "";
    string shotMessage = "Show choices to check the table.";
    public PresentationPreferences ShotPresentation => app.State.Presentation;
    public IReadOnlyList<ShotChoice> ShotChoices => CurrentShotCatalog ? app.State.ShotChoices.Choices : [];
    public long ChoiceTableVersion => app.State.ShotChoices.TableVersion;
    bool CurrentShotCatalog => app.State.ShotChoices.Matches(app.State.Camera, app.State.Advice.TargetGroup, DateTimeOffset.UtcNow)
        && table.State.Table?.Version == app.State.ShotChoices.TableVersion && !EditingTable && selection.Kind == ViewingSource.Live;
    public string CandidateStatus => !app.State.Presentation.ShotChoicesEnabled ? TvReplaySelected ? $"Choices are off · {TvReplayKind} on TV." : "Choices are off · live table on both screens."
        : !CanAnalyzeTable ? "Become host on an active camera to check shot choices."
        : !HasTableCalibration ? "Calibrate the table before showing choices."
        : ShotChoices.Count == 0 ? shotMessage : $"{ShotChoices.Count} choices · cycle every 2 seconds · expires after 15 seconds";
    public bool CanShowChoices => active && selection.Kind == ViewingSource.Live && CanCastLive && !EditingTable;

    public Task SetShotDisplayAsync(long expectedVersion, SceneTarget target, ShotDisplayMode mode) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || EditingTable) return;
        if (expectedVersion != app.State.Version) { ShowError("The controls changed. Try again."); return; }
        ClearSceneCheck();
        var result = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new SetShotDisplay(target, mode)), foreground.Token);
        if (!result.Succeeded) ShowError(result.Message);
        else { shotLoopFaulted = false; EnsureShotLoop(); RenderShotScenes(); Publish(); }
    });
    public Task PinShotAsync(long expectedVersion, SceneTarget target, long tableVersion, string id) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || EditingTable || !CurrentShotCatalog) { ShowError("That layout changed. Check the latest choices."); return; }
        var result = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new PinShot(target, tableVersion, id)), foreground.Token);
        if (!result.Succeeded) ShowError(result.Message); else { RenderShotScenes(); Publish(); }
    });
    public Task ShowRequestedChoicesAsync(long expectedVersion, SceneTarget target) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || EditingTable || expectedVersion != app.State.Version) return;
        await RefreshShotChoicesAsync(foreground.Token);
        await SetShotDisplayAsync(app.State.Version, target, ShotDisplayMode.Cycling);
    });
    public SkyEyeState CommandState => app.State;
    public Task ShowCuePathAsync(long expectedVersion, long tableVersion, string id) => ExecuteApplicationCommandAsync(
        new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new ShowAnnotation(SceneTarget.Phone, tableVersion, id, ShotAnnotations.CueBallPath)));
    public ObserveTable? ObservationRequest => player is null || table.State.Source is not { } source ? null : new(source, table.State.Generation, MediaGeneration());
    public Task<CommandReceipt> ExecuteApplicationCommandAsync(CommandEnvelope request, CancellationToken cancellation = default) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || EditingTable) return new CommandReceipt(request.OperationId, CommandOutcome.Rejected, app.State.Version, "Use the active application surface.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, foreground?.Token ?? CancellationToken.None);
        var receipt = await app.DispatchAsync(request, linked.Token);
        if (receipt.Succeeded)
        {
            if (request.Command is ObserveTable { IncludeChoices: true } && table.State.Table is { } observed)
                shotCarousel.Use(table.State.Choices, observed.CapturedAt);
            var target = request.Command switch { SetShotDisplay c => c.Target, PinShot c => c.Target, ShowShot c => c.Target, ShowAnnotation c => c.Target, ClearAnnotations c => c.Target, _ => (SceneTarget?)null };
            if (target is SceneTarget.Tv or SceneTarget.Both && TvReplaySelected) { tvReplay.Clear(); tvLive.Clear(); PlayTv(); }
            if (target is SceneTarget.Phone or SceneTarget.Both && selection.Kind == ViewingSource.LocalClip
                && request.Command is ClearAnnotations or SetShotDisplay { Mode: ShotDisplayMode.Live }) await WatchLiveAsync();
            RenderShotScenes(); Publish();
        }
        return receipt;
    });
    void EnsureShotLoop()
    {
        if (!active || foreground is null || suppressShotLoop || shotLoopFaulted || EditingTable || table.State.Picking || SceneCheck || selection.Kind != ViewingSource.Live || shotLifetime is not null) return;
        shotLifetime = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token);
        if (shotCarousel.Candidates.Length == 0) shotCarousel.Start(Android.OS.SystemClock.ElapsedRealtime());
        _ = ShotLoopAsync(shotLifetime.Token);
    }
    async Task ShotLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            do
            {
                if (!active || token.IsCancellationRequested) return;

                if (!app.State.Presentation.ShotChoicesEnabled || !CanAnalyzeTable || !HasTableCalibration || EditingTable) await ClearShotCatalogAsync(token);
                else if (!CurrentShotCatalog) await ClearShotCatalogAsync(token);
                if (token.IsCancellationRequested) return;
                RenderShotScenes();
                ManageShotRelay();
                // Candidate/catalog and camera callbacks publish UI changes. The
                // native renderer can tick without rerendering the sheet five times/sec.
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested && active) { shotLoopFaulted = true; StopShotScenes(); ShowError("Shot choices interrupted. Turn them off and on to retry."); }
        }
    }
    async Task ClearShotCatalogAsync(CancellationToken token)
    {
        shotCarousel.Clear();
        if (app.State.ShotChoices != ShotChoiceCatalog.Empty)
            await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.System, app.State.Version, new PublishShotCatalog(ShotChoiceCatalog.Empty)), token);
    }
    async Task ReconcileShotCatalogAsync(CancellationToken token)
    {
        if (app.State.ShotChoices != ShotChoiceCatalog.Empty && !CurrentShotCatalog)
        {
            await ClearShotCatalogAsync(token);
            shotMessage = "Choices expired or the layout changed. Request a fresh observation.";
        }
    }
    async Task RefreshShotChoicesAsync(CancellationToken token)
    {
        shotMessage = "Checking table…";
        try
        {
            await UpdateTableAsync(token, includeChoices: true);
            token.ThrowIfCancellationRequested();
            var snapshot = table.State; var layout = snapshot.Table;
            if (!CanAnalyzeTable || EditingTable || snapshot.Source != app.State.Camera.Scope || layout is null || !layout.Settled || layout.Occluded)
            {
                shotMessage = layout?.Occluded == true ? "Table unclear or obstructed. Request again when clear." : "Settlement is not established. Request again when the balls are still.";
                await ClearShotCatalogAsync(token); return;
            }
            shotCarousel.Use(snapshot.Choices, layout.CapturedAt);
            shotMessage = shotCarousel.Candidates.Length == 0 ? "No clear candidate for this layout and group." : "";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { await ClearShotCatalogAsync(token); shotMessage = "Table view unavailable. Request again when useful."; }
    }
    void RenderShotScenes()
    {
        if (!active || player is null || SceneCheck) return;
        var now = DateTimeOffset.UtcNow;
        var snapshot = table.State;
        bool current = CurrentShotCatalog && snapshot.Calibration is not null && shotCarousel.Candidates.All(s => s.TableVersion == snapshot.Table?.Version);
        var index = current ? shotCarousel.Index(Android.OS.SystemClock.ElapsedRealtime(), now) : -1;
        long mediaGeneration;
        using (var metrics = JsonDocument.Parse(player.PlaybackMetrics()!)) mediaGeneration = metrics.RootElement.GetProperty("mediaGeneration").GetInt64();
        void Surface(SurfacePresentation surface, bool tv, ref string last)
        {
            Shot? shot = current ? surface.Mode switch
            {
                ShotDisplayMode.Cycling when index >= 0 => shotCarousel.Candidates[index],
                ShotDisplayMode.Pinned => shotCarousel.Candidates.FirstOrDefault(s => s.Id == surface.ShotId && s.TableVersion == surface.TableVersion),
                _ => null
            } : null;
            var key = $"{mediaGeneration}:{surface.Mode}:{surface.Annotations}:{shot?.Id}:{app.State.ShotChoices.ExpiresAt.ToUnixTimeMilliseconds()}:{shotMessage}:{CanAnalyzeTable}:{EditingTable}:{Playing}:{(shot is null ? Android.OS.SystemClock.ElapsedRealtime() / 2000 : 0)}";
            if (last == key) return;
            last = key;
            if (surface.Mode is not (ShotDisplayMode.Cycling or ShotDisplayMode.Pinned) || selection.Kind != ViewingSource.Live || EditingTable || !Playing) { player.ClearTableSceneForTarget(tv); return; }
            var caption = shot is null ? CandidateStatus : $"{(surface.Mode == ShotDisplayMode.Pinned ? "Pinned" : "Candidate " + (index + 1) + " / " + shotCarousel.Candidates.Length)} · {shot.Type} · pocket {shot.Pocket + 1}";
            try
            {
                var scene = shot is not null ? ShotSceneProjection.Create(shot, snapshot.Calibration!, ++sceneRevision, mediaGeneration, now, app.State.ShotChoices.ExpiresAt, caption, surface.Mode == ShotDisplayMode.Pinned, surface.Annotations)
                    : new TableScene(++sceneRevision, mediaGeneration, snapshot.Table?.Version ?? 0, now, TimeSpan.FromSeconds(3), [], [], [], caption);
                if (!player.SetTableSceneForTarget(JsonSerializer.Serialize(scene, sceneJson), tv)) last = "";
            }
            catch (ArgumentException) { player.ClearTableSceneForTarget(tv); shotMessage = "Candidate is outside the camera view. Check calibration."; }
        }
        Surface(app.State.Presentation.Phone, false, ref lastPhoneShot);
        Surface(app.State.Presentation.Tv, true, ref lastTvShot);
    }
    void ManageShotRelay()
    {
        bool needed = TvConnected && !TvReplaySelected && active && Playing && selection.Kind == ViewingSource.Live && CanCastLive && !EditingTable
            && (TvCorrectionRequested || app.State.Presentation.Tv.Mode is ShotDisplayMode.Cycling or ShotDisplayMode.Pinned);
        if (!needed)
        {
            CloseShotRelay();
            if (!TvReplaySelected && CanCastLive && effects.Latest is { } live) cast?.Configure(live.Stream, true);
            return;
        }
        if (shotRelay is null && sceneRelayClosing.IsCompleted && Android.OS.SystemClock.ElapsedRealtime() >= relayRetryAt)
        {
            try
            {
                shotRelayListener = new(this);
                shotRelay = new(Platform.CurrentActivity!, player!, client.Address, shotRelayListener, () => cast?.Metrics() ?? "{}", sharedScene: true);
            }
            catch { relayRetryAt = Android.OS.SystemClock.ElapsedRealtime() + 30000; shotMessage = "TV overlay unavailable. Phone choices remain available."; }
        }
        if (shotRelay is { Ready: true }) cast?.Configure(shotRelay.Url, true);
    }
    void CloseShotRelay()
    {
        var relay = shotRelay; shotRelay = null;
        if (relay is not null) { cast?.ReturnToSourceIfOwned(relay.Url, CanCastLive && effects.Latest is { } view ? view.Stream : ""); sceneRelayClosing = relay.DisposeAsync().AsTask(); }
        shotRelayListener?.Dispose(); shotRelayListener = null;
    }
    void StopShotScenes(bool closeRelay=true)
    {
        shotLifetime?.Cancel(); shotLifetime?.Dispose(); shotLifetime = null;
        if(closeRelay)CloseShotRelay(); shotCarousel.Clear(); player?.ClearTableScene(); lastPhoneShot = lastTvShot = "";
    }
    sealed class ShotRelayListener(ShellSession owner) : Java.Lang.Object, Bridge.OverlayEncoder.IListener
    {
        public void OnError(string? message)
        {
            if (owner.shotRelayListener != this) return;
            owner.CloseShotRelay(); owner.relayRetryAt = Android.OS.SystemClock.ElapsedRealtime() + 30000;
            owner.ShowError("TV overlay interrupted. The camera feed is available; retrying automatically.");
        }
    }
}
