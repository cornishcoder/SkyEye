using System.Text.Json;
using SkyEye.Core;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    string? lastScene;
    public bool FrameTableRequested { get; private set; }
    public bool FrameGuides { get; private set; }
    public bool CanFrameTable => HasSource && selection.Kind == ViewingSource.Live && HasTableCalibration && !EditingTable;
    public string FramingStatus { get; private set; } = "Whole camera · all pockets visible";
    public string LiveAvailability => app.State.Camera.Reachability != SkyEye.Core.Application.CameraReachability.Reachable ? "Connect the camera to watch live."
        : app.State.Camera.Capture != SkyEye.Core.CaptureState.Active ? app.State.Camera.CanControl ? "Start recording above to enable live viewing." : "The camera host needs to start recording before live viewing is available."
        : "Live viewing is silent. Recording keeps room audio.";
    public void SetTableFraming(bool enabled) { FrameTableRequested = enabled; RevealControls(); Publish(); }
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
            var calibration = table.State.Calibration;
            var projection = FrameTableRequested && CanFrameTable && calibration is not null ? SceneProjection.Table(width, height, videoWidth, videoHeight, calibration)
                : SceneProjection.Portrait(width, height, videoWidth, videoHeight);
            var guides = FrameGuides && CanFrameTable && calibration is not null ? PoolGeometry.Pockets.Select(p => calibration.Unmap(p)).Select(p => new[] { p.X, p.Y }).ToArray() : [];
            scene = JsonSerializer.Serialize(new { width, height, values = projection.Values, clip = new[] { projection.Left, projection.Top, projection.Left + projection.Width, projection.Top + projection.Height }, framed = projection.Framed, guides });
            status = projection.Fallback ?? (projection.Framed ? "Calibrated table · pocket border retained" : FrameTableRequested ? "Whole camera · calibrated live view required" : "Whole camera · all pockets visible");
        }
        if (scene != lastScene)
        {
            player.SetSceneTransform(scene); lastScene = scene;
            if (FrameGuides) Android.Util.Log.Info("SkyEyeShell", "Framing observed: " + player.FramingMetrics());
        }
        if (FramingStatus != status) { FramingStatus = status; Changed?.Invoke(); }
    }
}
