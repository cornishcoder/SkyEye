using System.Collections.Immutable;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

internal sealed record LiveDetectActivity(Guid Id, string Timeline, long StartSourceTimeUs, long StartFrameId,
    DateTimeOffset StartedAt, string StartReason, bool MovementObserved = false,
    bool ShotClipSuppressed = false);

internal sealed record LiveDetectTableEventObservation(EvidenceSource Source, Guid CameraConnectionId,
    long MediaGeneration, long FrameId, DateTimeOffset CapturedAt, long SourceTimeUs, string Timeline,
    string CalibrationId, LiveDetectCounts Counts, ImmutableArray<SettledBallPosition> Layout,
    bool Obstructed, bool LayoutUncertain, bool MovementObserved, bool PackedRack);

internal sealed record LiveDetectEventBatch(string CheckOutcome,
    ImmutableArray<LiveDetectLogTableEvent> TableEvents, LiveDetectActivity? ClipActivity,
    string? ClipEventType, long? ClipEndSourceTimeUs,
    LiveDetectCounts? SettledBaseline, DateTimeOffset? SettledBaselineAt,
    bool SettledBaselineMatchesCurrent, int BaselineMatchSamples)
{
    public IEnumerable<string> Messages => TableEvents.Select(tableEvent => tableEvent.Message);
}

/// <summary>
/// Converts one fresh, image-free Live Detect result into a check outcome and typed table events.
/// This is a transient perception reducer; GameState remains the only canonical persisted state.
/// </summary>
internal sealed class LiveDetectTableEventTracker
{
    LiveDetectCountCandidateTracker candidateTracker = new();
    StableTableChangeTracker stableLayoutTracker = new();
    string? timeline;
    long? mediaGeneration;
    Guid? lastStableChangeId;
    bool? lastClearLayout;
    bool stableBaselineEstablished;
    SettledTableCounts? baselineCounts;
    ImmutableArray<SettledBallPosition> baselineLayout = [];
    DateTimeOffset? baselineCapturedAt;
    int stableAtBaselineSamples;
    int currentBaselineStableSamples;
    LiveDetectActivity? activity;

    public void Reset()
    {
        candidateTracker.Reset();
        stableLayoutTracker = new();
        timeline = null;
        mediaGeneration = null;
        lastStableChangeId = null;
        lastClearLayout = null;
        stableBaselineEstablished = false;
        baselineCounts = null;
        baselineLayout = [];
        baselineCapturedAt = null;
        stableAtBaselineSamples = 0;
        currentBaselineStableSamples = 0;
        activity = null;
    }

    public LiveDetectEventBatch Observe(LiveDetectTableEventObservation sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        bool timelineChanged = timeline is not null && timeline != sample.Timeline;
        if (timelineChanged) Reset();
        bool mediaGenerationChanged = mediaGeneration is { } previousGeneration
            && sample.MediaGeneration != previousGeneration;
        mediaGeneration = sample.MediaGeneration;
        timeline = sample.Timeline;

        bool candidateEligible = !sample.LayoutUncertain && !sample.Obstructed;
        bool stableSampleEligible = candidateEligible && IsStableInventory(sample.Counts)
            && sample.Layout.Length == sample.Counts.Total;
        var settledCounts = new SettledTableCounts(sample.Counts.Reds, sample.Counts.Yellows,
            sample.Counts.Cue, sample.Counts.Black);
        var stableBefore = stableLayoutTracker.Snapshot;
        var stableSnapshot = stableLayoutTracker.Observe(new StableTableLayoutSample(sample.Source,
            sample.SourceTimeUs, sample.CapturedAt,
            stableSampleEligible ? settledCounts : new(0, 0, 0, 0),
            stableSampleEligible ? sample.Layout : [], stableSampleEligible));

        var events = ImmutableArray.CreateBuilder<LiveDetectLogTableEvent>();
        string? passEventType = mediaGenerationChanged ? "source_generation_changed" : null;
        LiveDetectActivity? clipActivity = null;
        string? clipEventType = null;
        long? clipEndSourceTimeUs = null;
        bool baselineAdvancedThisSample = false;

        void Add(string type, string message, Guid? activityId = null,
            LiveDetectCounts? before = null, LiveDetectCounts? after = null,
            long? beforeSourceTimeUs = null, int? movedBallCount = null,
            double? largestMovement = null,
            ImmutableArray<SettledBallPosition> beforeLayout = default,
            ImmutableArray<SettledBallPosition> afterLayout = default)
        {
            events.Add(new(Guid.NewGuid(), activityId, type, sample.CapturedAt,
                sample.CameraConnectionId, sample.MediaGeneration, sample.FrameId,
                sample.SourceTimeUs, sample.Timeline, sample.CalibrationId, message,
                before, after, beforeSourceTimeUs, movedBallCount, largestMovement,
                ToLoggedPositions(beforeLayout), ToLoggedPositions(afterLayout)));
        }

        void StartActivity(string reason)
        {
            if (activity is not null || !stableBaselineEstablished) return;
            activity = new(Guid.NewGuid(), sample.Timeline, sample.SourceTimeUs,
                sample.FrameId, sample.CapturedAt, reason);
            Add("activity_started", $"Table activity observed · {reason}; waiting for a clear settled result.",
                activity.Id, after: ToLiveDetectCounts(baselineCounts));
        }

        if (mediaGenerationChanged)
        {
            stableAtBaselineSamples = 0;
            currentBaselineStableSamples = 0;
            if (activity is { } interruptedActivity)
            {
                bool recentMovementEpisode = interruptedActivity.MovementObserved
                    && interruptedActivity.StartSourceTimeUs > 0
                    && sample.SourceTimeUs >= interruptedActivity.StartSourceTimeUs
                    && sample.SourceTimeUs - interruptedActivity.StartSourceTimeUs
                        <= LiveDetectShotClipWindowPolicy.MaximumInterruptedActivitySeconds * 1_000_000L;
                string interruptionMessage = recentMovementEpisode
                    ? "Decoder restarted before the activity settled; a bounded review clip was requested. No shot outcome inferred."
                    : interruptedActivity.MovementObserved
                        ? "Decoder restarted after the bounded review window; movement continuity is unknown. No shot outcome inferred and no clip was queued."
                        : "Decoder restarted; movement continuity is unknown. No shot outcome inferred.";
                Add("activity_interrupted", interruptionMessage,
                    interruptedActivity.Id, before: ToLiveDetectCounts(baselineCounts),
                    after: stableSampleEligible ? sample.Counts : null,
                    beforeSourceTimeUs: stableSnapshot.BaselineSourceTimeUs,
                    beforeLayout: baselineLayout,
                    afterLayout: stableSampleEligible ? sample.Layout : []);
                if (recentMovementEpisode)
                {
                    clipActivity = interruptedActivity;
                    clipEventType = "activity_interrupted";
                    clipEndSourceTimeUs = sample.SourceTimeUs;
                }
                activity = null;
            }
        }

        if (stableBefore.BaselineEstablished && lastClearLayout is true && !stableSampleEligible)
        {
            string type = sample.Obstructed ? "obstruction_started" : "layout_uncertain";
            string message = sample.Obstructed
                ? "Table view obstructed · positions and inventory cannot confirm an event."
                : "Table layout uncertain · retaining the last clear baseline.";
            StartActivity(sample.Obstructed ? "obstruction" : "uncertain view");
            Add(type, message, activity?.Id);
        }
        else if (lastClearLayout is false && stableSampleEligible && activity is not null)
        {
            Add("clear_view_returned", "Clear classified view returned · checking for a settled layout.",
                activity.Id, after: sample.Counts);
        }

        // The first frame of a restarted decoder has no spatial continuity with the
        // prior generation. It may still establish a fresh layout, but that jump is
        // not evidence that balls moved.
        if (stableBaselineEstablished && stableSampleEligible && sample.MovementObserved
            && !mediaGenerationChanged)
        {
            StartActivity("ball positions or inventory changed");
            if (activity is { MovementObserved: false } moving)
            {
                activity = moving with { MovementObserved = true };
                passEventType = "movement_observed";
                Add("movement_observed", "Ball movement/layout change observed · waiting for the settled result.",
                    moving.Id, before: ToLiveDetectCounts(baselineCounts), after: sample.Counts,
                    beforeSourceTimeUs: stableSnapshot.BaselineSourceTimeUs);
            }
        }
        lastClearLayout = stableSampleEligible;

        bool rackReset = false;
        // Inventory candidates need the same complete, non-empty support as the
        // settled-layout tracker. A clear-looking zero-detection frame is not proof
        // that every ball was potted and must not create a count-decrease event.
        if (candidateTracker.Observe(sample.SourceTimeUs, sample.Counts, stableSampleEligible,
            sample.PackedRack) is { } candidate)
        {
            string candidateType = candidate.Kind switch
            {
                LiveDetectChangeKind.Racked => "rack_detected",
                LiveDetectChangeKind.Increase => "balls_returned_candidate",
                LiveDetectChangeKind.Changed => "table_inventory_changed_candidate",
                _ => "possible_pot"
            };
            passEventType = candidateType;
            foreach (var message in InventoryEvents(candidate))
                Add(candidateType, message, activity?.Id, candidate.Before, candidate.After,
                    afterLayout: sample.Layout);
            if (candidate.Kind == LiveDetectChangeKind.Increase && activity is { } returning)
                activity = returning with { ShotClipSuppressed = true };
            rackReset = candidate.Kind == LiveDetectChangeKind.Racked;
        }

        if (rackReset && activity is { } rackActivity)
        {
            Add("activity_ended", "Packed rack detected · closing table activity without a shot clip.",
                rackActivity.Id, before: ToLiveDetectCounts(baselineCounts), after: sample.Counts);
            activity = null;
        }

        if (!stableBefore.BaselineEstablished && stableSnapshot.BaselineEstablished)
        {
            stableBaselineEstablished = true;
            baselineCounts = stableSnapshot.BaselineCounts;
            baselineLayout = sample.Layout;
            baselineCapturedAt = sample.CapturedAt;
            stableAtBaselineSamples = 0;
            baselineAdvancedThisSample = true;
            passEventType = "settled_layout_baseline";
            Add("settled_layout_baseline", $"Stable table baseline established · {sample.Counts.Summary}.",
                after: sample.Counts, afterLayout: sample.Layout);
        }

        var latestStableChange = stableSnapshot.RecentEvents.LastOrDefault();
        if (latestStableChange is { } settledChange && settledChange.Id != lastStableChangeId)
        {
            lastStableChangeId = settledChange.Id;
            baselineCounts = settledChange.After;
            baselineLayout = settledChange.AfterLayout;
            baselineCapturedAt = settledChange.CapturedAt;
            stableAtBaselineSamples = 0;
            baselineAdvancedThisSample = true;
            bool settledRack = sample.PackedRack;
            bool activityObservedBeforeSettlement = activity is not null;
            bool movementObserved = activity is { MovementObserved: true };
            if (!settledRack && activity is null)
                StartActivity("stable layout transition confirmed");
            bool sameInventory = settledChange.Before == settledChange.After;
            var beforeCounts = ToLiveDetectCounts(settledChange.Before);
            var afterCounts = ToLiveDetectCounts(settledChange.After);
            bool countDecreased = beforeCounts is { } beforeInventory && afterCounts is { } afterInventory
                && (afterInventory.Reds < beforeInventory.Reds || afterInventory.Yellows < beforeInventory.Yellows
                    || afterInventory.Black < beforeInventory.Black || afterInventory.Cue < beforeInventory.Cue);
            bool countIncreased = beforeCounts is { } beforeInventory2 && afterCounts is { } afterInventory2
                && (afterInventory2.Reds > beforeInventory2.Reds || afterInventory2.Yellows > beforeInventory2.Yellows
                    || afterInventory2.Black > beforeInventory2.Black || afterInventory2.Cue > beforeInventory2.Cue);
            string settledType = settledRack ? "rack_layout_settled"
                : sameInventory ? movementObserved ? "no_pot_observed"
                    : activityObservedBeforeSettlement ? "settled_layout_change_unclassified" : "layout_changed_without_activity_boundary"
                : countDecreased && !countIncreased ? "possible_pot_settled"
                : countIncreased && !countDecreased ? "balls_returned_settled"
                : "settled_inventory_change";
            passEventType = settledType;
            string settledMessage = settledRack
                ? "Packed rack reached a stable layout; inventory baseline advanced."
                : sameInventory
                    ? settledType switch
                    {
                        "no_pot_observed" => $"No pot observed after movement · inventory unchanged; {settledChange.MovedBallCount ?? 0} ball(s) moved. Shooter/legality and turn are not inferred.",
                        "settled_layout_change_unclassified" => $"Stable layout changed during an activity · inventory unchanged; {settledChange.MovedBallCount ?? 0} ball(s) moved, but movement was not independently observed. No shot outcome inferred.",
                        _ => $"Stable layout changed without an activity boundary · inventory unchanged; {settledChange.MovedBallCount ?? 0} ball(s) moved. No shot outcome inferred."
                    }
                    : countDecreased && !countIncreased
                        ? $"Settled inventory decrease · possible pot; {BuildCountSummary(settledChange.After)}."
                        : countIncreased && !countDecreased
                            ? $"Settled inventory increase · balls returned; {BuildCountSummary(settledChange.After)}."
                            : $"Settled table change · {BuildCountSummary(settledChange.After)}.";
            Add(settledType, settledMessage, activity?.Id,
                ToLiveDetectCounts(settledChange.Before), ToLiveDetectCounts(settledChange.After),
                settledChange.BeforeLayoutSourceTimeUs ?? settledChange.BeforeSourceTimeUs,
                settledChange.MovedBallCount, settledChange.LargestMovement,
                settledChange.BeforeLayout, settledChange.AfterLayout);

            if (activity is { } settledActivity)
            {
                Add("activity_ended", settledMessage, settledActivity.Id,
                    ToLiveDetectCounts(settledChange.Before), ToLiveDetectCounts(settledChange.After),
                    settledChange.BeforeLayoutSourceTimeUs ?? settledChange.BeforeSourceTimeUs,
                    settledChange.MovedBallCount, settledChange.LargestMovement,
                    settledChange.BeforeLayout, settledChange.AfterLayout);
                if (!settledRack && !settledActivity.ShotClipSuppressed
                    && settledType is ("no_pot_observed" or "possible_pot_settled" or "settled_inventory_change"))
                {
                    clipActivity = settledActivity;
                    clipEventType = settledType;
                    clipEndSourceTimeUs = sample.SourceTimeUs;
                }
                activity = null;
            }
        }

        if (stableSampleEligible && stableBaselineEstablished && activity is not null
            && baselineCounts == settledCounts
            && SettledLayoutReconciler.SameLayout(baselineLayout, sample.Layout,
                SettledLayoutReconciler.MovementThreshold))
            stableAtBaselineSamples++;
        else stableAtBaselineSamples = 0;

        if (!stableSampleEligible) currentBaselineStableSamples = 0;
        else if (baselineAdvancedThisSample) currentBaselineStableSamples = StableTableChangeTracker.RequiredStableSamples;
        else if (stableBaselineEstablished && baselineCounts == settledCounts
            && SettledLayoutReconciler.SameLayout(baselineLayout, sample.Layout))
            currentBaselineStableSamples++;
        else currentBaselineStableSamples = 0;

        if (activity is { } unchangedActivity && stableAtBaselineSamples >= StableTableChangeTracker.RequiredStableSamples)
        {
            string endedType = unchangedActivity.ShotClipSuppressed
                ? "activity_ended" : "activity_ended_without_layout_change";
            string endedMessage = unchangedActivity.ShotClipSuppressed
                ? "Activity ended after a balls-returned candidate; no shot clip queued."
                : "Activity ended with the previous clear layout and inventory unchanged; no shot outcome inferred.";
            Add(endedType, endedMessage, unchangedActivity.Id,
                before: ToLiveDetectCounts(baselineCounts), after: sample.Counts,
                beforeSourceTimeUs: stableSnapshot.BaselineSourceTimeUs, afterLayout: sample.Layout);
            passEventType = endedType;
            if (unchangedActivity.MovementObserved && !unchangedActivity.ShotClipSuppressed)
            {
                clipActivity = unchangedActivity;
                clipEventType = "activity_ended_without_layout_change";
                clipEndSourceTimeUs = sample.SourceTimeUs;
            }
            activity = null;
            stableAtBaselineSamples = 0;
        }

        string checkOutcome = sample.Obstructed ? "obstructed"
            : sample.LayoutUncertain ? "layout_uncertain"
            : passEventType ?? (!stableBaselineEstablished ? "baseline_pending"
                : baselineCounts == settledCounts && SettledLayoutReconciler.SameLayout(baselineLayout, sample.Layout)
                    ? "clear_unchanged"
                    : baselineCounts == settledCounts ? "layout_change_pending" : "inventory_change_pending");
        bool settledBaselineMatchesCurrent = stableSampleEligible
            && currentBaselineStableSamples >= StableTableChangeTracker.RequiredStableSamples;
        return new(checkOutcome, events.ToImmutable(), clipActivity, clipEventType, clipEndSourceTimeUs,
            ToLiveDetectCounts(baselineCounts), baselineCapturedAt, settledBaselineMatchesCurrent,
            currentBaselineStableSamples);
    }

    static bool IsStableInventory(LiveDetectCounts counts) =>
        counts.Reds is >= 0 and <= 7 && counts.Yellows is >= 0 and <= 7
        && counts.Black is 0 or 1 && counts.Cue is 0 or 1 && counts.Other == 0
        && counts.Total is > 0 and <= 16;

    static LiveDetectCounts? ToLiveDetectCounts(SettledTableCounts? counts) =>
        counts is { IsComplete: true }
            ? new(counts.Reds!.Value, counts.Yellows!.Value, counts.BlackBalls!.Value, counts.CueBalls!.Value, 0)
            : null;

    static LiveDetectLoggedPosition[]? ToLoggedPositions(ImmutableArray<SettledBallPosition> layout) =>
        layout.IsDefaultOrEmpty ? null : layout.Select(ball => new LiveDetectLoggedPosition(
            ball.Colour.ToString(), ball.X, ball.Y)).ToArray();

    static string BuildCountSummary(SettledTableCounts counts) =>
        $"{counts.Reds} reds · {counts.Yellows} yellows · {counts.BlackBalls} black · {counts.CueBalls} cue ball";

    static IEnumerable<string> InventoryEvents(LiveDetectCountCandidate candidate) => candidate.Kind switch
    {
        LiveDetectChangeKind.Racked => [$"Rack detected · {candidate.After.Summary}. New inventory baseline."],
        LiveDetectChangeKind.Increase => [$"Balls returned · {candidate.After.Summary}."],
        LiveDetectChangeKind.Changed => [$"Table inventory changed · {candidate.After.Summary}."],
        _ => PossiblePotEvents(candidate.Before, candidate.After)
    };

    static IEnumerable<string> PossiblePotEvents(LiveDetectCounts before, LiveDetectCounts after)
    {
        if (after.Reds < before.Reds) yield return PossiblePot("red", before.Reds, after.Reds);
        if (after.Yellows < before.Yellows) yield return PossiblePot("yellow", before.Yellows, after.Yellows);
        if (after.Black < before.Black) yield return PossiblePot("black", before.Black, after.Black);
        if (after.Cue < before.Cue) yield return PossiblePot("cue ball", before.Cue, after.Cue);

        static string PossiblePot(string colour, int beforeCount, int afterCount) =>
            $"Possible pot · {colour} detections {beforeCount} → {afterCount}. Confirm on the table before recording.";
    }
}
