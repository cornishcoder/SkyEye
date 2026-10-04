using Android.Widget;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Text.Json;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    readonly CandidateCarousel carousel = new();
    Button overlayToggle = null!;
    TextView overlayStatus = null!;
    string overlayMessage = "Candidate overlay is off.";
    long overlayRefreshAt;
    long overlayRelayRetryAt;
    int overlayRun;
    OverlayRelay? overlayRelay;
    OverlayListener? overlayListener;
    void BuildCandidateOverlayUi(LinearLayout root)
    {
        overlayToggle = Add(root, "Candidate overlay: off", () =>
            _ = DispatchAppAsync(new ShowShotChoices(!renderedAppState.Presentation.ShotChoicesEnabled)));
        overlayStatus = Label("Turn on to cycle shots every 2 seconds; refresh every 10 seconds.", 13); root.AddView(overlayStatus);
        BindAppState();
    }
    void ConfigureCandidateCast(bool owns)
        => cast?.Configure(overlayEnabled && resumed && overlayRelay is { Ready: true } ? overlayRelay.Url : stream, owns);
    void StartCandidateOverlay()
    {
        if (!resumed || lifetime.IsCancellationRequested) return;
        int run = ++overlayRun; carousel.Start(Android.OS.SystemClock.ElapsedRealtime()); overlayRefreshAt = 0;
        ClearCandidatePresentation("Checking table…");
        _ = RunCandidateOverlayAsync(run);
    }
    void PauseCandidateOverlay()
    {
        ++overlayRun; var relay = overlayRelay; overlayRelay = null;
        if (relay is not null) _ = relay.DisposeAsync();
        player.SetOverlay("{}"); cast?.Configure(stream, app.State.Camera.CanControl);
        carousel.Clear(); if (tableView is not null) RefreshCandidates();
    }
    async Task RunCandidateOverlayAsync(int run)
    {
        try
        {
            while (run == overlayRun && overlayEnabled && resumed && !lifetime.IsCancellationRequested)
            {
                if (!connected || !app.State.Camera.CanControl || currentState != "Active") ClearCandidatePresentation("Connect to an active camera and become host to see candidates.");
                else
                {
                    if (overlayRelay is null && Android.OS.SystemClock.ElapsedRealtime() >= overlayRelayRetryAt)
                    {
                        overlayListener = new OverlayListener(this);
                        try { overlayRelay = new OverlayRelay(this, player, client.Address, overlayListener, () => cast?.Metrics() ?? "{}"); }
                        catch { overlayRelayRetryAt = Android.OS.SystemClock.ElapsedRealtime() + 30000; overlayMessage = "TV candidate view unavailable. Phone overlay is available."; }
                    }
                    ConfigureCandidateCast(true);
                    if (calibration is null) ClearCandidatePresentation("Calibrate the table below to see candidates.");
                    else if (!busy && Android.OS.SystemClock.ElapsedRealtime() >= overlayRefreshAt)
                    {
                        overlayRefreshAt = Android.OS.SystemClock.ElapsedRealtime() + CandidateCarousel.RefreshMilliseconds;
                        try { await RefreshAutomaticCandidatesAsync(run); }
                        catch (OperationCanceledException) { }
                        catch { if (run == overlayRun) ClearCandidatePresentation("Table view unavailable. Retrying automatically…"); }
                    }
                }
                if (run != overlayRun) break;
                RefreshCandidates();
                int index = carousel.Index(Android.OS.SystemClock.ElapsedRealtime(), DateTimeOffset.UtcNow);
                overlayStatus.Text = index < 0 ? carousel.Candidates.Length > 0 ? "Waiting for a fresh table view…" : overlayMessage
                    : $"Candidate {index + 1} of {carousel.Candidates.Length} · {carousel.Candidates[index].Type} · refresh every 10 seconds";
                await Task.Delay(200, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    async Task RefreshAutomaticCandidatesAsync(int run)
    {
        await UpdateTableAsync();
        if (run != overlayRun || !overlayEnabled || !app.State.Camera.CanControl) return;
        if (tableState is { Settled: false, Occluded: false })
        {
            ClearCandidatePresentation("Waiting for the balls to settle…");
            await Task.Delay(2100, lifetime.Token); if (run != overlayRun || !overlayEnabled) return; await UpdateTableAsync();
        }
        if (run != overlayRun || !overlayEnabled || !app.State.Camera.CanControl) return;
        var snapshot = tableAnalysis.State; var state = snapshot.Table;
        if (state is null || state.Occluded || !state.Settled || snapshot.Source != app.State.Camera.Scope)
        {
            ClearCandidatePresentation(tableState?.Occluded == true ? "Table unclear or obstructed. Retrying automatically…" : "Waiting for the balls to settle…"); return;
        }
        var confirmed = tableAnalysis.Confirm(state.Version);
        carousel.Update(confirmed, DateTimeOffset.UtcNow);
        overlayMessage = carousel.Candidates.Length == 0 ? "No clear candidate for this layout and group." : "";
        PublishCandidatePresentation(); ShowTable();
    }
    void ClearCandidatePresentation(string message)
    {
        carousel.Clear(); overlayMessage = message;
        if (overlayStatus is not null) overlayStatus.Text = message;
        PublishCandidatePresentation();
    }
    void PublishCandidatePresentation()
    {
        if (player is null) return;
        var snapshot = tableAnalysis.State; var currentCalibration = snapshot.Calibration;
        double[] Image(Point p) { var mapped = currentCalibration!.Unmap(p); return [mapped.X, mapped.Y]; }
        var state = snapshot.Table;
        bool current = state is not null && app.State.Camera.CanControl && snapshot.Source == app.State.Camera.Scope && state.Group == group
            && carousel.Candidates.All(s => s.TableVersion == state.Version);
        var shots = currentCalibration is null || !current ? [] : carousel.Candidates.Select(s => new
        {
            type = s.Type, pocket = s.Pocket + 1, cue = s.CuePath.Select(Image).ToArray(), target = s.TargetPath.Select(Image).ToArray(), ghost = Image(s.GhostBall)
        }).ToArray();
        player.SetOverlay(JsonSerializer.Serialize(new { enabled = overlayEnabled && resumed, message = overlayMessage, origin = carousel.OriginMilliseconds,
            capturedAt = tableState?.CapturedAt.ToUnixTimeMilliseconds() ?? 0, updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            expiresAt = carousel.ExpiresAt == default ? 0 : carousel.ExpiresAt.ToUnixTimeMilliseconds(), shots }, JsonDefaults.Options));
    }
    sealed class OverlayListener(MainActivity owner) : Java.Lang.Object, Bridge.OverlayEncoder.IListener
    {
        public void OnError(string? message) => owner.RunOnUiThread(() => { var relay = owner.overlayRelay; owner.overlayRelay = null; owner.overlayRelayRetryAt = Android.OS.SystemClock.ElapsedRealtime() + 30000; if (relay is not null) _ = relay.DisposeAsync(); owner.cast?.Configure(owner.stream, owner.app.State.Camera.CanControl); owner.Say(message ?? "TV candidate view unavailable."); });
    }
}
