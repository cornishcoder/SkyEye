using SkyEye.Companion.Hybrid;

internal static class LiveDetectShotClipGroupingChecks
{
    public static void Run()
    {
        var activityOne = Guid.NewGuid();
        var activityTwo = Guid.NewGuid();
        var eventOne = Guid.NewGuid();
        var eventTwo = Guid.NewGuid();
        var first = Candidate(activityOne, "timeline-one", 100_000_000, 110_000_000,
            "activity_ended_without_layout_change", eventOne);
        var overlapping = Candidate(activityTwo, "timeline-one", 112_000_000, 120_000_000,
            "settled_inventory_change", eventTwo);

        var group = new LiveDetectEventClipGroup(first);
        Require(group.TryAdd(overlapping), "overlapping padded source ranges coalesce into one shot clip");
        Require(group.WindowStartSourceTimeUs == 98_000_000
            && group.WindowEndSourceTimeUs == 122_000_000,
            "coalesced clip uses the union of each event's lead-in and post-roll range");
        Require(group.ActivityIds.SequenceEqual([activityOne, activityTwo])
            && group.TableEventIds.SequenceEqual([eventOne, eventTwo])
            && group.EventTypes.SequenceEqual(["activity_ended_without_layout_change", "settled_inventory_change"]),
            "coalesced clip retains each activity, table-event ID and event type");

        var touching = Candidate(Guid.NewGuid(), "timeline-one", 124_000_000, 130_000_000,
            "possible_pot_settled", Guid.NewGuid());
        Require(!group.TryAdd(touching), "half-open clip ranges that only touch stay separate");
        var otherTimeline = Candidate(Guid.NewGuid(), "timeline-two", 112_000_000, 120_000_000,
            "possible_pot_settled", Guid.NewGuid());
        Require(!group.TryAdd(otherTimeline), "source ranges from different timelines never coalesce");
        Console.WriteLine("PASS Live Detect shot clip grouping coalesces overlaps and preserves distinct events");
    }

    static LiveDetectEventClipCandidate Candidate(Guid activityId, string timeline,
        long activityStartSourceTimeUs, long settledSourceTimeUs, string eventType, Guid tableEventId) =>
        new(new LiveDetectActivity(activityId, timeline, activityStartSourceTimeUs, 1,
                DateTimeOffset.UnixEpoch, "fixture", MovementObserved: true),
            eventType, settledSourceTimeUs, [tableEventId]);

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
