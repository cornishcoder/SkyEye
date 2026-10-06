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
        Console.WriteLine("PASS manual match ledger: two frames, earlier winner correction, duplicate, rebuild, explicit finish and retained match archive");
    }
}
