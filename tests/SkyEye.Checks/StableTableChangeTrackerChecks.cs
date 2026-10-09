using System.Collections.Immutable;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

static class StableTableChangeTrackerChecks
{
    public static void Run()
    {
        var tracker = new StableTableChangeTracker();
        var source = new EvidenceSource("camera", "live", 2, "calibration", "connection");
        var start = DateTimeOffset.UtcNow;
        var original = Layout(.2, .2, .8, .8);
        var counts = new SettledTableCounts(1, 1, 0, 0);

        Observe(tracker, source, counts, original, start, 1);
        Observe(tracker, source, counts, Layout(.202, .2, .8, .8), start, 2);
        var baseline = Observe(tracker, source, counts, original, start, 3);
        Check(baseline.BaselineEstablished && baseline.RecentEvents.IsEmpty,
            "The first stable clear layout becomes an observation baseline without requiring canonical match counts");

        const long farSourceTime = 4_000_000_000_000_000;
        var farFuture = start.AddYears(100);
        var quiet = ObserveAt(tracker, source, counts, Layout(.204, .2, .8, .8), farFuture, farSourceTime);
        Check(quiet.RecentEvents.IsEmpty && quiet.BaselineSourceTimeUs == 1,
            "An arbitrarily long in-process quiet period and small position jitter retain the original baseline");

        var obstructed = tracker.Observe(Sample(source, counts, original, farFuture.AddSeconds(1), farSourceTime + 1, clear: false));
        Check(obstructed.BaselineEstablished && obstructed.RecentEvents.IsEmpty,
            "An obstruction pauses comparison without discarding the baseline");

        var moved = Layout(.25, .22, .8, .8);
        ObserveAt(tracker, source, counts, moved, farFuture.AddSeconds(2), farSourceTime + 2);
        ObserveAt(tracker, source, counts, moved, farFuture.AddSeconds(3), farSourceTime + 3);
        var changedLayout = ObserveAt(tracker, source, counts, moved, farFuture.AddSeconds(4), farSourceTime + 4);
        Check(changedLayout.RecentEvents.Length == 1
            && changedLayout.RecentEvents[0].MovedBallCount == 1
            && changedLayout.BaselineSourceTimeUs == farSourceTime + 2,
            "Three matching clear views raise one position-change event and advance the baseline");
        var unchanged = ObserveAt(tracker, source, counts, moved, farFuture.AddSeconds(5), farSourceTime + 5);
        Check(unchanged.RecentEvents.Length == 1,
            "The promoted layout does not repeatedly raise the same event");

        var afterPotCounts = new SettledTableCounts(1, 0, 0, 0);
        var afterPot = Layout(.25, .22);
        ObserveAt(tracker, source, afterPotCounts, afterPot, farFuture.AddSeconds(6), farSourceTime + 6);
        ObserveAt(tracker, source, afterPotCounts, afterPot, farFuture.AddSeconds(7), farSourceTime + 7);
        var changedCounts = ObserveAt(tracker, source, afterPotCounts, afterPot, farFuture.AddSeconds(8), farSourceTime + 8);
        Check(changedCounts.RecentEvents.Length == 2
            && changedCounts.RecentEvents[^1].Before == counts
            && changedCounts.RecentEvents[^1].After == afterPotCounts
            && changedCounts.BaselineCounts == afterPotCounts,
            "A stable count change is reported independently of canonical match state and becomes the next baseline");

        var nextSource = source with { Calibration = "new-calibration" };
        ObserveAt(tracker, nextSource, afterPotCounts, afterPot, farFuture.AddSeconds(9), farSourceTime + 9);
        ObserveAt(tracker, nextSource, afterPotCounts, afterPot, farFuture.AddSeconds(10), farSourceTime + 10);
        var reset = ObserveAt(tracker, nextSource, afterPotCounts, afterPot, farFuture.AddSeconds(11), farSourceTime + 11);
        Check(reset.BaselineEstablished && reset.RecentEvents.Length == 2,
            "A camera/calibration scope change reacquires a baseline without fabricating a table-change event");

        Console.WriteLine("PASS live table change watcher: independent bootstrap, no elapsed-time expiry, jitter/obstruction, stable position/count events, promotion and source reset");
    }

    static StableTableChangeSnapshot Observe(StableTableChangeTracker tracker, EvidenceSource source,
        SettledTableCounts counts, ImmutableArray<SettledBallPosition> layout, DateTimeOffset start, long tick)
        => tracker.Observe(Sample(source, counts, layout, start.AddSeconds(tick), tick));

    static StableTableChangeSnapshot ObserveAt(StableTableChangeTracker tracker, EvidenceSource source,
        SettledTableCounts counts, ImmutableArray<SettledBallPosition> layout, DateTimeOffset capturedAt, long sourceTimeUs)
        => tracker.Observe(Sample(source, counts, layout, capturedAt, sourceTimeUs));

    static StableTableLayoutSample Sample(EvidenceSource source, SettledTableCounts counts,
        ImmutableArray<SettledBallPosition> layout, DateTimeOffset captured, long sourceTimeUs, bool clear = true)
        => new(source, sourceTimeUs, captured, counts, layout, clear);

    static ImmutableArray<SettledBallPosition> Layout(params double[] xy)
    {
        var result = ImmutableArray.CreateBuilder<SettledBallPosition>();
        for (int i = 0; i < xy.Length; i += 2)
            result.Add(new(i == 0 ? BallColour.Red : BallColour.Yellow, xy[i], xy[i + 1]));
        return result.ToImmutable();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
