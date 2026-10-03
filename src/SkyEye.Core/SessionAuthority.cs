using System.Security.Cryptography;

namespace SkyEye.Core;

public enum CaptureState { Ready, Active, Paused, Stopped, Faulted }
public sealed record Lease(string Token, string Owner, long Generation, DateTimeOffset ExpiresAt);
public sealed record Segment(string Name, long Sequence, string Timeline, long StartUs, double Duration, DateTimeOffset CapturedAt);
public sealed record Clip(string Id, string Name, double Duration, long Bytes, string Sha256, DateTimeOffset CreatedAt, int PortraitRotation = 90);

// Camera-side authority. Host name is display data, never proof of ownership.
public sealed class SessionAuthority(TimeProvider? clock = null)
{
    readonly object gate = new();
    readonly TimeProvider time = clock ?? TimeProvider.System;
    Lease? lease;
    long generation;
    public CaptureState State { get; private set; } = CaptureState.Ready;
    public long Revision { get; private set; }
    public string SessionId { get; private set; } = Guid.NewGuid().ToString("N");
    public Lease? Current { get { lock (gate) { Expire(); return lease; } } }
    void Expire() { if (lease is not null && lease.ExpiresAt <= time.GetUtcNow()) { lease = null; Revision++; } }
    public Lease Claim(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 80) throw new ArgumentException("Choose a host name of 1–80 characters.");
        lock (gate)
        {
            Expire();
            if (lease is not null) throw new InvalidOperationException("Another companion already controls this camera.");
            lease = new(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), owner, ++generation, time.GetUtcNow().AddSeconds(30));
            Revision++; return lease;
        }
    }
    public void Require(string? token)
    {
        lock (gate)
        {
            Expire();
            if (lease is null || token is null || token.Length != lease.Token.Length ||
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(token), System.Text.Encoding.ASCII.GetBytes(lease.Token)))
                throw new UnauthorizedAccessException("Host control has expired or belongs to another companion.");
        }
    }
    public Lease Heartbeat(string token) { lock (gate) { Require(token); return lease = lease! with { ExpiresAt = time.GetUtcNow().AddSeconds(30) }; } }
    public void Release(string token) { lock (gate) { Require(token); lease = null; Revision++; } }
    public void Transition(CaptureState next)
    {
        lock (gate)
        {
            bool allowed = next == CaptureState.Faulted || (State, next) switch
            {
                (CaptureState.Ready or CaptureState.Stopped, CaptureState.Active) => true,
                (CaptureState.Active, CaptureState.Paused or CaptureState.Stopped) => true,
                (CaptureState.Paused, CaptureState.Active or CaptureState.Stopped) => true,
                (CaptureState.Faulted, CaptureState.Stopped) => true,
                _ => false
            };
            if (State == next) return;
            if (!allowed) throw new InvalidOperationException($"Cannot change {State} to {next}.");
            if (next == CaptureState.Active && State is CaptureState.Ready or CaptureState.Stopped) SessionId = Guid.NewGuid().ToString("N");
            State = next; Revision++;
        }
    }
}

public static class ReplaySelection
{
    public static readonly int[] Durations = [30, 60, 120, 300, 600];
    public static Segment[] Select(IEnumerable<Segment> history, int seconds, long endUs)
    {
        if (!Durations.Contains(seconds)) throw new ArgumentException("Replay duration must be 30, 60, 120, 300 or 600 seconds.");
        var ordered = history.Where(x => x.StartUs < endUs).OrderBy(x => x.Sequence).ToArray();
        if (ordered.Length == 0) return [];
        // A pause starts a new timeline; never disguise that gap as continuous video.
        var timeline = ordered[^1].Timeline;
        return ordered.Where(x => x.Timeline == timeline && x.StartUs + x.Duration * 1_000_000 > endUs - seconds * 1_000_000L).ToArray();
    }
}
