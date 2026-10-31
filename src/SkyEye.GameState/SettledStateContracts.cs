using System.Collections.Immutable;
using System.Text.Json.Serialization;

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

/// <summary>A detector-observed position normalized to the playable rectangle, without a durable ball identity.</summary>
public sealed record SettledBallPosition(BallColour Colour, double X, double Y);

public sealed record SettledLayoutDifference(bool Comparable, int MovedBallCount, double LargestMovement)
{
    public bool Changed => Comparable && MovedBallCount > 0;
}

/// <summary>One source-referenced table observation. Clear and settled are separate eligibility facts.</summary>
public sealed record SettledTableEvidence(Guid EvidenceId, EvidenceSource Source, long? SourceTimeUs,
    DateTimeOffset CapturedAt, SettledTableCounts Counts, bool Settled, bool Clear)
{
    /// <summary>Optional identity-free spatial evidence; X and Y are normalized to 0..1.</summary>
    public ImmutableArray<SettledBallPosition> Layout { get; init; } = [];

    /// <summary>The source timestamp for Layout, which can be newer than the canonical count baseline.</summary>
    public long? LayoutSourceTimeUs { get; init; }

    /// <summary>True only when the bounded O08 verifier supplied the clear/still proof.</summary>
    public bool BoundedStillnessVerified { get; init; }

    /// <summary>Number of consecutive matching clear samples supporting this settled layout.</summary>
    public int StableClearSampleCount { get; init; }

    /// <summary>True when bounded stillness verification or three matching clear samples prove a stable layout.</summary>
    [JsonIgnore]
    public bool HasBoundedClearStillnessProof => BoundedStillnessVerified || StableClearSampleCount >= 3;

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
        && SourceTimeUs is >= 0 && Counts is not null && StableClearSampleCount is >= 0 and <= 120
        && ValidLayout(Layout, LayoutSourceTimeUs, Counts);
    internal bool IsComplete => IsValid && Settled && Clear && Counts.IsValid;

    static bool ValidLayout(ImmutableArray<SettledBallPosition> layout, long? sourceTimeUs, SettledTableCounts counts)
    {
        if (layout.IsDefaultOrEmpty) return sourceTimeUs is null;
        if (sourceTimeUs is not >= 0 || layout.Length is < 1 or > 16
            || layout.Any(ball => ball is null || ball.Colour is not (BallColour.Red or BallColour.Yellow or BallColour.Cue or BallColour.Black)
                || !double.IsFinite(ball.X) || !double.IsFinite(ball.Y) || ball.X is < 0 or > 1 || ball.Y is < 0 or > 1))
            return false;
        return layout.Count(ball => ball.Colour == BallColour.Red) == counts.Reds
            && layout.Count(ball => ball.Colour == BallColour.Yellow) == counts.Yellows
            && layout.Count(ball => ball.Colour == BallColour.Cue) == counts.CueBalls
            && layout.Count(ball => ball.Colour == BallColour.Black) == counts.BlackBalls;
    }

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

    /// <summary>Whether a live snapshot still describes the match context frozen at activity start.</summary>
    public bool IsCompatibleWith(GameSnapshot snapshot) => Matches(snapshot);

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
            || activityStartSourceTimeUs is { } start
                && start <= Math.Max(before.SourceTimeUs ?? -1, before.LayoutSourceTimeUs ?? -1))
            return false;

        context = new(id, game, before, activityStartSourceTimeUs);
        return true;
    }

    internal bool IsValid => Id != Guid.Empty && Game is not null && Game.IsValid
        && Before is not null && Before.IsComplete
        && (ActivityStartSourceTimeUs is null || ActivityStartSourceTimeUs > Before.SourceTimeUs)
        && (Before.LayoutSourceTimeUs is null || ActivityStartSourceTimeUs is { } start && start > Before.LayoutSourceTimeUs);
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

/// <summary>A plain-language, non-probabilistic outcome recommendation from settled counts.</summary>
public sealed record SettledOutcomeRecommendation(string Kind, string Strength, string Summary,
    bool AutoApprovalEligible, ImmutableArray<string> Alternatives);

/// <summary>Compares same-colour positions without assigning identities to indistinguishable balls.</summary>
public static class SettledLayoutReconciler
{
    public const double MovementThreshold = .018;
    public const double StableLayoutTolerance = .010;
    public const double LivePositionNoiseTolerance = MovementThreshold;

    /// <summary>
    /// Returns whether every position in the smaller view has a nearby counterpart
    /// in the larger view. Inventory count is deliberately evaluated separately;
    /// missing or extra detections are not themselves evidence that balls moved.
    /// </summary>
    public static bool PositionsCompatible(ImmutableArray<SettledBallPosition> first,
        ImmutableArray<SettledBallPosition> second, double tolerance = LivePositionNoiseTolerance)
    {
        if (first.IsDefaultOrEmpty || second.IsDefaultOrEmpty
            || !double.IsFinite(tolerance) || tolerance < 0) return false;

        var smaller = first.Length <= second.Length ? first : second;
        var remaining = (first.Length <= second.Length ? second : first)
            .Select(ball => (ball.X, ball.Y)).ToList();
        foreach (var ball in smaller)
        {
            if (remaining.Count == 0) return false;
            int nearest = 0;
            double nearestSquared = double.PositiveInfinity;
            for (int index = 0; index < remaining.Count; index++)
            {
                double dx = remaining[index].X - ball.X;
                double dy = remaining[index].Y - ball.Y;
                double squared = dx * dx + dy * dy;
                if (squared < nearestSquared) { nearest = index; nearestSquared = squared; }
            }
            if (Math.Sqrt(nearestSquared) > tolerance) return false;
            remaining.RemoveAt(nearest);
        }
        return true;
    }

    public static SettledLayoutDifference Compare(ImmutableArray<SettledBallPosition> before,
        ImmutableArray<SettledBallPosition> after, double threshold = MovementThreshold)
    {
        if (before.IsDefaultOrEmpty || after.IsDefaultOrEmpty || !double.IsFinite(threshold) || threshold < 0)
            return new(false, 0, 0);

        int moved = 0;
        double largest = 0;
        foreach (var colour in new[] { BallColour.Red, BallColour.Yellow, BallColour.Cue, BallColour.Black })
        {
            var oldBalls = before.Where(ball => ball.Colour == colour).ToArray();
            var newBalls = after.Where(ball => ball.Colour == colour).ToArray();
            if (oldBalls.Length != newBalls.Length) return new(false, 0, 0);
            if (oldBalls.Length == 0) continue;

            int bestMoved = int.MaxValue;
            double bestDistance = double.PositiveInfinity;
            double bestLargest = 0;
            var used = new bool[newBalls.Length];
            Search(0, 0, 0, 0);
            moved += bestMoved;
            largest = Math.Max(largest, bestLargest);

            void Search(int index, int movedCount, double distance, double pairLargest)
            {
                if (index == oldBalls.Length)
                {
                    if (movedCount < bestMoved || movedCount == bestMoved && distance < bestDistance)
                    {
                        bestMoved = movedCount;
                        bestDistance = distance;
                        bestLargest = pairLargest;
                    }
                    return;
                }
                for (int candidate = 0; candidate < newBalls.Length; candidate++)
                {
                    if (used[candidate]) continue;
                    double dx = oldBalls[index].X - newBalls[candidate].X;
                    double dy = oldBalls[index].Y - newBalls[candidate].Y;
                    double displacement = Math.Sqrt(dx * dx + dy * dy);
                    used[candidate] = true;
                    Search(index + 1, movedCount + (displacement > threshold ? 1 : 0),
                        distance + displacement * displacement, Math.Max(pairLargest, displacement));
                    used[candidate] = false;
                }
            }
        }
        return new(true, moved, largest);
    }

    public static bool SameLayout(ImmutableArray<SettledBallPosition> first,
        ImmutableArray<SettledBallPosition> second, double tolerance = StableLayoutTolerance)
    {
        var difference = Compare(first, second, tolerance);
        return difference.Comparable && !difference.Changed;
    }
}

/// <summary>A settled table comparison. Stable count decreases may be treated as pots and changed same-count layouts as legal no-pot shots by policy, without inventing identity or pocket facts.</summary>
public sealed record SettledInventoryComparison(ActivityWindowContext Context, SettledTableEvidence After,
    ImmutableArray<BallCountChange> Changes)
{
    public SettledLayoutDifference LayoutDifference { get; init; } = new(false, 0, 0);
    public int RemovedFromTable => Changes.Sum(change => change.Removed);
    public int AddedToTable => Changes.Sum(change => change.Added);
    public bool LayoutChanged => LayoutDifference.Changed;

    public SettledOutcomeRecommendation Recommendation
    {
        get
        {
            var before = Context.Before.Counts;
            var after = After.Counts;
            bool cueChanged = before.CueBalls != after.CueBalls;
            bool blackChanged = before.BlackBalls != after.BlackBalls;
            bool repeatedClearEvidence = After.HasBoundedClearStillnessProof;

            if (RemovedFromTable > 0 && AddedToTable == 0)
            {
                string detail = string.Join(" and ", Changes.Where(change => change.Removed > 0)
                    .Select(change => change.Colour switch
                    {
                        BallColour.Cue => "cue-ball scratch",
                        BallColour.Black => "black-ball pot",
                        _ => $"{change.Removed} {change.Colour.ToString().ToLowerInvariant()} pot{(change.Removed == 1 ? "" : "s")}"
                    }));
                return new("settled-pot-outcome", repeatedClearEvidence ? "strong" : "suggested",
                    $"{(repeatedClearEvidence ? "Assume" : "Likely")} {detail} from the clear settled count decrease.", repeatedClearEvidence,
                    ["Correct the observed counts", "Record a non-pot removal", "Reject this observation"]);
            }

            if (Changes.IsEmpty)
                return LayoutChanged
                    ? new("settled-no-pot-shot", After.HasBoundedClearStillnessProof ? "strong" : "suggested",
                        $"The settled layout changed ({LayoutDifference.MovedBallCount} ball(s) moved) with no count change. Assume a legal no-pot shot and pass the turn unless the player reports a foul or other exception.",
                        After.HasBoundedClearStillnessProof,
                        ["Report a foul to Sky", "Correct or undo this result"])
                    : new("no-net-count-change", "informational", "No net ball-count or settled-layout change was detected.", false,
                        ["Review the table", "Reject this observation"]);

            if (cueChanged && !blackChanged && after.CueBalls < before.CueBalls)
                return new("cue-ball-removed", "review", "Cue ball missing from the settled table; check for a scratch or other removal.", false,
                    ["Review a possible scratch", "Treat the cue ball as lifted or missing", "Correct the observed counts"]);

            if (blackChanged && !cueChanged && after.BlackBalls < before.BlackBalls)
                return new("black-ball-removed", "review", "Black ball missing; frame result and legality need review.", false,
                    ["Review frame result and legality", "Treat the black as removed for another reason", "Correct the observed counts"]);

            if (AddedToTable > 0)
                return new("presence-increased", "review", "More balls are visible; check for a return, replacement, rack or count error.", false,
                    ["Review a returned or replaced ball", "Correct the observed counts", "Reject this observation"]);

            return new("mixed-inventory-change", "review", "Counts changed in more than one way; choose the outcome after review.", false,
                ["Correct the observed counts", "Review a pot and a separate removal", "Reject this observation"]);
        }
    }
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
            || context.Before.LayoutSourceTimeUs is { } layoutTime && after.SourceTimeUs <= layoutTime
            || context.ActivityStartSourceTimeUs is { } start && after.SourceTimeUs <= start)
            return new(SettledComparisonStatus.NonMonotonicSourceTime, "The after-table source time must follow the before-table and activity start.");

        var beforeCounts = context.Before.Counts;
        var afterCounts = after.Counts;
        var changes = ImmutableArray.CreateBuilder<BallCountChange>(4);
        Add(BallColour.Red, beforeCounts.Reds!.Value, afterCounts.Reds!.Value);
        Add(BallColour.Yellow, beforeCounts.Yellows!.Value, afterCounts.Yellows!.Value);
        Add(BallColour.Cue, beforeCounts.CueBalls!.Value, afterCounts.CueBalls!.Value);
        Add(BallColour.Black, beforeCounts.BlackBalls!.Value, afterCounts.BlackBalls!.Value);
        var layoutDifference = SettledLayoutReconciler.Compare(context.Before.Layout, after.Layout);
        return new(SettledComparisonStatus.Ready, "Settled inventory and available layout evidence are comparable; review the outcome recommendation and alternatives.",
            new(context, after, changes.ToImmutable()) { LayoutDifference = layoutDifference });

        void Add(BallColour colour, int before, int current)
        {
            if (before != current) changes.Add(new(colour, before, current));
        }
    }
}
