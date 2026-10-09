using System.Collections.Immutable;
using SkyEye.GameState;

namespace SkyEye.Core.Analysis;

/// <summary>One clear detector view used only for in-memory stable-layout change detection.</summary>
public sealed record StableTableLayoutSample(EvidenceSource? Source, long SourceTimeUs,
    DateTimeOffset CapturedAt, SettledTableCounts Counts, ImmutableArray<SettledBallPosition> Layout,
    bool Clear);

/// <summary>A stable observed table-layout change. It deliberately carries no game-rule interpretation.</summary>
public sealed record StableTableChangeEvent(Guid Id, EvidenceSource Source,
    long BeforeSourceTimeUs, long AfterSourceTimeUs, DateTimeOffset CapturedAt,
    SettledTableCounts Before, SettledTableCounts After,
    int? MovedBallCount, double? LargestMovement, string Summary);

/// <summary>Runtime-only status for the rolling table-layout watcher.</summary>
public sealed record StableTableChangeSnapshot(bool BaselineEstablished, string Status,
    EvidenceSource? Source, long? BaselineSourceTimeUs, SettledTableCounts? BaselineCounts,
    int StableSamples, ImmutableArray<StableTableChangeEvent> RecentEvents);

/// <summary>
/// Holds a stable layout in memory and reports settled A/B changes. Obstruction and uncertain
/// observations suspend comparison without discarding A; an accepted B becomes the next A.
/// This watcher is intentionally independent of canonical game inventory and rules.
/// </summary>
public sealed class StableTableChangeTracker
{
    public const int RequiredStableSamples = 3;
    const int MaximumRecentEvents = 12;

    EvidenceSource? source;
    long? lastSourceTimeUs;
    SettledTableCounts? baselineCounts;
    ImmutableArray<SettledBallPosition> baselineLayout = [];
    long? baselineSourceTimeUs;
    SettledTableCounts? probeCounts;
    ImmutableArray<SettledBallPosition> probeLayout = [];
    long? probeFirstSourceTimeUs;
    int probeStableSamples;
    readonly Queue<StableTableChangeEvent> recentEvents = new();
    string status = "Waiting for three matching clear views to establish a table baseline.";

    public StableTableChangeSnapshot Snapshot => new(
        baselineCounts is not null, status, source, baselineSourceTimeUs, baselineCounts,
        probeStableSamples, recentEvents.ToImmutableArray());

    public StableTableChangeSnapshot Observe(StableTableLayoutSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!IsUsable(sample))
        {
            ResetProbe();
            status = baselineCounts is null
                ? "Waiting for three matching fresh, clear, classified views to establish a baseline."
                : "Baseline held; waiting for a fresh, clear, classified view.";
            return Snapshot;
        }

        if (source != sample.Source)
        {
            ResetBaseline();
            source = sample.Source;
            lastSourceTimeUs = null;
            status = "Camera or calibration changed; establishing a fresh baseline.";
        }
        else if (lastSourceTimeUs is { } previousTime && sample.SourceTimeUs <= previousTime)
        {
            ResetBaseline();
            lastSourceTimeUs = null;
            status = "Camera timeline changed; establishing a fresh baseline.";
        }
        lastSourceTimeUs = sample.SourceTimeUs;

        if (baselineCounts is null)
        {
            if (!AccumulateProbe(sample)) return Snapshot;
            PromoteProbeToBaseline();
            status = "Watching for a stable table change; baseline established.";
            return Snapshot;
        }

        if (sample.Counts == baselineCounts
            && SettledLayoutReconciler.SameLayout(baselineLayout, sample.Layout))
        {
            ResetProbe();
            status = "Watching the same stable table; baseline held.";
            return Snapshot;
        }

        if (sample.Counts == baselineCounts)
        {
            var difference = SettledLayoutReconciler.Compare(baselineLayout, sample.Layout);
            if (!difference.Comparable || !difference.Changed)
            {
                ResetProbe();
                status = "Watching the same table; position jitter is within tolerance.";
                return Snapshot;
            }
        }

        if (!AccumulateProbe(sample))
        {
            status = $"A table change is visible; confirming a stable new layout ({probeStableSamples}/{RequiredStableSamples}).";
            return Snapshot;
        }

        var moved = sample.Counts == baselineCounts
            ? SettledLayoutReconciler.Compare(baselineLayout, probeLayout)
            : new SettledLayoutDifference(false, 0, 0);
        var beforeCounts = baselineCounts;
        var beforeTime = baselineSourceTimeUs ?? 0;
        var summary = BuildSummary(beforeCounts!, probeCounts!, moved);
        var change = new StableTableChangeEvent(Guid.NewGuid(), sample.Source!, beforeTime,
            sample.SourceTimeUs, sample.CapturedAt, beforeCounts!, probeCounts!,
            moved.Comparable ? moved.MovedBallCount : null,
            moved.Comparable ? moved.LargestMovement : null, summary);
        recentEvents.Enqueue(change);
        while (recentEvents.Count > MaximumRecentEvents) recentEvents.Dequeue();

        PromoteProbeToBaseline();
        status = $"Stable table change detected: {summary} The new layout is now the baseline.";
        return Snapshot;
    }

    bool AccumulateProbe(StableTableLayoutSample sample)
    {
        bool matches = probeCounts == sample.Counts && !probeLayout.IsDefaultOrEmpty
            && SettledLayoutReconciler.SameLayout(probeLayout, sample.Layout);
        if (!matches)
        {
            probeCounts = sample.Counts;
            probeLayout = sample.Layout;
            probeFirstSourceTimeUs = sample.SourceTimeUs;
            probeStableSamples = 1;
        }
        else probeStableSamples++;

        if (probeStableSamples < RequiredStableSamples) return false;
        return true;
    }

    void PromoteProbeToBaseline()
    {
        baselineCounts = probeCounts;
        baselineLayout = probeLayout;
        baselineSourceTimeUs = probeFirstSourceTimeUs;
        ResetProbe();
    }

    void ResetProbe()
    {
        probeCounts = null;
        probeLayout = [];
        probeFirstSourceTimeUs = null;
        probeStableSamples = 0;
    }

    void ResetBaseline()
    {
        baselineCounts = null;
        baselineLayout = [];
        baselineSourceTimeUs = null;
        ResetProbe();
    }

    static bool IsUsable(StableTableLayoutSample sample)
    {
        if (!sample.Clear || sample.Source is null || sample.SourceTimeUs < 0
            || sample.Counts is not { IsComplete: true } counts
            || counts.Reds is < 0 or > 16 || counts.Yellows is < 0 or > 16
            || counts.CueBalls is < 0 or > 1 || counts.BlackBalls is < 0 or > 1
            || sample.Layout.IsDefaultOrEmpty || sample.Layout.Length > 16)
            return false;

        return sample.Layout.All(ball => ball is not null && ball.Colour is
                (BallColour.Red or BallColour.Yellow or BallColour.Cue or BallColour.Black)
                && double.IsFinite(ball.X) && double.IsFinite(ball.Y) && ball.X is >= 0 and <= 1 && ball.Y is >= 0 and <= 1)
            && sample.Layout.Length == counts.Reds + counts.Yellows + counts.CueBalls + counts.BlackBalls
            && sample.Layout.Count(ball => ball.Colour == BallColour.Red) == counts.Reds
            && sample.Layout.Count(ball => ball.Colour == BallColour.Yellow) == counts.Yellows
            && sample.Layout.Count(ball => ball.Colour == BallColour.Cue) == counts.CueBalls
            && sample.Layout.Count(ball => ball.Colour == BallColour.Black) == counts.BlackBalls;
    }

    static string BuildSummary(SettledTableCounts before, SettledTableCounts after, SettledLayoutDifference movement)
    {
        static string Counts(SettledTableCounts counts) =>
            $"{counts.Reds} red, {counts.Yellows} yellow, {counts.CueBalls} cue ball, {counts.BlackBalls} black";
        string movementText = movement.Changed ? $"; {movement.MovedBallCount} ball(s) changed position" : "";
        return $"{Counts(before)} → {Counts(after)}{movementText}.";
    }
}
