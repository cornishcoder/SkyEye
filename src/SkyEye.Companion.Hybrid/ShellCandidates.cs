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
    long nextShotRefresh, relayRetryAt;
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
        : ShotChoices.Count == 0 ? shotMessage : $"{ShotChoices.Count} choices · cycle every 2 seconds · update every 10 seconds";
    public bool CanShowChoices => active && selection.Kind == ViewingSource.Live && CanCastLive && !EditingTable;

    public Task SetShotDisplayAsync(long expectedVersion, SceneTarget target, ShotDisplayMode mode) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || EditingTable) return;
        ClearSceneCheck();
        var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new SetShotDisplay(target, mode)), foreground.Token);
        if (!result.Succeeded) ShowError(result.Message);
        else { shotLoopFaulted = false; EnsureShotLoop(); RenderShotScenes(); Publish(); }
    });
    public Task PinShotAsync(long expectedVersion, SceneTarget target, long tableVersion, string id) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || EditingTable || !CurrentShotCatalog) { ShowError("That layout changed. Check the latest choices."); return; }
        var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new PinShot(target, tableVersion, id)), foreground.Token);
        if (!result.Succeeded) ShowError(result.Message); else { RenderShotScenes(); Publish(); }
    });
    void EnsureShotLoop()
    {
        if (!active || foreground is null || suppressShotLoop || shotLoopFaulted || EditingTable || table.State.Picking || SceneCheck || selection.Kind != ViewingSource.Live || !(app.State.Presentation.ShotChoicesEnabled || FrameTableRequested) || shotLifetime is not null) return;
        shotLifetime = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token);
        shotCarousel.Start(Android.OS.SystemClock.ElapsedRealtime()); nextShotRefresh = 0;
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
                if (!(app.State.Presentation.ShotChoicesEnabled || FrameTableRequested)) { StopShotScenes(); return; }
                if (!app.State.Presentation.ShotChoicesEnabled || !CanAnalyzeTable || !HasTableCalibration || EditingTable) await ClearShotCatalogAsync(token);
                else if (Android.OS.SystemClock.ElapsedRealtime() >= nextShotRefresh)
                {
                    nextShotRefresh = Android.OS.SystemClock.ElapsedRealtime() + CandidateCarousel.RefreshMilliseconds;
                    await RefreshShotChoicesAsync(token);
                }
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
            await ClearShotCatalogAsync(token); nextShotRefresh = 0;
            shotMessage = "Layout changed. Checking fresh choices…";
        }
    }
    async Task RefreshShotChoicesAsync(CancellationToken token)
    {
        shotMessage = "Checking table…";
        try
        {
            await UpdateTableAsync(token);
            if (table.State.Table is { Settled: false, Occluded: false })
            {
                shotMessage = "Waiting for the balls to settle…"; RenderShotScenes(); Publish();
                await Task.Delay(2100, token); await UpdateTableAsync(token);
            }
            token.ThrowIfCancellationRequested();
            var snapshot = table.State; var layout = snapshot.Table;
            if (!CanAnalyzeTable || EditingTable || snapshot.Source != app.State.Camera.Scope || layout is null || !layout.Settled || layout.Occluded)
            {
                shotMessage = layout?.Occluded == true ? "Table unclear or obstructed. Checking again automatically…" : "Waiting for a clear, settled table…";
                await ClearShotCatalogAsync(token); return;
            }
            var confirmed = table.Confirm(layout.Version);
            shotCarousel.Update(confirmed, DateTimeOffset.UtcNow);
            var catalog = new ShotChoiceCatalog(snapshot.Source, confirmed.Version, app.State.Advice.TargetGroup, confirmed.CapturedAt, shotCarousel.ExpiresAt,
                shotCarousel.Candidates.Select(s => new ShotChoice(s.Id, s.Type, s.Pocket)).ToImmutableArray());
            var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.System, app.State.Version, new PublishShotCatalog(catalog)), token);
            if (!result.Succeeded) { await ClearShotCatalogAsync(token); shotMessage = "Layout changed. Checking again automatically…"; }
            else shotMessage = shotCarousel.Candidates.Length == 0 ? "No clear candidate for this layout and group." : "";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { await ClearShotCatalogAsync(token); shotMessage = "Table view unavailable. Checking again automatically…"; }
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
            var key = $"{mediaGeneration}:{surface.Mode}:{shot?.Id}:{app.State.ShotChoices.ExpiresAt.ToUnixTimeMilliseconds()}:{shotMessage}:{CanAnalyzeTable}:{EditingTable}:{Playing}:{(shot is null ? Android.OS.SystemClock.ElapsedRealtime() / 2000 : 0)}";
            if (last == key) return;
            last = key;
            if (surface.Mode == ShotDisplayMode.Live || selection.Kind != ViewingSource.Live || EditingTable || !Playing) { player.ClearTableSceneForTarget(tv); return; }
            var caption = shot is null ? CandidateStatus : $"{(surface.Mode == ShotDisplayMode.Pinned ? "Pinned" : "Candidate " + (index + 1) + " / " + shotCarousel.Candidates.Length)} · {shot.Type} · pocket {shot.Pocket + 1}";
            try
            {
                var scene = shot is not null ? ShotSceneProjection.Create(shot, snapshot.Calibration!, ++sceneRevision, mediaGeneration, now, app.State.ShotChoices.ExpiresAt, caption, surface.Mode == ShotDisplayMode.Pinned)
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
        bool needed = !TvReplaySelected && active && Playing && selection.Kind == ViewingSource.Live && CanCastLive && !EditingTable
            && (app.State.Presentation.Tv.Mode != ShotDisplayMode.Live || FrameTableRequested && AutomaticPocketsCurrent);
        if (!needed) { CloseShotRelay(); return; }
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
    void StopShotScenes()
    {
        shotLifetime?.Cancel(); shotLifetime?.Dispose(); shotLifetime = null;
        CloseShotRelay(); shotCarousel.Clear(); player?.ClearTableScene(); lastPhoneShot = lastTvShot = "";
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
