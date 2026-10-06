using System.Collections.Immutable;
using SkyEye.GameState;

static class ManualMatchChecks
{
    public static void Run()
    {
        var game = GameSession.Empty;
        GameInputEnvelope Input(GameInput input, string frame = "one", string match = "evening")
            => new(Guid.NewGuid(), match, frame, DateTimeOffset.UnixEpoch, input, Provenance: Provenance.PlayerConfirmed);
        GameSession Apply(GameInputEnvelope input) { var result = game.Accept(input); Scenarios.Check(result.Accepted, result.Message); return game = result.Session; }
        Apply(Input(new StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3)));
        Apply(Input(new StartFrame("rich")));
        var firstWin = Input(new PlayerCorrection(new SetFrameWinner("rich")));
        Apply(firstWin);
        Apply(Input(new StartFrame("mike"), "two"));
        Apply(Input(new PlayerCorrection(new SetFrameWinner("rich")), "two"));
        Scenarios.Check(game.State.Match!.Score("rich") == 2 && game.State.Match.Frames.Length == 2, "Rich wins two declared frames without resetting score");
        Apply(Input(new PlayerCorrection(new SetFrameWinner("mike"))));
        Scenarios.Check(game.State.Match.Score("rich") == 1 && game.State.Match.Score("mike") == 1 && game.State.Frame!.Id == "two"
            && game.State.Frame.Winner!.Value == "rich", "Correcting the first frame gives 1-1 without overwriting current frame");
        Scenarios.Check(game.Accept(firstWin).Outcome == InputOutcome.Duplicate, "Earlier winner ID remains duplicate after a correction");
        var restored = GameSession.Restore(game.History);
        Scenarios.Check(GameJson.WriteState(restored.State) == GameJson.WriteState(game.State), "Frame ledger and corrected scores survive event reconstruction");
        Scenarios.Check(!game.Accept(Input(new StartMatch([new("a", "A"), new("b", "B")], 2), match: "replacement")).Accepted,
            "An unfinished match cannot be replaced silently");
        Apply(Input(new FinishMatch(), "two"));
        Apply(Input(new StartMatch([new("a", "Alice"), new("b", "Bob")], 2), match: "next-match"));
        Apply(Input(new StartFrame("a"), match: "next-match"));
        Scenarios.Check(game.State.Archives.Single().Match.Score("rich") == 1 && game.State.Archives.Single().Match.Score("mike") == 1
            && game.State.Match!.Score("a") == 0, "Starting another match retains the completed ledger and resets only the new match");
        var groups = Input(new DeclareColours("a", BallColour.Red), match: "next-match");
        Apply(groups);
        Scenarios.Check(game.State.Frame!.Colours["a"].Value == BallColour.Red && game.State.Frame.Colours["b"].Value == BallColour.Yellow,
            "One explicit group declaration sets complementary colours for both players");
        Apply(Input(new UndoDecision(groups.Id), match: "next-match"));
        Scenarios.Check(game.State.Frame.OpenTable && game.History.Any(e => e.InputId == groups.Id), "Undo restores open table while preserving the original decision");
        Scenarios.Check(!game.Accept(Input(new UndoDecision(groups.Id), match: "next-match")).Accepted, "A decision cannot be undone twice");
        var turn = Input(new PlayerCorrection(new ChangeTurn("b")), match: "next-match");
        Apply(turn);
        Apply(Input(new PlayerCorrection(new SetFoul(true)), match: "next-match"));
        Apply(Input(new UndoDecision(turn.Id), match: "next-match"));
        Scenarios.Check(game.State.Frame.CurrentPlayer.Value == "a" && game.State.Frame.Foul!.Value == true,
            "Undo of a turn preserves later explicitly declared facts");
        var award = Input(new PlayerCorrection(new SetFrameWinner("a")), match: "next-match");
        Apply(award);
        Apply(Input(new StartFrame("b"), "next-frame", "next-match"));
        Apply(Input(new UndoDecision(award.Id), match: "next-match"));
        Scenarios.Check(game.State.Match!.Score("a") == 0 && game.State.Frame!.Id == "next-frame"
            && game.State.Match.Frames[0].Winner is null && game.State.Match.Frames[0].Closed, "Undo an earlier result without deleting the later frame");
        Scenarios.Check(GameJson.WriteState(GameSession.Restore(game.History).State) == GameJson.WriteState(game.State), "Groups and undo reconstruct through the same journal codec");
        Console.WriteLine("PASS manual match ledger: two frames, earlier winner correction, duplicate, rebuild, explicit finish and retained match archive");
    }
}
