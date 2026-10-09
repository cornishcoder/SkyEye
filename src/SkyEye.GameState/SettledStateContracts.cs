using System.Collections.Immutable;

namespace SkyEye.GameState;

/// <summary>Identity-free counts from one complete, settled and unobstructed table observation.</summary>
public sealed record SettledTableCounts(int? Reds, int? Yellows, int? CueBalls, int? BlackBalls)
{
    public bool IsComplete => Reds is not null && Yellows is not null && CueBalls is not null && BlackBalls is not null;

    internal bool IsValid => IsComplete
        && Reds is >= 0 and <= 16 && Yellows is >= 0 and <= 16
        && CueBalls is >= 0 and <= 1 && BlackBalls is >= 0 and <= 1;

    internal static SettledTableCounts? From(TableState table)
    {
        var counts = new SettledTableCounts(table.Remaining(BallColour.Red), table.Remaining(BallColour.Yellow),
            table.Remaining(BallColour.Cue), table.Remaining(BallColour.Black));
        return counts.IsValid ? counts : null;
    }
}

/// <summary>One source-referenced table observation. Clear and settled are separate eligibility facts.</summary>
public sealed record SettledTableEvidence(Guid EvidenceId, EvidenceSource Source, long? SourceTimeUs,
    DateTimeOffset CapturedAt, SettledTableCounts Counts, bool Settled, bool Clear)
{
    public static bool TryCapture(GameSnapshot snapshot, out SettledTableEvidence evidence)
    {
        evidence = null!;
        if (snapshot.Table.Motion != TableMotion.Settled || snapshot.Table.Uncertain
            || snapshot.Table.Source is not { } source || snapshot.Table.LastSettled is not { } settled
            || settled.EvidenceId == Guid.Empty || settled.Source != source || settled.SourceTimeUs is not { } sourceTime
            || !ValidSource(source) || sourceTime < 0 || SettledTableCounts.From(snapshot.Table) is not { } counts)
            return false;

        evidence = new(settled.EvidenceId, source, sourceTime, settled.At, counts, Settled: true, Clear: true);
        return true;
    }

    internal bool IsValid => EvidenceId != Guid.Empty && Source is not null && ValidSource(Source)
        && SourceTimeUs is >= 0 && Counts is not null;
    internal bool IsComplete => IsValid && Settled && Clear && Counts.IsValid;

    static bool ValidSource(EvidenceSource source) => Text(source.Camera) && Text(source.Timeline)
        && Text(source.Calibration) && source.Generation > 0 && (source.Connection is null || Text(source.Connection));
    static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}

/// <summary>Canonical match facts frozen when an activity window begins.</summary>
public sealed record FrozenGameContext(string MatchId, string FrameId, long GameVersion, string CurrentPlayer,
    int Visit, bool OpenTable, ImmutableDictionary<string, Fact<BallColour?>> Groups, Fact<bool?>? Foul, RulePosition? Rules)
{
    public static bool TryCapture(GameSnapshot snapshot, out FrozenGameContext context)
    {
        context = null!;
        if (snapshot.Match is not { Finished: false } match || snapshot.Frame is not { Winner: null } frame
            || !Text(match.Id) || !Text(frame.Id) || !Text(frame.CurrentPlayer.Value) || frame.Visit < 1
            || frame.Colours is null)
            return false;

        context = new(match.Id, frame.Id, snapshot.Version, frame.CurrentPlayer.Value, frame.Visit,
            frame.OpenTable, frame.Colours, frame.Foul, frame.Rules);
        return true;
    }

    internal bool IsValid => Text(MatchId) && Text(FrameId) && GameVersion >= 0 && Text(CurrentPlayer)
        && Visit >= 1 && Groups is not null && Groups.All(group => Text(group.Key) && group.Value is not null);

    // GameVersion is retained for audit; compatible non-game events may advance it while an activity window is open.
    internal bool Matches(GameSnapshot snapshot)
    {
        if (!IsValid || snapshot.Match is not { Finished: false } match || match.Id != MatchId
            || snapshot.Frame is not { Winner: null } frame || frame.Id != FrameId
            || frame.CurrentPlayer.Value != CurrentPlayer || frame.Visit != Visit || frame.OpenTable != OpenTable
            || !SameRules(frame.Rules, Rules) || frame.Colours.Count != Groups.Count)
            return false;
        return Groups.All(group => frame.Colours.TryGetValue(group.Key, out var current) && SameFact(current, group.Value));
    }

    static bool SameFact<T>(Fact<T>? left, Fact<T>? right) => left is null ? right is null
        : right is not null && EqualityComparer<T>.Default.Equals(left.Value, right.Value)
            && left.Source == right.Source && left.Confidence == right.Confidence && left.EvidenceId == right.EvidenceId;

    static bool SameRules(RulePosition? left, RulePosition? right) => left is null ? right is null
        : right is not null && left.Profile == right.Profile && left.Player == right.Player
            && left.PlayerGroup == right.PlayerGroup && left.Reds == right.Reds && left.Yellows == right.Yellows
            && left.CueBall == right.CueBall && left.Target == right.Target && left.Foul == right.Foul
            && left.Winner == right.Winner && left.BreakComplete == right.BreakComplete && left.Pending == right.Pending
            && left.Reason == right.Reason && left.Clauses.SequenceEqual(right.Clauses)
            && left.LastInputId == right.LastInputId && left.BlackNeedsRespot == right.BlackNeedsRespot;

    static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
}

/// <summary>Pre-shot baseline and canonical context retained across activity and obstruction.</summary>
public sealed record ActivityWindowContext(Guid Id, FrozenGameContext Game, SettledTableEvidence Before,
    long? ActivityStartSourceTimeUs)
{
    public static bool TryBegin(Guid id, GameSnapshot snapshot, long? activityStartSourceTimeUs,
        out ActivityWindowContext context)
    {
        context = null!;
        if (id == Guid.Empty || !FrozenGameContext.TryCapture(snapshot, out var game)
            || !SettledTableEvidence.TryCapture(snapshot, out var before)
            || activityStartSourceTimeUs is < 0
            || activityStartSourceTimeUs is { } start && start <= before.SourceTimeUs)
            return false;

        context = new(id, game, before, activityStartSourceTimeUs);
        return true;
    }

    internal bool IsValid => Id != Guid.Empty && Game is not null && Game.IsValid
        && Before is not null && Before.IsComplete
        && (ActivityStartSourceTimeUs is null || ActivityStartSourceTimeUs > Before.SourceTimeUs);
}

public enum SettledComparisonStatus
{
    Ready,
    InvalidContext,
    IncompleteEvidence,
    AwaitingClearTable,
    SourceDiscontinuity,
    NonMonotonicSourceTime
}

public sealed record BallCountChange(BallColour Colour, int Before, int After)
{
    public int Removed => Math.Max(0, Before - After);
    public int Added => Math.Max(0, After - Before);
}

/// <summary>A count-only comparison. It makes no claim about identity, cause, pocket or legality.</summary>
public sealed record SettledInventoryComparison(ActivityWindowContext Context, SettledTableEvidence After,
    ImmutableArray<BallCountChange> Changes)
{
    public int RemovedFromTable => Changes.Sum(change => change.Removed);
    public int AddedToTable => Changes.Sum(change => change.Added);
}

public sealed record SettledComparisonResult(SettledComparisonStatus Status, string Reason,
    SettledInventoryComparison? Comparison = null)
{
    public bool Ready => Status == SettledComparisonStatus.Ready && Comparison is not null;
}

/// <summary>Pure O04.02 count comparison over the O03 frozen activity context.</summary>
public static class SettledStateReconciler
{
    public static SettledComparisonResult Compare(ActivityWindowContext? context, SettledTableEvidence? after)
    {
        if (context is null || !context.IsValid)
            return new(SettledComparisonStatus.InvalidContext, "A valid frozen game context and trusted before-table are required.");
        if (after is null || !after.IsValid)
            return new(SettledComparisonStatus.IncompleteEvidence, "The after-table counts or source evidence are incomplete or invalid.");
        if (context.Before.Source != after.Source)
            return new(SettledComparisonStatus.SourceDiscontinuity, "The camera, timeline, generation, connection or calibration changed.");
        if (!after.Settled || !after.Clear)
            return new(SettledComparisonStatus.AwaitingClearTable, "Wait for a settled observation with a clear table view.");
        if (!after.Counts.IsValid)
            return new(SettledComparisonStatus.IncompleteEvidence, "The clear after-table does not have valid complete ball counts.");
        if (after.SourceTimeUs <= context.Before.SourceTimeUs
            || context.ActivityStartSourceTimeUs is { } start && after.SourceTimeUs <= start)
            return new(SettledComparisonStatus.NonMonotonicSourceTime, "The after-table source time must follow the before-table and activity start.");

        var beforeCounts = context.Before.Counts;
        var afterCounts = after.Counts;
        var changes = ImmutableArray.CreateBuilder<BallCountChange>(4);
        Add(BallColour.Red, beforeCounts.Reds!.Value, afterCounts.Reds!.Value);
        Add(BallColour.Yellow, beforeCounts.Yellows!.Value, afterCounts.Yellows!.Value);
        Add(BallColour.Cue, beforeCounts.CueBalls!.Value, afterCounts.CueBalls!.Value);
        Add(BallColour.Black, beforeCounts.BlackBalls!.Value, afterCounts.BlackBalls!.Value);
        return new(SettledComparisonStatus.Ready, "Settled inventory counts are comparable; removed balls are not assumed to be potted.",
            new(context, after, changes.ToImmutable()));

        void Add(BallColour colour, int before, int current)
        {
            if (before != current) changes.Add(new(colour, before, current));
        }
    }
}
