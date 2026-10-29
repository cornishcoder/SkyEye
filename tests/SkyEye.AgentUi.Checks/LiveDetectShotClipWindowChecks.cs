using SkyEye.Companion.Hybrid;

internal static class LiveDetectShotClipWindowChecks
{
    public static async Task Run()
    {
        var id = Guid.NewGuid();
        string timeline = Guid.NewGuid().ToString("N");
        long movement = 100_000_000;
        long settled = 125_000_000;
        long request = 127_000_000;

        Require(LiveDetectShotClipWindowPolicy.TryCreate(id, timeline, movement, settled, request, out var window),
            "short table activity fits a supported clip window");
        Require(window.StartSourceTimeUs == movement - LiveDetectShotClipWindowPolicy.LeadInSeconds * 1_000_000L,
            "shot clip retains the configured lead-in before first activity");
        Require(window.EndSourceTimeUs == settled + LiveDetectShotClipWindowPolicy.PostRollSeconds * 1_000_000L,
            "shot clip records the post-roll end after the settled layout");
        Require(window.SaveSeconds == 60,
            "supported clip duration includes the full event, post-roll, and save-boundary margin");

        long postRollEnd = settled + LiveDetectShotClipWindowPolicy.PostRollSeconds * 1_000_000L;
        Require(!LiveDetectShotClipWindowPolicy.TryCreate(id, timeline, movement, settled,
                postRollEnd - 166_255, out _),
            "a frame 166ms before source-time post-roll is rejected");
        Require(LiveDetectShotClipWindowPolicy.TryCreate(id, timeline, movement, settled,
                postRollEnd, out _),
            "a source frame exactly at the post-roll boundary is accepted");

        int reads = 0;
        var ready = await LiveDetectShotClipWindowPolicy.WaitForPostRollAsync(timeline, settled, _ =>
            Task.FromResult(++reads == 1
                ? new LiveDetectShotClipWindowPolicy.PostRollObservation(timeline, postRollEnd - 166_255, true)
                : new LiveDetectShotClipWindowPolicy.PostRollObservation(timeline, postRollEnd, true)),
            timeout: TimeSpan.FromMilliseconds(100), pollInterval: TimeSpan.FromMilliseconds(1));
        Require(ready.Status == LiveDetectShotClipWindowPolicy.PostRollWaitStatus.Ready && reads == 2,
            "post-roll waits for a source timestamp at the boundary");

        var changed = await LiveDetectShotClipWindowPolicy.WaitForPostRollAsync(timeline, settled, _ =>
            Task.FromResult(new LiveDetectShotClipWindowPolicy.PostRollObservation("other-timeline", postRollEnd, true)),
            timeout: TimeSpan.FromMilliseconds(100), pollInterval: TimeSpan.FromMilliseconds(1));
        Require(changed.Status == LiveDetectShotClipWindowPolicy.PostRollWaitStatus.TimelineChanged,
            "post-roll wait exits on timeline change");

        var unavailable = await LiveDetectShotClipWindowPolicy.WaitForPostRollAsync(timeline, settled, _ =>
            Task.FromResult(new LiveDetectShotClipWindowPolicy.PostRollObservation(timeline, null, false)),
            timeout: TimeSpan.FromMilliseconds(100), pollInterval: TimeSpan.FromMilliseconds(1));
        Require(unavailable.Status == LiveDetectShotClipWindowPolicy.PostRollWaitStatus.SessionUnavailable,
            "post-roll wait exits when the camera session is lost");

        var pendingRead = new TaskCompletionSource<LiveDetectShotClipWindowPolicy.PostRollObservation>();
        var timedOut = await LiveDetectShotClipWindowPolicy.WaitForPostRollAsync(timeline, settled, _ => pendingRead.Task,
            timeout: TimeSpan.FromMilliseconds(5), pollInterval: TimeSpan.FromMilliseconds(1));
        Require(timedOut.Status == LiveDetectShotClipWindowPolicy.PostRollWaitStatus.TimedOut,
            "post-roll wait remains bounded when frame readback stalls");

        Require(!LiveDetectShotClipWindowPolicy.TryCreate(id, "not-a-timeline", movement, settled, request, out _),
            "clip windows reject malformed camera timelines");
        Require(!LiveDetectShotClipWindowPolicy.TryCreate(id, timeline, settled, movement, request, out _),
            "clip windows reject reversed event boundaries");
        Require(!LiveDetectShotClipWindowPolicy.TryCreate(id, timeline, movement, settled, 126_000_000, out _),
            "clip windows reject a save request earlier than the required post-roll");

        Console.WriteLine("PASS event-based clip windows include lead-in, settled post-roll, and supported save margins");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
