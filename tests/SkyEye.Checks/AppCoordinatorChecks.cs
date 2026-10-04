using SkyEye.Core.Application;

static class AppCoordinatorChecks
{
    public static async Task Run()
    {
        static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
        var observed = new List<long>(); int observerFailures = 0;
        var app = new AppCoordinator(_ => observerFailures++);
        using var observer = app.Subscribe(s => observed.Add(s.Version));
        using var brokenObserver = app.Subscribe(_ => throw new InvalidOperationException("fixture observer"));
        var original = app.State;
        var show = new CommandEnvelope(Guid.NewGuid(), CommandOrigin.Touch, 0, new ShowShotChoices(true));
        var shown = await app.DispatchAsync(show);
        Check(shown.Succeeded && shown.DecisionVersion == 1 && app.State.Presentation.ShotChoicesEnabled, "touch preference commit");
        Check(!original.Presentation.ShotChoicesEnabled && original.Version == 0, "previous snapshot is immutable");
        var group = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Sky, 1, new SetTargetGroup(AdviceTargetGroup.Red)));
        Check(group.Succeeded && app.State.Version == 2 && app.State.Advice.TargetGroup == AdviceTargetGroup.Red, "Sky uses the same writer");
        var duplicate = await app.DispatchAsync(show);
        Check(duplicate.Duplicate && duplicate.DecisionVersion == 1 && app.State.Version == 2, "duplicate receipt cannot roll back current state");
        var conflict = await app.DispatchAsync(show with { Command = new ShowShotChoices(false) });
        Check(conflict.Outcome == CommandOutcome.Conflict && app.State.Version == 2, "operation ID cannot change its payload");
        var stale = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Sky, 1, new ShowShotChoices(false)));
        Check(stale.Outcome == CommandOutcome.Stale && app.State.Presentation.ShotChoicesEnabled, "stale request cannot mutate state");
        var unchanged = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 2, new ShowShotChoices(true)));
        Check(unchanged.Outcome == CommandOutcome.Unchanged && app.State.Version == 2, "idempotent preference does not create a new version");
        var invalid = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 2, new SetTargetGroup((AdviceTargetGroup)99)));
        Check(invalid.Outcome == CommandOutcome.Rejected && app.State.Version == 2, "invalid group rejected");
        Check(observed.SequenceEqual([1L, 2L]) && observerFailures == 2, "ordered notifications and observer failure isolation");
        observer.Dispose(); brokenObserver.Dispose();
        await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 2, new ShowShotChoices(false)));
        Check(observed.Count == 2 && app.State.Version == 3, "unsubscribe removes listener");

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 3, new ShowShotChoices(true)), cancelled.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { }
        Check(app.State.Version == 3, "cancel before commit preserves canonical state");

        var racing = new AppCoordinator();
        var competing = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => racing.DispatchAsync(
            new(Guid.NewGuid(), CommandOrigin.Touch, 0, new ShowShotChoices(true))))));
        Check(competing.Count(r => r.Outcome == CommandOutcome.Applied) == 1 && competing.Count(r => r.Outcome == CommandOutcome.Stale) == 23
            && racing.State.Version == 1, "concurrent requests serialize without lost updates");
        Console.WriteLine("PASS canonical immutable preferences, touch/Sky ordering, stale/conflicting/duplicate requests, notifications and cancellation");
    }
}
