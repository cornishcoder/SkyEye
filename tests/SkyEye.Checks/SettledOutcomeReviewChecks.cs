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
        Check(G.SettledStateReconciler.Compare(context, after).Comparison?.Recommendation.AutoApprovalEligible == true,
            "The fixture is a strongly supported likely-pot recommendation, not a canonical pot.");

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
