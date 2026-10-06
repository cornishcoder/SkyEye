using SkyEye.Core.Application;
using SkyEye.GameState;

static class RulesApplicationChecks
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-rules-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameJournalStore(root); var app = new AppCoordinator(gameJournal: store);
            async Task<CommandReceipt> Manual(GameInput input)
            {
                var e = new GameInputEnvelope(Guid.NewGuid(), "match", "frame", DateTimeOffset.UnixEpoch, input, Provenance: Provenance.PlayerConfirmed);
                return await app.DispatchAsync(new(e.Id, CommandOrigin.Touch, app.State.Version, new SubmitGameInput(e, app.State.Game.Version)));
            }
            Check((await Manual(new StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3))).Succeeded, "Local match");
            Check((await Manual(new StartFrame("rich"))).Succeeded, "Local frame");
            CommandEnvelope Shot(DeclaredShot facts, Guid? replaces = null) => new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
                new DeclareShotOutcome("match", "frame", app.State.Game.Version, facts, replaces));
            var pot = Shot(new(false, DeclaredLegality.Legal, RedsPotted: 1, CallRequired: false, RedsBefore: 7, YellowsBefore: 7));
            Check((await app.DispatchAsync(pot)).Succeeded && app.State.Game.Frame!.CurrentPlayer.Value == "rich" && app.State.Advice.TargetGroup == AdviceTargetGroup.Red,
                "Declared legal red assigns group and continues");
            Check((await app.DispatchAsync(pot)).Duplicate && store.Load().History.Count(e => e.Event is RulesShotRecordedEvent) == 1, "Duplicate is durable without re-evaluating against new state");
            var miss = Shot(new(false, DeclaredLegality.Legal)); await app.DispatchAsync(miss);
            Check(app.State.Game.Frame!.CurrentPlayer.Value == "mike" && app.State.Advice.TargetGroup == AdviceTargetGroup.Yellow, "Miss changes player and targets");
            var foul = Shot(new(false, DeclaredLegality.StandardFoul, YellowsPotted: 1)); await app.DispatchAsync(foul);
            Check(app.State.Match.Rules?.CueBall == CueBallEntitlement.Anywhere && app.State.Game.Frame.CurrentPlayer.Value == "rich", "Foul projects next player's placement entitlement");
            var correction = Shot(new(false, DeclaredLegality.Legal, YellowsPotted: 1), foul.OperationId);
            Check((await app.DispatchAsync(correction)).Succeeded && app.State.Match.Rules?.CueBall == CueBallEntitlement.None
                && app.State.Game.Frame.CurrentPlayer.Value == "mike" && app.State.Match.Rules.Yellows == 6,
                "Replacing foul with legal yellow evaluates original before-state and repairs turn/entitlement/counts");
            Check((await Manual(new UndoDecision(correction.OperationId))).Succeeded && app.State.Match.Rules?.CueBall == CueBallEntitlement.Anywhere,
                "Undo correction restores the original ruling");
            var restored = new AppCoordinator(gameJournal: new GameJournalStore(root));
            Check(GameJson.WriteState(restored.State.Game) == GameJson.WriteState(app.State.Game) && !restored.State.GameTableCurrent,
                "Recorded interpretation, replacement and undo reconstruct exactly on restart");
            Check((await restored.DispatchAsync(foul)).Duplicate, "Duplicate survives process reconstruction");
            var pending = Shot(new(false, DeclaredLegality.Unknown, RedsPotted: 1)); await app.DispatchAsync(pending);
            Check(app.State.Match.Rules?.Pending == true, "Unknown shot leaves decision pending");
            var stale = Shot(new(false, DeclaredLegality.Legal)) with { Command = new DeclareShotOutcome("match", "frame", 0, new(false, DeclaredLegality.Legal)) };
            Check((await app.DispatchAsync(stale)).Outcome == CommandOutcome.Stale, "Stale game version cannot record another outcome");
            Check((await app.DispatchAsync(Shot(new(false, DeclaredLegality.Legal)) with { Origin = CommandOrigin.Vision })).Outcome == CommandOutcome.Rejected,
                "Perception cannot impersonate complete player legality");
            Console.WriteLine("PASS declared rules application: profile/events, groups/turns, entitlements, correction/undo, durable duplicates, restart and uncertainty");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                || !Path.GetFileName(root).StartsWith("skyeye-rules-")) throw new InvalidOperationException("Unsafe cleanup path.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    static void Check(bool yes, string message) { if (!yes) throw new Exception(message); }
}
