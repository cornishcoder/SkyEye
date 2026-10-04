using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Application;

static class ClipSaveChecks
{
    static readonly ClipSaveScope Scope = new(new string('A', 64), Guid.NewGuid().ToString("N"), 1, 4);
    static CommandEnvelope Request(int seconds = 30, long version = 0) => new(Guid.NewGuid(), CommandOrigin.Touch, version, new SaveClip(seconds, Scope));
    static Clip FixtureClip() => new(Guid.NewGuid().ToString("N"), "Fixture clip", 29.8, 1234, new string('B', 64), DateTimeOffset.UtcNow);
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-save-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var effect = new FixtureEffect((_, _, _) => Task.FromResult(FixtureClip()));
            var journal = new ClipSaveExecutor(Path.Combine(root, "success"), effect);
            var app = new AppCoordinator(clipSaves: journal);
            var request = Request();
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => app.DispatchAsync(request)));
            Check(effect.Calls == 1 && results.Count(r => !r.Duplicate) == 1 && results.All(r => r.Succeeded), "concurrent duplicate saves execute once");
            Check(results.All(r => r.SavedClip?.Id == results[0].SavedClip?.Id) && results[0].SavedClip is not null, "duplicate returns same acknowledged clip");
            await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 0, new SetTargetGroup(AdviceTargetGroup.Red)));
            var retry = await app.DispatchAsync(request);
            Check(retry.Duplicate && retry.DecisionVersion == 0 && app.State.Version == 1 && app.State.Advice.TargetGroup == AdviceTargetGroup.Red, "old receipt never rolls back current preferences");
            var restarted = new AppCoordinator(clipSaves: new ClipSaveExecutor(Path.Combine(root, "success"), effect));
            Check((await restarted.DispatchAsync(request)).Duplicate && effect.Calls == 1, "restart replays durable outcome without camera call");
            Check((await restarted.DispatchAsync(request with { Command = new SaveClip(60, Scope) })).Outcome == CommandOutcome.Conflict, "save ID binds duration");
            Check((await restarted.DispatchAsync(request with { Command = new SaveClip(30, Scope with { HostGeneration = 2 }) })).Outcome == CommandOutcome.Conflict, "save ID binds host/session scope");
            Check((await restarted.DispatchAsync(request with { Command = new ShowShotChoices(true) })).Outcome == CommandOutcome.Conflict, "save ID cannot be reused by a preference");
            var invalid = await app.DispatchAsync(Request(31, 1));
            Check(invalid.Outcome == CommandOutcome.Rejected && effect.Calls == 1, "unsupported duration cannot save");
            var staleRequest = Request(version: 0);
            Check((await app.DispatchAsync(staleRequest)).Outcome == CommandOutcome.Stale && effect.Calls == 1, "stale admission cannot save");
            Check((await restarted.DispatchAsync(staleRequest)).Outcome == CommandOutcome.Stale && effect.Calls == 1, "stale refusal is durable even when new app version matches");
            var preferenceRequest = new CommandEnvelope(Guid.NewGuid(), CommandOrigin.Touch, 1, new ShowShotChoices(true));
            await app.DispatchAsync(preferenceRequest);
            Check((await app.DispatchAsync(preferenceRequest with { Command = new SaveClip(30, Scope) })).Outcome == CommandOutcome.Conflict, "recent preference ID cannot become a save");

            var failedEffect = new FixtureEffect((_, _, _) => throw new ClipSaveRefusedException("No recording history is available."));
            var failedPath = Path.Combine(root, "failure"); var failedRequest = Request();
            var failure = await new AppCoordinator(clipSaves: new(failedPath, failedEffect)).DispatchAsync(failedRequest);
            var failedRetry = await new AppCoordinator(clipSaves: new(failedPath, failedEffect)).DispatchAsync(failedRequest);
            Check(failure.Outcome == CommandOutcome.Failed && !failure.Succeeded && failedRetry.Duplicate && failedRetry.Outcome == CommandOutcome.Failed && failedEffect.Calls == 1, "known failed save remains failed across restart");

            var ambiguousEffect = new FixtureEffect((_, _, _) => throw new HttpRequestException("fixture lost acknowledgement"));
            var ambiguousPath = Path.Combine(root, "uncertain"); var ambiguousRequest = Request();
            var uncertain = await new AppCoordinator(clipSaves: new(ambiguousPath, ambiguousEffect)).DispatchAsync(ambiguousRequest);
            var uncertainRetry = await new AppCoordinator(clipSaves: new(ambiguousPath, ambiguousEffect)).DispatchAsync(ambiguousRequest);
            Check(uncertain.Outcome == CommandOutcome.Uncertain && uncertainRetry.Duplicate && ambiguousEffect.Calls == 1, "lost acknowledgement is never retried");

            var interruptedPath = Path.Combine(root, "interrupted"); Directory.CreateDirectory(interruptedPath); var interrupted = Request();
            var pendingRecord = new { schema = 1, interrupted.OperationId, interrupted.Origin, interrupted.ExpectedVersion, command = (SaveClip)interrupted.Command, decisionVersion = 0, receipt = (CommandReceipt?)null };
            File.WriteAllText(Path.Combine(interruptedPath, interrupted.OperationId.ToString("N") + ".json"), JsonSerializer.Serialize(pendingRecord, JsonDefaults.Options));
            Check((await new AppCoordinator(clipSaves: new(interruptedPath, effect)).DispatchAsync(interrupted)).Outcome == CommandOutcome.Uncertain && effect.Calls == 1, "crash after reservation cannot resend");
            File.WriteAllText(Path.Combine(interruptedPath, interrupted.OperationId.ToString("N") + ".json"), "{corrupt");
            Check((await new AppCoordinator(clipSaves: new(interruptedPath, effect)).DispatchAsync(interrupted)).Outcome == CommandOutcome.Uncertain && effect.Calls == 1, "corrupt journal fails closed");

            using var preCancelled = new CancellationTokenSource(); preCancelled.Cancel();
            var cancelPath = Path.Combine(root, "cancel-before");
            try { await new AppCoordinator(clipSaves: new(cancelPath, effect)).DispatchAsync(Request(), preCancelled.Token); throw new Exception("cancel ignored"); }
            catch (OperationCanceledException) { }
            Check(effect.Calls == 1 && !Directory.EnumerateFiles(cancelPath, "*.json").Any(), "cancel before admission creates no save or reservation");

            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var heldEffect = new FixtureEffect(async (_, _, ct) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, ct); return FixtureClip(); });
            var heldPath = Path.Combine(root, "cancel-after"); var heldRequest = Request();
            var heldApp = new AppCoordinator(clipSaves: new(heldPath, heldEffect));
            var heldSave = heldApp.DispatchAsync(heldRequest, cancellation.Token); await entered.Task;
            var preference = await heldApp.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 0, new ShowShotChoices(true))).WaitAsync(TimeSpan.FromSeconds(2));
            Check(preference.Succeeded && heldApp.State.Version == 1, "slow save does not block preference commands");
            cancellation.Cancel();
            Check((await heldSave).Outcome == CommandOutcome.Uncertain && (await new AppCoordinator(clipSaves: new(heldPath, heldEffect)).DispatchAsync(heldRequest)).Duplicate
                && heldEffect.Calls == 1, "cancel after reservation retains uncertainty and cannot repeat effect");

            var malformedEffect = new FixtureEffect((_, _, _) => Task.FromResult(FixtureClip() with { Duration = double.NaN }));
            Check((await new AppCoordinator(clipSaves: new(Path.Combine(root, "invalid-ack"), malformedEffect)).DispatchAsync(Request())).Outcome == CommandOutcome.Uncertain, "invalid acknowledgement is not reported saved");
            var fullPath = Path.Combine(root, "bounded"); var bounded = new AppCoordinator(clipSaves: new(fullPath, effect, capacity: 1));
            var boundedRequest = Request(); await bounded.DispatchAsync(boundedRequest); int before = effect.Calls;
            Check((await bounded.DispatchAsync(Request())).Outcome == CommandOutcome.Failed && effect.Calls == before && (await bounded.DispatchAsync(boundedRequest)).Duplicate, "full journal rejects new saves without discarding replay protection");
            var lockedPath = Path.Combine(root, "locked"); var locked = new AppCoordinator(clipSaves: new(lockedPath, effect));
            using (var owner = new FileStream(Path.Combine(lockedPath, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Check((await locked.DispatchAsync(Request())).Outcome == CommandOutcome.Uncertain && effect.Calls == before, "another process writer prevents dispatch");
            Console.WriteLine("PASS durable clip save deduplication/restart, scope/conflicts, bounds, stale/failed/uncertain outcomes, crash/cancellation and concurrent preferences");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
    sealed class FixtureEffect(Func<Guid, SaveClip, CancellationToken, Task<Clip>> save) : IClipSaveEffect
    {
        public int Calls;
        public Task<Clip> SaveAsync(Guid operationId, SaveClip command, CancellationToken cancellation)
        { Interlocked.Increment(ref Calls); return save(operationId, command, cancellation); }
    }
}
