using System.Text.Json;
using SkyEye.Core;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    static readonly JsonSerializerOptions sceneJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    SkyEye.Companion.OverlayRelay? sceneRelay;
    SceneRelayListener? sceneRelayListener;
    CancellationTokenSource? sceneLifetime;
    long sceneRevision;
    public bool SceneCheck { get; private set; }
    public bool CanSceneCheck => !TvReplaySelected && IsLab && active && Playing && selection.Kind == ViewingSource.Live && player is not null && !EditingTable;
    public string SceneCheckStatus { get; private set; } = "A labelled scene check works even with the table lights off.";

    public Task ToggleSceneCheckAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (SceneCheck) { ClearSceneCheck(); Publish(); return; }
        if (!CanSceneCheck || player is null || foreground is null) return;
        suppressShotLoop = true; StopShotScenes();
        try { await sceneRelayClosing.WaitAsync(foreground.Token); }
        catch (OperationCanceledException) { suppressShotLoop = false; return; }
        var owner = player;
        using var metrics = JsonDocument.Parse(owner.PlaybackMetrics()!);
        var generation = metrics.RootElement.GetProperty("mediaGeneration").GetInt64();
        var version = ++sceneRevision;
        TableScene Create(long media, long revision) => new(revision, media, 0, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(60),
            [new([new(.25, .3), new(.5, .5), new(.75, .7)], SceneInk.Cyan), new([new(.3, .7), new(.7, .3)], SceneInk.Yellow, true)],
            [new(new(.5, .5), .035, SceneInk.Green, SceneMarkerKind.Ball), new(new(.75, .7), .035, SceneInk.Cyan, SceneMarkerKind.Pocket), new(new(.7, .3), .025, SceneInk.Yellow, SceneMarkerKind.Ghost)],
            [new(new(.5, .55), "Shared highlight")], $"Scene check v{version} · no shot advice", new("Demo A", 2, "Demo B", 1));
        // Exercise the actual Android publication gate, not just the C# serializer.
        if (owner.SetTableScene(JsonSerializer.Serialize(Create(generation + 1, version), sceneJson))) throw new InvalidOperationException("Scene accepted an unrelated media source.");
        if (!owner.SetTableScene(JsonSerializer.Serialize(Create(generation, version), sceneJson))) { suppressShotLoop = false; ShowError("Video changed. Try the scene check again."); return; }
        if (owner.SetTableScene(JsonSerializer.Serialize(Create(generation, version), sceneJson))) throw new InvalidOperationException("Scene accepted a duplicate revision.");
        Android.Util.Log.Info("SkyEyeShell", "Scene gate passed: wrong source and duplicate revision rejected");
        SceneCheck = true; suppressShotLoop = false;
        sceneLifetime = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token);
        var token = sceneLifetime.Token;
        try
        {
            var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No current activity.");
            sceneRelayListener = new(this);
            sceneRelay = new(activity, owner, client.Address, sceneRelayListener, () => cast?.Metrics() ?? "{}", sharedScene: true);
            SceneCheckStatus = "Preparing phone and TV scene check…"; Publish();
            for (int i = 0; i < 30 && sceneRelay is { Ready: false }; i++) await Task.Delay(1000, token);
            if (token.IsCancellationRequested || !SceneCheck || player != owner || sceneRelay is null) return;
            if (!sceneRelay.Ready) { ClearSceneCheck(); ShowError("Scene feed did not become ready. The camera is still recording."); return; }
            cast?.Configure(sceneRelay.Url, true);
            SceneCheckStatus = "Scene ready · expires after 60 seconds · choose TV if needed";
            Android.Util.Log.Info("SkyEyeShell", "Phone scene drawn: " + owner.SceneMetrics());
            Publish();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested) { ClearSceneCheck(); ShowError("Scene check could not start. Live camera playback remains available."); } }
    });

    void ClearSceneCheck()
    {
        if (!SceneCheck && sceneRelay is null && sceneLifetime is null) return;
        sceneLifetime?.Cancel(); sceneLifetime?.Dispose(); sceneLifetime = null;
        player?.ClearTableScene();
        var relay = sceneRelay; sceneRelay = null;
        // Only replace this relay's receiver content. Another app may have taken over the TV.
        if (relay is not null) cast?.ReturnToSourceIfOwned(relay.Url, CanCastLive && effects.Latest is { } view ? view.Stream : "");
        if (relay is not null) sceneRelayClosing = relay.DisposeAsync().AsTask();
        sceneRelayListener?.Dispose(); sceneRelayListener = null;
        SceneCheck = false; SceneCheckStatus = "Scene cleared · live camera remains available";
        lastPhoneShot = lastTvShot = "";
    }
    sealed class SceneRelayListener(ShellSession owner) : Java.Lang.Object, Bridge.OverlayEncoder.IListener
    {
        public void OnError(string? message)
        {
            if (!owner.SceneCheck || owner.sceneRelayListener != this) return;
            owner.ClearSceneCheck(); owner.ShowError("Annotated feed interrupted. TV returned to the camera feed.");
        }
    }
}
