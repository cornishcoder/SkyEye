using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Companion;

static class CameraControlAdapterChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static async Task Run()
    {
        var allocator = new TcpListener(IPAddress.Loopback, 0); allocator.Start(); int port = ((IPEndPoint)allocator.LocalEndpoint).Port; allocator.Stop();
        var clock = new TestTime(); var authority = new SessionAuthority(clock);
        int mutations = 0, renewals = 0; Guid lastCommandId = Guid.Empty; bool loseStartAck = false;
        await using var server = new LocalHttpServer(port, (request, _) =>
        {
            object Status()
            {
                var host = authority.Current;
                return new { armed = true, state = authority.State.ToString(), authority.SessionId, authority.Revision,
                    host = host is null ? null : new { host.Owner, host.Generation, host.ExpiresAt }, readToken = "fixture-native-view-token", streamPath = "/live/index.m3u8?access=fixture-native-view-token" };
            }
            HttpResponse response;
            if (request.Path == "/status") response = HttpResponse.Json(Status());
            else if (request.Path == "/host/claim")
            {
                mutations++; var args = request.Json<Dictionary<string, JsonElement>>(); response = HttpResponse.Json(authority.Claim(args["owner"].GetString()!));
            }
            else
            {
                authority.Require(request.HostToken);
                if (request.Path == "/host/heartbeat") { renewals++; response = HttpResponse.Json(authority.Heartbeat(request.HostToken!)); }
                else if (request.Path == "/host/release") { mutations++; authority.Release(request.HostToken!); response = HttpResponse.Json(new { released = true }); }
                else
                {
                    mutations++; var args = request.Json<Dictionary<string, JsonElement>>(); lastCommandId = Guid.ParseExact(args["commandId"].GetString()!, "N");
                    authority.Transition(request.Path switch { "/session/start" or "/session/resume" => CaptureState.Active, "/session/pause" => CaptureState.Paused, "/session/stop" => CaptureState.Stopped, _ => throw new ArgumentException("unsupported") });
                    response = loseStartAck && request.Path == "/session/start" ? HttpResponse.Json(new { error = "fixture lost acknowledgement" }, 500) : HttpResponse.Json(Status());
                }
            }
            return Task.FromResult(response);
        });
        server.Start();
        using var client = new CameraClient(); string? persistedToken = null;
        var adapter = new CameraControlEffect(client, value => persistedToken = value);
        var app = new AppCoordinator(cameraControls: adapter);
        CommandEnvelope Request(SkyEyeCommand command, CommandOrigin origin = CommandOrigin.Touch) => new(Guid.NewGuid(), origin, app.State.Version, command);
        string address = $"127.0.0.1:{port}";
        Check((await app.DispatchAsync(Request(new ConnectCamera(address)))).Succeeded && app.State.Camera.Ownership == CameraOwnership.Available, "native status connects without ownership assumption");
        Check((await app.DispatchAsync(Request(new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Start)))).Outcome == CommandOutcome.Rejected && mutations == 0, "native guest cannot control capture");
        await app.DispatchAsync(Request(new ClaimCameraHost(app.State.Camera.Scope!, "fixture owner")));
        Check(app.State.Camera.CanControl && client.HostToken == persistedToken && persistedToken is { Length: 64 } && renewals > 0, "claimed credential stays native and ownership requires heartbeat");
        var savedCredential = client.HostToken!; var oldScope = app.State.Camera.Scope!;
        var start = Request(new ChangeCapture(oldScope, CaptureAction.Start)); await app.DispatchAsync(start);
        Check(app.State.Camera.Capture == CaptureState.Active && lastCommandId == start.OperationId && mutations == 2, "capture command preserves operation ID and publishes fresh active state");
        int before = mutations;
        Check((await app.DispatchAsync(start)).Duplicate && mutations == before, "completed native capture does not resend");
        Check((await app.DispatchAsync(Request(new ChangeCapture(oldScope, CaptureAction.Stop)))).Outcome == CommandOutcome.Stale && mutations == before, "old native session/revision cannot mutate capture");
        int priorRenewals = renewals;
        Check((await app.DispatchAsync(Request(new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Start)))).Outcome == CommandOutcome.Unchanged
            && mutations == before && renewals > priorRenewals, "apparent capture no-op still verifies host authority");
        await app.DispatchAsync(Request(new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Pause)));
        await app.DispatchAsync(Request(new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Resume)));
        Check(app.State.Camera.Capture == CaptureState.Active, "pause/resume share authority adapter");
        var epoch = app.State.Camera.ConnectionId;
        await app.DispatchAsync(Request(new ConnectCamera(address)));
        Check(app.State.Camera.ConnectionId != epoch && app.State.Camera.CanControl && persistedToken == savedCredential, "same-endpoint reconnect retains credential only after fresh proof");
        clock.Advance(31);
        await app.DispatchAsync(Request(new RefreshCamera(app.State.Camera.ConnectionId), CommandOrigin.System));
        Check(!app.State.Camera.CanControl && app.State.Camera.Ownership == CameraOwnership.Available && client.HostToken is null && persistedToken is null
            && app.State.Camera.Capture == CaptureState.Active, "host expiry clears native credential without stopping observed recording");
        var other = authority.Claim("other companion");
        client.HostToken = "fixture-invalid-credential";
        await app.DispatchAsync(Request(new RefreshCamera(app.State.Camera.ConnectionId), CommandOrigin.System));
        Check(app.State.Camera.Ownership == CameraOwnership.Guest && client.HostToken is null && app.State.Camera.HostOwner == "other companion", "displayed host and invalid saved token cannot prove control");
        authority.Release(other.Token);
        await app.DispatchAsync(Request(new RefreshCamera(app.State.Camera.ConnectionId), CommandOrigin.System));
        await app.DispatchAsync(Request(new ClaimCameraHost(app.State.Camera.Scope!, "fixture owner")));
        await app.DispatchAsync(Request(new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Stop)));
        loseStartAck = true; var lost = Request(new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Start));
        Check((await app.DispatchAsync(lost)).Outcome == CommandOutcome.Uncertain && app.State.Camera.Capture == CaptureState.Active && app.State.Camera.CanControl,
            "lost mutation acknowledgement keeps uncertainty while publishing separately verified actual state");
        before = mutations; Check((await app.DispatchAsync(lost)).Duplicate && mutations == before, "unknown mutation is not automatically retried");
        loseStartAck = false;
        await app.DispatchAsync(Request(new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Stop)));
        await app.DispatchAsync(Request(new ReleaseCameraHost(app.State.Camera.Scope!)));
        Check(app.State.Camera.Ownership == CameraOwnership.Available && client.HostToken is null && persistedToken is null, "release removes persisted credential and confirms available host");
        var canonical = JsonSerializer.Serialize(app.State, JsonDefaults.Options);
        Check(!canonical.Contains(savedCredential) && !canonical.Contains("fixture-native-view-token") && !canonical.Contains("http://") && !canonical.Contains("fixture-invalid-credential"), "canonical camera state excludes native credentials and viewing URLs");
        Check(adapter.Latest?.Connection == app.State.Camera, "native media binding matches canonical connection");
        Console.WriteLine("PASS real camera control HTTP adapter: host proof, scoped operations, reconnect, lease expiry, invalid token, capture no-op proof, lost acknowledgement and native-only credentials");
    }
}
