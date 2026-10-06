using System.Security.Cryptography;

namespace SkyEye.Core;

public sealed record RecentReplayRequest(string CommandId, string Timeline, int Seconds, long EndUs);
public sealed record RecentReplayAsset(Clip Clip, string Timeline, long StartUs, long EndUs, DateTimeOffset ExpiresAt);
public sealed record RecentReplayWindow(Segment[] Segments, long StartUs, long EndUs);

public static class RecentReplay
{
    public static readonly int[] Durations = [15, 30, 60];
    public static void Validate(RecentReplayRequest request)
    {
        if (!Guid.TryParseExact(request.CommandId, "N", out _) || !Guid.TryParseExact(request.Timeline, "N", out _)
            || !Durations.Contains(request.Seconds) || request.EndUs <= 0)
            throw new ArgumentException("Invalid recent replay request.");
    }
    public static RecentReplayWindow Select(IEnumerable<Segment> history, RecentReplayRequest request, string currentTimeline, long nowUs)
    {
        Validate(request);
        if (request.Timeline != currentTimeline || request.EndUs > nowUs || nowUs - request.EndUs > 30_000_000)
            throw new InvalidOperationException("The camera recording changed or this replay request is too old. Request recent footage again.");
        // Start at the preceding segment's keyframe. Report that actual boundary;
        // never imply sample-exact cutting or combine discontinuous timelines.
        long start = Math.Max(0, request.EndUs - request.Seconds * 1_000_000L);
        var selected = history.Where(s => s.Timeline == request.Timeline && s.StartUs < request.EndUs
            && double.IsFinite(s.Duration) && s.Duration > 0 && s.Duration <= 4
            && s.StartUs + s.Duration * 1_000_000 > start).OrderBy(s => s.StartUs).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("No recent recording footage is available yet.");
        for (int i = 1; i < selected.Length; i++)
            if (Math.Abs(selected[i].StartUs - (selected[i - 1].StartUs + selected[i - 1].Duration * 1_000_000)) > 100_000)
                throw new InvalidOperationException("Recent footage contains a recording gap. Try a shorter replay.");
        if (selected[^1].StartUs + selected[^1].Duration * 1_000_000 < request.EndUs - 100_000)
            throw new InvalidOperationException("The camera has not finished the requested footage yet. Try again shortly.");
        return new(selected, selected[0].StartUs, request.EndUs);
    }
}

// A separate ephemeral directory: no ClipLibrary publication or saved catalogue
// entry. Capacity never evicts a still-valid replay another receiver may read.
public sealed class RecentReplayStore
{
    public const int MaximumAssets = 2;
    public const long MaximumBytes = 128L * 1024 * 1024;
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(10);
    readonly string root;
    readonly TimeProvider clock;
    readonly object gate = new();
    readonly SemaphoreSlim writer = new(1);
    readonly Dictionary<string, Entry> entries = [];
    readonly Dictionary<string, RecentReplayRequest> requests = [];
    readonly Queue<string> requestOrder = [];
    sealed record Entry(RecentReplayRequest Request, RecentReplayAsset Asset, long CreatedTimestamp);
    public RecentReplayStore(string directory, TimeProvider? clock = null)
    {
        root = Path.GetFullPath(directory); this.clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(root);
        // Process restart invalidates temporary replay URLs. Only our generated
        // files in this dedicated directory are removed; saved clips are elsewhere.
        foreach (var path in Directory.EnumerateFiles(root))
            if (Path.GetExtension(path) is ".mp4" or ".part" && Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)) File.Delete(path);
    }
    string PathFor(string id, string extension) => Path.Combine(root, id + extension);
    bool Expired(Entry entry) => clock.GetElapsedTime(entry.CreatedTimestamp) >= Retention;
    public void Cleanup()
    {
        lock (gate)
            foreach (var entry in entries.Values.Where(Expired).ToArray())
            {
                // Keep expired entries if deletion fails: disk capacity remains
                // accounted for and access is still refused. Next sweep retries.
                try { File.Delete(PathFor(entry.Asset.Clip.Id, ".mp4")); entries.Remove(entry.Asset.Clip.Id); } catch (IOException) { }
            }
    }
    public RecentReplayAsset? Find(string id)
    {
        lock (gate) return Guid.TryParseExact(id, "N", out _) && entries.TryGetValue(id, out var entry)
            && !Expired(entry) && File.Exists(PathFor(id, ".mp4")) ? entry.Asset : null;
    }
    public string? MediaPath(string id) => Find(id) is null ? null : PathFor(id, ".mp4");
    public async Task<RecentReplayAsset> CreateAsync(RecentReplayRequest request, Func<string, CancellationToken, Task<(double Duration, long StartUs, long EndUs)>> assemble, CancellationToken ct)
    {
        RecentReplay.Validate(request);
        await writer.WaitAsync(ct);
        string id = Guid.NewGuid().ToString("N"), part = PathFor(id, ".part");
        try
        {
            Cleanup();
            lock (gate)
            {
                if (requests.TryGetValue(request.CommandId, out var previous))
                {
                    if (previous != request) throw new ArgumentException("Replay command ID was already used for different footage.");
                    var existing = entries.Values.FirstOrDefault(e => e.Request == request && !Expired(e));
                    if (existing is not null) return existing.Asset;
                    throw new InvalidOperationException("That temporary replay expired. Request a new recent replay.");
                }
                if (entries.Count >= MaximumAssets || entries.Values.Sum(e => e.Asset.Clip.Bytes) >= MaximumBytes)
                    throw new InvalidOperationException("Two recent replays are already available. Reuse one or wait for it to expire.");
            }
            var output = await assemble(part, ct);
            ct.ThrowIfCancellationRequested();
            await using var file = File.OpenRead(part);
            long bytes = file.Length;
            if (bytes <= 0 || !double.IsFinite(output.Duration) || output.Duration <= 0 || output.Duration > request.Seconds + 5
                || output.StartUs < 0 || output.StartUs >= output.EndUs || output.EndUs != request.EndUs
                || output.EndUs - output.StartUs > (request.Seconds + 4) * 1_000_000L)
                throw new IOException("Camera returned invalid recent replay media.");
            lock (gate) if (bytes > MaximumBytes - entries.Values.Sum(e => e.Asset.Clip.Bytes)) throw new InvalidOperationException("Temporary replay storage is full. Wait for an existing replay to expire.");
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)); file.Close();
            var now = clock.GetUtcNow();
            var clip = new Clip(id, "Recent replay", output.Duration, bytes, hash, now);
            var asset = new RecentReplayAsset(clip, request.Timeline, output.StartUs, output.EndUs, now + Retention);
            ct.ThrowIfCancellationRequested();
            lock (gate)
            {
                File.Move(part, PathFor(id, ".mp4"));
                entries.Add(id, new(request, asset, clock.GetTimestamp()));
                requests.Add(request.CommandId, request); requestOrder.Enqueue(request.CommandId);
                while (requestOrder.Count > 64) requests.Remove(requestOrder.Dequeue());
            }
            return asset;
        }
        finally { try { File.Delete(part); } finally { writer.Release(); } }
    }
}
