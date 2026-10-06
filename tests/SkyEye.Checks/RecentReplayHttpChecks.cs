using System.Net;
using System.Net.Sockets;
using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Companion;

static class RecentReplayHttpChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static async Task Refused(Func<Task> action) { try { await action(); } catch (InvalidOperationException) { return; } throw new Exception("Expected recent replay refusal"); }
    public static async Task Run()
    {
        var allocator = new TcpListener(IPAddress.Loopback, 0); allocator.Start(); int port = ((IPEndPoint)allocator.LocalEndpoint).Port; allocator.Stop();
        string root = Path.Combine(Path.GetTempPath(), "skyeye-recent-http-" + Guid.NewGuid().ToString("N"));
        string timeline = Guid.NewGuid().ToString("N"); int prepares = 0; bool oldCamera = false, wrongWindow = false;
        TaskCompletionSource? entered = null, release = null;
        RecentReplayAsset? last = null;
        var store = new RecentReplayStore(Path.Combine(root, "temporary"));
        await using var server = new LocalHttpServer(port, async (r, ct) =>
        {
            if (r.Path == "/status") return HttpResponse.Json(new { state = "Active", timeline, sourceTimeUs = 123_000_000L,
                recentReplayVersion = oldCamera ? 0 : 1, readToken = "fixture-replay-access", host = (object?)null });
            Check(r.Query.GetValueOrDefault("access") == "fixture-replay-access" && r.HostToken is null, "recent replay uses frozen read access and never claims host authority");
            if (r.Path == "/replays/recent")
            {
                prepares++; var request = r.Json<RecentReplayRequest>();
                Check(request.EndUs == 123_000_000L && request.Timeline == timeline && request.Seconds == 15, "fresh camera clock determines replay window");
                if (entered is not null) { entered.TrySetResult(); await release!.Task.WaitAsync(ct); }
                if (wrongWindow) return HttpResponse.Json(last! with { EndUs = last!.EndUs + 1 });
                var asset = await store.CreateAsync(request, async (path, token) => { await File.WriteAllBytesAsync(path, Enumerable.Range(0, 64).Select(i => (byte)i).ToArray(), token); return (15, 108_000_000L, request.EndUs); }, ct);
                last = asset; return HttpResponse.Json(asset);
            }
            if (r.Path.StartsWith("/replays/")) return store.MediaPath(r.Path[9..]) is { } path ? new(200, "video/mp4", FilePath: path) : HttpResponse.Json(new { error = "expired" }, 410);
            return HttpResponse.Json(Array.Empty<Clip>());
        });
        server.Start(); using var current = new CameraClient(); current.Connect($"127.0.0.1:{port}"); current.HostToken = "must-not-be-sent"; await current.StatusAsync();
        var camera = new CameraConnection(Guid.NewGuid(), CameraEndpoint.Identity(current.Address), CameraReachability.Reachable, true, CaptureState.Active, CameraOwnership.Guest, null, 0, null, 0);
        var service = new CompanionClipService(current, () => camera, new ClipLibrary(Path.Combine(root, "saved")), new NoExport());
        try
        {
            var replay = await service.RecentAsync(15, default);
            Check(prepares == 1 && replay.Asset.EndUs == 123_000_000 && service.Library.Local().Length == 0, "temporary replay acknowledged without permanent catalogue or local-library entry");
            using var viewer = new HttpClient(); using var request = new HttpRequestMessage(HttpMethod.Get, service.TvSource(replay)); request.Headers.Range = new(10, 19);
            using var response = await viewer.SendAsync(request);
            Check(response.StatusCode == HttpStatusCode.PartialContent && (await response.Content.ReadAsByteArrayAsync()).SequenceEqual(Enumerable.Range(10, 10).Select(i => (byte)i)), "temporary TV media supports buffered seek ranges");
            oldCamera = true; await Refused(() => service.RecentAsync(15, default)); Check(prepares == 1, "old camera capability refuses before export"); oldCamera = false;
            wrongWindow = true; await Refused(() => service.RecentAsync(15, default)); wrongWindow = false;
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = service.RecentAsync(15, default); await entered.Task;
            camera = camera with { ConnectionId = Guid.NewGuid() };
            current.Connect("127.0.0.1:1"); release.SetResult();
            await Refused(() => pending);
            current.Connect($"127.0.0.1:{port}"); await current.StatusAsync();
            await Refused(() => { service.TvSource(replay); return Task.CompletedTask; });
            Check(service.Library.Local().Length == 0 && !Directory.EnumerateFiles(Path.Combine(root, "saved")).Any(), "replay cannot contaminate saved library or redirect after reconnect");
            Console.WriteLine("PASS recent replay HTTP: camera-clock request, read-only credential boundary, metadata validation, capability gate, buffered ranges and stale connection rejection");
        }
        finally { if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected fixture root"); Directory.Delete(root, true); }
    }
    sealed class NoExport : IPortraitClipExporter { public Task ExportAsync(string input, string output, int rotation, CancellationToken cancellation) => throw new Exception("Recent replay must not export into local library"); }
}
