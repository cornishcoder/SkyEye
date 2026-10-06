using System.Text.Json;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    readonly TvReplaySelection tvReplay = new();
    readonly TvLiveSelection tvLive = new();
    bool tvCanPause, tvCanSeek;
    public bool TvReplaySelected => tvReplay.Selected;
    public string? TvReplayRequest => tvReplay.Request;
    public long TvReplayPosition => tvReplay.PositionSeconds;
    public long TvReplayDuration => tvReplay.DurationSeconds;
    public bool CanPauseTvReplay => TvConnected && tvReplay.CanControl && tvCanPause && tvReplay.PlayerState == 2;
    public bool CanResumeTvReplay => TvConnected && tvReplay.CanControl && tvReplay.PlayerState == 3;
    public bool CanSeekTvReplay => TvConnected && tvReplay.CanControl && tvCanSeek;
    public string TvCommandStatus => tvReplay.Command is not { } command ? "" : tvReplay.CommandOutcome switch
    {
        TvCommandOutcome.Pending => "Waiting for TV confirmation…",
        TvCommandOutcome.Confirmed => command.Action switch { TvReplayAction.Pause => "TV confirmed pause", TvReplayAction.Resume => "TV confirmed resume", _ => $"TV confirmed seek to {Clock(command.PositionMs / 1000)}" },
        TvCommandOutcome.Rejected => "TV declined that request. Check playback and try again.",
        TvCommandOutcome.Unknown => "TV has not confirmed that request. Check playback before retrying.",
        _ => ""
    };
    public string TvLiveConfirmation => tvLive.Outcome switch
    {
        TvCommandOutcome.Pending => "Waiting for TV to play live…", TvCommandOutcome.Confirmed => "TV confirmed live playback",
        TvCommandOutcome.Unknown => "TV has not confirmed live playback yet. You can retry Play live on TV.", _ => ""
    };
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

    public Task PlayClipOnTvAsync(ShellClipChoice choice) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || cast is null || foreground is null || TransferringClip || PreparingRecentReplay) return;
        ObserveCast();
        if (!TvConnected) { TvStatus = "Choose a TV before playing a clip"; Publish(); return; }
        var admitted = catalog;
        var clip = admitted?.ConnectionId == choice.ConnectionId ? admitted.Clips.FirstOrDefault(c => c.Id == choice.Id) : null;
        if (admitted is null || clip is null) { ShowError("Refresh camera clips before playing this shot on TV."); return; }
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(foreground.Token,clipLifetime.Token);
        transferCancellation=cancellation;
        try
        {
            preparingCorrected=true;
            var token=cancellation.Token;var previousClip=tvReplay.Request;var previousLive=tvLive.Request;var connection=app.State.Camera.ConnectionId;
            ClipStatus="Preparing corrected TV footage…";Publish();
            var prepared=await correctedClips.SavedAsync(admitted,clip,null,token);
            if(!active||disposed||token.IsCancellationRequested||connection!=app.State.Camera.ConnectionId||previousClip!=tvReplay.Request||previousLive!=tvLive.Request)return;
            var source = correctedClips.Serve(prepared,false);RefreshLocalClips();
            ClearSceneCheck(); CloseShotRelay();
            tvLive.Clear(); tvCanPause = tvCanSeek = false;
            var request = tvReplay.Begin(admitted.ConnectionId, clip.Id, clip.Name, clip.Duration);
            await app.DispatchAsync(new(Guid.NewGuid(), SkyEye.Core.Application.CommandOrigin.System, app.State.Version,
                new SkyEye.Core.Application.PublishReplayPresentation(SkyEye.Core.Application.SceneTarget.Tv, admitted.ConnectionId, clip.Id)), token);
            cast.PlayClip(source, clip.Id, clip.Name, (long)Math.Ceiling(clip.Duration * 1000), request);
            ClipStatus="Prepared clip ready, with room audio.";
            TvStatus = "Sending your saved shot to TV…";
            Message = app.State.Camera.Capture == SkyEye.Core.CaptureState.Active
                ? "Saved shot on TV · camera recording continues." : "Saved shot on TV · with room audio.";
            DismissNotice(); Publish();
        }
        catch (OperationCanceledException) { if(!disposed)ClipStatus="Preparation cancelled. You can retry this clip."; }
        catch (Exception) { ShowError("The clip could not be sent. Refresh camera clips and try again."); }
        finally{if(transferCancellation==cancellation)transferCancellation=null;preparingCorrected=false;if(!disposed)Publish();}
    });
    bool ObserveTvReplay(JsonElement metrics)
    {
        if (!tvReplay.Selected || !metrics.TryGetProperty("request", out var request)
            || !metrics.TryGetProperty("clipId", out var clip)
            || !metrics.TryGetProperty("positionMs", out var position)
            || !metrics.TryGetProperty("durationMs", out var duration)
            || !metrics.TryGetProperty("playerState", out var state)) return false;
        var changed = tvReplay.Observe(request.GetString(), clip.GetString(), position.GetInt64(), duration.GetInt64(), state.GetInt32(),
            metrics.TryGetProperty("idleReason", out var idle) ? idle.GetInt32() : 0);
        if (tvReplay.Request == request.GetString() && tvReplay.ClipId == clip.GetString())
        {
            var pause = metrics.TryGetProperty("canPause", out var canPause) && canPause.GetBoolean();
            var seek = metrics.TryGetProperty("canSeek", out var canSeek) && canSeek.GetBoolean();
            changed |= pause != tvCanPause || seek != tvCanSeek; tvCanPause = pause; tvCanSeek = seek;
            if (metrics.TryGetProperty("commandId", out var id) && metrics.TryGetProperty("commandRequest", out var commandRequest)
                && metrics.TryGetProperty("commandOutcome", out var outcome))
                changed |= tvReplay.ObserveCommand(commandRequest.GetString(), id.GetString(), outcome.GetString());
        }
        if (changed && tvReplay.Confirmed && tvReplay.PlayerState == 1)
            TvStatus = tvReplay.IdleReason == 1 ? "TV clip finished · play another shot or return live" : "TV clip stopped · play another shot or return live";
        return changed;
    }
    public void ControlTvReplay(string? request, TvReplayAction action, long seconds = 0)
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(() => ControlTvReplay(request, action, seconds)); return; }
        if (!active || disposed || cast is null) return;
        ObserveCast();
        if (!TvConnected || request != tvReplay.Request || !tvReplay.CanControl
            || action == TvReplayAction.Pause && !CanPauseTvReplay || action == TvReplayAction.Seek && !CanSeekTvReplay) return;
        var command = tvReplay.BeginCommand(request, Guid.NewGuid().ToString("N"), action, seconds, DateTimeOffset.UtcNow);
        if (command is null) return;
        try
        {
            if (!cast.ControlClip(command.Request, command.Id, action.ToString().ToLowerInvariant(), command.PositionMs))
                tvReplay.ObserveCommand(command.Request, command.Id, "rejected");
        }
        catch { tvReplay.ObserveCommand(command.Request, command.Id, "rejected"); }
        ObserveCast(); Publish();
    }
    bool ObserveTvLive(JsonElement metrics)
    {
        if (!metrics.TryGetProperty("liveRequest", out var request) || !metrics.TryGetProperty("stream", out var stream)
            || !metrics.TryGetProperty("playerState", out var state)) return false;
        var changed = tvLive.Observe(request.GetString(), stream.GetString(), state.GetInt32());
        if (changed) TvStatus = tvLive.PlayerState switch
        {
            4 => "TV buffering live table…", 3 => "TV live playback paused", 1 => "TV live playback stopped",
            2 when tvLive.Stream == "scene" => AutomaticPocketsCurrent && FrameTableRequested ? "TV playing rectangular table" : "TV playing table overlay",
            2 when tvLive.Stream == "candidates" => "TV playing candidate overlay", 2 => "TV playing live camera",
            _ => "Waiting for TV live playback…"
        };
        return changed;
    }
    bool ExpireTvCommands() => tvReplay.ExpireCommand(DateTimeOffset.UtcNow) | tvLive.Expire(DateTimeOffset.UtcNow);
}
