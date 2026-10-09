using System.Collections.Immutable;

namespace SkyEye.GameState;

public enum DeclaredLegality { Unknown, Legal, StandardFoul, LossOfTurn, LossOfFrame }
public enum CueBallEntitlement { None, Anywhere, Baulk }
public enum LegalTarget { Unknown, OpenColours, Red, Yellow, Black, Finished }
// The player declares complete legality; this DTO is not a contact detector.
public sealed record DeclaredShot(bool Break, DeclaredLegality Legality, int RedsPotted = 0, int YellowsPotted = 0,
    bool BlackPotted = false, bool CueBallPotted = false, bool CueBallOffTable = false,
    bool? LegalBreak = null, BallColour? FirstContact = null, bool? CallRequired = null, BallColour? CalledGroup = null,
    int? RedsBefore = null, int? YellowsBefore = null);
public sealed record RulePosition(string Profile, string Player, BallColour? PlayerGroup, int? Reds, int? Yellows,
    CueBallEntitlement CueBall, LegalTarget Target, bool? Foul, string? Winner, bool BreakComplete,
    bool Pending, string Reason, ImmutableArray<string> Clauses, Guid? LastInputId = null)
{
    public bool BlackNeedsRespot { get; init; }
}
public sealed record RuleRuling(string Shooter, DeclaredShot Facts, RulePosition Position, Guid? ReplacesInputId = null);
// Only the application rules consumer produces this interpretation.
public sealed record ApplyShotRuling(RuleRuling Ruling) : GameInput;
public sealed record RulesShotRecordedEvent(RuleRuling Ruling) : GameEvent;
public sealed record SettledInventoryAcceptedEvent(int ContractVersion, ActivityWindowContext Context,
    SettledTableEvidence After) : GameEvent;
public sealed record SettledInventoryCorrectedEvent(int ContractVersion, SettledTableCounts Counts,
    ActivityWindowContext? Context = null, SettledTableEvidence? Observed = null) : GameEvent;
public sealed record SettledInventoryRejectedEvent(int ContractVersion, ActivityWindowContext Context,
    SettledTableEvidence After) : GameEvent;

public static class GameRevisions
{
    public static HashSet<Guid> Suppressed(IEnumerable<EventEnvelope> history)
    {
        var events = history.ToArray();
        var ids = events.Where(e => e.Event is DecisionUndoneEvent).Select(e => ((DecisionUndoneEvent)e.Event).InputId).ToHashSet();
        foreach (var e in events.Where(e => !ids.Contains(e.InputId) && e.Event is RulesShotRecordedEvent { Ruling.ReplacesInputId: not null }))
            ids.Add(((RulesShotRecordedEvent)e.Event).Ruling.ReplacesInputId!.Value);
        return ids;
    }
}
