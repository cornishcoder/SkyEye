using SkyEye.Companion.Hybrid;

internal static class LiveDetectFreshFrameWaitChecks
{
    public static void Run()
    {
        var tracker = new LiveDetectFreshFrameWaitTracker();
        var start = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        Require(tracker.Begin(start), "first stale-frame observation starts a wait episode");
        Require(!tracker.Begin(start.AddMilliseconds(150)) && tracker.WaitingSince == start,
            "repeated polling coalesces into the original wait episode");
        Require(tracker.Recover(start.AddSeconds(2)) == TimeSpan.FromSeconds(2)
            && tracker.WaitingSince is null,
            "a fresh frame closes the wait with its measured duration");
        Require(tracker.Recover(start.AddSeconds(3)) is null,
            "a recovery with no active wait does not emit a duplicate transition");
        Require(tracker.Begin(start.AddSeconds(4))
            && tracker.Stop(start.AddSeconds(5)) == TimeSpan.FromSeconds(1)
            && tracker.WaitingSince is null,
            "stopping during a stale period closes it as unresolved");
        Console.WriteLine("PASS Live Detect fresh-frame waits coalesce and close as recovered or unresolved episodes");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
