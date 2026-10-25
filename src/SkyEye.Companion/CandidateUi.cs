using Android.Widget;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Text.Json;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;
using Button = Android.Widget.Button;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    readonly CandidateCarousel carousel = new();
    Button overlayToggle = null!;
    TextView overlayStatus = null!;
    string overlayMessage = "Candidate overlay is off.";
    long overlayRelayRetryAt;
    int overlayRun;
    OverlayRelay? overlayRelay;
    OverlayListener? overlayListener;
    void BuildCandidateOverlayUi(LinearLayout root)
    {
        overlayToggle = Add(root, "Candidate overlay: off", () => _ = RequestChoicesUiAsync());
        Add(root, "Observe table / refresh choices", () => _ = ActionAsync(async () =>
        { if (overlayEnabled) await RequestCandidatesAsync(overlayRun); else await UpdateTableAsync(); }));
        overlayStatus = Label("Request choices to observe once; computed choices cycle every 2 seconds.", 13); root.AddView(overlayStatus);
        BindAppState();
    }
    void ConfigureCandidateCast(bool owns)
    {
        bool tvConnected = false;
        try { using var metrics = JsonDocument.Parse(cast?.Metrics() ?? "{}"); tvConnected = metrics.RootElement.TryGetProperty("connected", out var value) && value.GetBoolean(); } catch { }
        if (!tvConnected || !overlayEnabled)
        {
            var previous = overlayRelay; overlayRelay = null;
            if (previous is not null) _ = previous.DisposeAsync();
            cast?.Configure(stream, owns); return;
        }
        if(resumed&&connected&&!offline&&currentState=="Active"&&overlayRelay is null&&Android.OS.SystemClock.ElapsedRealtime()>=overlayRelayRetryAt)
        {
            overlayListener=new OverlayListener(this);
            try{overlayRelay=new OverlayRelay(this,player,client.Address,overlayListener,()=>cast?.Metrics()??"{}");}
            catch{overlayRelayRetryAt=Android.OS.SystemClock.ElapsedRealtime()+30000;}
        }
        cast?.Configure(resumed&&overlayRelay is{Ready:true}?overlayRelay.Url:stream,owns);
    }
    void StartCandidateOverlay()
    {
        if (!resumed || lifetime.IsCancellationRequested) return;
        int run = ++overlayRun; carousel.Start(Android.OS.SystemClock.ElapsedRealtime());
        ClearCandidatePresentation("Checking table…");
        _ = RunCandidateOverlayAsync(run);
    }
    async Task RequestChoicesUiAsync()
    {
        var enabled = !overlayEnabled;
        await DispatchAppAsync(new ShowShotChoices(enabled));
        if (enabled && overlayEnabled) await RequestCandidatesAsync(overlayRun);
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
                    ConfigureCandidateCast(true);
                    if (calibration is null) ClearCandidatePresentation("Calibrate the table below to see candidates.");
                }
                if (run != overlayRun) break;
                RefreshCandidates();
                int index = carousel.Index(Android.OS.SystemClock.ElapsedRealtime(), DateTimeOffset.UtcNow);
                overlayStatus.Text = index < 0 ? carousel.Candidates.Length > 0 ? "Waiting for a fresh table view…" : overlayMessage
                    : $"Candidate {index + 1} of {carousel.Candidates.Length} · {carousel.Candidates[index].Type} · expires after 15 seconds";
                await Task.Delay(200, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
    }
    async Task RequestCandidatesAsync(int run)
    {
        try { await RefreshAutomaticCandidatesAsync(run); }
        catch (OperationCanceledException) { }
        catch { if (run == overlayRun) ClearCandidatePresentation("Table view unavailable. Request again when useful."); }
    }
    async Task RefreshAutomaticCandidatesAsync(int run)
    {
        await UpdateTableAsync(ObservationMode.VerifyForAdvice);
        if (run != overlayRun || !overlayEnabled || !app.State.Camera.CanControl) return;
        if (run != overlayRun || !overlayEnabled || !app.State.Camera.CanControl) return;
        var snapshot = tableAnalysis.State; var state = snapshot.Table;
        if (state is null || state.Occluded || !state.Settled || snapshot.Source != app.State.Camera.Scope)
        {
            ClearCandidatePresentation(tableState?.Occluded == true ? "Table unclear or obstructed. Request again when clear." : "Settlement is not established. Request again when the balls are still."); return;
        }
        carousel.Update(state, DateTimeOffset.UtcNow);
        overlayMessage = carousel.Candidates.Length == 0 ? NoShotCandidatesMessage(state, DateTimeOffset.UtcNow) : "";
        PublishCandidatePresentation(); ShowTable();
    }
    static string NoShotCandidatesMessage(TableState state, DateTimeOffset now)
    {
        if (!ShotPlanner.Eligible(state, now))
            return !ShotPlanner.HasSafeBallSpacing(state)
                ? "Shot advice unavailable: some detected ball centres are inside the minimum spacing check. No candidates were returned."
                : "Shot advice unavailable: this layout does not pass all shot safety checks. No candidates were returned.";
        return "No supported shot path was found for this layout and group.";
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
