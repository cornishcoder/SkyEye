using System.Collections.Immutable;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

internal sealed record LiveDetectActivity(Guid Id, string Timeline, long StartSourceTimeUs, long StartFrameId,
    DateTimeOffset StartedAt, string StartReason, bool MovementObserved = false,
    bool ShotClipSuppressed = false);

/// <remarks><paramref name="UncertainRegions"/> is set only when <paramref name="LayoutUncertain"/> comes
/// from those regions alone; <paramref name="UncertainOutsideRegions"/> is the broad flag the rest of
/// the table would have had without them.</remarks>
internal sealed record LiveDetectTableEventObservation(EvidenceSource Source, Guid CameraConnectionId,
    long MediaGeneration, long FrameId, DateTimeOffset CapturedAt, long SourceTimeUs, string Timeline,
    string CalibrationId, LiveDetectCounts Counts, ImmutableArray<SettledBallPosition> Layout,
    bool Obstructed, bool Uncertain, bool LayoutUncertain, bool MovementObserved, bool PackedRack,
    ImmutableArray<LiveDetectUncertainRegion> UncertainRegions = default, bool? UncertainOutsideRegions = null,
    SettledChangePass? SettledChange = null);

/// <summary>A doubtful table area in table units (x 0..2, y 0..1); layout positions use x / 2.</summary>
internal readonly record struct LiveDetectUncertainRegion(double X, double Y, double Radius)
{
    public bool Contains(SettledBallPosition ball)
    {
        double dx = ball.X * 2 - X, dy = ball.Y - Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

/// <summary>
/// Reducer choices for decisions D1 and D2 (docs/claude/d1-d2-implementation.md).
/// D1: a pocket-colour flag covers only its pocket regions once a baseline exists.
/// D2: a first baseline may form with the black or cue not seen; a later sighting corrects it.
/// A first baseline formed while a pocket region is flagged stays off: in the saved logs it added
/// false movement and no-pot clips on static fixtures.
/// </summary>
/// <remarks>D3: <paramref name="SettledChange"/> <c>Shadow</c> computes the settled-change rules from any
/// evidence on the observation and reports them on the batch; it never changes an outcome or event.</remarks>
internal sealed record LiveDetectReducerOptions(bool PocketColourRegionOnly = true,
    bool FirstBaselineWithPocketRegion = false, bool FirstBaselineWithoutBlackOrCue = true,
    LiveDetectSettledChangeMode SettledChange = LiveDetectSettledChangeMode.Shadow)
{
    public static LiveDetectReducerOptions Default { get; } = new();
    /// <summary>The reducer as it behaved before D1 and D2.</summary>
    public static LiveDetectReducerOptions Previous { get; } = new(false, false, false);
}

internal sealed record LiveDetectEventBatch(string CheckOutcome,
    ImmutableArray<LiveDetectLogTableEvent> TableEvents, LiveDetectActivity? ClipActivity,
    string? ClipEventType, long? ClipEndSourceTimeUs,
    LiveDetectCounts? SettledBaseline, DateTimeOffset? SettledBaselineAt,
    bool SettledBaselineMatchesCurrent, int BaselineMatchSamples)
{
    public IEnumerable<string> Messages => TableEvents.Select(tableEvent => tableEvent.Message);
    /// <summary>D2: the settled baseline formed without the black seen; not evidence it is off the table.</summary>
    public bool BaselineBlackNotSeen { get; init; }
    /// <summary>D2: the settled baseline formed without the cue ball seen.</summary>
    public bool BaselineCueNotSeen { get; init; }
    /// <summary>D1: only a pocket region is flagged; balls inside it are individually uncertain.</summary>
    public bool PocketRegionUncertain { get; init; }
    public int PocketRegionBalls { get; init; }
    /// <summary>D3 shadow output (proposals and non-observed ball states); null when there is no evidence
    /// or the mode is off. Nothing in the reducer reads it.</summary>
    public LiveDetectSettledChangeShadow? SettledChange { get; init; }
}

/// <summary>
/// Converts one fresh, image-free Live Detect result into a check outcome and typed table events.
/// This is a transient perception reducer; GameState remains the only canonical persisted state.
/// </summary>
internal sealed class LiveDetectTableEventTracker
{
    readonly LiveDetectReducerOptions options;
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
    bool blackNotSeen, cueNotSeen;
    readonly LiveDetectSettledChangePolicy settledChangePolicy = new();
    bool settledChangeSeen;
    long? baselineConfirmedSourceTimeUs;

    public LiveDetectTableEventTracker(LiveDetectReducerOptions? options = null) =>
        this.options = options ?? LiveDetectReducerOptions.Default;

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
        blackNotSeen = cueNotSeen = false;
        settledChangePolicy.Reset();
        settledChangeSeen = false;
        baselineConfirmedSourceTimeUs = null;
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

        // D1: a pocket-colour flag that is the only layout flag covers its pocket regions only. The
        // balls inside them are individually uncertain; a red or yellow count change against the
        // baseline, which such a ball could explain, waits until the regions clear.
        bool pocketRegionOnly = options.PocketColourRegionOnly && sample.LayoutUncertain && !sample.Obstructed
            && !sample.UncertainRegions.IsDefaultOrEmpty;
        bool layoutUncertain = sample.LayoutUncertain, uncertain = sample.Uncertain;
        int pocketRegionBalls = 0;
        if (pocketRegionOnly)
        {
            uncertain = sample.UncertainOutsideRegions ?? sample.Uncertain;
            layoutUncertain = stableBaselineEstablished
                ? baselineCounts is not { } trusted
                    || trusted.Reds != sample.Counts.Reds || trusted.Yellows != sample.Counts.Yellows
                : !options.FirstBaselineWithPocketRegion;
            pocketRegionBalls = sample.Layout.IsDefault ? 0
                : sample.Layout.Count(ball => sample.UncertainRegions.Any(region => region.Contains(ball)));
        }

        bool candidateEligible = !layoutUncertain && !sample.Obstructed;
        // A broad uncertain result can describe a real post-shot cue/black absence,
        // so keep accepting it after a trusted baseline. Without prior state a missed
        // cue or black is indistinguishable from a genuine incomplete table, so it may
        // seed the first baseline only under D2, recorded as "not seen", never absent.
        bool firstBaselineAllowed = !uncertain || options.FirstBaselineWithoutBlackOrCue
            && (sample.Counts.Black == 0 || sample.Counts.Cue == 0);
        bool stableSampleEligible = candidateEligible && IsStableInventory(sample.Counts)
            && sample.Layout.Length == sample.Counts.Total
            && (stableBaselineEstablished || firstBaselineAllowed);
        // Before a baseline only certain samples feed inventory candidates, so an
        // unseen black or cue cannot become a pot candidate.
        bool candidateSampleEligible = stableSampleEligible && (stableBaselineEstablished || !uncertain);
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
            settledChangePolicy.Reset();
            baselineConfirmedSourceTimeUs = null;
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

        bool baselinePositionMoved = stableBaselineEstablished && stableSampleEligible
            && !SettledLayoutReconciler.PositionsCompatible(baselineLayout, sample.Layout);
        bool observedMovement = sample.MovementObserved || baselinePositionMoved;

        // The first frame of a restarted decoder has no spatial continuity with the
        // prior generation. It may still establish a fresh layout, but that jump is
        // not evidence that balls moved.
        if (stableBaselineEstablished && stableSampleEligible && observedMovement
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
        if (CorrectNotSeen(candidateTracker.Observe(sample.SourceTimeUs, sample.Counts, candidateSampleEligible,
            sample.PackedRack)) is { } candidate)
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
            if (options.FirstBaselineWithoutBlackOrCue && uncertain)
            {
                blackNotSeen = sample.Counts.Black == 0;
                cueNotSeen = sample.Counts.Cue == 0;
                if (blackNotSeen || cueNotSeen) candidateTracker.SeedBaseline(sample.Counts);
            }
            passEventType = "settled_layout_baseline";
            Add("settled_layout_baseline", $"Stable table baseline established · {LiveDetectInventoryReadoutPolicy.BaselineSummary(sample.Counts, blackNotSeen, cueNotSeen)}.",
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
            // D2: the first sighting of a kind the baseline formed without corrects the baseline;
            // it is never a ball returned to the table.
            var settledBefore = settledChange.Before;
            bool blackCorrected = blackNotSeen && settledChange.After.BlackBalls > settledBefore.BlackBalls;
            bool cueCorrected = cueNotSeen && settledChange.After.CueBalls > settledBefore.CueBalls;
            bool pureCorrection = false;
            if (blackCorrected || cueCorrected)
            {
                settledBefore = settledBefore with
                {
                    BlackBalls = blackCorrected ? settledChange.After.BlackBalls : settledBefore.BlackBalls,
                    CueBalls = cueCorrected ? settledChange.After.CueBalls : settledBefore.CueBalls
                };
                blackNotSeen &= !blackCorrected;
                cueNotSeen &= !cueCorrected;
                // The candidate tracker may confirm the same sighting later; it must not see a return.
                candidateTracker.SeedBaseline(sample.Counts);
                var otherBalls = settledChange.AfterLayout.Where(ball =>
                    !(blackCorrected && ball.Colour == BallColour.Black || cueCorrected && ball.Colour == BallColour.Cue))
                    .ToImmutableArray();
                pureCorrection = !settledRack && settledBefore == settledChange.After
                    && SettledLayoutReconciler.PositionsCompatible(settledChange.BeforeLayout, otherBalls);
                string seen = blackCorrected && cueCorrected ? "black and cue ball" : blackCorrected ? "black" : "cue ball";
                passEventType = "baseline_corrected";
                Add("baseline_corrected", $"Baseline corrected · {seen} now seen; it was not seen when the baseline formed, so it is not a ball returned to the table.",
                    activity?.Id, ToLiveDetectCounts(settledChange.Before), ToLiveDetectCounts(settledChange.After),
                    settledChange.BeforeLayoutSourceTimeUs ?? settledChange.BeforeSourceTimeUs,
                    beforeLayout: settledChange.BeforeLayout, afterLayout: settledChange.AfterLayout);
            }
            if (!settledRack && activity is null && !pureCorrection)
                StartActivity("stable layout transition confirmed");
            bool sameInventory = settledBefore == settledChange.After;
            var beforeCounts = ToLiveDetectCounts(settledBefore);
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
            if (!pureCorrection) passEventType = settledType;
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
            if (!pureCorrection)
                Add(settledType, settledMessage, activity?.Id,
                    ToLiveDetectCounts(settledBefore), ToLiveDetectCounts(settledChange.After),
                    settledChange.BeforeLayoutSourceTimeUs ?? settledChange.BeforeSourceTimeUs,
                    settledChange.MovedBallCount, settledChange.LargestMovement,
                    settledChange.BeforeLayout, settledChange.AfterLayout);

            if (!pureCorrection && activity is { } settledActivity)
            {
                Add("activity_ended", settledMessage, settledActivity.Id,
                    ToLiveDetectCounts(settledBefore), ToLiveDetectCounts(settledChange.After),
                    settledChange.BeforeLayoutSourceTimeUs ?? settledChange.BeforeSourceTimeUs,
                    settledChange.MovedBallCount, settledChange.LargestMovement,
                    settledChange.BeforeLayout, settledChange.AfterLayout);
                // A settled decrease still needs a review clip when an earlier,
                // transient increase occurred in the same activity. The clip is
                // evidence only; it does not admit a pot into canonical match state.
                if (!settledRack
                    && settledType is ("no_pot_observed" or "possible_pot_settled" or "settled_inventory_change")
                    && (!settledActivity.ShotClipSuppressed || settledType == "possible_pot_settled"))
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
            : layoutUncertain ? "layout_uncertain"
            : passEventType ?? (!stableBaselineEstablished ? "baseline_pending"
                : baselineCounts == settledCounts && SettledLayoutReconciler.SameLayout(baselineLayout, sample.Layout)
                    ? "clear_unchanged"
                    : baselineCounts == settledCounts ? "layout_change_pending" : "inventory_change_pending");
        bool settledBaselineMatchesCurrent = stableSampleEligible
            && currentBaselineStableSamples >= StableTableChangeTracker.RequiredStableSamples;
        // D3 shadow: runs last, reads copies of the state above, and is reported only on the batch.
        if (!stableSnapshot.BaselineEstablished) baselineConfirmedSourceTimeUs = null;
        else if (baselineAdvancedThisSample) baselineConfirmedSourceTimeUs = sample.SourceTimeUs;
        LiveDetectSettledChangeShadow? settledChangeShadow = null;
        settledChangeSeen |= sample.SettledChange is not null;
        if (options.SettledChange == LiveDetectSettledChangeMode.Shadow && settledChangeSeen)
            settledChangeShadow = settledChangePolicy.Observe(new(stableSnapshot.BaselineEstablished, baselineAdvancedThisSample,
                stableSnapshot.BaselineSourceTimeUs, baselineLayout, sample.Layout,
                stableSampleEligible, sample.Obstructed, pocketRegionOnly ? sample.UncertainRegions : [],
                blackNotSeen, cueNotSeen, sample.SourceTimeUs, baselineConfirmedSourceTimeUs), sample.SettledChange);
        return new(checkOutcome, events.ToImmutable(), clipActivity, clipEventType, clipEndSourceTimeUs,
            ToLiveDetectCounts(baselineCounts), baselineCapturedAt, settledBaselineMatchesCurrent,
            currentBaselineStableSamples)
        {
            BaselineBlackNotSeen = stableBaselineEstablished && blackNotSeen,
            BaselineCueNotSeen = stableBaselineEstablished && cueNotSeen,
            PocketRegionUncertain = pocketRegionOnly,
            PocketRegionBalls = pocketRegionBalls,
            SettledChange = settledChangeShadow
        };
    }

    /// <summary>D2: an inventory candidate whose only increase is a kind the baseline formed without is a
    /// correction, not a return; with other changes, those alone decide the candidate.</summary>
    LiveDetectCountCandidate? CorrectNotSeen(LiveDetectCountCandidate? candidate)
    {
        if (candidate is not { } raw || raw.Kind == LiveDetectChangeKind.Racked || !blackNotSeen && !cueNotSeen)
            return candidate;
        var before = raw.Before with
        {
            Black = blackNotSeen && raw.After.Black > raw.Before.Black ? raw.After.Black : raw.Before.Black,
            Cue = cueNotSeen && raw.After.Cue > raw.Before.Cue ? raw.After.Cue : raw.Before.Cue
        };
        if (before == raw.Before) return candidate;
        if (before == raw.After) return null;
        return new LiveDetectCountCandidate(before, raw.After)
        {
            Kind = LiveDetectCountCandidateTracker.IsNamedCountDecrease(before, raw.After) ? LiveDetectChangeKind.Decrease
                : LiveDetectCountCandidateTracker.IsNamedCountDecrease(raw.After, before) ? LiveDetectChangeKind.Increase
                : LiveDetectChangeKind.Changed
        };
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
