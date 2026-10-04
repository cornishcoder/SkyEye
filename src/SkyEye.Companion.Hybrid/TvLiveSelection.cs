namespace SkyEye.Companion.Hybrid;

// Return-live is acknowledged only by the receiver playing this request.
public sealed class TvLiveSelection
{
    public string? Request { get; private set; }
    public Guid Connection { get; private set; }
    public TvCommandOutcome Outcome { get; private set; }
    DateTimeOffset deadline;
    public string Begin(Guid connection, DateTimeOffset now)
    {
        if (connection == Guid.Empty) throw new ArgumentException("Current camera connection required.");
        Connection = connection; Request = Guid.NewGuid().ToString("N");
        Outcome = TvCommandOutcome.Pending; deadline = now + TimeSpan.FromSeconds(20); return Request;
    }
    public bool Observe(string? request, string? stream, int playerState)
    {
        if (Request is null || request != Request || stream is not ("camera" or "scene" or "candidates")
            || playerState != 2 || Outcome == TvCommandOutcome.Confirmed) return false;
        Outcome = TvCommandOutcome.Confirmed; return true;
    }
    public bool Expire(DateTimeOffset now)
    {
        if (Outcome != TvCommandOutcome.Pending || now < deadline) return false;
        Outcome = TvCommandOutcome.Unknown; return true;
    }
    public void Clear() { Request = null; Connection = Guid.Empty; Outcome = TvCommandOutcome.None; }
}
