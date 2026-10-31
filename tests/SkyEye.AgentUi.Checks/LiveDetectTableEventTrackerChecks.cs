using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Companion.Hybrid;
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

    static LiveDetectTableEventTracker NewBaseline()
    {
        var tracker = new LiveDetectTableEventTracker();
        for (int i = 0; i < 3; i++) tracker.Observe(Sample(i, BaseCounts, BaseLayout));
        return tracker;
    }

    static LiveDetectTableEventObservation Sample(int index, LiveDetectCounts counts,
        ImmutableArray<SettledBallPosition> layout, bool obstructed = false,
        bool layoutUncertain = false, bool movementObserved = false, bool packedRack = false,
        long sourceGeneration = 1, long mediaGeneration = 3)
    {
        long sourceTimeUs = index * 400_000L;
        var timeline = "event-check-timeline";
        var calibration = "event-check-calibration";
        return new(new("camera", timeline, sourceGeneration, calibration, ConnectionId.ToString("N")),
            ConnectionId, mediaGeneration, index + 1, Start.AddTicks(sourceTimeUs * 10), sourceTimeUs,
            timeline, calibration, counts, layout, obstructed, layoutUncertain,
            movementObserved, packedRack);
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

            bool clear = !frame.Obstructed && !frame.LayoutUncertain && frame.Other == 0;
            bool movementObserved = clear && previousClear is { Length: > 0 }
                && !SettledLayoutReconciler.PositionsCompatible(CaptureLayout(previousClear), CaptureLayout(frame.Balls));
            if (clear) previousClear = frame.Balls;

            var layout = CaptureLayout(frame.Balls);
            var counts = new LiveDetectCounts(frame.Reds, frame.Yellows, frame.Black, frame.Cue, frame.Other);
            var source = new EvidenceSource("replay-camera", frame.Timeline, 1,
                frame.CalibrationId, frame.CameraConnectionId.ToString("N"));
            var result = tracker.Observe(new(source, frame.CameraConnectionId, frame.MediaGeneration,
                frame.FrameId, frame.CapturedAt, frame.SourceTimeUs, frame.Timeline, frame.CalibrationId,
                counts, layout, frame.Obstructed, frame.LayoutUncertain, movementObserved,
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
