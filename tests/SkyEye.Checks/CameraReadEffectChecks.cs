using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Companion;

static class CameraReadEffectChecks
{
    static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
    static async Task Refused(Func<Task> work) { try { await work(); } catch (InvalidOperationException) { return; } throw new Exception("Expected stale read refusal"); }
    public static async Task Run()
    {
        var allocator = new TcpListener(IPAddress.Loopback, 0); allocator.Start(); int port = ((IPEndPoint)allocator.LocalEndpoint).Port; allocator.Stop();
        string root = Path.Combine(Path.GetTempPath(), "skyeye-read-effects-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        byte[] bytes = Enumerable.Range(0, 512).Select(i => (byte)i).ToArray(); string master = Path.Combine(root, "fixture-source.mp4"); await File.WriteAllBytesAsync(master, bytes);
        var clip = new Clip(Guid.NewGuid().ToString("N"), "Fixture", 30, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), DateTimeOffset.UtcNow);
        var clock = new TestTime(); var authority = new SessionAuthority(clock); var lease = authority.Claim("fixture"); authority.Transition(CaptureState.Active);
        TaskCompletionSource? entered = null, released = null; bool holdCatalog = false, holdFrame = false, changeCapture = false;
        int fileRequests = 0; string? sentRange = null, sentHost = null;
        await using var server = new LocalHttpServer(port, async (request, cancellation) =>
        {
            if (request.Path == "/status")
            {
                var host = authority.Current;
                return HttpResponse.Json(new { authority.SessionId, authority.Revision, state = authority.State.ToString(),
                    host = host is null ? null : new { host.Generation }, readToken = "fixture-read-only" });
            }
            if (request.Path == "/clips")
            {
                Check(request.Query.GetValueOrDefault("access") == "fixture-read-only", "catalog uses frozen viewing access");
                if (holdCatalog) { entered!.TrySetResult(); await released!.Task.WaitAsync(cancellation); }
                return HttpResponse.Json(new[] { clip });
            }
            if (request.Path == "/clips/" + clip.Id)
            {
                Check(request.Query.GetValueOrDefault("access") == "fixture-read-only", "download uses admitted viewing access");
                fileRequests++; sentRange = request.Headers.GetValueOrDefault("Range"); return new(200, "video/mp4", FilePath: master);
            }
            authority.Require(request.HostToken);
            if (request.Path == "/host/heartbeat") return HttpResponse.Json(authority.Heartbeat(request.HostToken!));
            if (request.Path == "/table/snapshot")
            {
                sentHost = request.HostToken;
                if (holdFrame) { entered!.TrySetResult(); await released!.Task.WaitAsync(cancellation); }
                if (changeCapture) authority.Transition(CaptureState.Paused);
                return new(200, "image/jpeg", [1, 2, 3], Headers: new() { ["X-SkyEye-Captured-At"] = clock.GetUtcNow().ToString("O"), ["X-SkyEye-Timeline"] = "fixture-timeline" });
            }
            return HttpResponse.Json(new { error = "missing" }, 404);
        });
        server.Start(); using var current = new CameraClient(); current.Connect($"127.0.0.1:{port}"); current.HostToken = lease.Token; await current.StatusAsync();
        var camera = new CameraConnection(Guid.NewGuid(), CameraEndpoint.Identity(current.Address), CameraReachability.Reachable, true, CaptureState.Active,
            CameraOwnership.Owned, "fixture", lease.Generation, authority.SessionId, authority.Revision);
        var exporter = new Exporter(); var clips = new CompanionClipService(current, () => camera, new ClipLibrary(Path.Combine(root, "library")), exporter);
        var frames = new CameraTableFrameSource(current, () => camera);
        try
        {
            var catalog = await clips.LoadAsync(default); Check(catalog.Clips.Single() == clip && fileRequests == 0, "guest-readable catalog keeps connection identity");
            using (var frozen = new CameraClipTransfer(current))
            {
                current.Connect("127.0.0.1:1"); string part = Path.Combine(root, "resume.part"); await File.WriteAllBytesAsync(part, bytes[..100]);
                await frozen.DownloadAsync(clip, part, _ => { }, default);
                Check(sentRange == "bytes=100-" && (await File.ReadAllBytesAsync(part)).SequenceEqual(bytes), "real frozen camera transfer resumes range and verifies full checksum across reconnect");
                try { await frozen.DownloadAsync(clip with { Sha256 = new string('A', 64) }, part, _ => { }, default); throw new Exception("Expected hash refusal"); } catch (IOException) { }
                Check(!File.Exists(part), "bad checksum removes unverified source");
            }
            current.Connect($"127.0.0.1:{port}"); current.HostToken = lease.Token; await current.StatusAsync();
            camera = camera with { ConnectionId = Guid.NewGuid() }; await Refused(() => clips.DownloadAsync(catalog, clip, null, default));
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously); released = new(TaskCreationOptions.RunContinuationsAsynchronously); holdCatalog = true;
            var oldCatalog = clips.LoadAsync(default); await entered.Task; camera = camera with { ConnectionId = Guid.NewGuid() }; released.TrySetResult();
            await Refused(() => oldCatalog); holdCatalog = false;
            catalog = await clips.LoadAsync(default); await clips.DownloadAsync(catalog, clip, null, default);
            Check(clips.Library.Local().Length == 1, "real transport publishes local conversion through extracted service");
            var frame = await frames.ReadAsync(camera.Scope!, default); Check(frame.Jpeg.SequenceEqual(new byte[] { 1, 2, 3 }) && sentHost == lease.Token, "scoped frame validates authority and preserves source time/timeline");
            camera = camera with { Ownership = CameraOwnership.Guest }; await Refused(() => frames.ReadAsync(camera.Scope!, default)); camera = camera with { Ownership = CameraOwnership.Owned };
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously); released = new(TaskCreationOptions.RunContinuationsAsynchronously); holdFrame = true;
            var oldFrame = frames.ReadAsync(camera.Scope!, default); await entered.Task;
            camera = camera with { ConnectionId = Guid.NewGuid() }; current.Connect("127.0.0.1:1"); current.HostToken = "new-native-token"; released.TrySetResult();
            await Refused(() => oldFrame); Check(sentHost == lease.Token, "reconnect cannot redirect frame request or replace admitted host credential");
            holdFrame = false; current.Connect($"127.0.0.1:{port}"); current.HostToken = lease.Token; await current.StatusAsync();
            changeCapture = true; await Refused(() => frames.ReadAsync(camera.Scope!, default));
            Check(authority.State == CaptureState.Paused, "capture change during snapshot is rejected by postflight scope");
            Console.WriteLine("PASS real HTTP read effects: frozen viewing/host credentials, resumed checksum, stale catalog/frame rejection, guest gating and post-snapshot capture scope");
        }
        finally { if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected fixture root"); Directory.Delete(root, true); }
    }
    sealed class Exporter : IPortraitClipExporter
    {
        public Task ExportAsync(string input, string output, int rotation, CancellationToken cancellation) => File.WriteAllTextAsync(output, "fixture portrait", cancellation);
    }
}
