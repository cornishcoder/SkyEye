namespace SkyEye.Core.Application;

public enum CommandOrigin { Touch, Sky, Vision, System }
public abstract record SkyEyeCommand;
public sealed record ShowShotChoices(bool Enabled) : SkyEyeCommand;
public sealed record SetTargetGroup(AdviceTargetGroup Group) : SkyEyeCommand;
// Native adapters supply these non-secret identities after observing camera status.
public sealed record ClipSaveScope(string CameraIdentity, string SessionId, long HostGeneration, long CaptureRevision);
public sealed record SaveClip(int Seconds, ClipSaveScope Scope) : SkyEyeCommand;
public sealed record CommandEnvelope(Guid OperationId, CommandOrigin Origin, long ExpectedVersion, SkyEyeCommand Command);
public enum CommandOutcome { Applied, Unchanged, Stale, Rejected, Conflict, Failed, Uncertain }

/// <summary>Receipt for an operation, not a replacement for the current app snapshot.
/// A duplicate retains the original decision version even if later commands have committed.</summary>
public sealed record CommandReceipt(Guid OperationId, CommandOutcome Outcome, long DecisionVersion, string Message, bool Duplicate = false, Clip? SavedClip = null)
{
    public bool Succeeded => Outcome is CommandOutcome.Applied or CommandOutcome.Unchanged;
}
