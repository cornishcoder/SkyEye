namespace SkyEye.Companion.Hybrid;

/// <summary>Coalesces repeated stale-frame polls into one observable wait episode.</summary>
internal sealed class LiveDetectFreshFrameWaitTracker
{
    public DateTimeOffset? WaitingSince { get; private set; }

    public bool Begin(DateTimeOffset occurredAt)
    {
        if (WaitingSince is not null) return false;
        WaitingSince = occurredAt;
        return true;
    }

    public TimeSpan? Recover(DateTimeOffset occurredAt) => Complete(occurredAt);

    public TimeSpan? Stop(DateTimeOffset occurredAt) => Complete(occurredAt);

    TimeSpan? Complete(DateTimeOffset occurredAt)
    {
        if (WaitingSince is not { } startedAt) return null;
        WaitingSince = null;
        return occurredAt >= startedAt ? occurredAt - startedAt : TimeSpan.Zero;
    }
}
