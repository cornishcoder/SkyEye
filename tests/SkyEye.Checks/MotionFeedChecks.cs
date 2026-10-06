using System.Threading.Channels;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class MotionFeedChecks
{
    public static async Task Run()
    {
        var camera = new CameraScope(Guid.NewGuid(), new string('A', 64), "session", 1, 1);
        var scope = new MotionFeedScope(camera, 7, "calibration-one", "timeline-one");
        var clock = new Clock();
        var leases = new Leases(); var source = new Source(clock, leases); var worker = new Worker();
        await using (var feed = new TableMotionFeed(source, worker, scope, () => true, new(60), clock))
        {
            feed.Start(); source.Push(1, 1000); await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            source.Push(2, 34000); source.Push(3, 67000); source.Push(4, 100000);
            await Until(() => feed.Snapshot.Acquired == 4);
            Check(leases.Peak <= 2 && feed.Snapshot.Replaced >= 2, "Overload cannot accumulate native frames; replaced frames are disposed");
            var stop = feed.StopAsync();
            Check(!stop.IsCompleted && leases.Alive == 1, "Stopping retains only the active worker's lease until it exits");
            worker.Release.TrySetResult(); await stop;
            Check(leases.Alive == 0 && leases.Created == leases.Disposed && feed.Snapshot.Latest is null,
                "Stop disposes every image exactly once and suppresses a late result");
            Check(feed.Snapshot.Active == 0 && feed.Snapshot.Pending == 0, "Stopped slots are empty");
        }
        leases = new(); source = new(clock, leases); worker = new();
        await using (var feed = new TableMotionFeed(source, worker, scope, () => true, new(60), clock))
        {
            feed.Start(); source.Push(5, 1000); await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            source.Push(5, 1000); source.Push(4, 500); source.Push(6, 2000, generation: 8);
            await Until(() => feed.Snapshot.ScopeRejected > 0);
            worker.Release.TrySetResult(); await feed.Completion;
            Check(feed.Snapshot.DuplicateOrBackwards == 2 && leases.Alive == 0 && feed.Snapshot.Processed == 0,
                "Duplicate/backwards input and changed decoder scope never publish a late measurement");
        }
        leases = new(); source = new(clock, leases); worker = new() { Block = false };
        await using (var feed = new TableMotionFeed(source, worker, scope, () => true, new(60), clock))
        {
            feed.Start(); source.Push(1, 1000); await Until(() => feed.Snapshot.Processed == 1);
            source.Push(2, 34000); await Until(() => feed.Snapshot.Processed == 2);
            source.Push(3, 400000); await Until(() => feed.Snapshot.Processed == 3);
            Check(worker.Contexts[0].Reset && !worker.Contexts[1].Reset && worker.Contexts[1].DeltaSeconds == .033
                && worker.Contexts[2].Reset && feed.Snapshot.GapResets == 1, "Motion uses source delta time and resets over gaps");
            source.Push(4, 500000, old: true); await Until(() => feed.Snapshot.Stale == 1);
            await feed.StopAsync(); Check(leases.Alive == 0, "Stale acquisition is discarded");
        }
        leases = new(); source = new(clock, leases); worker = new() { Fail = true };
        await using (var feed = new TableMotionFeed(source, worker, scope, () => true, new(60), clock))
        {
            feed.Start(); source.Push(1, 1000); await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            worker.Release.TrySetResult(); await feed.Completion;
            Check(feed.Snapshot.WorkerFailures == 1 && leases.Alive == 0, "Worker failure closes feed ownership");
        }
        // Acquisition ignores cancellation deliberately: stop must still own/dispose its late return.
        leases = new(); source = new(clock, leases) { IgnoreCancellation = true }; worker = new() { Block = false };
        await using (var feed = new TableMotionFeed(source, worker, scope, () => true, new(60), clock))
        {
            feed.Start(); await source.Reading.Task; var stop = feed.StopAsync(); source.Push(1, 1000); await stop;
            Check(leases.Alive == 0 && worker.Contexts.Count == 0, "A late acquisition after stop is disposed without processing");
        }
        Console.WriteLine("PASS motion feed: two-frame ownership bound, overload replacement, exact disposal, source/generation fences, duplicate/backwards, source-time gaps, stale/cancel/failure and late-result suppression");
    }
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static async Task Until(Func<bool> ready)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (!ready()) { if (DateTimeOffset.UtcNow >= deadline) throw new Exception("Motion-feed check timeout"); await Task.Delay(2); }
    }
    sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddDays(10); }
    sealed class Leases { public int Alive, Created, Disposed, Peak; }
    sealed class Pixels : ITableFrameImage
    {
        readonly Leases leases; bool disposed;
        public Pixels(Leases leases) { this.leases = leases; Interlocked.Increment(ref leases.Created); int alive = Interlocked.Increment(ref leases.Alive); leases.Peak = Math.Max(leases.Peak, alive); }
        public byte[] EncodeForReview() => throw new Exception("Feed must not request JPEG");
        public void Dispose() { if (disposed) throw new Exception("Image disposed twice"); disposed = true; Interlocked.Decrement(ref leases.Alive); Interlocked.Increment(ref leases.Disposed); }
    }
    sealed class Source(Clock clock, Leases leases) : ITableFrameSource
    {
        readonly Channel<(long Id, long Pts, long Generation, bool Old)> input = Channel.CreateUnbounded<(long,long,long,bool)>();
        public bool IgnoreCancellation;
        public TaskCompletionSource Reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Push(long id, long pts, long generation = 7, bool old = false) => input.Writer.TryWrite((id, pts, generation, old));
        public async Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
        {
            Reading.TrySetResult(); var item = await input.Reader.ReadAsync(IgnoreCancellation ? CancellationToken.None : cancellation);
            return new([], clock.GetUtcNow().AddSeconds(item.Old ? -10 : 0), "timeline-one")
            { Image = new Pixels(leases), SourceTimeUs = item.Pts, DecodedReceipt = new(item.Generation, item.Id, item.Pts, 100, 640, 360) };
        }
    }
    sealed class Worker : ITableMotionProcessor
    {
        public bool Block = true, Fail;
        public readonly List<MotionFrameContext> Contexts = [];
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MotionMeasurement> MeasureAsync(TableFrame frame, MotionFrameContext context, CancellationToken cancellation)
        {
            Contexts.Add(context); Entered.TrySetResult(); if (Block) await Release.Task;
            if (Fail) throw new IOException("Synthetic worker failure");
            return new(context.SourceTimeUs, [], 0, 0);
        }
    }
}
