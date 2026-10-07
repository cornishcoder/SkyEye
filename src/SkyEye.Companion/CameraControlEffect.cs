using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

// The full protocol response and viewing access remain native, outside app/agent
// snapshots. UI may use them only when this binding matches canonical state.
public sealed record NativeCameraView(CameraConnection Connection, JsonElement Status, string Stream)
{
    public string PhoneStream { get; init; } = Stream;
    public long StatusReceiptTimestamp { get; init; }
    public double StatusRoundTripMs { get; init; }
}

public sealed class CameraControlEffect(CameraClient current, Action<string?>? persistHost = null) : ICameraControlEffect
{
    NativeCameraView? latest;
    public NativeCameraView? Latest => Volatile.Read(ref latest);

    public async Task<CameraControlResult> ExecuteAsync(Guid operationId, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
    {
        bool mutationSent = false;
        try
        {
            if (command is ConnectCamera connect)
            {
                var endpoint = CameraEndpoint.Parse(connect.Address);
                if (!string.Equals(current.Address, endpoint, StringComparison.OrdinalIgnoreCase)) ClearHost();
                current.Connect(endpoint);
                return new(await ObserveAsync(admitted, cancellation).ConfigureAwait(false), CommandOutcome.Applied, "Connected to camera.");
            }
            if (string.IsNullOrEmpty(current.Address) || CameraEndpoint.Identity(current.Address) != admitted.CameraIdentity)
                return new(admitted.Unreachable(), CommandOutcome.Stale, "The native camera connection changed. Reconnect before controlling it.");
            var observed = await ObserveAsync(admitted, cancellation).ConfigureAwait(false);
            if (command is RefreshCamera) return new(observed, CommandOutcome.Applied, "Camera status refreshed.");
            var expected = command switch { ClaimCameraHost c => c.Scope, ReleaseCameraHost c => c.Scope, ChangeCapture c => c.Scope, _ => null };
            if (expected != observed.Scope)
                return new(observed, CommandOutcome.Stale, "Camera session or ownership changed. Check its current state.");
            if (command is ClaimCameraHost claim)
            {
                if (observed.Ownership != CameraOwnership.Available)
                    return new(observed, CommandOutcome.Failed, "Another host already controls this camera.");
                mutationSent = true;
                var result = await current.PostAsync("host/claim", new { owner = claim.Owner }, cancellation).ConfigureAwait(false);
                var token = result.GetProperty("token").GetString();
                if (token is not { Length: 64 } || !token.All(Uri.IsHexDigit)) throw new IOException("Invalid host acknowledgement.");
                current.HostToken = token; persistHost?.Invoke(token);
            }
            else
            {
                if (!observed.CanControl) return new(observed, CommandOutcome.Failed, "Host control has expired or belongs to another companion.");
                if (command is ChangeCapture unchanged && (unchanged.Action == CaptureAction.Start && observed.Capture == CaptureState.Active
                    || unchanged.Action == CaptureAction.Stop && observed.Capture is CaptureState.Ready or CaptureState.Stopped))
                    return new(observed, CommandOutcome.Unchanged, "Capture is already in that state.");
                mutationSent = true;
                if (command is ReleaseCameraHost)
                {
                    await current.PostAsync("host/release", new { }, cancellation).ConfigureAwait(false); ClearHost();
                }
                else if (command is ChangeCapture capture)
                {
                    var route = capture.Action switch { CaptureAction.Start => "start", CaptureAction.Pause => "pause", CaptureAction.Resume => "resume", CaptureAction.Stop => "stop", _ => throw new ArgumentException("Unsupported capture action.") };
                    await current.PostAsync("session/" + route, new { commandId = operationId.ToString("N") }, cancellation).ConfigureAwait(false);
                }
                else throw new ArgumentException("Unsupported camera command.");
            }
            var acknowledged = await ObserveAsync(admitted, cancellation).ConfigureAwait(false);
            bool agrees = command switch
            {
                ClaimCameraHost => acknowledged.CanControl,
                ReleaseCameraHost => !acknowledged.CanControl,
                ChangeCapture c => acknowledged.Capture == (c.Action switch { CaptureAction.Start or CaptureAction.Resume => CaptureState.Active, CaptureAction.Pause => CaptureState.Paused, _ => CaptureState.Stopped }),
                _ => false
            };
            if (!agrees) return new(acknowledged, CommandOutcome.Uncertain, "Camera status differs from the requested result. Check its observed state.");
            return new(acknowledged, CommandOutcome.Applied, command switch
            {
                ClaimCameraHost => "You control this camera session.", ReleaseCameraHost => "Host control released.",
                ChangeCapture c => "Capture is " + (c.Action switch { CaptureAction.Start or CaptureAction.Resume => "active", CaptureAction.Pause => "paused", _ => "stopped" }) + ".",
                _ => "Camera updated."
            });
        }
        catch (Exception e)
        {
            bool refused = !mutationSent || e is CameraRequestException request && (request.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden
                || request.StatusCode == System.Net.HttpStatusCode.Conflict && command is ClaimCameraHost);
            CameraConnection observed;
            try { observed = await ObserveAsync(admitted, cancellation).ConfigureAwait(false); }
            catch { observed = admitted.Unreachable(); }
            return new(observed, refused ? CommandOutcome.Failed : CommandOutcome.Uncertain,
                refused ? "Camera request failed. Check its observed status before trying again." : "Camera control result is unknown. Refresh its status; this request will not be repeated automatically.");
        }
    }

    async Task<CameraConnection> ObserveAsync(CameraConnection admitted, CancellationToken cancellation)
    {
        if (CameraEndpoint.Identity(current.Address) != admitted.CameraIdentity) throw new IOException("Native endpoint no longer matches admission.");
        var status = await current.StatusAsync(cancellation).ConfigureAwait(false);
        bool owns = false; long verifiedGeneration = 0;
        if (current.HostToken is { } token)
        {
            try
            {
                var lease = await current.PostAsync("host/heartbeat", new { }, cancellation).ConfigureAwait(false);
                verifiedGeneration = lease.GetProperty("generation").GetInt64();
                status = await current.StatusAsync(cancellation).ConfigureAwait(false);
                var hostAfterProof = status.GetProperty("host");
                owns = hostAfterProof.ValueKind != JsonValueKind.Null && hostAfterProof.GetProperty("generation").GetInt64() == verifiedGeneration;
                if (!owns) ClearHost();
            }
            catch (CameraRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Forbidden)
            { ClearHost(); status = await current.StatusAsync(cancellation).ConfigureAwait(false); }
        }
        if (!Enum.TryParse<CaptureState>(status.GetProperty("state").GetString(), out var capture) || !Enum.IsDefined(capture)) throw new IOException("Invalid camera state.");
        var host = status.GetProperty("host"); bool available = host.ValueKind == JsonValueKind.Null;
        string? owner = available ? null : host.GetProperty("owner").GetString();
        if (owner?.Length > 80) owner = owner[..80];
        var observed = admitted with { Reachability = CameraReachability.Reachable, Armed = status.GetProperty("armed").GetBoolean(), Capture = capture,
            Ownership = available ? CameraOwnership.Available : owns ? CameraOwnership.Owned : CameraOwnership.Guest, HostOwner = owner,
            HostGeneration = available ? 0 : host.GetProperty("generation").GetInt64(), SessionId = status.GetProperty("sessionId").GetString(), CaptureRevision = status.GetProperty("revision").GetInt64() };
        var stream = current.MediaUrl(status.GetProperty("streamPath").GetString()!);
        var phoneStream = status.TryGetProperty("liveTransportVersion", out var version) && version.GetInt32() == 1
            && status.TryGetProperty("phoneStreamPath", out var path) && path.GetString() is { } phonePath && phonePath.StartsWith("/live/stream.ts?")
            ? current.MediaUrl(phonePath) : stream;
        Volatile.Write(ref latest, new(observed, status, stream) { PhoneStream = phoneStream,
            StatusReceiptTimestamp = current.StatusReceiptTimestamp, StatusRoundTripMs = current.StatusRoundTripMs });
        return observed;
    }
    void ClearHost() { current.HostToken = null; persistHost?.Invoke(null); }
}
