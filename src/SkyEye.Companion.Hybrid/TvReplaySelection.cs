namespace SkyEye.Companion.Hybrid;

public enum TvReplayAction { Pause, Resume, Seek }
public enum TvCommandOutcome { None, Pending, Confirmed, Rejected, Unknown }
public sealed record TvReplayCommand(string Request, string Id, TvReplayAction Action, long PositionMs);

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
    public long PositionMs { get; private set; }
    public long DurationMs { get; private set; }
    public TvReplayCommand? Command { get; private set; }
    public TvCommandOutcome CommandOutcome { get; private set; }
    public bool CommandPending => CommandOutcome == TvCommandOutcome.Pending;
    public bool CanControl => Selected && Confirmed && !CommandPending && PlayerState is 2 or 3;
    bool commandAccepted;
    int expectedSeekState;
    DateTimeOffset commandDeadline;
    readonly HashSet<string> commandIds = [];

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
        PositionMs = positionMs; DurationMs = durationMs;
        PlayerState = playerState; IdleReason = idleReason;
        return ConfirmCommand() || changed;
    }
    public TvReplayCommand? BeginCommand(string? expectedRequest, string id, TvReplayAction action, long seconds, DateTimeOffset now)
    {
        if (!CanControl || Request != expectedRequest || !Guid.TryParseExact(id, "N", out _) || commandIds.Contains(id)
            || !Enum.IsDefined(action) || action == TvReplayAction.Pause && PlayerState != 2
            || action == TvReplayAction.Resume && PlayerState != 3 || seconds < 0 || seconds > DurationSeconds
            || seconds > long.MaxValue / 1000) return null;
        // Keep the end seek inside the clip, rather than accidentally completing it.
        var target = Math.Min(seconds * 1000, Math.Max(0, DurationMs - 250));
        Command = new(Request!, id, action, target); commandIds.Add(id);
        CommandOutcome = TvCommandOutcome.Pending; commandAccepted = false;
        expectedSeekState = PlayerState; commandDeadline = now + TimeSpan.FromSeconds(10);
        return Command;
    }
    public bool ObserveCommand(string? request, string? id, string? outcome)
    {
        if (!CommandPending || Command is null || Command.Request != request || Command.Id != id) return false;
        if (outcome == "rejected") { CommandOutcome = TvCommandOutcome.Rejected; return true; }
        if (outcome != "accepted") return false;
        commandAccepted = true; return ConfirmCommand();
    }
    bool ConfirmCommand()
    {
        if (!CommandPending || !commandAccepted || Command is null) return false;
        bool observed = Command.Action switch
        {
            TvReplayAction.Pause => PlayerState == 3,
            TvReplayAction.Resume => PlayerState == 2,
            TvReplayAction.Seek => PlayerState == expectedSeekState && Math.Abs(PositionMs - Command.PositionMs) <= 2000,
            _ => false
        };
        if (!observed) return false;
        CommandOutcome = TvCommandOutcome.Confirmed; return true;
    }
    public bool ExpireCommand(DateTimeOffset now)
    {
        if (!CommandPending || now < commandDeadline) return false;
        CommandOutcome = TvCommandOutcome.Unknown; return true;
    }
    public void Clear()
    {
        Connection = Guid.Empty; Request = ClipId = null; Name = "";
        PositionSeconds = DurationSeconds = 0; PlayerState = IdleReason = 0; Confirmed = false;
        PositionMs = DurationMs = 0; Command = null; CommandOutcome = TvCommandOutcome.None;
        commandAccepted = false; commandIds.Clear();
    }
}
