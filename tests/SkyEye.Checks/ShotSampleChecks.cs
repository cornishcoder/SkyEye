using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class ShotSampleChecks
{
    static readonly ClipSaveScope Scope = new(new string('A', 64), Guid.NewGuid().ToString("N"), 1, 3);
    static readonly ShotSampleContext Context = new("calibration-1", 6, "timeline-1", 123, DateTimeOffset.UtcNow, "config-1");
    static Clip Clip() => new(Guid.NewGuid().ToString("N"), "camera clip", 28.4, 1234, new string('B', 64), DateTimeOffset.UtcNow,
        RecordingOrigin: new(Guid.NewGuid().ToString("N"), 123_000, 1920, 1080, 0));
    static ShotSampleRecord Request(string words = "just potted a red") => new(1, Guid.NewGuid(), "pending", "player-reported",
        new(words, DateTimeOffset.UtcNow, [new("outcome", "potted"), new("ball", "red")], []), Scope, Context,
        DateTimeOffset.UtcNow, 30, 5, 2, "The exact shot time is unknown.");
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-samples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ShotSampleStore(Path.Combine(root, "samples"));
            var effect = new FixtureEffect(); var journal = new ClipSaveExecutor(Path.Combine(root, "saves"), effect);
            var app = new AppCoordinator(clipSaves: journal);
            Task<CommandReceipt> Save(Guid id, ClipSaveScope scope, CancellationToken ct) => app.DispatchAsync(new(id, CommandOrigin.Sky, app.State.Version, new SaveClip(30, scope)), ct);
            var collector = new ShotSampleCollector(store, (_, _) => Task.CompletedTask);
            var request = Request(); var result = await collector.CollectAsync(request, TimeSpan.FromSeconds(2), Save, CancellationToken.None);
            var loaded = new ShotSampleStore(Path.Combine(root, "samples")).Read(request.OperationId);
            Check(result.Status == "saved" && loaded?.Clip?.Id == result.Clip?.Id && loaded?.Clip?.Sha256 == result.Clip?.Sha256
                && loaded?.Clip?.RecordingOrigin?.StartUs == 123_000 && loaded?.Report.OriginalWords == request.Report.OriginalWords
                && loaded?.Provenance == "player-reported" && loaded?.RequestedSeconds == 30 && loaded?.DesiredSampleSeconds == 5
                && loaded?.Context.CalibrationId == "calibration-1", "sample retains original report, scope, actual clip identity, origin and duration intent");
            Check(!ShotSampleStore.Valid(request.Report with { Fields = [new("ball", "yellow")] })
                && !ShotSampleStore.Valid(request.Report with { Notes = ["a foul"] }), "invented metadata is rejected");
            var miss = request.Report with { OriginalWords = "missed that one", Fields = [new("outcome", "missed")], Notes = [] };
            Check(ShotSampleStore.Valid(miss), "a sparse miss needs no invented ball or ruling");

            var pending = Request(); store.Write(pending);
            var receipt = await Save(pending.OperationId, pending.Scope, CancellationToken.None);
            Check(receipt.SavedClip is not null && store.Recover(journal) == 1
                && store.Read(pending.OperationId)?.Clip?.Id == receipt.SavedClip.Id, "restart recovers a linked clip only from its acknowledged operation");
            var unknown = Request(); store.Write(unknown);
            Check(store.Recover(journal) == 0 && store.Read(unknown.OperationId)?.Status == "pending", "unacknowledged pending samples are never guessed or retried");

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = new ShotSampleCollector(store, async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); });
            var first = held.CollectAsync(Request(), TimeSpan.FromSeconds(2), Save, CancellationToken.None); await entered.Task;
            var overlap = await held.CollectAsync(Request(), TimeSpan.FromSeconds(2), Save, CancellationToken.None);
            Check(overlap.Status == "busy" && effect.Calls == 2, "overlapping report is explicitly rejected without a second save");
            release.SetResult(); Check((await first).Status == "saved", "admitted sample completes after overlap");
            var failed = await collector.CollectAsync(Request(), TimeSpan.Zero,
                (id, _, _) => Task.FromResult(new CommandReceipt(id, CommandOutcome.Failed, 0, "Camera refused.")), CancellationToken.None);
            var uncertain = await collector.CollectAsync(Request(), TimeSpan.Zero,
                (id, _, _) => Task.FromResult(new CommandReceipt(id, CommandOutcome.Uncertain, 0, "Save result unknown.")), CancellationToken.None);
            Check(failed.Status == "failed" && failed.Clip is null && uncertain.Status == "unknown" && uncertain.Clip is null, "failed and uncertain saves are not represented as linked clips");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var interrupted = await collector.CollectAsync(Request(), TimeSpan.Zero, Save, cancelled.Token);
            Check(interrupted.Status == "failed" && interrupted.Message!.Contains("before"), "pre-save cancellation is reported accurately");
            Console.WriteLine("PASS shot sample transcript validation, durable clip linkage/recovery, overlap, failure and uncertainty");
        }
        finally { Directory.Delete(root, true); }
    }

    sealed class FixtureEffect : IClipSaveEffect
    {
        public int Calls;
        public Task<Clip> SaveAsync(Guid id, SaveClip command, CancellationToken cancellation)
        { Calls++; return Task.FromResult(Clip()); }
    }
}
