using System.Collections.Immutable;
using System.Text.Json;
using G = SkyEye.GameState;

static class SettledOutcomeReviewChecks
{
    public static void Run()
    {
        var source = new G.EvidenceSource("camera", "timeline", 1, "calibration", "connection");
        var beforeAt = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var before = new G.SettledTableEvidence(Guid.NewGuid(), source, 200, beforeAt,
            new(1, 1, 1, 1), Settled: true, Clear: true)
        { StableClearSampleCount = 3, BoundedStillnessVerified = true };
        var game = G.GameSession.Empty;
        game = Apply(game, Input(game, new G.StartMatch([new("rich", "Rich"), new("guest", "Guest")], 3), 1,
            provenance: G.Provenance.PlayerConfirmed));
        game = Apply(game, Input(game, new G.StartFrame("rich"), 2, provenance: G.Provenance.PlayerConfirmed));
        game = Apply(game, Input(game, new G.SelectSource(), 3, source, 100));
        game = Apply(game, Input(game, new G.EstablishSettledBaseline(1, before), 4, source, 200));
        Check(G.SettledTableEvidence.TryCapture(game.State, out var trusted) && trusted.Counts == before.Counts,
            "The review fixture starts from a trusted identity-free baseline.");
        Check(G.ActivityWindowContext.TryBegin(Guid.NewGuid(), game.State, 250, out var context),
            "Freeze the match and source context before activity.");
        var after = new G.SettledTableEvidence(Guid.NewGuid(), source, 300, beforeAt.AddSeconds(1),
            new(1, 0, 1, 1), Settled: true, Clear: true)
        { StableClearSampleCount = 3, BoundedStillnessVerified = true };
        var comparison = G.SettledStateReconciler.Compare(context, after).Comparison;
        Check(comparison?.Recommendation is { Kind: "settled-pot-outcome", AutoApprovalEligible: true },
            "A bounded clear decrease is eligible for the settled pot/scratch policy.");
        Check(SkyEye.Core.Rules.SettledOutcomeRuleInference.TryInfer(game.State, comparison, out var yellowRuling)
            && yellowRuling.Facts is { YellowsPotted: 1, FirstContact: G.BallColour.Yellow, CallRequired: false }
            && yellowRuling.Position is { Pending: false, Player: "rich", PlayerGroup: G.BallColour.Yellow, Target: G.LegalTarget.Black },
            "An open-table single-group decrease is interpreted through the rules engine and assigns the group.");

        var openTableAdvice = SkyEye.Core.Rules.SettledOutcomeRulesAdvisor.Evaluate(game.State,
            G.SettledStateReconciler.Compare(context, after).Comparison);
        Check(openTableAdvice.Status == "conditional" && openTableAdvice.Branches.Length == 3
            && openTableAdvice.Branches.Single(branch => branch.Id == "legal").Pending
            && openTableAdvice.FactsToConfirm.Any(fact => fact.Contains("group call", StringComparison.OrdinalIgnoreCase)),
            "Open-table rule previews keep turn assignment pending until call and contact facts are declared.");

        var groupedGame = Apply(game, Input(game, new G.DeclareColours("rich", G.BallColour.Red), 5,
            provenance: G.Provenance.PlayerConfirmed));
        Check(G.ActivityWindowContext.TryBegin(Guid.NewGuid(), groupedGame.State, 250, out var groupedContext),
            "Freeze a known group context for conditional rule previews.");
        var groupedAfter = after with { EvidenceId = Guid.NewGuid(), SourceTimeUs = 350,
            CapturedAt = after.CapturedAt.AddSeconds(1), Counts = new(0, 1, 1, 1) };
        var groupedComparison = G.SettledStateReconciler.Compare(groupedContext, groupedAfter).Comparison;
        var groupedVersionBeforeAdvice = groupedGame.State.Version;
        var groupedAdvice = SkyEye.Core.Rules.SettledOutcomeRulesAdvisor.Evaluate(groupedGame.State, groupedComparison);
        Check(groupedAdvice.Branches.Single(branch => branch.Id == "legal") is { Pending: false, NextPlayer: "Rich", Target: G.LegalTarget.Black }
            && groupedAdvice.Branches.Single(branch => branch.Id == "foul") is { Pending: false, NextPlayer: "Guest", CueBall: G.CueBallEntitlement.Anywhere }
            && groupedAdvice.Branches.Single(branch => branch.Id == "loss-of-turn") is { Pending: false, NextPlayer: "Guest", CueBall: G.CueBallEntitlement.None }
            && groupedAdvice.FactsToConfirm.Any(fact => fact.Contains("legality", StringComparison.OrdinalIgnoreCase))
            && groupedGame.State.Version == groupedVersionBeforeAdvice,
            "Grouped-shot previews use conditional rules branches without mutating canonical state.");

        var acceptedInput = Input(game, new G.AcceptSettledInventory(1, context, after), 5, source, 300,
            G.Provenance.PlayerConfirmed);
        var acceptedResult = game.Accept(acceptedInput);
        Check(acceptedResult.Accepted && acceptedResult.Session.State.Table.Remaining(G.BallColour.Yellow) == 0
            && acceptedResult.Session.State.Frame!.Pots.Count == 0 && acceptedResult.Session.State.Frame.CurrentPlayer.Value == "rich",
            "Accept updates observed group counts without inventing pot identity, score or turn.");
        var acceptedReload = Reload(acceptedResult.Session);
        Check(G.SettledTableEvidence.TryCapture(acceptedReload.State, out var acceptedBaseline)
            && acceptedBaseline.EvidenceId == after.EvidenceId && acceptedBaseline.Counts == after.Counts,
            "Accepted B becomes the durable next baseline after reload.");

        var potInput = Input(game, new G.AcceptSettledOutcome(1, context, after, yellowRuling), 5, source, 300,
            G.Provenance.PlayerConfirmed);
        var potResult = game.Accept(potInput);
        Check(potResult.Accepted && potResult.Session.State.Table.Remaining(G.BallColour.Yellow) == 0
            && potResult.Session.State.Frame!.Colours["rich"].Value == G.BallColour.Yellow
            && potResult.Session.State.Frame.Rules is { Pending: false, Player: "rich" }
            && potResult.Session.State.Table is { Motion: G.TableMotion.Settled, Uncertain: false }
            && potResult.Session.History.Count(item => item.InputId == potInput.Id) == 2,
            "Pot assumption atomically updates counts, inferred group/rules state and keeps the verified settled table current.");
        var potReload = Reload(potResult.Session);
        var potUndone = Apply(potReload, Input(potReload, new G.UndoDecision(potInput.Id), 6,
            provenance: G.Provenance.PlayerCorrected));
        Check(potUndone.State.Table.Remaining(G.BallColour.Yellow) == 1
            && potUndone.State.Frame!.OpenTable && potUndone.State.Frame.Rules is null
            && potUndone.State.Table.LastSettled?.EvidenceId == before.EvidenceId,
            "One undo restores the pre-pot inventory, open table and baseline after reload.");

        var labAfter = after with
        {
            EvidenceId = Guid.NewGuid(),
            SourceTimeUs = 350,
            CapturedAt = after.CapturedAt.AddSeconds(1),
            BoundedStillnessVerified = false,
            StableClearSampleCount = 3
        };
        var labContext = G.ActivityWindowContext.TryBegin(Guid.NewGuid(), game.State, 250, out var frozenLabContext)
            ? frozenLabContext : throw new Exception("Freeze the isolated three-sample camera result.");
        var labComparison = G.SettledStateReconciler.Compare(labContext, labAfter).Comparison
            ?? throw new Exception("The isolated clear-sample count change should reconcile.");
        bool labRulingInferred = SkyEye.Core.Rules.SettledOutcomeRuleInference.TryInfer(game.State, labComparison, out var labRuling);
        Check(labComparison.Recommendation.AutoApprovalEligible && labRulingInferred,
            "Three fresh matching clear samples make an isolated settled decrease eligible for automatic pot handling.");
        var labPotInput = Input(game, new G.AcceptSettledOutcome(1, labContext, labAfter, labRuling), 6,
            source, 350, G.Provenance.Inferred) with { Confidence = .9 };
        var labPotResult = game.Accept(labPotInput);
        var labReload = labPotResult.Accepted ? Reload(labPotResult.Session) : game;
        Check(labPotResult.Accepted && labReload.State.Table.Remaining(G.BallColour.Yellow) == 0
            && labReload.State.Frame!.Colours["rich"].Value == G.BallColour.Yellow
            && labReload.History.Any(item => item.InputId == labPotInput.Id
                && item.Event is G.SettledInventoryAcceptedEvent)
            && G.GameSession.CanUndo(labReload.History.Single(item => item.InputId == labPotInput.Id
                && item.Event is G.RulesShotRecordedEvent)),
            "An isolated three-clear-sample decrease applies its pot and rules outcome automatically, and is undoable after reload.");
        Check(!System.Text.Encoding.UTF8.GetString(G.GameJson.WriteHistory(labReload.History))
                .Contains("HasBoundedClearStillnessProof", StringComparison.OrdinalIgnoreCase),
            "The derived stillness helper stays out of journal serialization and preserves stored checksums.");

        var blackGame = BaselineGame(source, new(0, 1, 1, 1), beforeAt);
        blackGame = Apply(blackGame, Input(blackGame, new G.DeclareColours("rich", G.BallColour.Red), 5,
            provenance: G.Provenance.PlayerConfirmed));
        Check(G.ActivityWindowContext.TryBegin(Guid.NewGuid(), blackGame.State, 250, out var blackContext),
            "Freeze the cleared-group match before a black-ball change.");
        var blackAfter = after with { EvidenceId = Guid.NewGuid(), SourceTimeUs = 300,
            CapturedAt = beforeAt.AddSeconds(2), Counts = new(0, 1, 1, 0) };
        var blackComparison = G.SettledStateReconciler.Compare(blackContext, blackAfter).Comparison;
        Check(SkyEye.Core.Rules.SettledOutcomeRuleInference.TryInfer(blackGame.State, blackComparison, out var blackRuling)
            && blackRuling.Facts.BlackPotted && blackRuling.Position is { Pending: false, Winner: "rich" },
            "A settled black-ball disappearance on the cleared group wins the frame through the rules engine.");
        var blackInput = Input(blackGame, new G.AcceptSettledOutcome(1, blackContext, blackAfter, blackRuling), 6,
            source, 300, G.Provenance.Inferred) with { Confidence = .9 };
        var blackResult = blackGame.Accept(blackInput);
        var blackReload = blackResult.Accepted ? Reload(blackResult.Session) : blackGame;
        Check(blackResult.Accepted && blackReload.State.Match!.Score("rich") == 1
            && blackReload.State.Frame!.Winner!.Value == "rich"
            && blackReload.State.Table.Remaining(G.BallColour.Black) == 0
            && blackReload.History.Any(item => item.InputId == blackInput.Id && item.Event is G.RulesShotRecordedEvent)
            && G.GameSession.CanUndo(blackReload.History.Single(item => item.InputId == blackInput.Id
                && item.Event is G.RulesShotRecordedEvent)),
            "An inferred black pot updates the frame/match score, survives reload and is undoable.");

        var scratchGame = BaselineGame(source, new(1, 1, 1, 1), beforeAt);
        scratchGame = Apply(scratchGame, Input(scratchGame, new G.DeclareColours("rich", G.BallColour.Red), 5,
            provenance: G.Provenance.PlayerConfirmed));
        Check(G.ActivityWindowContext.TryBegin(Guid.NewGuid(), scratchGame.State, 250, out var scratchContext),
            "Freeze the grouped table before a cue-ball disappearance.");
        var scratchAfter = after with { EvidenceId = Guid.NewGuid(), SourceTimeUs = 300,
            CapturedAt = beforeAt.AddSeconds(2), Counts = new(1, 1, 0, 1) };
        var scratchComparison = G.SettledStateReconciler.Compare(scratchContext, scratchAfter).Comparison;
        Check(SkyEye.Core.Rules.SettledOutcomeRuleInference.TryInfer(scratchGame.State, scratchComparison, out var scratchRuling)
            && scratchRuling.Facts.CueBallPotted
            && scratchRuling.Position is { Pending: false, Player: "guest", Foul: true, CueBall: G.CueBallEntitlement.Anywhere },
            "A missing cue ball is inferred as a scratch and the rules engine awards cue-in-hand to the opponent.");

        var correctionInput = Input(game, new G.CorrectSettledInventory(1, context, after,
            new(1, 1, 1, 1)), 5, source, 300, G.Provenance.PlayerConfirmed);
        var correctedResult = game.Accept(correctionInput);
        var correctionEvent = correctedResult.Session.History.Single(item => item.InputId == correctionInput.Id
            && item.Event is G.SettledInventoryCorrectedEvent);
        Check(correctedResult.Accepted && correctedResult.Session.State.Table.Remaining(G.BallColour.Yellow) == 1
            && correctedResult.Session.State.Table.LastSettled?.SourceTimeUs == after.SourceTimeUs
            && correctedResult.Session.State.Table.LastSettled?.At == after.CapturedAt
            && correctedResult.Session.State.Table.LastSettled?.EvidenceId == correctionInput.Id
            && ((G.SettledInventoryCorrectedEvent)correctionEvent.Event).Observed == after,
            "Count correction keeps B's source time, gives corrected counts their own baseline ID, and retains exact detector evidence.");
        var correctedReload = Reload(correctedResult.Session);
        Check(correctedReload.State.Table.Remaining(G.BallColour.Yellow) == 1
            && correctedReload.State.Table.GroupCounts[G.BallColour.Yellow].Source == G.Provenance.PlayerCorrected
            && correctedReload.State.Table.LastSettled?.EvidenceId == correctionInput.Id,
            "Corrected counts and evidence provenance rebuild identically after reload.");
        var malformedCorrection = correctedResult.Session.History.Select(item => item.InputId == correctionInput.Id
            ? item with { Event = ((G.SettledInventoryCorrectedEvent)item.Event) with { Observed = null } } : item).ToArray();
        Throws(() => G.GameSession.Restore(malformedCorrection), "Evidence-linked corrections require both frozen context and detector evidence.");

        var rejectedInput = Input(game, new G.RejectSettledInventory(1, context, after), 5, source, 300,
            G.Provenance.PlayerConfirmed);
        var rejectedResult = game.Accept(rejectedInput);
        Check(rejectedResult.Accepted && rejectedResult.Session.State.Table.Remaining(G.BallColour.Yellow) == 1
            && rejectedResult.Session.State.Table.LastSettled?.EvidenceId == before.EvidenceId
            && rejectedResult.Session.History.Any(item => item.Event is G.SettledInventoryRejectedEvent rejection
                && rejection.Context.Before.EvidenceId == before.EvidenceId && rejection.After.Counts == after.Counts),
            "Reject is journalled for duplicate suppression while canonical inventory remains unchanged.");
        var rejectedReload = Reload(rejectedResult.Session);
        Check(rejectedReload.State.Table.Remaining(G.BallColour.Yellow) == 1
            && rejectedReload.History.Any(item => item.Event is G.SettledInventoryRejectedEvent),
            "Rejected evidence remains available after reload without changing the match.");
        var undone = Apply(rejectedReload, Input(rejectedReload, new G.UndoDecision(rejectedInput.Id), 6,
            provenance: G.Provenance.PlayerConfirmed));
        Check(G.GameRevisions.Suppressed(undone.History).Contains(rejectedInput.Id),
            "A player can undo a rejected review to make it eligible again.");
        Console.WriteLine("PASS live settled outcome review: accept, correction with retained evidence, durable rejection and undo");
    }

    static G.GameInputEnvelope Input(G.GameSession game, G.GameInput input, int second,
        G.EvidenceSource? source = null, long? pts = null, G.Provenance provenance = G.Provenance.Observed)
        => new(Guid.NewGuid(), game.State.Match?.Id ?? "match", game.State.Frame?.Id ?? "frame",
            DateTimeOffset.UnixEpoch.AddMinutes(1).AddSeconds(second), input, source, pts, provenance);

    static G.GameSession BaselineGame(G.EvidenceSource source, G.SettledTableCounts counts, DateTimeOffset capturedAt)
    {
        var game = G.GameSession.Empty;
        game = Apply(game, Input(game, new G.StartMatch([new("rich", "Rich"), new("guest", "Guest")], 3), 1,
            provenance: G.Provenance.PlayerConfirmed));
        game = Apply(game, Input(game, new G.StartFrame("rich"), 2, provenance: G.Provenance.PlayerConfirmed));
        game = Apply(game, Input(game, new G.SelectSource(), 3, source, 100));
        var evidence = new G.SettledTableEvidence(Guid.NewGuid(), source, 200, capturedAt, counts,
            Settled: true, Clear: true) { BoundedStillnessVerified = true, StableClearSampleCount = 3 };
        return Apply(game, Input(game, new G.EstablishSettledBaseline(1, evidence), 4, source, 200));
    }

    static G.GameSession Apply(G.GameSession game, G.GameInputEnvelope input)
    {
        var result = game.Accept(input);
        Check(result.Accepted, result.Message);
        return result.Session;
    }

    static G.GameSession Reload(G.GameSession game)
    {
        var bytes = G.GameJson.WriteHistory(game.History);
        var events = JsonSerializer.Deserialize(bytes, G.GameJsonContext.Default.ImmutableArrayEventEnvelope);
        return G.GameSession.Restore(events);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static void Throws(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception(message);
    }
}
