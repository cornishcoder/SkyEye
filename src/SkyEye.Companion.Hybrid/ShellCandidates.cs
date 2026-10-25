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
    CancellationTokenSource? candidateVerification;
    string? lastVerificationStatus;
    public bool CheckingTable => candidateVerification is not null;
    public void CancelTableVerification() { candidateVerification?.Cancel(); table.CancelVerification(); }
    public PresentationPreferences ShotPresentation => app.State.Presentation;
    public IReadOnlyList<ShotChoice> ShotChoices => CurrentShotCatalog ? app.State.ShotChoices.Choices : [];
    public long ChoiceTableVersion => app.State.ShotChoices.TableVersion;
    bool CurrentShotCatalog => app.State.ShotChoices.Matches(app.State.Camera, app.State.Advice.TargetGroup, DateTimeOffset.UtcNow)
        && table.State.Table?.Version == app.State.ShotChoices.TableVersion && !EditingTable && CanAnalyzeTable;
    public string CandidateStatus => CheckingTable ? "Checking table… Keep the view clear and the balls still."
        : SessionMode == TableSessionMode.Dark ? "Unavailable · dark session has no image analysis or shot physics."
        : !CanAnalyzeTable ? "Become host on an active camera to check shot choices."
        : !HasTableCalibration ? "Calibrate the table before showing choices."
        : ShotChoices.Count == 0 && lastVerificationStatus is not null ? lastVerificationStatus
        : !app.State.Presentation.ShotChoicesEnabled ? TvReplaySelected ? $"Choices are off · {TvReplayKind} on TV." : "Choices are off · live table on both screens."
        : ShotChoices.Count == 0 ? shotMessage : $"{ShotChoices.Count} choices · cycle every 2 seconds · expires after 15 seconds";
    public bool CanShowChoices => active && CanAnalyzeTable && CanCastLive && !EditingTable;

    static string NoShotCandidatesMessage(TableState layout, DateTimeOffset now)
    {
        if (!ShotPlanner.Eligible(layout, now))
            return !ShotPlanner.HasSafeBallSpacing(layout)
                ? "Shot advice unavailable: some detected ball centres are inside the minimum spacing check. No candidates were returned."
                : "Shot advice unavailable: this layout does not pass all shot safety checks. No candidates were returned.";
        return "No supported shot path was found for this layout and group.";
    }

    async Task<ShotChoiceCatalog?> ComputeAndPublishShotCatalogAsync(ObserveTable request, TableObservation observation,
        CancellationToken cancellation)
    {
        if (observation.Outcome != ObservationOutcome.Ready) return null;
        try
        {
            var appSnapshot = app.State;
            var tableSnapshot = table.State;
            if (appSnapshot.Observation is not { Outcome: ObservationOutcome.Ready } current
                || current.Source != request.Source || current.AnalysisGeneration != request.AnalysisGeneration
                || current.MediaGeneration != request.MediaGeneration || current.TableVersion != observation.TableVersion
                || !appSnapshot.Camera.CanControl || appSnapshot.Camera.Capture != CaptureState.Active || appSnapshot.Camera.Scope != request.Source
                || tableSnapshot.Source != request.Source || tableSnapshot.Generation != request.AnalysisGeneration
                || tableSnapshot.Table is not { } layout || layout.Version != observation.TableVersion
                || !Enum.TryParse<AdviceTargetGroup>(layout.Group, true, out var group)
                || appSnapshot.Advice.TargetGroup != group)
                return null;

            // Publish clear/still observation first. Planning runs afterwards on a
            // worker, so it cannot hold up readiness or the canonical count baseline.
            var shots = await Task.Run(() => table.ComputeChoices(request.AnalysisGeneration, observation.TableVersion), cancellation)
                .ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var completedTable = table.State;
            var completedApp = app.State;
            if (completedTable.Source != request.Source || completedTable.Generation != request.AnalysisGeneration
                || completedTable.Table?.Version != observation.TableVersion
                || completedApp.Observation is not { Outcome: ObservationOutcome.Ready } completedObservation
                || completedObservation.Source != request.Source || completedObservation.AnalysisGeneration != request.AnalysisGeneration
                || completedObservation.MediaGeneration != request.MediaGeneration || completedObservation.TableVersion != observation.TableVersion
                || completedApp.Advice.TargetGroup != group || completedApp.Camera.Scope != request.Source || !completedApp.Camera.CanControl
                || completedApp.Camera.Capture != CaptureState.Active)
                return null;

            layout = completedTable.Table!;
            var catalog = new ShotChoiceCatalog(request.Source, layout.Version, group, layout.CapturedAt,
                layout.CapturedAt.AddSeconds(ObservationPolicy.CatalogueLifetimeSeconds),
                shots.Select(shot => new ShotChoice(shot.Id, shot.Type, shot.Pocket)).ToImmutableArray());
            var published = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.System, app.State.Version,
                new PublishShotCatalog(catalog)), cancellation).ConfigureAwait(false);
            if (!published.Succeeded) return null;

            if (catalog.Choices.IsEmpty)
            {
                shotMessage = lastVerificationStatus = NoShotCandidatesMessage(layout, DateTimeOffset.UtcNow);
            }
            else lastVerificationStatus = null;
            return catalog;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        catch (Exception)
        {
            if (app.State.Observation is { Outcome: ObservationOutcome.Ready } && table.State.Table is { } layout)
                shotMessage = lastVerificationStatus = "Shot advice could not be computed; the clear, still table remains current.";
            return null;
        }
    }

    public Task SetShotDisplayAsync(long expectedVersion, SceneTarget target, ShotDisplayMode mode) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || EditingTable) return;
        if (expectedVersion != app.State.Version) { ShowError("The controls changed. Try again."); return; }
        if (mode == ShotDisplayMode.Live) lastVerificationStatus = null;
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
        if (!active || foreground.IsCancellationRequested || !CurrentShotCatalog || ShotChoices.Count == 0) return;
        await SetShotDisplayAsync(app.State.Version, target, ShotDisplayMode.Cycling);
    });
    public SkyEyeState CommandState => app.State;
    public Task<CommandReceipt> ShowCuePathAsync(long expectedVersion, long tableVersion, string id) => ExecuteApplicationCommandAsync(
        new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new ShowAnnotation(SceneTarget.Phone, tableVersion, id, ShotAnnotations.CueBallPath)));
    public Task ShowHudAsync(long expectedVersion, SceneTarget target, long tableVersion, string id, ShotAnnotations elements) =>
        ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new ShowAnnotation(target, tableVersion, id, elements)));
    public ObserveTable? ObservationRequest => player is null || table.State.Source is not { } source ? null : new(source, table.State.Generation, MediaGeneration());
    public Task<CommandReceipt> ExecuteApplicationCommandAsync(CommandEnvelope request, CancellationToken cancellation = default) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || EditingTable) return new CommandReceipt(request.OperationId, CommandOutcome.Rejected, app.State.Version, "Use the active application surface.");
        if (SessionMode == TableSessionMode.Dark && request.Command is SkyEye.Core.Application.ObserveTable or PinShot or ShowShot or ShowAnnotation
            or SetShotDisplay { Mode: ShotDisplayMode.Cycling or ShotDisplayMode.Pinned })
            return new CommandReceipt(request.OperationId, CommandOutcome.Rejected, app.State.Version,
                "Image-based table analysis and shot physics are unavailable during a dark session.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, foreground?.Token ?? CancellationToken.None);
        var receipt = await app.DispatchAsync(request, linked.Token);
        if (request.Command is ObserveTable { Mode: ObservationMode.VerifyForAdvice }) lastVerificationStatus = receipt.Message;
        if (receipt.Succeeded && request.Command is ObserveTable { IncludeChoices: true } observedRequest
            && receipt.Observation is { Outcome: ObservationOutcome.Ready } observedResult)
        {
            var catalog = await ComputeAndPublishShotCatalogAsync(observedRequest, observedResult, linked.Token);
            if (catalog is not null && app.State.Observation is { } updatedObservation)
                receipt = receipt with { Observation = updatedObservation };
        }
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

                // A requested catalogue may arrive before its show command. Keep
                // it while the surfaces are live; display mode is not freshness.
                if (!CanAnalyzeTable || !HasTableCalibration || EditingTable) await ClearShotCatalogAsync(token);
                else if ((app.State.Presentation.ShotChoicesEnabled || app.State.ShotChoices != ShotChoiceCatalog.Empty) && !CurrentShotCatalog) await ExpireShotPresentationAsync(token);
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
    async Task ExpireShotPresentationAsync(CancellationToken token)
    {
        await ClearShotCatalogAsync(token);
        foreach (var target in new[] { SceneTarget.Phone, SceneTarget.Tv })
        {
            var surface = target == SceneTarget.Phone ? app.State.Presentation.Phone : app.State.Presentation.Tv;
            if (surface.Mode is ShotDisplayMode.Cycling or ShotDisplayMode.Pinned)
                await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.System, app.State.Version, new ClearAnnotations(target)), token);
        }
        shotMessage = "Choices expired. Request a fresh observation when useful.";
    }
    async Task ReconcileShotCatalogAsync(CancellationToken token)
    {
        if (app.State.ShotChoices != ShotChoiceCatalog.Empty && !CurrentShotCatalog)
        {
            await ClearShotCatalogAsync(token);
            shotMessage = "Choices expired or the layout changed. Request a fresh observation.";
        }
    }
    async Task RefreshShotChoicesAsync(CancellationToken token, Guid? observationOperationId = null, CommandOrigin origin = CommandOrigin.Touch)
    {
        if (candidateVerification is not null) return;
        using var checking = CancellationTokenSource.CreateLinkedTokenSource(token);
        candidateVerification = checking; token = checking.Token;
        lastVerificationStatus = null;
        shotMessage = "Checking table…";
        try
        {
            await ClearShotCatalogAsync(token); RenderShotScenes(); Publish();
            await VerifyTableWithDiagnosticsPausedAsync(token, operationId: observationOperationId, origin: origin);
            token.ThrowIfCancellationRequested();
            var snapshot = table.State; var layout = snapshot.Table;
            if (!CanAnalyzeTable || EditingTable || snapshot.Source != app.State.Camera.Scope || app.State.Observation?.Outcome != ObservationOutcome.Ready || layout is null || !layout.Settled || layout.Occluded)
            {
                await ClearShotCatalogAsync(token); return;
            }
            shotCarousel.Use(snapshot.Choices, layout.CapturedAt);
            shotMessage = shotCarousel.Candidates.Length == 0 ? NoShotCandidatesMessage(layout, DateTimeOffset.UtcNow) : "";
            lastVerificationStatus = shotCarousel.Candidates.Length == 0 ? shotMessage : null;
        }
        catch (OperationCanceledException) { lastVerificationStatus = shotMessage = "Table check cancelled."; }
        catch (Exception) { await ClearShotCatalogAsync(token); shotMessage = "Table view unavailable. Request again when useful."; }
        finally { candidateVerification = null; Publish(); }
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
            var tvGeneration = tv ? BackgroundTvService.Instance?.DecoderGeneration ?? 0 : 0;
            var key = $"{mediaGeneration}:{tvGeneration}:{surface.Mode}:{surface.Annotations}:{shot?.Id}:{app.State.ShotChoices.ExpiresAt.ToUnixTimeMilliseconds()}:{shotMessage}:{CanAnalyzeTable}:{EditingTable}:{Playing}:{(shot is null ? Android.OS.SystemClock.ElapsedRealtime() / 2000 : 0)}";
            if (last == key) return;
            last = key;
            if (surface.Mode is not (ShotDisplayMode.Cycling or ShotDisplayMode.Pinned) || selection.Kind != ViewingSource.Live || EditingTable || !Playing)
            { player.ClearTableSceneForTarget(tv); if (tv && active) BackgroundTvService.Scene(""); return; }
            var caption = shot is null ? CandidateStatus : $"{(surface.Mode == ShotDisplayMode.Pinned ? "Pinned" : "Candidate " + (index + 1) + " / " + shotCarousel.Candidates.Length)} · {shot.Type} · pocket {shot.Pocket + 1}";
            var number = shot is null ? 0 : Array.FindIndex(shotCarousel.Candidates, s => s.Id == shot.Id) + 1;
            try
            {
                var scene = shot is not null ? ShotSceneProjection.Create(shot, snapshot.Calibration!, ++sceneRevision, mediaGeneration, now, app.State.ShotChoices.ExpiresAt,
                    tv ? $"Candidate {number} · {shot.Type} · pocket {shot.Pocket + 1}" : $"{shot.Type} · pocket {shot.Pocket + 1}",
                    surface.Mode == ShotDisplayMode.Pinned, surface.Annotations, number, detailed:tv)
                    : new TableScene(++sceneRevision, mediaGeneration, snapshot.Table?.Version ?? 0, now, TimeSpan.FromSeconds(3), [], [], [], caption);
                var json = JsonSerializer.Serialize(scene, sceneJson);
                if (!player.SetTableSceneForTarget(json, tv)) last = "";
                if (tv) BackgroundTvService.Scene(json);
            }
            catch (ArgumentException error) { player.ClearTableSceneForTarget(tv); shotMessage = "Candidate is outside the camera view. Check calibration."; Android.Util.Log.Warn("SkyEyeShell", "Shot scene refused: " + error.Message); }
        }
        Surface(app.State.Presentation.Phone, false, ref lastPhoneShot);
        Surface(app.State.Presentation.Tv, true, ref lastTvShot);
    }
    void ManageShotRelay()
    {
        EnsureBackgroundTv();
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
