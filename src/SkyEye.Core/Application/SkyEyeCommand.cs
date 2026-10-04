namespace SkyEye.Core.Application;

public enum CommandOrigin { Touch, Sky, Vision, System }
public abstract record SkyEyeCommand;
public sealed record ShowShotChoices(bool Enabled) : SkyEyeCommand;
public sealed record SetTargetGroup(AdviceTargetGroup Group) : SkyEyeCommand;
public sealed record CommandEnvelope(Guid OperationId, CommandOrigin Origin, long ExpectedVersion, SkyEyeCommand Command);
public enum CommandOutcome { Applied, Unchanged, Stale, Rejected, Conflict }

/// <summary>Receipt for an operation, not a replacement for the current app snapshot.
/// A duplicate retains the original decision version even if later commands have committed.</summary>
public sealed record CommandReceipt(Guid OperationId, CommandOutcome Outcome, long DecisionVersion, string Message, bool Duplicate = false)
{
    public bool Succeeded => Outcome is CommandOutcome.Applied or CommandOutcome.Unchanged;
}
