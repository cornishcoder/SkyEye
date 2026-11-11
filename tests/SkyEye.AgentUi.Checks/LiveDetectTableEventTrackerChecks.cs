using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Companion.Hybrid;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

internal static class LiveDetectTableEventTrackerChecks
{
    static readonly DateTimeOffset Start = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    static readonly Guid ConnectionId = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    static readonly LiveDetectCounts BaseCounts = new(1, 0, 0, 1, 0);
    static readonly ImmutableArray<SettledBallPosition> BaseLayout =
    [new(BallColour.Red, .2, .3), new(BallColour.Cue, .8, .7)];

    public static void Run()
    {
        BaselineAndMissedShotProduceEventsAndClipCandidate();
        UncertainFirstViewCannotSeedBaseline();
        UncertainCueScratchAfterBaselineStillSettles();
        ObstructionWithoutMovementDoesNotProduceShotOutcome();
        SettledInventoryDecreaseProducesPotCandidate();
        ReturnedBallActivityDoesNotProduceShotClip();
        TransientReturnCandidateDoesNotSuppressSettledPotClip();
        ReRackIsReportedWithoutShotClip();
        DecoderRecoveryDoesNotCreateMovementOrClip();
        DecoderRecoveryInterruptsActivityWithBoundedReviewClip();
        UnobservedActivityInterruptionDoesNotCreateReviewClip();
        OldInterruptedActivityDoesNotCreateOversizedClip();
        TransientInventoryDropoutDoesNotBecomeMovement();
        CumulativePositionChangeStartsMovementActivity();
        FirstBaselineFormsWithBlackNotSeenAndLaterSightingCorrectsIt();
        NotSeenBlackAtBaselineFormationIsNotAPotCandidate();
        UncertainFirstViewWithBlackAndCueSeenStillCannotSeedBaseline();
        PocketRegionFlagCoversOnlyItsRegion();
        PocketRegionFlagBeforeBaselineStillWaits();
        VisionRegionFieldsAreOmittedWhenUnset();
        Console.WriteLine("PASS Live Detect table event reducer handles baseline, activity, decoder recovery, no-pot, pot and re-rack sequences");
    }

    static void TransientInventoryDropoutDoesNotBecomeMovement()
    {
        var tracker = NewBaseline();
        var missingRed = ImmutableArray.Create(new SettledBallPosition(BallColour.Cue, .8, .7));
        var firstDrop = tracker.Observe(Sample(4, new(0, 0, 0, 1, 0), missingRed));
        var secondDrop = tracker.Observe(Sample(5, new(0, 0, 0, 1, 0), missingRed));
        var recovered = tracker.Observe(Sample(6, BaseCounts, BaseLayout));
        Require(firstDrop.TableEvents.All(e => e.Type != "movement_observed")
            && secondDrop.TableEvents.All(e => e.Type != "movement_observed")
            && recovered.TableEvents.All(e => e.Type is not ("movement_observed" or "possible_pot" or "possible_pot_settled"))
            && recovered.ClipActivity is null,
            "a two-frame missing red at the same layout is neither movement, a pot, nor a clip");
        Require(SettledLayoutReconciler.PositionsCompatible(BaseLayout, missingRed),
            "position compatibility ignores the single missing circle while matching the unchanged cue ball");
        var jittered = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .217, .3),
            new SettledBallPosition(BallColour.Cue, .8, .7));
        var displaced = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .219, .3),
            new SettledBallPosition(BallColour.Cue, .8, .7));
        Require(SettledLayoutReconciler.PositionsCompatible(BaseLayout, jittered)
            && !SettledLayoutReconciler.PositionsCompatible(BaseLayout, displaced),
            "measured static jitter remains compatible while a larger position change is surfaced");
    }

    static void CumulativePositionChangeStartsMovementActivity()
    {
        var tracker = NewBaseline();
        var drifted = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .235, .3),
            new SettledBallPosition(BallColour.Cue, .8, .7));
        var first = tracker.Observe(Sample(4, BaseCounts, drifted));
        Require(first.TableEvents.Any(e => e.Type == "movement_observed"),
            "movement is detected against the stable baseline even when the adjacent-frame signal is absent");
        tracker.Observe(Sample(5, BaseCounts, drifted));
        var settled = tracker.Observe(Sample(6, BaseCounts, drifted));
        Require(settled.TableEvents.Any(e => e.Type == "no_pot_observed")
            && settled.ClipEventType == "no_pot_observed"
            && settled.ClipActivity is { MovementObserved: true },
            "a real same-inventory displacement still settles as a no-pot activity with a clip window");
    }

    static void BaselineAndMissedShotProduceEventsAndClipCandidate()
    {
        var tracker = new LiveDetectTableEventTracker();
        LiveDetectEventBatch baseline = default!;
        for (int i = 0; i < 3; i++) baseline = tracker.Observe(Sample(i, BaseCounts, BaseLayout));
        Require(baseline.CheckOutcome == "settled_layout_baseline", "three clear frames establish the table baseline");
        var still = tracker.Observe(Sample(3, BaseCounts, BaseLayout));
        Require(still.CheckOutcome == "clear_unchanged" && still.TableEvents.IsEmpty,
            "unchanged checks remain recorded outcomes without duplicate table events");

        var obscured = tracker.Observe(Sample(4, BaseCounts, [], obstructed: true));
        Require(obscured.CheckOutcome == "obstructed"
            && obscured.TableEvents.Any(e => e.Type == "obstruction_started"),
            "obstruction opens an activity without changing the inventory baseline");
        var obscuredAgain = tracker.Observe(Sample(5, BaseCounts, [], obstructed: true));
        Require(obscuredAgain.TableEvents.All(e => e.Type != "obstruction_started"),
            "continuous obstruction emits one transition rather than a repeated event per check");

        var afterShot = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .34, .39),
            new SettledBallPosition(BallColour.Cue, .75, .64));
        var returned = tracker.Observe(Sample(6, BaseCounts, afterShot, movementObserved: true));
        Require(returned.TableEvents.Any(e => e.Type == "clear_view_returned")
            && returned.TableEvents.Any(e => e.Type == "movement_observed"),
            "clear frames after obstruction expose independently observed movement");
        tracker.Observe(Sample(7, BaseCounts, afterShot));
        var settled = tracker.Observe(Sample(8, BaseCounts, afterShot));
        Require(settled.CheckOutcome == "no_pot_observed"
            && settled.TableEvents.Any(e => e.Type == "no_pot_observed"),
            "same inventory after observed movement and a stable layout produces a no-pot observation");
        Require(settled.ClipActivity is { MovementObserved: true }
            && settled.ClipEventType == "no_pot_observed"
            && settled.ClipEndSourceTimeUs == 3_200_000,
            "a settled no-pot observation carries the activity range needed to save its shot clip");
    }

    static void UncertainFirstViewCannotSeedBaseline()
    {
        // The reducer before decision D2 (kept as an option).
        var tracker = new LiveDetectTableEventTracker(LiveDetectReducerOptions.Previous);
        var partialCounts = new LiveDetectCounts(1, 1, 0, 0, 0);
        var partialLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .2, .3),
            new SettledBallPosition(BallColour.Yellow, .6, .7));
        LiveDetectEventBatch lastPartial = default!;
        for (int i = 0; i < 3; i++)
            lastPartial = tracker.Observe(Sample(i, partialCounts, partialLayout, uncertain: true));

        Require(lastPartial.CheckOutcome == "baseline_pending"
            && lastPartial.TableEvents.All(e => e.Type != "settled_layout_baseline")
            && lastPartial.SettledBaseline is null,
            "a stable but uncertain first inventory cannot become the trusted baseline");

        var completeCounts = new LiveDetectCounts(1, 0, 1, 1, 0);
        var completeLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .2, .3),
            new SettledBallPosition(BallColour.Black, .5, .5),
            new SettledBallPosition(BallColour.Cue, .8, .7));
        LiveDetectEventBatch complete = default!;
        for (int i = 3; i < 6; i++) complete = tracker.Observe(Sample(i, completeCounts, completeLayout));
        Require(complete.CheckOutcome == "settled_layout_baseline"
            && complete.TableEvents.Any(e => e.Type == "settled_layout_baseline"),
            "three complete, certain observations can establish the initial baseline after uncertain views");
    }

    static void UncertainCueScratchAfterBaselineStillSettles()
    {
        var tracker = NewBaseline();
        var cueAbsent = new LiveDetectCounts(1, 0, 0, 0, 0);
        var cueAbsentLayout = ImmutableArray.Create(new SettledBallPosition(BallColour.Red, .2, .3));
        tracker.Observe(Sample(4, cueAbsent, cueAbsentLayout, uncertain: true, movementObserved: true));
        tracker.Observe(Sample(5, cueAbsent, cueAbsentLayout, uncertain: true));
        var settled = tracker.Observe(Sample(6, cueAbsent, cueAbsentLayout, uncertain: true));

        Require(settled.CheckOutcome == "possible_pot_settled"
            && settled.TableEvents.Any(e => e.Type == "possible_pot_settled"
                && e.Message.Contains("cue ball", StringComparison.Ordinal))
            && settled.SettledBaseline is { Cue: 0 },
            "uncertain cue absence after a trusted baseline remains available for a confirmed scratch candidate");
    }

    static void ObstructionWithoutMovementDoesNotProduceShotOutcome()
    {
        var tracker = NewBaseline();
        tracker.Observe(Sample(4, BaseCounts, [], obstructed: true));
        tracker.Observe(Sample(5, BaseCounts, BaseLayout));
        tracker.Observe(Sample(6, BaseCounts, BaseLayout));
        var ended = tracker.Observe(Sample(7, BaseCounts, BaseLayout));
        Require(ended.TableEvents.Any(e => e.Type == "activity_ended_without_layout_change")
            && ended.TableEvents.All(e => e.Type != "no_pot_observed")
            && ended.ClipActivity is null,
            "obstruction alone cannot become a miss or a shot clip");
    }

    static void SettledInventoryDecreaseProducesPotCandidate()
    {
        var tracker = NewBaseline();
        var afterCounts = new LiveDetectCounts(0, 0, 0, 1, 0);
        var afterLayout = ImmutableArray.Create(new SettledBallPosition(BallColour.Cue, .78, .69));
        tracker.Observe(Sample(4, afterCounts, afterLayout, movementObserved: true));
        tracker.Observe(Sample(5, afterCounts, afterLayout));
        var settled = tracker.Observe(Sample(6, afterCounts, afterLayout));
        Require(settled.CheckOutcome == "possible_pot_settled"
            && settled.TableEvents.Any(e => e.Type == "possible_pot_settled"),
            "a clear, repeated settled inventory decrease produces a possible-pot event");
        Require(settled.ClipEventType == "possible_pot_settled" && settled.ClipActivity is not null,
            "a settled possible-pot event carries its activity range for automatic clip saving");
    }

    static void ReRackIsReportedWithoutShotClip()
    {
        var tracker = NewBaseline();
        var rackCounts = new LiveDetectCounts(7, 7, 1, 1, 0);
        var rack = ImmutableArray.CreateBuilder<SettledBallPosition>(16);
        var objectColours = Enumerable.Repeat(BallColour.Red, 7)
            .Concat(Enumerable.Repeat(BallColour.Yellow, 7)).Append(BallColour.Black).ToArray();
        int objectIndex = 0;
        for (int row = 0; row < 5; row++)
            for (int col = 0; col <= row; col++)
                rack.Add(new(objectColours[objectIndex++],
                    .45 + (col - row / 2d) * .018, .3 + row * .016));
        rack.Add(new(BallColour.Cue, .8, .7));

        tracker.Observe(Sample(4, rackCounts, rack.ToImmutable(), movementObserved: true, packedRack: true));
        tracker.Observe(Sample(5, rackCounts, rack.ToImmutable(), packedRack: true));
        var settled = tracker.Observe(Sample(6, rackCounts, rack.ToImmutable(), packedRack: true));
        Require(settled.TableEvents.Any(e => e.Type == "rack_detected")
            && settled.TableEvents.Any(e => e.Type == "rack_layout_settled"),
            "three supported packed-rack observations report and settle the new frame baseline");
        Require(settled.ClipActivity is null,
            "re-racking updates the table baseline without saving a shot clip");
    }

    static void ReturnedBallActivityDoesNotProduceShotClip()
    {
        var tracker = NewBaseline();
        var withoutCue = new LiveDetectCounts(1, 0, 0, 0, 0);
        var withoutCueLayout = ImmutableArray.Create(new SettledBallPosition(BallColour.Red, .2, .3));
        tracker.Observe(Sample(4, withoutCue, withoutCueLayout, movementObserved: true));
        tracker.Observe(Sample(5, withoutCue, withoutCueLayout));
        tracker.Observe(Sample(6, withoutCue, withoutCueLayout));
        tracker.Observe(Sample(7, withoutCue, withoutCueLayout));

        var withCue = BaseCounts;
        var withCueLayout = BaseLayout;
        tracker.Observe(Sample(8, withCue, withCueLayout, movementObserved: true, sourceGeneration: 2));
        tracker.Observe(Sample(9, withCue, withCueLayout, sourceGeneration: 2));
        var returnedCandidate = tracker.Observe(Sample(10, withCue, withCueLayout, sourceGeneration: 2));
        Require(returnedCandidate.TableEvents.Any(e => e.Type == "balls_returned_candidate"),
            "a count increase during activity is recorded as a returned-ball candidate");
        tracker.Observe(Sample(11, withCue, withCueLayout, sourceGeneration: 2));
        var ended = tracker.Observe(Sample(12, withCue, withCueLayout, sourceGeneration: 2));
        Require(ended.TableEvents.Any(e => e.Type == "activity_ended")
            && ended.TableEvents.All(e => e.Type != "activity_ended_without_layout_change")
            && ended.ClipActivity is null,
            "a returned-ball candidate that reaches a new clear baseline closes without a shot clip");
    }

    static void TransientReturnCandidateDoesNotSuppressSettledPotClip()
    {
        var tracker = NewBaseline();
        var movedLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .34, .39),
            new SettledBallPosition(BallColour.Cue, .75, .64));
        tracker.Observe(Sample(4, BaseCounts, movedLayout, movementObserved: true));

        var transientCounts = new LiveDetectCounts(2, 0, 0, 1, 0);
        var transientLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .34, .39),
            new SettledBallPosition(BallColour.Red, .52, .48),
            new SettledBallPosition(BallColour.Cue, .75, .64));
        tracker.Observe(Sample(5, transientCounts, transientLayout));
        tracker.Observe(Sample(6, transientCounts, transientLayout));
        var returnedCandidate = tracker.Observe(Sample(7, transientCounts, transientLayout));
        Require(returnedCandidate.TableEvents.Any(e => e.Type == "balls_returned_candidate"),
            "a repeated transient extra-ball reading remains visible as a returned-ball candidate");

        var afterPotCounts = new LiveDetectCounts(1, 0, 0, 0, 0);
        var afterPotLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .34, .39));
        tracker.Observe(Sample(8, afterPotCounts, afterPotLayout, movementObserved: true));
        tracker.Observe(Sample(9, afterPotCounts, afterPotLayout));
        var settled = tracker.Observe(Sample(10, afterPotCounts, afterPotLayout));

        Require(settled.TableEvents.Any(e => e.Type == "possible_pot_settled"),
            "a clear stable decrease supersedes the transient increase candidate");
        Require(settled.ClipEventType == "possible_pot_settled" && settled.ClipActivity is not null,
            "a settled possible-pot event retains its review clip even after a transient increase");
    }

    static void DecoderRecoveryDoesNotCreateMovementOrClip()
    {
        var tracker = NewBaseline();
        var recovered = tracker.Observe(Sample(4, BaseCounts, BaseLayout, movementObserved: true,
            sourceGeneration: 2, mediaGeneration: 4));
        Require(recovered.CheckOutcome == "source_generation_changed"
            && recovered.TableEvents.All(e => e.Type != "movement_observed")
            && recovered.TableEvents.All(e => e.Type != "activity_started")
            && recovered.ClipActivity is null,
            "the first spatially incompatible frame after a decoder restart cannot start movement or a clip");

        for (int i = 5; i <= 7; i++)
            tracker.Observe(Sample(i, BaseCounts, BaseLayout, sourceGeneration: 2, mediaGeneration: 4));

        var movedLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .34, .39),
            new SettledBallPosition(BallColour.Cue, .75, .64));
        var moved = tracker.Observe(Sample(8, BaseCounts, movedLayout, movementObserved: true,
            sourceGeneration: 2, mediaGeneration: 4));
        Require(moved.TableEvents.Any(e => e.Type == "movement_observed"),
            "continuous spatial evidence after decoder recovery can still start real movement activity");
    }

    static void DecoderRecoveryInterruptsActivityWithBoundedReviewClip()
    {
        var tracker = NewBaseline();
        var movedLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .34, .39),
            new SettledBallPosition(BallColour.Cue, .75, .64));
        var moving = tracker.Observe(Sample(4, BaseCounts, movedLayout, movementObserved: true));
        Require(moving.TableEvents.Any(e => e.Type == "movement_observed"),
            "a continuous pre-restart movement signal starts an activity");

        var recovered = tracker.Observe(Sample(5, BaseCounts, movedLayout, movementObserved: true,
            sourceGeneration: 2, mediaGeneration: 4));
        Require(recovered.TableEvents.Any(e => e.Type == "activity_interrupted")
            && recovered.TableEvents.All(e => e.Type != "movement_observed")
            && recovered.ClipActivity is { MovementObserved: true }
            && recovered.ClipEventType == "activity_interrupted"
            && recovered.ClipEndSourceTimeUs == 2_000_000,
            "recent observed movement interrupted by decoder recovery requests a bounded review clip without inferring an outcome");

        LiveDetectEventBatch settled = default!;
        for (int i = 6; i <= 8; i++)
            settled = tracker.Observe(Sample(i, BaseCounts, movedLayout,
                sourceGeneration: 2, mediaGeneration: 4));
        Require(settled.TableEvents.All(e => e.Type != "no_pot_observed")
            && settled.ClipActivity is null,
            "the post-recovery stable layout cannot complete an interrupted activity as a no-pot shot");
    }

    static void OldInterruptedActivityDoesNotCreateOversizedClip()
    {
        var tracker = NewBaseline();
        var movedLayout = ImmutableArray.Create(
            new SettledBallPosition(BallColour.Red, .34, .39),
            new SettledBallPosition(BallColour.Cue, .75, .64));
        tracker.Observe(Sample(4, BaseCounts, movedLayout, movementObserved: true));

        var recovered = tracker.Observe(Sample(80, BaseCounts, movedLayout,
            sourceGeneration: 2, mediaGeneration: 4));
        Require(recovered.TableEvents.Any(e => e.Type == "activity_interrupted"
                && e.Message.Contains("bounded review window", StringComparison.Ordinal))
            && recovered.ClipActivity is null && recovered.ClipEventType is null,
            "a decoder restart more than the configured bound after activity start is logged as unknown without requesting a long clip");
    }

    static void UnobservedActivityInterruptionDoesNotCreateReviewClip()
    {
        var tracker = NewBaseline();
        tracker.Observe(Sample(4, BaseCounts, [], obstructed: true));

        var recovered = tracker.Observe(Sample(5, BaseCounts, [], obstructed: true,
            sourceGeneration: 2, mediaGeneration: 4));
        Require(recovered.TableEvents.Any(e => e.Type == "activity_interrupted")
            && recovered.ClipActivity is null && recovered.ClipEventType is null,
            "an interrupted obstruction without an independently observed movement signal remains clip-free");
    }

    static void FirstBaselineFormsWithBlackNotSeenAndLaterSightingCorrectsIt()
    {
        var tracker = new LiveDetectTableEventTracker();
        LiveDetectEventBatch baseline = default!;
        for (int i = 0; i < 3; i++) baseline = tracker.Observe(Sample(i, BaseCounts, BaseLayout, uncertain: true));
        Require(baseline.CheckOutcome == "settled_layout_baseline" && baseline.SettledBaseline == BaseCounts
            && baseline.BaselineBlackNotSeen && !baseline.BaselineCueNotSeen
            && baseline.TableEvents.Any(e => e.Message.Contains("black not seen", StringComparison.Ordinal)),
            "D2: a stable first view without the black forms a baseline that says the black was not seen");

        var withBlack = new LiveDetectCounts(1, 0, 1, 1, 0);
        var withBlackLayout = BaseLayout.Add(new SettledBallPosition(BallColour.Black, .5, .5));
        var batches = Enumerable.Range(3, 4).Select(i => tracker.Observe(Sample(i, withBlack, withBlackLayout))).ToArray();
        var types = batches.SelectMany(batch => batch.TableEvents).Select(e => e.Type).ToArray();
        Require(types.Count(type => type == "baseline_corrected") == 1
            && !types.Any(type => type is "balls_returned_candidate" or "balls_returned_settled" or "possible_pot"
                or "possible_pot_settled" or "table_inventory_changed_candidate" or "settled_inventory_change"
                or "activity_started" or "movement_observed")
            && batches[^1].SettledBaseline == withBlack && !batches[^1].BaselineBlackNotSeen
            && batches[^1].SettledBaselineMatchesCurrent,
            "D2: the first sighting of a not-seen black corrects the baseline and is never a ball returned");

        var afterPot = Enumerable.Range(7, 3).Select(i => tracker.Observe(Sample(i, BaseCounts, BaseLayout,
            uncertain: true, movementObserved: i == 7))).ToArray();
        Require(afterPot.SelectMany(batch => batch.TableEvents).Any(e => e.Type == "possible_pot_settled"),
            "D2: once corrected, the black is an ordinary ball and its later loss is a pot candidate");
    }

    static void NotSeenBlackAtBaselineFormationIsNotAPotCandidate()
    {
        var tracker = new LiveDetectTableEventTracker();
        var withBlack = new LiveDetectCounts(1, 0, 1, 1, 0);
        var withBlackLayout = BaseLayout.Add(new SettledBallPosition(BallColour.Black, .5, .5));
        var batches = new List<LiveDetectEventBatch> { tracker.Observe(Sample(0, withBlack, withBlackLayout)) };
        for (int i = 1; i < 7; i++) batches.Add(tracker.Observe(Sample(i, BaseCounts, BaseLayout, uncertain: true)));
        Require(batches.Any(batch => batch.CheckOutcome == "settled_layout_baseline")
            && batches.SelectMany(batch => batch.TableEvents).All(e => e.Type is not ("possible_pot" or "possible_pot_settled")),
            "D2: a black seen once and then missed while the first baseline forms is not a pot candidate");
    }

    static void UncertainFirstViewWithBlackAndCueSeenStillCannotSeedBaseline()
    {
        var tracker = new LiveDetectTableEventTracker();
        var counts = new LiveDetectCounts(1, 0, 1, 1, 0);
        var layout = BaseLayout.Add(new SettledBallPosition(BallColour.Black, .5, .5));
        LiveDetectEventBatch last = default!;
        for (int i = 0; i < 3; i++) last = tracker.Observe(Sample(i, counts, layout, uncertain: true));
        Require(last.CheckOutcome == "baseline_pending" && last.SettledBaseline is null,
            "D2 only covers an unseen black or cue; other broad uncertainty still cannot seed a baseline");
    }

    static readonly ImmutableArray<LiveDetectUncertainRegion> MiddlePocket = [new(1, 0, .102)];

    static void PocketRegionFlagCoversOnlyItsRegion()
    {
        // Red in open play, yellow at the top middle pocket mouth (table x .98, y .03).
        var counts = new LiveDetectCounts(1, 1, 1, 1, 0);
        var layout = ImmutableArray.Create(new SettledBallPosition(BallColour.Red, .25, .3),
            new SettledBallPosition(BallColour.Yellow, .49, .03), new SettledBallPosition(BallColour.Black, .5, .5),
            new SettledBallPosition(BallColour.Cue, .8, .7));
        LiveDetectEventBatch Flagged(LiveDetectTableEventTracker tracker, int i, LiveDetectCounts c,
            ImmutableArray<SettledBallPosition> l, bool movement = false) =>
            tracker.Observe(Sample(i, c, l, uncertain: true, layoutUncertain: true, movementObserved: movement,
                regions: MiddlePocket, uncertainOutsideRegions: false));

        var tracker = new LiveDetectTableEventTracker();
        for (int i = 0; i < 3; i++) tracker.Observe(Sample(i, counts, layout));
        var held = Flagged(tracker, 3, counts, layout);
        Require(held.CheckOutcome == "clear_unchanged" && held.SettledBaselineMatchesCurrent
            && held.PocketRegionUncertain && held.PocketRegionBalls == 1 && held.TableEvents.IsEmpty,
            "D1: a pocket-colour flag on an unchanged table marks only the ball at that pocket uncertain");

        var moved = layout.SetItem(0, new SettledBallPosition(BallColour.Red, .35, .3));
        var settled = Enumerable.Range(4, 3).Select(i => Flagged(tracker, i, counts, moved, movement: i == 4)).ToArray();
        Require(settled.SelectMany(batch => batch.TableEvents).Any(e => e.Type == "no_pot_observed"),
            "D1: the rest of the table still settles and reports events while the pocket region is flagged");

        var yellowGone = new LiveDetectCounts(1, 0, 1, 1, 0);
        var withoutYellow = moved.RemoveAt(1);
        var waiting = Enumerable.Range(7, 4).Select(i => Flagged(tracker, i, yellowGone, withoutYellow)).ToArray();
        Require(waiting.All(batch => batch.CheckOutcome == "layout_uncertain")
            && waiting.SelectMany(batch => batch.TableEvents).All(e => e.Type is not ("possible_pot" or "possible_pot_settled")),
            "D1: a red or yellow count change while the region is flagged waits; it may be the ball at the pocket");
        var cleared = Enumerable.Range(11, 3).Select(i => tracker.Observe(Sample(i, yellowGone, withoutYellow))).ToArray();
        Require(cleared.SelectMany(batch => batch.TableEvents).Any(e => e.Type == "possible_pot"),
            "D1: once the region clears, the missing yellow is reported as an ordinary pot candidate");

        var previous = new LiveDetectTableEventTracker(LiveDetectReducerOptions.Previous);
        for (int i = 0; i < 3; i++) previous.Observe(Sample(i, counts, layout));
        Require(Flagged(previous, 3, counts, layout).CheckOutcome == "layout_uncertain",
            "with D1 off, the pocket-colour flag still voids the whole layout");
    }

    static void PocketRegionFlagBeforeBaselineStillWaits()
    {
        var tracker = new LiveDetectTableEventTracker();
        LiveDetectEventBatch last = default!;
        for (int i = 0; i < 3; i++)
            last = tracker.Observe(Sample(i, BaseCounts, BaseLayout, uncertain: true, layoutUncertain: true,
                regions: MiddlePocket, uncertainOutsideRegions: false));
        Require(last.CheckOutcome == "layout_uncertain" && last.SettledBaseline is null,
            "D1: a first baseline does not form while a pocket region is flagged (option off by default)");
    }

    static void VisionRegionFieldsAreOmittedWhenUnset()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var plain = new VisionResult([], false, [], 0, 0, false, "check", [], 0, false, []);
        var flagged = plain with
        {
            LayoutUncertain = true,
            UncertainRegions = [new UncertainTableRegion(new Point(1, 0), .102, "pocket-colour")],
            LayoutUncertainOutsideRegions = false,
            UncertainOutsideRegions = false
        };
        string plainJson = JsonSerializer.Serialize(plain, options), flaggedJson = JsonSerializer.Serialize(flagged, options);
        Require(!plainJson.Contains("uncertainRegions", StringComparison.Ordinal)
            && !plainJson.Contains("OutsideRegions", StringComparison.Ordinal)
            && flaggedJson.Contains("\"uncertainRegions\"", StringComparison.Ordinal)
            && flaggedJson.Contains("\"layoutUncertainOutsideRegions\":false", StringComparison.Ordinal)
            && flagged.HasUncertainLayout,
            "D1 region fields are omitted unless set, and the whole-table layout flag stays for other consumers");
    }

    static LiveDetectTableEventTracker NewBaseline()
    {
        var tracker = new LiveDetectTableEventTracker();
        for (int i = 0; i < 3; i++) tracker.Observe(Sample(i, BaseCounts, BaseLayout));
        return tracker;
    }

    static LiveDetectTableEventObservation Sample(int index, LiveDetectCounts counts,
        ImmutableArray<SettledBallPosition> layout, bool obstructed = false,
        bool uncertain = false, bool layoutUncertain = false, bool movementObserved = false, bool packedRack = false,
        long sourceGeneration = 1, long mediaGeneration = 3,
        ImmutableArray<LiveDetectUncertainRegion> regions = default, bool? uncertainOutsideRegions = null)
    {
        long sourceTimeUs = index * 400_000L;
        var timeline = "event-check-timeline";
        var calibration = "event-check-calibration";
        return new(new("camera", timeline, sourceGeneration, calibration, ConnectionId.ToString("N")),
            ConnectionId, mediaGeneration, index + 1, Start.AddTicks(sourceTimeUs * 10), sourceTimeUs,
            timeline, calibration, counts, layout, obstructed, uncertain, layoutUncertain,
            movementObserved, packedRack, regions, uncertainOutsideRegions);
    }

    public static void Replay(string path, Guid runId)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var tracker = new LiveDetectTableEventTracker();
        LiveDetectLoggedBall[]? previousClear = null;
        string? previousTimeline = null;
        var outcomes = new Dictionary<string, int>(StringComparer.Ordinal);
        var eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int frameCount = 0;

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<LiveDetectLogRecord>(line, options);
            if (record?.RunId != runId || record.Kind != "frame" || record.Frame is not { } frame) continue;
            if (previousTimeline is not null && previousTimeline != frame.Timeline)
            {
                tracker.Reset();
                previousClear = null;
            }
            previousTimeline = frame.Timeline;

            bool clear = !frame.Uncertain && !frame.Obstructed && !frame.LayoutUncertain && frame.Other == 0;
            bool movementObserved = clear && previousClear is { Length: > 0 }
                && !SettledLayoutReconciler.PositionsCompatible(CaptureLayout(previousClear), CaptureLayout(frame.Balls));
            if (clear) previousClear = frame.Balls;

            var layout = CaptureLayout(frame.Balls);
            var counts = new LiveDetectCounts(frame.Reds, frame.Yellows, frame.Black, frame.Cue, frame.Other);
            var source = new EvidenceSource("replay-camera", frame.Timeline, 1,
                frame.CalibrationId, frame.CameraConnectionId.ToString("N"));
            var result = tracker.Observe(new(source, frame.CameraConnectionId, frame.MediaGeneration,
                frame.FrameId, frame.CapturedAt, frame.SourceTimeUs, frame.Timeline, frame.CalibrationId,
                counts, layout, frame.Obstructed, frame.Uncertain, frame.LayoutUncertain, movementObserved,
                frame.PackedRackEvidence));
            outcomes[result.CheckOutcome] = outcomes.GetValueOrDefault(result.CheckOutcome) + 1;
            foreach (var tableEvent in result.TableEvents)
            {
                eventCounts[tableEvent.Type] = eventCounts.GetValueOrDefault(tableEvent.Type) + 1;
                Console.WriteLine($"{tableEvent.CapturedAt:O} PTS={tableEvent.SourceTimeUs} {tableEvent.Type}: {tableEvent.Message}");
            }
            if (result.ClipEventType is { } clipType && result.ClipActivity is { } clipActivity)
                Console.WriteLine($"CLIP CANDIDATE {clipType} activity={clipActivity.Id} startPTS={clipActivity.StartSourceTimeUs} endPTS={result.ClipEndSourceTimeUs}");
            frameCount++;
        }

        Console.WriteLine($"Replay run {runId}: {frameCount} frames; outcomes [{string.Join(", ", outcomes.Select(kv => $"{kv.Key}={kv.Value}"))}]; events [{string.Join(", ", eventCounts.Select(kv => $"{kv.Key}={kv.Value}"))}].");
        Console.WriteLine("Replay used saved detector output; it did not rerun vision, write match state, or save camera clips.");
    }

    static ImmutableArray<SettledBallPosition> CaptureLayout(LiveDetectLoggedBall[] balls)
    {
        if (balls.Length is < 1 or > 16) return [];
        var result = ImmutableArray.CreateBuilder<SettledBallPosition>(balls.Length);
        foreach (var ball in balls)
        {
            var colour = ball.Colour.Trim().ToLowerInvariant() switch
            {
                "red" => BallColour.Red,
                "yellow" => BallColour.Yellow,
                "black" => BallColour.Black,
                "cue" or "cue ball" or "cue-ball" => BallColour.Cue,
                _ => BallColour.Unknown
            };
            double x = ball.X / 2d;
            if (colour == BallColour.Unknown || !double.IsFinite(x) || !double.IsFinite(ball.Y)
                || x is < 0 or > 1 || ball.Y is < 0 or > 1) return [];
            result.Add(new(colour, x, ball.Y));
        }
        return result.ToImmutable();
    }

    static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception("Live Detect event tracker check failed: " + name);
    }
}
