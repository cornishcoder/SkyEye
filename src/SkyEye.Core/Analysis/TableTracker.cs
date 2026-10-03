namespace SkyEye.Core.Analysis;

/// <summary>Associates automatic observations and corrections with a capture timeline
/// and calibration. Duplicate or interrupted frames cannot establish settlement.</summary>
public sealed class TableTracker
{
    public TableState? State { get; private set; }
    public bool PhysicallyObstructed { get; private set; }
    Ball[] previous = []; string timeline = ""; DateTimeOffset stableSince;
    long version; bool manual;
    public void Reset() { State = null; previous = []; timeline = ""; manual = false; PhysicallyObstructed = false; }
    public TableState Observe(string calibrationId, string captureTimeline, DateTimeOffset capturedAt, Ball[] detected, string group, bool uncertain, bool obstructed)
    {
        var old = State;
        bool sameSource = old is not null && old.CalibrationId == calibrationId && timeline == captureTimeline;
        double interval = old is null ? 0 : (capturedAt - old.CapturedAt).TotalSeconds;
        if (sameSource && interval <= 0) return old!;
        bool same = sameSource && interval <= 6 && SameLayout(previous, detected);
        if (!same) { stableSince = capturedAt; manual = false; }
        bool confirmed = same && old!.Confirmed && !obstructed;
        bool occluded = obstructed || uncertain && !(manual && confirmed);
        bool settled = same && (capturedAt - stableSince).TotalSeconds >= 1;
        bool sameVersion = same && old!.Occluded == occluded && old.Group == group;
        State = new(sameVersion ? old!.Version : ++version, capturedAt, confirmed ? old!.Balls : detected.ToArray(), group, settled, occluded, confirmed, calibrationId);
        timeline = captureTimeline; previous = detected.ToArray(); PhysicallyObstructed = obstructed; return State;
    }
    public TableState Confirm(DateTimeOffset now)
    {
        var state = State ?? throw new InvalidOperationException("Analyze the table first.");
        if (!state.Settled || state.Occluded || (now - state.CapturedAt).TotalSeconds is > 6 or < -2)
            throw new InvalidOperationException("Wait for a fresh, settled, unobstructed view and check every ball.");
        State = state with { Confirmed = true }; return State;
    }
    public TableState Correct(Ball[] balls, DateTimeOffset now)
    {
        var state = State ?? throw new InvalidOperationException("Analyze the table before correcting it.");
        if (!state.Settled || PhysicallyObstructed || (now - state.CapturedAt).TotalSeconds is > 6 or < -2)
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
            if (match is null || (match.Position - ball.Position).Length > .008) return false; remaining.Remove(match);
        }
        return true;
    }
}
