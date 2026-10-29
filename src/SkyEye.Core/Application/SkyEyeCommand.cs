namespace SkyEye.Core.Application;

public enum CommandOrigin { Touch, Sky, Vision, System }
public abstract record SkyEyeCommand;
public sealed record ShowShotChoices(bool Enabled) : SkyEyeCommand;
public sealed record SetTargetGroup(AdviceTargetGroup Group) : SkyEyeCommand;
// Native adapters supply these non-secret identities after observing camera status.
public sealed record ClipSaveScope(string CameraIdentity, string SessionId, long HostGeneration, long CaptureRevision);
// Source-time event clips are cut on the camera timeline. The saved clip begins
// at the preceding independently decodable segment keyframe and ends at EndUs.
public sealed record ClipSourceRange(string Timeline, long StartUs, long EndUs);
// Seconds is a convenience input for older callers; the camera adapter converts it
// to a source range before saving. SourceRange is the canonical request form.
public sealed record SaveClip(int Seconds, ClipSaveScope Scope, ClipSourceRange? SourceRange = null) : SkyEyeCommand;
public sealed record CommandEnvelope(Guid OperationId, CommandOrigin Origin, long ExpectedVersion, SkyEyeCommand Command);
public enum CommandOutcome { Applied, Unchanged, Stale, Rejected, Conflict, Failed, Uncertain }

/// <summary>Receipt for an operation, not a replacement for the current app snapshot.
/// A duplicate retains the original decision version even if later commands have committed.</summary>
public sealed record CommandReceipt(Guid OperationId, CommandOutcome Outcome, long DecisionVersion, string Message, bool Duplicate = false, Clip? SavedClip = null)
{
    public TableObservation? Observation { get; init; }
    public bool Succeeded => Outcome is CommandOutcome.Applied or CommandOutcome.Unchanged;
}
