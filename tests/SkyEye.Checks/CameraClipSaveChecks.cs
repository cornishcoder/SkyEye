using System.Net;
using System.Net.Sockets;
using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Companion;

static class CameraClipSaveChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static async Task Run()
    {
        var allocation = new TcpListener(IPAddress.Loopback, 0); allocation.Start(); int port = ((IPEndPoint)allocation.LocalEndpoint).Port; allocation.Stop();
        var clock = new TestTime(); var authority = new SessionAuthority(clock); var lease = authority.Claim("fixture companion"); authority.Transition(CaptureState.Active);
        int saves = 0, heartbeats = 0; string mode = "success"; Guid sentId = Guid.Empty;
        var root = Path.Combine(Path.GetTempPath(), "skyeye-camera-save-" + Guid.NewGuid().ToString("N"));
        TaskCompletionSource? statusEntered = null, statusReleased = null;
        await using var server = new LocalHttpServer(port, async (request, ct) =>
        {
            if (request.Path == "/status")
            {
                if (statusEntered is not null) { statusEntered.TrySetResult(); await statusReleased!.Task.WaitAsync(ct); }
                var host = authority.Current;
                return HttpResponse.Json(new { readToken = "fixture-viewing-access", authority.SessionId, authority.Revision,
                    host = host is null ? null : new { host.Generation }, state = authority.State.ToString() });
            }
            authority.Require(request.HostToken);
            if (request.Path == "/host/heartbeat") { Interlocked.Increment(ref heartbeats); authority.Heartbeat(request.HostToken!); return HttpResponse.Json(new { renewed = true }); }
            if (request.Path == "/clips/save")
            {
                Interlocked.Increment(ref saves);
                var payload = request.Json<Dictionary<string, System.Text.Json.JsonElement>>();
                sentId = Guid.ParseExact(payload["commandId"].GetString()!, "N");
                Check(payload["seconds"].GetInt32() == 30, "wire duration preserved");
                if (mode == "no-history") return HttpResponse.Json(new { error = "No recording history is available yet." }, 409);
                if (mode == "uncertain") return HttpResponse.Json(new { error = "fixture failure after possible save" }, 500);
                return HttpResponse.Json(new Clip(Guid.NewGuid().ToString("N"), "Fixture replay", 29.7, 1234, new string('C', 64), DateTimeOffset.UtcNow));
            }
            return HttpResponse.Json(new { error = "missing" }, 404);
        });
        server.Start();
        using var client = new CameraClient(); client.Connect($"127.0.0.1:{port}"); client.HostToken = lease.Token;
        try
        {
            await client.StatusAsync();
            var scope = client.SaveScope!;
            int retained=0;
            var app = new AppCoordinator(clipSaves: new(root, new CameraClipSaveEffect(client,_=>retained++)));
            CommandEnvelope Request(ClipSaveScope? target = null) => new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, new SaveClip(30, target ?? scope));
            var first = Request(); var result = await app.DispatchAsync(first);
            Check(result.Succeeded && result.SavedClip is not null && saves == 1 && heartbeats == 1 && sentId == first.OperationId, "real native adapter proves host and sends original command ID once");
            Check((await app.DispatchAsync(first)).Duplicate && saves == 1 && heartbeats == 1, "durable replay avoids even preflight HTTP");
            Check(retained==1,"annotation retention receives only the verified first acknowledgement, not a duplicate save");
            Check((await app.DispatchAsync(Request(scope with { CaptureRevision = scope.CaptureRevision + 1 }))).Outcome == CommandOutcome.Failed && saves == 1, "changed capture revision cannot save");
            client.HostToken = null;
            Check((await app.DispatchAsync(Request())).Outcome == CommandOutcome.Failed && saves == 1, "guest cannot save");
            client.HostToken = "fixture-invalid-host-token";
            Check((await app.DispatchAsync(Request())).Outcome == CommandOutcome.Failed && saves == 1, "displayed host generation is not ownership proof");
            client.HostToken = lease.Token;
            mode = "no-history"; var refused = Request();
            Check((await app.DispatchAsync(refused)).Outcome == CommandOutcome.Failed && (await app.DispatchAsync(refused)).Duplicate && saves == 2, "wire no-history refusal remains failed on retry");
            mode = "uncertain"; var lost = Request();
            Check((await app.DispatchAsync(lost)).Outcome == CommandOutcome.Uncertain && (await app.DispatchAsync(lost)).Duplicate && saves == 3, "server failure after possible save cannot be retried");
            mode = "success";
            statusEntered = new(TaskCreationOptions.RunContinuationsAsynchronously); statusReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var frozen = app.DispatchAsync(Request()); await statusEntered.Task;
            client.Connect("127.0.0.1:1"); client.HostToken = "fixture-new-camera-token";
            statusReleased.SetResult();
            Check((await frozen).Succeeded && saves == 4, "connection changes cannot redirect an admitted effect or replace its credential");
            statusEntered = null; statusReleased = null;
            client.Connect($"127.0.0.1:{port}"); client.HostToken = lease.Token;
            clock.Advance(31);
            Check((await app.DispatchAsync(Request())).Outcome == CommandOutcome.Failed && saves == 4, "expired lease cannot dispatch save");
            Check((await app.DispatchAsync(first)).Succeeded && saves == 4, "historical acknowledgement stays historical after lease expiry");
            Check(retained==2,"failed, uncertain, rejected and duplicate saves do not create invented clip annotation bindings");
            var content = string.Join("", Directory.EnumerateFiles(root, "*.json").Select(File.ReadAllText));
            Check(!content.Contains(lease.Token) && !content.Contains("fixture-invalid-host-token") && !content.Contains("fixture-viewing-access") && !content.Contains(client.Address), "native credentials and endpoint never enter operation journal");
            Console.WriteLine("PASS real camera HTTP save adapter: command ID, host proof/expiry, scoped capture, guest/refused/ambiguous outcomes, frozen endpoint and secret boundary");
        }
        finally
        {
            // All effect tasks have completed; this is a checked test-only directory.
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected fixture path");
            Directory.Delete(root, recursive: true);
        }
    }
}
