using System.Text.Json;
using SkyEye.Core;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    string? lastScene;
    string lastTvFraming = "";
    public bool FrameTableRequested { get; private set; } = true;
    public bool FrameGuides { get; private set; }
    public bool CanFrameTable => HasSource && selection.Kind == ViewingSource.Live && !EditingTable;
    public string FramingStatus { get; private set; } = "Whole camera · all pockets visible";
    public string LiveAvailability => app.State.Camera.Reachability != SkyEye.Core.Application.CameraReachability.Reachable ? "Connect the camera to watch live."
        : app.State.Camera.Capture != SkyEye.Core.CaptureState.Active ? app.State.Camera.CanControl ? "Start recording above to enable live viewing." : "The camera host needs to start recording before live viewing is available."
        : "Live viewing is silent. Recording keeps room audio.";
    public void SetTableFraming(bool enabled) { FrameTableRequested = enabled; if (!enabled) StopPocketFraming(); RevealControls(); Publish(); }
    public void ToggleFrameGuides() { FrameGuides = !FrameGuides; Publish(); }
    public void UpdateFraming(int width, int height)
    {
        if (player is null || width <= 0 || height <= 0) return;
        string scene, status;
        if (selection.Kind == ViewingSource.LocalClip)
        { scene = ""; status = "Portrait clip · original framing"; }
        else
        {
            int videoWidth = 1920, videoHeight = 1080;
            try
            {
                using var metrics = JsonDocument.Parse(player.PlaybackMetrics()!);
                videoWidth = metrics.RootElement.GetProperty("width").GetInt32(); videoHeight = metrics.RootElement.GetProperty("height").GetInt32();
            }
            catch { }
            if (videoWidth <= 0 || videoHeight <= 0) return;
            var calibration = AutomaticPocketsCurrent ? new TableCalibration(automaticPockets!.Corners.ToArray()) : null;
            var projection = FrameTableRequested && CanFrameTable && calibration is not null ? SceneProjection.Pockets(width, height, videoWidth, videoHeight, calibration)
                : SceneProjection.Portrait(width, height, videoWidth, videoHeight);
            var guides = FrameGuides && CanFrameTable && calibration is not null ? PoolGeometry.Pockets.Select(p => calibration.Unmap(p)).Select(p => new[] { p.X, p.Y }).ToArray() : [];
            var pockets = projection.Framed ? calibration!.Corners.Select(p => new[] { p.X, p.Y }).ToArray() : [];
            scene = JsonSerializer.Serialize(new { width, height, values = projection.Values, clip = new[] { projection.Left, projection.Top, projection.Left + projection.Width, projection.Top + projection.Height }, framed = projection.Framed, guides, pockets });
            status = projection.Framed ? "Rectangular table · corner pockets found automatically" : FrameTableRequested && selection.Kind == ViewingSource.Live ? "Finding corner pockets automatically · whole camera for now" : "Whole camera · all pockets visible";
            var tvProjection = projection.Framed ? SceneProjection.Pockets(960, 540, videoWidth, videoHeight, calibration!, portrait: false) : null;
            var tvFraming = tvProjection is null ? "" : JsonSerializer.Serialize(new { width = 960, height = 540, values = tvProjection.Values, clip = new[] { tvProjection.Left, tvProjection.Top, tvProjection.Left + tvProjection.Width, tvProjection.Top + tvProjection.Height }, pockets });
            if (tvFraming != lastTvFraming) { player.SetTvSceneTransform(tvFraming); lastTvFraming = tvFraming; }
        }
        if (scene != lastScene)
        {
            player.SetSceneTransform(scene); lastScene = scene;
            if (FrameGuides) Android.Util.Log.Info("SkyEyeShell", "Framing observed: " + player.FramingMetrics());
        }
        if (FramingStatus != status) { FramingStatus = status; Changed?.Invoke(); }
    }
}
