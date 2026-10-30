using System.Text.Json;
using System.Collections.Immutable;
using SkyEye.Core;
using Point = SkyEye.Core.Point;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    sealed record IdentifyBallsFrame(Ball[] Balls, bool LayoutUncertain, bool Obstructed,
        long MediaGeneration, long TableVersion, long FrameId, DateTimeOffset CapturedAt,
        TableCalibration Calibration);

    static readonly JsonSerializerOptions sceneJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    static readonly Point[] identifyLabelOffsets =
    [
        new(0, -.075), new(.075, 0), new(0, .075), new(-.075, 0),
        new(.055, -.055), new(.055, .055), new(-.055, .055), new(-.055, -.055)
    ];
    SkyEye.Companion.OverlayRelay? sceneRelay;
    SceneRelayListener? sceneRelayListener;
    CancellationTokenSource? sceneLifetime;
    long sceneRevision;
    IdentifyBallsFrame? latestIdentifyBallsFrame;
    bool identifyBallsEnabled;
    string identifyBallsOverlayStatus = "";
    public bool SceneCheck { get; private set; }
    public bool CanSceneCheck => !TvReplaySelected && IsLab && active && Playing && selection.Kind == ViewingSource.Live && player is not null && !EditingTable;
    public string SceneCheckStatus { get; private set; } = "A labelled scene check works even with the table lights off.";
    public bool IdentifyBallsEnabled => identifyBallsEnabled;
    public bool CanToggleIdentifyBalls => LiveDetectRunning && HasTableCalibration && Playing
        && selection.Kind == ViewingSource.Live && player is not null && !EditingTable && !SceneCheck;
    public string IdentifyBallsShortcutLabel => identifyBallsEnabled ? "Hide ball identity overlay" : "Show ball identity overlay";
    public string IdentifyBallsTileText => identifyBallsEnabled ? "Identities on" : "Identities off";
    public string IdentifyBallsOverlayStatus => identifyBallsOverlayStatus;

    public Task ToggleIdentifyBallsAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (!CanToggleIdentifyBalls) return;
        identifyBallsEnabled = !identifyBallsEnabled;
        if (!identifyBallsEnabled)
        {
            player?.ClearIdentityScene();
            identifyBallsOverlayStatus = "";
        }
        else if (latestIdentifyBallsFrame is { } frame)
            PublishIdentifyBallsScene(frame);
        else
        {
            player?.ClearIdentityScene();
            identifyBallsOverlayStatus = "On · waiting for the next fresh Live Detect frame.";
        }
        Changed?.Invoke();
    });

    void ResetIdentifyBallsOverlay(bool notify = true)
    {
        bool wasVisible = identifyBallsEnabled || latestIdentifyBallsFrame is not null;
        identifyBallsEnabled = false;
        latestIdentifyBallsFrame = null;
        identifyBallsOverlayStatus = "";
        player?.ClearIdentityScene();
        if (notify && wasVisible) Changed?.Invoke();
    }

    void InvalidateIdentifyBallsFrame()
    {
        latestIdentifyBallsFrame = null;
        player?.ClearIdentityScene();
        if (identifyBallsEnabled)
            identifyBallsOverlayStatus = "On · waiting for a fresh camera frame; overlay cleared.";
    }

    void UpdateIdentifyBallsFrame(IdentifyBallsFrame frame)
    {
        latestIdentifyBallsFrame = frame;
        if (identifyBallsEnabled) PublishIdentifyBallsScene(frame);
    }

    void PublishIdentifyBallsScene(IdentifyBallsFrame frame)
    {
        if (!identifyBallsEnabled || !CanToggleIdentifyBalls || player is null)
        {
            player?.ClearIdentityScene();
            return;
        }

        try
        {
            using var metrics = JsonDocument.Parse(player.PlaybackMetrics()!);
            if (metrics.RootElement.GetProperty("mediaGeneration").GetInt64() != frame.MediaGeneration)
            {
                player.ClearIdentityScene();
                identifyBallsOverlayStatus = "On · waiting for a fresh camera frame; overlay cleared.";
                return;
            }

            int videoWidth = metrics.RootElement.GetProperty("width").GetInt32();
            int videoHeight = metrics.RootElement.GetProperty("height").GetInt32();
            var scene = CreateIdentifyBallsScene(frame, videoWidth, videoHeight);
            var json = JsonSerializer.Serialize(scene, sceneJson);
            if (!player.SetIdentityScene(json))
            {
                identifyBallsOverlayStatus = "On · the video changed; waiting for the next fresh frame.";
                return;
            }

            int shown = scene.Markers.Length - PoolGeometry.Pockets.Length;
            string count = shown == frame.Balls.Length ? $"{shown} detector balls labeled" : $"{shown} of {frame.Balls.Length} detector balls labeled";
            string quality = frame.Obstructed ? " · obstructed" : frame.LayoutUncertain ? " · uncertain layout" : "";
            identifyBallsOverlayStatus = $"On · {count}{quality} · frame {frame.FrameId} at {frame.CapturedAt.ToLocalTime():HH:mm:ss}. Legend: R red · Y yellow · 8 black · C cue · ? other · P1–P6 pockets.";
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException)
        {
            player?.ClearIdentityScene();
            identifyBallsOverlayStatus = "On · calibrated overlay unavailable for this frame.";
            Android.Util.Log.Warn("SkyEyeShell", "Identify balls scene unavailable: " + error.GetType().Name);
        }
    }

    TableScene CreateIdentifyBallsScene(IdentifyBallsFrame frame, int videoWidth, int videoHeight)
    {
        if (videoWidth <= 0 || videoHeight <= 0) throw new ArgumentException("Video size is unavailable.");
        ScenePoint Image(Point tablePoint)
        {
            var point = frame.Calibration.Unmap(tablePoint);
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)
                || point.X is < -.001 or > 1.001 || point.Y is < -.001 or > 1.001)
                throw new ArgumentException("Calibrated geometry is outside the current image.");
            return new(Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1));
        }
        var rail = new[] { new Point(0, 0), new Point(2, 0), new Point(2, 1), new Point(0, 1), new Point(0, 0) }
            .Select(Image).ToImmutableArray();
        var paths = new[] { new ScenePath(rail, SceneInk.Cyan) };
        var markers = new List<SceneMarker>(Math.Min(frame.Balls.Length, 18) + PoolGeometry.Pockets.Length);
        var annotations = new List<SceneAnnotation>(Math.Min(frame.Balls.Length, 18) + PoolGeometry.Pockets.Length);
        for (int ballIndex = 0; ballIndex < Math.Min(frame.Balls.Length, 18); ballIndex++)
        {
            var ball = frame.Balls[ballIndex];
            if (!double.IsFinite(ball.Position.X) || !double.IsFinite(ball.Position.Y)
                || ball.Position.X is < 0 or > 2 || ball.Position.Y is < 0 or > 1) continue;
            var centre = Image(ball.Position);
            var ink = BallInk(ball.Kind);
            markers.Add(new(centre, ProjectedBallRadius(frame.Calibration, ball.Position, videoWidth, videoHeight), ink, SceneMarkerKind.Ball));
            annotations.Add(new(Image(BallLabelPoint(ball.Position, ballIndex)), BallIdentityCode(ball.Kind)));
        }

        for (int index = 0; index < PoolGeometry.Pockets.Length; index++)
        {
            var pocket = PoolGeometry.Pockets[index];
            markers.Add(new(Image(pocket), .014, SceneInk.Cyan, SceneMarkerKind.Pocket));
            var labelPoint = new Point(
                pocket.X == 0 ? .10 : pocket.X == 2 ? 1.90 : 1.0,
                pocket.Y == 0 ? .10 : .90);
            annotations.Add(new(Image(labelPoint), $"P{index + 1}"));
        }

        string certainty = frame.Obstructed ? " · obstructed" : frame.LayoutUncertain ? " · uncertain" : "";
        return new(++sceneRevision, frame.MediaGeneration, frame.TableVersion, DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(3), paths, markers, annotations,
            $"Live Detect detections{certainty}");
    }

    static double ProjectedBallRadius(TableCalibration calibration, Point centre, int videoWidth, int videoHeight)
    {
        Point xProbe = calibration.Unmap(centre.X <= 1 ? centre + new Point(PoolGeometry.Radius, 0) : centre - new Point(PoolGeometry.Radius, 0));
        Point yProbe = calibration.Unmap(centre.Y <= .5 ? centre + new Point(0, PoolGeometry.Radius) : centre - new Point(0, PoolGeometry.Radius));
        Point imageCentre = calibration.Unmap(centre);
        static double Pixels(Point a, Point b, int width, int height)
        {
            double x = (a.X - b.X) * width, y = (a.Y - b.Y) * height;
            return Math.Sqrt(x * x + y * y);
        }
        double radius = (Pixels(xProbe, imageCentre, videoWidth, videoHeight)
            + Pixels(yProbe, imageCentre, videoWidth, videoHeight)) / (2 * videoHeight);
        return double.IsFinite(radius) && radius is >= .001 and <= .1 ? radius : .014;
    }

    static Point BallLabelPoint(Point centre, int index)
    {
        var offset = identifyLabelOffsets[index % identifyLabelOffsets.Length];
        return new(Math.Clamp(centre.X + offset.X, .01, 1.99), Math.Clamp(centre.Y + offset.Y, .01, .99));
    }

    static string BallIdentityCode(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "red" => "R", "yellow" => "Y", "black" => "8", "cue" or "cue ball" or "cue-ball" => "C", _ => "?"
    };
    static SceneInk BallInk(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "red" => SceneInk.Red, "yellow" => SceneInk.Yellow, "cue" or "cue ball" or "cue-ball" => SceneInk.Cyan, _ => SceneInk.White
    };

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
