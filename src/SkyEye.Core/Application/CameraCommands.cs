using System.Security.Cryptography;
using System.Text;

namespace SkyEye.Core.Application;

public enum CameraReachability { Disconnected, Connecting, Reachable, Unreachable }
public enum CameraOwnership { Unknown, Available, Guest, Owned }
public enum CaptureAction { Start, Pause, Resume, Stop }
public sealed record CameraScope(Guid ConnectionId, string CameraIdentity, string SessionId, long HostGeneration, long CaptureRevision);

/// <summary>Control-relevant observed facts only. Credentials, viewing URLs and
/// frequently changing native telemetry are not part of this command projection.</summary>
public sealed record CameraConnection(Guid ConnectionId, string? CameraIdentity, CameraReachability Reachability,
    bool? Armed, CaptureState? Capture, CameraOwnership Ownership, string? HostOwner, long HostGeneration, string? SessionId, long CaptureRevision)
{
    public static CameraConnection Empty { get; } = new(Guid.Empty, null, CameraReachability.Disconnected, null, null, CameraOwnership.Unknown, null, 0, null, 0);
    public bool CanControl => Reachability == CameraReachability.Reachable && Ownership == CameraOwnership.Owned;
    public CameraScope? Scope => Reachability == CameraReachability.Reachable && CameraIdentity is not null && SessionId is not null
        ? new(ConnectionId, CameraIdentity, SessionId, HostGeneration, CaptureRevision) : null;
    public CameraConnection Unreachable() => this with { Reachability = CameraReachability.Unreachable, Armed = null, Capture = null, Ownership = CameraOwnership.Unknown, HostOwner = null };
}

public abstract record CameraCommand : SkyEyeCommand;
public sealed record ConnectCamera(string Address) : CameraCommand;
public sealed record RefreshCamera(Guid ConnectionId) : CameraCommand;
public sealed record ClaimCameraHost(CameraScope Scope, string Owner) : CameraCommand;
public sealed record ReleaseCameraHost(CameraScope Scope) : CameraCommand;
public sealed record ChangeCapture(CameraScope Scope, CaptureAction Action) : CameraCommand;
public sealed record CameraControlResult(CameraConnection Connection, CommandOutcome Outcome, string Message);
public interface ICameraControlEffect
{
    Task<CameraControlResult> ExecuteAsync(Guid operationId, CameraCommand command, CameraConnection admitted, CancellationToken cancellation);
}

public static class CameraEndpoint
{
    public static string Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 256) throw new ArgumentException("Enter the camera's local address, for example 192.168.1.100:8765.");
        var text = input.Trim(); if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/")
            throw new ArgumentException("Enter the camera's local address, for example 192.168.1.100:8765.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
    public static string Identity(string address) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Parse(address).ToLowerInvariant())));
}
