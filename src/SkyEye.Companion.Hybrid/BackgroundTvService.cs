using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion.Hybrid;

public sealed record BackgroundTvRequest(DeliverySource Source, string Address, string Stream, string Geometry,
    string Timeline, AnnotationJournalStore Journal);

[Service(Name = "uk.co.skyeye.companion.BackgroundTvService", Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class BackgroundTvService : Service
{
    public BackgroundTvService() { }
    public BackgroundTvService(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    static BackgroundTvRequest? requested;
    public static BackgroundTvService? Instance { get; private set; }
    public long DecoderGeneration => compositor?.Generation ?? 0;
#if SKYEYE_PERFORMANCE
    public bool RestartDecoderForCheck()
    {
        if (compositor is null || request is null) return false;
        compositor.RestartDecoder(request.Stream);
        return true;
    }
#endif
    readonly BackgroundDeliveryLease lease = new();
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? pipelineLifetime;
    readonly string access = Guid.NewGuid().ToString("N");
    Bridge.LiveGpuCompositor? compositor;
    Listener? listener;
    LocalHttpServer? server;
    BackgroundTvRequest? request;
    CameraClient? camera;
    PowerManager.WakeLock? wake;
    string directory = "";
    public string Url { get; private set; } = "";
    public bool Ready => compositor?.Ready() == true;
    public string Metrics => compositor?.Metrics() ?? "{}";
    [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof(BackgroundTvService))]
    public static void Request(BackgroundTvRequest request)
    {
        requested = request;
        var context = Android.App.Application.Context;
        if (Instance is { } running && running.request?.Source == request.Source)
        { running.request = request; running.compositor?.SetGeometry(request.Geometry); return; }
        context.StartForegroundService(new Intent(context, typeof(BackgroundTvService)));
    }
    public static void Clear()
    { requested = null; Android.App.Application.Context.StopService(new Intent(Android.App.Application.Context, typeof(BackgroundTvService))); }
    public static void Scene(string json) => Instance?.PublishScene(json);
    void PublishScene(string json)
    {
        if (compositor is null || request is null) return;
        if (json.Length == 0) { compositor.SetScene(""); return; }
        try
        {
            // Rebind validated native scene content to this decoder's own generation.
            var value = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            value["mediaGeneration"] = compositor.Generation;
            compositor.SetScene(value.ToJsonString());
        }
        catch (JsonException) { }
    }
    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnCreate()
    {
        base.OnCreate(); Instance = this;
        // A process kill can leave segments behind. Only this service's generated
        // UUID directories in the private cache are eligible for reclamation.
        var cache = Path.GetFullPath(CacheDir!.AbsolutePath);
        foreach (var stale in Directory.EnumerateDirectories(cache, "tv-service-*"))
            if (Path.GetDirectoryName(Path.GetFullPath(stale)) == cache &&
                Guid.TryParseExact(Path.GetFileName(stale)[11..], "N", out _))
                try { Directory.Delete(stale, true); } catch (IOException) { }
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("tv-delivery", "SkyEye TV playback", NotificationImportance.Low));
        using var open = PackageManager!.GetLaunchIntentForPackage(PackageName!)!;
        var pending = PendingIntent.GetActivity(this, 141, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new Notification.Builder(this, "tv-delivery").SetContentTitle("SkyEye table on TV")
            .SetContentText("Corrected TV playback continues while this phone is locked.").SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)
            .SetContentIntent(pending).SetOngoing(true).Build();
        StartForeground(141, notification, ForegroundService.TypeMediaPlayback);
        wake = ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "SkyEye:TvDelivery"); wake?.Acquire();
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (requested is not { } next) { StopSelf(); return StartCommandResult.NotSticky; }
        if (request?.Source == next.Source) return StartCommandResult.NotSticky;
        ClosePipeline();
        request = next; lease.Request(next.Source, true);
        try
        {
            directory = Path.Combine(CacheDir!.AbsolutePath, "tv-service-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            listener = new(); compositor = new(this, directory, listener);
            compositor.SetGeometry(next.Geometry);
            using var route = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp); route.Connect(new Uri(next.Address).Host, 8765);
            Url = $"http://{((IPEndPoint)route.LocalEndPoint!).Address}:8766/live/index.m3u8?access={access}&scene=1";
            server = new(8766, HandleAsync); server.Start(); compositor.Start(next.Stream);
            camera = new(); camera.Connect(next.Address); pipelineLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); _ = PollAsync(pipelineLifetime.Token);
        }
        catch (Exception error) { Android.Util.Log.Warn("SkyEyeTv", error.GetType().Name); StopSelf(); }
        return StartCommandResult.NotSticky;
    }
    Task<HttpResponse> HandleAsync(HttpRequest r, CancellationToken ct)
    {
        if (r.Method == "OPTIONS") return Task.FromResult(HttpResponse.Text(""));
        if (r.Method is not ("GET" or "HEAD")) return Task.FromResult(HttpResponse.Json(new { error = "Read-only media" }, 405));
        if (r.Path == "/status") return Task.FromResult(HttpResponse.Json(new { ready = Ready, streamPath = $"/live/index.m3u8?access={access}", video = JsonSerializer.Deserialize<JsonElement>(Metrics), source = request?.Source }));
        if (r.Query.GetValueOrDefault("access") != access) throw new UnauthorizedAccessException("Reconnect to TV playback.");
        if (r.Path == "/live/index.m3u8")
        { var text = compositor?.Playlist(access) ?? ""; return Task.FromResult(text.Length == 0 ? HttpResponse.Json(new { error = "Preparing TV" }, 503) : HttpResponse.Text(text, "application/vnd.apple.mpegurl")); }
        string name = r.Path.StartsWith("/live/") ? r.Path[6..] : "";
        if (name.Length == 0 || Path.GetFileName(name) != name || !name.EndsWith(".ts")) return Task.FromResult(HttpResponse.Json(new { error = "Not found" }, 404));
        var path = Path.Combine(directory, name);
        return Task.FromResult(File.Exists(path) ? new HttpResponse(200, "video/mp2t", FilePath: path) : HttpResponse.Json(new { error = "Expired segment" }, 404));
    }
    async Task PollAsync(CancellationToken token)
    {
        try
        {
            long? unavailableAt = null;
            while (!token.IsCancellationRequested && request is { } current && compositor is { } owner && camera is { } client)
            {
                JsonElement status;
                try { status = await client.StatusAsync(token); unavailableAt = null; }
                catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException && !token.IsCancellationRequested)
                {
                    unavailableAt ??= System.Diagnostics.Stopwatch.GetTimestamp();
                    if (System.Diagnostics.Stopwatch.GetElapsedTime(unavailableAt.Value) >= TimeSpan.FromSeconds(30))
                    { await MainThread.InvokeOnMainThreadAsync(StopSelf); return; }
                    await Task.Delay(1000, token); continue;
                }
                token.ThrowIfCancellationRequested();
                if (status.GetProperty("state").GetString() != "Active" || status.GetProperty("timeline").GetString() != current.Timeline)
                { await MainThread.InvokeOnMainThreadAsync(StopSelf); return; }
                owner.SetClock(current.Timeline, status.GetProperty("sourceTimeUs").GetInt64());
                using var samples = JsonDocument.Parse(owner.DrainAnnotations()!);
                foreach (var sample in samples.RootElement.EnumerateArray())
                {
                    var scene = sample.GetProperty("scene");
                    current.Journal.Observe(new(sample.GetProperty("timeline").GetString()!, sample.GetProperty("sourceUs").GetInt64(), SceneTarget.Tv,
                        AnnotationJournalStore.Fingerprint(sample.GetProperty("geometry").GetString()!), scene.ValueKind == JsonValueKind.Null ? null : scene.Deserialize<RecordedScene>(JsonDefaults.Options)));
                }
                await Task.Run(() => current.Journal.Flush(current.Timeline), token);
                var connected = await MainThread.InvokeOnMainThreadAsync(() => Bridge.LiveGpuCompositor.ReceiverConnected(this));
                lease.ObserveReceiver(connected);
                if (lease.ExpireReceiver()) { await MainThread.InvokeOnMainThreadAsync(StopSelf); return; }
                await Task.Delay(1000, token);
            }
        }
        catch (System.OperationCanceledException) { }
        catch (Exception error) { Android.Util.Log.Warn("SkyEyeTv", "Source unavailable: " + error.GetType().Name); await MainThread.InvokeOnMainThreadAsync(StopSelf); }
    }
    void ClosePipeline()
    { pipelineLifetime?.Cancel(); pipelineLifetime?.Dispose(); pipelineLifetime = null; compositor?.Close(); compositor?.Dispose(); compositor = null; listener?.Dispose(); listener = null; camera?.Dispose(); camera = null; if (server is not null) _ = server.DisposeAsync(); server = null; }
    public override void OnDestroy()
    {
        lifetime.Cancel(); lease.Clear(); ClosePipeline(); if (wake?.IsHeld == true) wake.Release(); wake?.Dispose();
        if (Instance == this) Instance = null; base.OnDestroy();
    }
    sealed class Listener : Java.Lang.Object, Bridge.LiveGpuCompositor.IListener
    { public void OnError(string? message) => Android.Util.Log.Warn("SkyEyeTv", message ?? "Compositor interrupted"); }
}
