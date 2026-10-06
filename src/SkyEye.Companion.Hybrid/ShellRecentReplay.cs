using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    CameraRecentReplay? recentReplay;
    CorrectedClip? correctedRecentReplay;
    public string TvReplayKind => TvReplaySelected && recentReplay?.Asset.Clip.Id == tvReplay.ClipId ? "recent replay" : "saved clip";
    public bool PreparingRecentReplay { get; private set; }
    public bool RecentReplaySupported => effects.Latest is { } view && view.Connection == app.State.Camera
        && view.Status.TryGetProperty("recentReplayVersion", out var version) && version.GetInt32() == 1;
    public bool CanRequestRecentReplay => active && TvConnected && CanCastLive && RecentReplaySupported && !PreparingRecentReplay && !TransferringClip;
    public bool CanWatchRecentReplay => active && TvConnected && !PreparingRecentReplay && recentReplay is { } replay
        && replay.ConnectionId == app.State.Camera.ConnectionId && app.State.Camera.Reachability == CameraReachability.Reachable
        && System.Diagnostics.Stopwatch.GetElapsedTime(replay.ReceivedTimestamp) < RecentReplayStore.Retention - TimeSpan.FromSeconds(45);
    string recentReplayStatus = "Replay the camera's most recent footage on TV, with room audio. Recording continues.";
    public string RecentReplayStatus => !RecentReplaySupported && CanLoadClips ? "Update SkyEye Camera to use recent replay. Saved clips still work."
        : recentReplay is not null && !PreparingRecentReplay && !CanWatchRecentReplay ? "Temporary replay unavailable. Connect your TV and request recent footage again." : recentReplayStatus;

    public Task RecentReplayAsync(int seconds) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!CanRequestRecentReplay || foreground is null || disposed) return;
        var token = foreground.Token; var oldClipRequest = tvReplay.Request; var oldLiveRequest = tvLive.Request;
        PreparingRecentReplay = true; recentReplayStatus = "Preparing recent camera footage…"; DismissNotice(); Publish();
        try
        {
            var result = await clips.RecentAsync(seconds, token);
            var prepared=await correctedClips.RecentAsync(result,token);
            if (disposed || token.IsCancellationRequested || !active) return;
            recentReplay = result;
            correctedRecentReplay=prepared;
            recentReplayStatus = $"Recent replay · {result.Asset.Clip.Duration:0.#} seconds available · expires after 10 minutes. Not added to saved clips.";
            // A late preparation must not replace a newer replay/live selection.
            ObserveCast();
            if (TvConnected && oldClipRequest == tvReplay.Request && oldLiveRequest == tvLive.Request) SendRecentReplayToTv(result);
        }
        catch (OperationCanceledException) { recentReplayStatus = "Recent replay preparation cancelled. You can request it again."; }
        catch (Exception e)
        {
            recentReplayStatus = e is CameraRequestException or InvalidOperationException ? e.Message : "Recent replay could not be prepared. Check the camera and try again.";
            if (!disposed && !token.IsCancellationRequested) ShowError(recentReplayStatus);
        }
        finally { PreparingRecentReplay = false; if (!disposed) Publish(); }
    });
    public void WatchRecentReplay()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(WatchRecentReplay); return; }
        if (!CanWatchRecentReplay || recentReplay is null || disposed) return;
        try { ObserveCast(); if (TvConnected) SendRecentReplayToTv(recentReplay); Publish(); }
        catch (Exception) { ShowError("Temporary replay unavailable. Request recent footage again."); }
    }
    void SendRecentReplayToTv(CameraRecentReplay replay)
    {
        _=clips.TvSource(replay);var prepared=correctedRecentReplay??throw new InvalidOperationException("Prepare corrected recent footage again.");
        var source = correctedClips.Serve(prepared,true); var clip = replay.Asset.Clip;
        ClearSceneCheck(); CloseShotRelay(); tvLive.Clear(); tvCanPause = tvCanSeek = false;
        var request = tvReplay.Begin(replay.ConnectionId, clip.Id, $"Recent replay · {clip.Duration:0.#} seconds", clip.Duration);
        cast!.PlayClip(source, clip.Id, "Recent replay", (long)Math.Ceiling(clip.Duration * 1000), request);
        TvStatus = "Sending recent footage to TV…";
        Message = app.State.Camera.Capture == CaptureState.Active ? "Recent replay on TV · camera recording continues." : "Recent replay on TV · with room audio.";
    }
}
