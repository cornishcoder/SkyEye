namespace SkyEye.Core.Analysis;

/// <summary>A bounded presentation cache for explicitly requested observations.
/// Advice tools still use ShotPlanner's stricter fresh-frame check.</summary>
public sealed class CandidateCarousel
{
    public const int CycleMilliseconds = 2000;
    public Shot[] Candidates { get; private set; } = [];
    public DateTimeOffset ExpiresAt { get; private set; }
    public long OriginMilliseconds { get; private set; }
    public void Start(long monotonicMilliseconds) { OriginMilliseconds = monotonicMilliseconds; Clear(); }
    public void Clear() { Candidates = []; ExpiresAt = default; }
    public void Update(TableState state, DateTimeOffset now)
    {
        Candidates = ShotPlanner.Suggest(state, now);
        ExpiresAt = state.CapturedAt.AddSeconds(15);
    }
    public int Index(long monotonicMilliseconds, DateTimeOffset now) => Candidates.Length == 0 || now >= ExpiresAt ? -1
        : (int)(Math.Max(0, monotonicMilliseconds - OriginMilliseconds) / CycleMilliseconds % Candidates.Length);
    public Shot? Current(long monotonicMilliseconds, DateTimeOffset now)
        => Index(monotonicMilliseconds, now) is var i && i >= 0 ? Candidates[i] : null;
}
