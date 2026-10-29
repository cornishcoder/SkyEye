using System.Security.Cryptography;
using SkyEye.Core.Application;

namespace SkyEye.Core;

public enum CaptureState { Ready, Active, Paused, Stopped, Faulted }
public sealed record Lease(string Token, string Owner, long Generation, DateTimeOffset ExpiresAt);
public sealed record Segment(string Name, long Sequence, string Timeline, long StartUs, double Duration, DateTimeOffset CapturedAt);
public sealed record Clip(string Id, string Name, double Duration, long Bytes, string Sha256, DateTimeOffset CreatedAt, int PortraitRotation = 90, Analysis.ClipRecordingOrigin? RecordingOrigin = null);

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

public static class ClipRangeSelection
{
    public static Segment[] SelectRange(IEnumerable<Segment> history, ClipSourceRange range,
        string currentTimeline, long nowUs)
    {
        if (!Guid.TryParseExact(range.Timeline, "N", out _) || range.StartUs <= 0
            || range.EndUs <= range.StartUs || range.EndUs - range.StartUs > 600_000_000L)
            throw new ArgumentException("Invalid source-time clip range.");
        if (range.Timeline != currentTimeline || range.EndUs > nowUs
            || nowUs - range.EndUs > 620_000_000L)
            throw new InvalidOperationException("The camera recording changed or the requested clip range is no longer recent.");

        var selected = history.Where(x => x.Timeline == range.Timeline
                && double.IsFinite(x.Duration) && x.Duration > 0 && x.Duration <= 4
                && x.StartUs < range.EndUs
                && x.StartUs + x.Duration * 1_000_000 > range.StartUs)
            .OrderBy(x => x.StartUs).ToArray();
        if (selected.Length == 0 || selected[0].StartUs >= range.EndUs
            || selected[^1].StartUs + selected[^1].Duration * 1_000_000 < range.EndUs - 100_000)
            throw new InvalidOperationException("The requested clip range is not fully available in camera history.");
        for (int i = 1; i < selected.Length; i++)
            if (Math.Abs(selected[i].StartUs - (selected[i - 1].StartUs + selected[i - 1].Duration * 1_000_000)) > 100_000)
                throw new InvalidOperationException("The requested clip range contains a recording gap.");
        return selected;
    }
}
