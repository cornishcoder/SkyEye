namespace SkyEye.Companion.Hybrid;

// Safe receiver proof, independent of phone playback and camera host control.
public sealed class TvReplaySelection
{
    public Guid Connection { get; private set; }
    public string? Request { get; private set; }
    public string? ClipId { get; private set; }
    public string Name { get; private set; } = "";
    public long PositionSeconds { get; private set; }
    public long DurationSeconds { get; private set; }
    public int PlayerState { get; private set; }
    public int IdleReason { get; private set; }
    public bool Confirmed { get; private set; }
    public bool Selected => Request is not null;

    public string Begin(Guid connection, string id, string name, double duration)
    {
        if (connection == Guid.Empty || !Guid.TryParseExact(id, "N", out _)
            || !double.IsFinite(duration) || duration <= 0 || duration > long.MaxValue / 1000d)
            throw new ArgumentException("Invalid camera clip.");
        Clear(); Connection = connection; ClipId = id; Name = name;
        DurationSeconds = (long)Math.Ceiling(duration); Request = Guid.NewGuid().ToString("N");
        return Request;
    }
    public bool Observe(string? request, string? clip, long positionMs, long durationMs, int playerState, int idleReason)
    {
        if (!Selected || Request != request || ClipId != clip || positionMs < 0 || durationMs <= 0) return false;
        var duration = (long)Math.Ceiling(durationMs / 1000d);
        // Cast resets position to zero after natural completion. FINISHED is
        // receiver proof of the end, not a new seek back to the beginning.
        var position = playerState == 1 && idleReason == 1 ? duration : Math.Min(positionMs, durationMs) / 1000;
        bool changed = !Confirmed || PositionSeconds != position || DurationSeconds != duration
            || PlayerState != playerState || IdleReason != idleReason;
        Confirmed = true; PositionSeconds = position; DurationSeconds = duration;
        PlayerState = playerState; IdleReason = idleReason; return changed;
    }
    public void Clear()
    {
        Connection = Guid.Empty; Request = ClipId = null; Name = "";
        PositionSeconds = DurationSeconds = 0; PlayerState = IdleReason = 0; Confirmed = false;
    }
}
