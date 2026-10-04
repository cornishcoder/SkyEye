using System.Text.Json;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;
using Org.Opencv.Android;
using Org.Opencv.Core;

namespace SkyEye.Companion.Hybrid.Probes;

/// <summary>Lab-only media gate. URLs/read tokens stay in the native host, outside agent state.</summary>
public sealed class NativeMediaProbe
{
    Bridge.PlayerBridge? player;
    Bridge.CastBridge? cast;
    string? source;
    bool resume;
    public string Status { get; private set; } = "Native player ready";
    public event Action? Changed;
    public void Report(string status) { Status = status; Changed?.Invoke(); }
    public void Attach(Bridge.PlayerBridge player, Bridge.CastBridge cast) { this.player = player; this.cast = cast; }
    public void Detach() { player = null; cast = null; source = null; resume = false; }
    public async Task FixtureAsync()
    {
        string path = Path.Combine(FileSystem.CacheDirectory, "skyeye-probe.mp4");
        await using (var input = await FileSystem.OpenAppPackageFileAsync("probe.mp4"))
        await using (var output = File.Create(path)) await input.CopyToAsync(output);
        Play(new Uri(path).AbsoluteUri);
    }
    public async Task CameraAsync(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var camera) || camera.Scheme != "http")
            throw new InvalidOperationException("Enter the camera's local HTTP address.");
        if (await Permissions.RequestAsync<LocalNetworkPermission>() != PermissionStatus.Granted)
            throw new InvalidOperationException("Allow nearby devices access to view the camera and use TV casting.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        string statusJson;
        try { statusJson = await http.GetStringAsync(new Uri(camera, "/status")); }
        catch (HttpRequestException e)
        {
            // Only the public status request is logged, before any stream/read token is obtained.
            Android.Util.Log.Warn("SkyEyeLab", $"Camera status request: {e.GetBaseException().Message}");
            throw;
        }
        using var status = JsonDocument.Parse(statusJson);
        if (status.RootElement.GetProperty("state").GetString() != "Active")
            throw new InvalidOperationException("Camera is ready; start recording from the owning companion for live playback.");
        string url = new Uri(camera, status.RootElement.GetProperty("streamPath").GetString()!).AbsoluteUri;
        Play(url);
        cast?.Configure(url, true);
    }
    void Play(string url)
    {
        if (player is null) throw new InvalidOperationException("Native player is not attached.");
        source = url; resume = true; player.Play(url); Report("Waiting for native playback…");
    }
    public void Stop() { resume = false; source = null; player?.Stop(); Report("Playback stopped"); }
    public void Suspend() { player?.Stop(); }
    public void Resume() { if (resume && source is not null) player?.Play(source); }
    public void CheckLibraries()
    {
        if (!OpenCVLoader.InitLocal()) throw new InvalidOperationException("OpenCV native load failed.");
        using var matrix = new Mat(2, 2, CvType.Cv8uc1, new Scalar(7));
        double sum = Org.Opencv.Core.Core.SumElems(matrix).Val![0];
        if (sum != 28) throw new InvalidOperationException("OpenCV native calculation failed.");
        string rtc = Bridge.NativeCompatibilityProbe.CheckWebRtc(Android.App.Application.Context)!;
        Report($"OpenCV {Org.Opencv.Core.Core.Version}: sum {sum}; {rtc}; runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
    }
}
