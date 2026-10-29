using SkyEye.Companion.Hybrid;

internal static class LiveDetectShotClipWindowChecks
{
    public static void Run()
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
