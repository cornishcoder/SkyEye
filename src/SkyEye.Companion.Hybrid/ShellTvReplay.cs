using System.Text.Json;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    readonly TvReplaySelection tvReplay = new();
    public bool TvReplaySelected => tvReplay.Selected;
    public string TvReplayProgress => !tvReplay.Selected ? "" : !tvReplay.Confirmed ? "Waiting for the TV…"
        : $"{tvReplay.Name} · {Clock(tvReplay.PositionSeconds)} / {Clock(tvReplay.DurationSeconds)} · {ReplayState}";
    string ReplayState => tvReplay.PlayerState switch
    {
        2 => "Playing with room audio", 3 => "Paused", 4 => "Buffering",
        1 when tvReplay.IdleReason == 1 => "Finished",
        1 when tvReplay.IdleReason == 4 => "Playback interrupted",
        _ => "Waiting for TV playback"
    };
    static string Clock(long seconds) => $"{seconds / 60}:{seconds % 60:00}";

    public void PlayClipOnTv(ShellClipChoice choice)
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(() => PlayClipOnTv(choice)); return; }
        if (!active || disposed || cast is null) return;
        ObserveCast();
        if (!TvConnected) { TvStatus = "Choose a TV before playing a clip"; Publish(); return; }
        var admitted = catalog;
        var clip = admitted?.ConnectionId == choice.ConnectionId ? admitted.Clips.FirstOrDefault(c => c.Id == choice.Id) : null;
        if (admitted is null || clip is null) { ShowError("Refresh camera clips before playing this shot on TV."); return; }
        try
        {
            var source = clips.TvSource(admitted, clip);
            ClearSceneCheck(); CloseShotRelay();
            var request = tvReplay.Begin(admitted.ConnectionId, clip.Id, clip.Name, clip.Duration);
            cast.PlayClip(source, clip.Id, clip.Name, (long)Math.Ceiling(clip.Duration * 1000), request);
            TvStatus = "Sending your saved shot to TV…";
            Message = "Saved shot on TV · camera recording continues."; DismissNotice(); Publish();
        }
        catch (Exception) { ShowError("The clip could not be sent. Refresh camera clips and try again."); }
    }
    bool ObserveTvReplay(JsonElement metrics)
    {
        if (!tvReplay.Selected || !metrics.TryGetProperty("request", out var request)
            || !metrics.TryGetProperty("clipId", out var clip)
            || !metrics.TryGetProperty("positionMs", out var position)
            || !metrics.TryGetProperty("durationMs", out var duration)
            || !metrics.TryGetProperty("playerState", out var state)) return false;
        return tvReplay.Observe(request.GetString(), clip.GetString(), position.GetInt64(), duration.GetInt64(), state.GetInt32(),
            metrics.TryGetProperty("idleReason", out var idle) ? idle.GetInt32() : 0);
    }
}
