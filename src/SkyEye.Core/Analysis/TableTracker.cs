namespace SkyEye.Core.Analysis;

/// <summary>Associates automatic observations and corrections with a capture timeline
/// and calibration. Duplicate or interrupted frames cannot establish settlement.</summary>
public sealed class TableTracker(double maximumObservationGapSeconds = 6)
{
    readonly double maximumGap = double.IsFinite(maximumObservationGapSeconds) && maximumObservationGapSeconds is >= 6 and <= 60
        ? maximumObservationGapSeconds : throw new ArgumentOutOfRangeException(nameof(maximumObservationGapSeconds));
    public TableState? State { get; private set; }
    public bool PhysicallyObstructed { get; private set; }
    Ball[] previous = [], stableAnchor = []; string timeline = ""; DateTimeOffset stableSince;
    long version; bool manual;
    public void Reset() { State = null; previous = []; stableAnchor = []; timeline = ""; manual = false; PhysicallyObstructed = false; }
    public TableState Observe(string calibrationId, string captureTimeline, DateTimeOffset capturedAt, Ball[] detected, string group, bool uncertain, bool obstructed)
    {
        var old = State;
        bool sameSource = old is not null && old.CalibrationId == calibrationId && timeline == captureTimeline;
        double interval = old is null ? 0 : (capturedAt - old.CapturedAt).TotalSeconds;
        if (sameSource && interval <= 0) return old!;
        // An ambiguous/obstructed view is not evidence that previously trusted
        // balls disappeared. Keep the last clear layout and its identity anchor;
        // advice remains blocked until a clear observation matches it again.
        bool retainLastClearLayout = obstructed || uncertain && !(manual && old?.Confirmed == true);
        if (sameSource && old is not null && retainLastClearLayout)
        {
            if (!old.Occluded || old.Settled || old.Group != group)
                State = old with { Version = ++version, Group = group, Settled = false, Occluded = true };
            PhysicallyObstructed = obstructed;
            return State!;
        }
        var returnAnchor = manual && old is not null ? old.Balls : stableAnchor;
        bool matchesLastKnown = sameSource && old is not null && SameLayout(returnAnchor, detected);
        bool same = sameSource && interval <= maximumGap && SameLayout(previous, detected) && SameLayout(stableAnchor, detected);
        bool clearReturnAfterLongOcclusion = sameSource && old is { Occluded: true } && interval > maximumGap
            && matchesLastKnown && !uncertain && !obstructed;
        if (clearReturnAfterLongOcclusion) stableSince = capturedAt;
        else if (!same) { stableSince = capturedAt; stableAnchor = detected.ToArray(); manual = false; }
        bool continuity = same || clearReturnAfterLongOcclusion;
        bool confirmed = continuity && old!.Confirmed && !obstructed;
        bool occluded = obstructed || uncertain && !(manual && confirmed);
        bool settled = same && (capturedAt - stableSince).TotalSeconds >= ObservationPolicy.MinimumStillSeconds;
        bool sameVersion = continuity && old!.Occluded == occluded && old.Group == group;
        State = new(sameVersion ? old!.Version : ++version, capturedAt, confirmed ? old!.Balls : detected.ToArray(), group, settled, occluded, confirmed, calibrationId);
        timeline = captureTimeline; previous = detected.ToArray(); PhysicallyObstructed = obstructed; return State;
    }
    public TableState Confirm(DateTimeOffset now)
    {
        var state = State ?? throw new InvalidOperationException("Analyze the table first.");
        if (!state.Settled || state.Occluded || (now - state.CapturedAt).TotalSeconds is > ObservationPolicy.ReviewFreshnessSeconds or < -2)
            throw new InvalidOperationException("Wait for a fresh, settled, unobstructed view and check every ball.");
        State = state with { Confirmed = true }; return State;
    }
    internal TableState Verified(string calibrationId, string captureTimeline, DateTimeOffset capturedAt, Ball[] balls, string group, bool still, bool unclear)
    {
        Reset(); timeline = captureTimeline; previous = balls.ToArray(); stableAnchor = balls.ToArray(); stableSince = capturedAt;
        // A successful bounded clear/still verification is itself the evidence
        // needed to trust this current layout; separate user confirmation is
        // not part of the normal readiness path.
        State = new(++version, capturedAt, balls.ToArray(), group, still, unclear, still && !unclear, calibrationId);
        return State;
    }
    public TableState Correct(Ball[] balls, DateTimeOffset now)
    {
        var state = State ?? throw new InvalidOperationException("Analyze the table before correcting it.");
        if (!state.Settled || PhysicallyObstructed || (now - state.CapturedAt).TotalSeconds is > ObservationPolicy.ReviewFreshnessSeconds or < -2)
            throw new InvalidOperationException("Corrections need a fresh, settled, unobstructed frame.");
        var corrected = state with { Version = ++version, Balls = balls.ToArray(), Confirmed = true, Occluded = false };
        if (!ShotPlanner.Eligible(corrected, now)) throw new ArgumentException("Check ball counts, colours and positions; balls cannot overlap.");
        State = corrected; manual = true; return State;
    }
    public TableState? SetGroup(string group)
    {
        if (group is not ("red" or "yellow" or "black" or "open")) throw new ArgumentException("Unknown group.");
        if (State is not null && State.Group != group) State = State with { Group = group, Version = ++version };
        return State;
    }
    public static bool SameLayout(Ball[] before, Ball[] after)
    {
        if (before.Length != after.Length) return false; var remaining = after.ToList();
        foreach (var ball in before)
        {
            var match = remaining.Where(b => b.Kind == ball.Kind).MinBy(b => (b.Position - ball.Position).Length);
            // Tolerate measured Hough centre jitter while checking a fixed anchor as
            // well as the previous frame, so slow cumulative motion cannot creep through.
            if (match is null || (match.Position - ball.Position).Length > .015) return false; remaining.Remove(match);
        }
        return true;
    }
}
