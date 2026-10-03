using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

sealed class OverlayRelay : IAsyncDisposable
{
    const int Port = 8766;
    readonly string access = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    readonly string directory;
    readonly Bridge.OverlayEncoder encoder;
    readonly LocalHttpServer server;
    readonly Func<string> castMetrics;
    public string Url { get; }
    public bool Ready => encoder.Ready();
    public OverlayRelay(MainActivity activity, Bridge.PlayerBridge player, string cameraAddress, Bridge.OverlayEncoder.IListener listener, Func<string> castMetrics)
    {
        this.castMetrics = castMetrics;
        // Use the interface routed to this camera, including devices with a VPN.
        using var route = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        route.Connect(new Uri(cameraAddress).Host, 8765);
        var address = ((IPEndPoint)route.LocalEndPoint!).Address;
        Url = $"http://{address}:{Port}/live/index.m3u8?access={access}";
        directory = Path.Combine(activity.CacheDir!.AbsolutePath, "overlay-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        encoder = new Bridge.OverlayEncoder(activity, player, directory, listener);
        server = new LocalHttpServer(Port, HandleAsync);
        try { server.Start(); encoder.Start(); }
        catch { encoder.Close(); _ = server.DisposeAsync(); throw; }
    }
    Task<HttpResponse> HandleAsync(HttpRequest request, CancellationToken cancellation)
    {
        if (request.Method == "OPTIONS") return Task.FromResult(HttpResponse.Text(""));
        if (request.Method is not ("GET" or "HEAD")) return Task.FromResult(HttpResponse.Json(new { error = "Read-only viewing feed" }, 405));
        // Like camera discovery, this advertises only read-only viewing access.
        if (request.Path == "/status") return Task.FromResult(HttpResponse.Json(new { streamPath = $"/live/index.m3u8?access={access}", ready = Ready, video = JsonSerializer.Deserialize<JsonElement>(encoder.Metrics() ?? "{}"), cast = JsonSerializer.Deserialize<JsonElement>(castMetrics()) }));
        if (request.Query.GetValueOrDefault("access") != access) throw new UnauthorizedAccessException("Reconnect to the candidate view.");
        if (request.Path == "/live/index.m3u8")
        {
            string manifest = encoder.Playlist(access) ?? "";
            return Task.FromResult(manifest.Length == 0 ? HttpResponse.Json(new { error = "Preparing candidate view" }, 503)
                : HttpResponse.Text(manifest, "application/vnd.apple.mpegurl"));
        }
        string name = request.Path.StartsWith("/live/") ? request.Path[6..] : "";
        if (name.Length == 0 || Path.GetFileName(name) != name || !name.EndsWith(".ts")) return Task.FromResult(HttpResponse.Json(new { error = "View not found" }, 404));
        string file = Path.Combine(directory, name);
        return Task.FromResult(File.Exists(file) ? new HttpResponse(200, "video/mp2t", FilePath: file) : HttpResponse.Json(new { error = "View expired" }, 404));
    }
    public async ValueTask DisposeAsync()
    {
        encoder.Close(); await server.DisposeAsync();
        // Worker shutdown is asynchronous; reclaim only this relay's cache later.
        _ = Task.Run(async () => { await Task.Delay(3000); try { Directory.Delete(directory, true); } catch (IOException) { } });
    }
}
