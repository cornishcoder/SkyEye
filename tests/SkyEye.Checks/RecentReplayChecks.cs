using SkyEye.Core;
using SkyEye.Core.Application;

static class RecentReplayChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static async Task Reject<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static async Task Run()
    {
        var timeline = Guid.NewGuid().ToString("N");
        var history = Enumerable.Range(0, 20).Select(i => new Segment("s" + i, i, timeline, 100_000_000 + i * 2_000_000L, 2, DateTimeOffset.UnixEpoch)).ToArray();
        RecentReplayRequest Request() => new(Guid.NewGuid().ToString("N"), timeline, 15, 139_000_000);
        var request = Request(); var window = RecentReplay.Select(history, request, timeline, 140_000_000);
        Check(window.StartUs == 124_000_000 && window.EndUs == 139_000_000 && window.Segments.Length == 8, "replay uses camera timestamp and preceding keyframe, not phone playback position");
        Check(RecentReplay.Select(history.TakeLast(2), request, timeline, 140_000_000).StartUs == 136_000_000, "short history reports its actual start");
        var sourceRange = new ClipSourceRange(timeline, 121_000_000, 127_500_000);
        var rangeSegments = ClipRangeSelection.SelectRange(history, sourceRange, timeline, 140_000_000);
        Check(rangeSegments.Length == 4 && rangeSegments[0].StartUs == 120_000_000
            && rangeSegments[^1].StartUs == 126_000_000,
            "durable range selection includes the preceding independently decodable segment and reaches the requested end");
        await Reject<InvalidOperationException>(() => Task.FromResult(ClipRangeSelection.SelectRange(
            history.Where(s => s.Sequence != 11), sourceRange, timeline, 140_000_000)));
        await Reject<InvalidOperationException>(() => Task.FromResult(ClipRangeSelection.SelectRange(
            history, sourceRange with { EndUs = 141_000_000 }, timeline, 140_000_000)));
        await Reject<InvalidOperationException>(() => Task.FromResult(ClipRangeSelection.SelectRange(
            history, sourceRange, Guid.NewGuid().ToString("N"), 140_000_000)));
        await Reject<InvalidOperationException>(() => Task.FromResult(RecentReplay.Select(history, request, Guid.NewGuid().ToString("N"), 140_000_000)));
        await Reject<InvalidOperationException>(() => Task.FromResult(RecentReplay.Select(history, request, timeline, 138_000_000)));
        await Reject<InvalidOperationException>(() => Task.FromResult(RecentReplay.Select(history, request, timeline, 170_000_000)));
        await Reject<InvalidOperationException>(() => Task.FromResult(RecentReplay.Select(history.Where(s => s.Sequence != 17), request, timeline, 140_000_000)));
        await Reject<InvalidOperationException>(() => Task.FromResult(RecentReplay.Select(history.Take(19), request, timeline, 140_000_000)));
        await Reject<ArgumentException>(() => Task.FromResult(RecentReplay.Select(history, request with { Seconds = 600 }, timeline, 140_000_000)));
        string root = Path.Combine(Path.GetTempPath(), "skyeye-recent-" + Guid.NewGuid().ToString("N"));
        var clock = new ReplayClock(); var store = new RecentReplayStore(root, clock); int exports = 0;
        async Task<(double, long, long)> Assemble(string path, CancellationToken ct) { exports++; await File.WriteAllBytesAsync(path, new byte[1024], ct); return (15, window.StartUs, window.EndUs); }
        try
        {
            var one = await store.CreateAsync(request, Assemble, default);
            var duplicate = await store.CreateAsync(request, Assemble, default);
            Check(one == duplicate && exports == 1, "same replay command returns original asset without export");
            await Reject<ArgumentException>(() => store.CreateAsync(request with { Seconds = 30 }, Assemble, default));
            var two = await store.CreateAsync(Request(), Assemble, default);
            await Reject<InvalidOperationException>(() => store.CreateAsync(Request(), Assemble, default));
            Check(exports == 2 && store.Find(one.Clip.Id) is not null && store.Find(two.Clip.Id) is not null, "capacity refuses export without evicting playable replays");
            clock.Utc = clock.Utc.AddDays(2);
            Check(store.Find(one.Clip.Id) is not null, "wall clock change cannot prematurely expire active replay");
            clock.Advance(600); Check(store.Find(one.Clip.Id) is null, "expiry denies access exactly at monotonic deadline");
            store.Cleanup(); Check(!Directory.EnumerateFiles(root).Any(), "expiry removes temporary files");
            await Reject<InvalidOperationException>(() => store.CreateAsync(request, Assemble, default));
            var failure = Request();
            await Reject<IOException>(() => store.CreateAsync(failure, async (path, ct) => { await File.WriteAllTextAsync(path, "partial", ct); throw new IOException("fixture"); }, default));
            Check(!Directory.EnumerateFiles(root).Any(), "failed export publishes nothing and removes partial");
            using var cancel = new CancellationTokenSource();
            await Reject<OperationCanceledException>(() => store.CreateAsync(failure, async (path, ct) => { var result = await Assemble(path, ct); cancel.Cancel(); return result; }, cancel.Token));
            Check(!Directory.EnumerateFiles(root).Any(), "cancelled export cannot publish asset");
            var recovered = await store.CreateAsync(failure, Assemble, default);
            Check(store.MediaPath("../escape") is null && !Directory.EnumerateFiles(root, "*.json").Any(), "no path injection and no saved-library metadata");
            _ = new RecentReplayStore(root, clock);
            Check(!File.Exists(store.MediaPath(recovered.Clip.Id)) && !Directory.EnumerateFiles(root).Any(), "restart clears ephemeral assets");
            await Reject<InvalidOperationException>(() => store.CreateAsync(Request(), async (path, ct) => { await using var huge = File.Create(path); huge.SetLength(RecentReplayStore.MaximumBytes + 1); return (15, window.StartUs, window.EndUs); }, default));
            Check(!Directory.EnumerateFiles(root).Any(), "byte budget rejects and cleans oversized output before hashing");
            Console.WriteLine("PASS recent replay: camera-clock windows, gaps/timeline/future/stale rejection, idempotency, non-evicting capacity, monotonic expiry, failure/cancellation cleanup, restart and byte limits");
        }
        finally { if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected fixture path"); Directory.Delete(root, true); }
    }
    sealed class ReplayClock : TimeProvider
    {
        public DateTimeOffset Utc = DateTimeOffset.UnixEpoch; long ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
        public override long GetTimestamp() => ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(int seconds) { ticks += seconds * TimeSpan.TicksPerSecond; Utc = Utc.AddSeconds(seconds); }
    }
}
