using System.Collections.Immutable;
using SkyEye.Companion.Hybrid;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

internal static class LiveDetectInventoryReadoutChecks
{
    static readonly DateTimeOffset Start = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    static readonly Guid ConnectionId = Guid.Parse("c0000000-0000-0000-0000-000000000011");
    static readonly LiveDetectCounts FullCounts = new(1, 0, 1, 1, 0);
    static readonly ImmutableArray<SettledBallPosition> FullLayout =
    [new(BallColour.Red, .2, .3), new(BallColour.Black, .5, .5), new(BallColour.Cue, .8, .7)];

    public static void Run()
    {
        UncertainInitialAbsenceDoesNotSeedCurrentInventory();
        CueAbsenceAfterTrustedBaselineRemainsUnknownInReadout();
        BlackAbsenceAfterTrustedBaselineRemainsUnknownInReadout();
        ObstructionDoesNotConfirmCurrentInventory();
        ClearSettledInventoryCanBeShownAsCurrent();
        Console.WriteLine("PASS READINESS-ABSENCE-SEMANTICS-CONTROLS 5/5 · event candidates retained; uncertain/obstructed current inventory not claimed");
    }

    static void UncertainInitialAbsenceDoesNotSeedCurrentInventory()
    {
        var tracker = new LiveDetectTableEventTracker();
        var counts = new LiveDetectCounts(1, 0, 0, 0, 0);
        var layout = ImmutableArray.Create(new SettledBallPosition(BallColour.Red, .2, .3));
        LiveDetectEventBatch last = default!;
        for (int i = 0; i < 3; i++) last = Observe(tracker, i, counts, layout, uncertain: true);

        Require(last.CheckOutcome == "baseline_pending" && last.SettledBaseline is null
            && !LiveDetectInventoryReadoutPolicy.IsCurrentInventoryClear(true, false, false, counts)
            && LiveDetectInventoryReadoutPolicy.SettledCurrentInventory(false, last) is null,
            "an unexplained first view missing cue and black stays unknown");
    }

    static void CueAbsenceAfterTrustedBaselineRemainsUnknownInReadout()
    {
        var tracker = NewFullBaseline();
        var counts = new LiveDetectCounts(1, 0, 1, 0, 0);
        var layout = ImmutableArray.Create(new SettledBallPosition(BallColour.Red, .2, .3),
            new SettledBallPosition(BallColour.Black, .5, .5));
        var settled = ObserveReduced(tracker, counts, layout);

        Require(settled.CheckOutcome == "possible_pot_settled" && settled.SettledBaselineMatchesCurrent
            && settled.SettledBaseline is { Cue: 0 },
            "trusted history still allows the event tracker to settle a cue-loss candidate");
        Require(!LiveDetectInventoryReadoutPolicy.IsCurrentInventoryClear(true, false, false, counts)
            && LiveDetectInventoryReadoutPolicy.SettledCurrentInventory(false, settled) is null,
            "broad cue uncertainty cannot be presented as a clear current inventory");
    }

    static void BlackAbsenceAfterTrustedBaselineRemainsUnknownInReadout()
    {
        var tracker = NewFullBaseline();
        var counts = new LiveDetectCounts(1, 0, 0, 1, 0);
        var layout = ImmutableArray.Create(new SettledBallPosition(BallColour.Red, .2, .3),
            new SettledBallPosition(BallColour.Cue, .8, .7));
        var settled = ObserveReduced(tracker, counts, layout);

        Require(settled.CheckOutcome == "possible_pot_settled" && settled.SettledBaselineMatchesCurrent
            && settled.SettledBaseline is { Black: 0 },
            "trusted history still allows the event tracker to settle a black-loss candidate");
        Require(!LiveDetectInventoryReadoutPolicy.IsCurrentInventoryClear(true, false, false, counts)
            && LiveDetectInventoryReadoutPolicy.SettledCurrentInventory(false, settled) is null,
            "broad black uncertainty cannot be presented as a clear current inventory");
    }

    static void ObstructionDoesNotConfirmCurrentInventory()
    {
        var tracker = NewFullBaseline();
        var obstructed = Observe(tracker, 3, FullCounts, [], obstructed: true);
        Require(obstructed.CheckOutcome == "obstructed"
            && !LiveDetectInventoryReadoutPolicy.IsCurrentInventoryClear(false, true, false, FullCounts)
            && LiveDetectInventoryReadoutPolicy.SettledCurrentInventory(false, obstructed) is null,
            "obstruction cannot confirm a current inventory");
    }

    static void ClearSettledInventoryCanBeShownAsCurrent()
    {
        var tracker = new LiveDetectTableEventTracker();
        LiveDetectEventBatch settled = default!;
        for (int i = 0; i < 3; i++) settled = Observe(tracker, i, FullCounts, FullLayout);
        Require(LiveDetectInventoryReadoutPolicy.IsCurrentInventoryClear(false, false, false, FullCounts)
            && LiveDetectInventoryReadoutPolicy.SettledCurrentInventory(true, settled) == FullCounts,
            "a clear settled inventory remains reportable as current");
    }

    static LiveDetectTableEventTracker NewFullBaseline()
    {
        var tracker = new LiveDetectTableEventTracker();
        for (int i = 0; i < 3; i++) Observe(tracker, i, FullCounts, FullLayout);
        return tracker;
    }

    static LiveDetectEventBatch ObserveReduced(LiveDetectTableEventTracker tracker,
        LiveDetectCounts counts, ImmutableArray<SettledBallPosition> layout)
    {
        LiveDetectEventBatch settled = default!;
        for (int i = 3; i < 6; i++)
            settled = Observe(tracker, i, counts, layout, uncertain: true, movementObserved: i == 3);
        return settled;
    }

    static LiveDetectEventBatch Observe(LiveDetectTableEventTracker tracker, int index,
        LiveDetectCounts counts, ImmutableArray<SettledBallPosition> layout,
        bool obstructed = false, bool uncertain = false, bool movementObserved = false)
    {
        long sourceTimeUs = index * 400_000L;
        const string timeline = "readout-policy-timeline";
        const string calibration = "readout-policy-calibration";
        var source = new EvidenceSource("camera", timeline, 1, calibration, ConnectionId.ToString("N"));
        return tracker.Observe(new(source, ConnectionId, 1, index + 1,
            Start.AddTicks(sourceTimeUs * 10), sourceTimeUs, timeline, calibration,
            counts, layout, obstructed, uncertain, LayoutUncertain: false, movementObserved, PackedRack: false));
    }

    static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Live Detect readout check failed: " + description);
    }
}
