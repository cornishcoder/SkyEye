using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.GameState;

static class Scenarios
{
    public static EvidenceSource Source = new("camera", "timeline", 1, "calibration");
    public static GameInputEnvelope Input(GameInput input, int second, EvidenceSource? source = null, Provenance provenance = Provenance.Observed)
        => new(Guid.NewGuid(), "match", "frame", DateTimeOffset.UnixEpoch.AddSeconds(second), input, source ?? Source, second * 1_000_000L, provenance);
    public static GameSession OpenTable()
    {
        var game = GameSession.Empty;
        game = Apply(game, Input(new StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3), 0, provenance: Provenance.PlayerConfirmed));
        game = Apply(game, Input(new StartFrame("rich"), 1, provenance: Provenance.PlayerConfirmed));
        return Apply(game, Input(new TableObservation([new("Y3", BallColour.Yellow, new(.4, .5), .9), new("R1", BallColour.Red, new(.2, .5), .9)], true), 2));
    }
    public static GameSession Apply(GameSession game, GameInputEnvelope input)
    {
        var result = game.Accept(input); Check(result.Accepted, result.Message); return result.Session;
    }
    public static void Ordering()
    {
        var game = OpenTable();
        Check(game.State.Frame!.OpenTable && game.State.Frame.CurrentPlayer.Value == "rich" && game.State.Table.Remaining(BallColour.Yellow) == 1, "Given Rich on an open settled table");
        var start = Input(new ShotStarted("shot"), 3);
        game = Apply(game, start);
        Check(game.State.Table.Motion == TableMotion.Moving, "Motion changes the canonical table");
        Check(game.Accept(start).Outcome == InputOutcome.Duplicate, "Duplicate evidence cannot append twice");
        Check(game.Accept(start with { Input = new ShotStarted("different") }).Outcome == InputOutcome.Conflict, "Duplicate ID with changed payload conflicts");
        Check(game.Accept(Input(new TableObservation([], true), 1)).Outcome == InputOutcome.Stale, "Older observation cannot overwrite current balls");
        Check(game.Accept(Input(new TableObservation([], true), 4, Source with { Timeline = "other" })).Outcome == InputOutcome.Stale, "Wrong timeline cannot mutate the frame");
        game = Apply(game, Input(new ShotEnded("shot"), 4));
        game = Apply(game, Input(new TableSettled(), 5));
        var json = JsonSerializer.Serialize(game.History);
        Check(JsonSerializer.Serialize(GameSession.Restore(JsonSerializer.Deserialize<ImmutableArray<EventEnvelope>>(json)).State) == JsonSerializer.Serialize(game.State), "Ordered history reconstructs deterministically");
        game = Apply(game, Input(new SelectSource(), 6, Source with { Timeline = "new", Generation = 2 }));
        Check(game.State.Match!.Id == "match" && game.State.Table.Motion == TableMotion.Unknown && game.State.Table.LastSettled is null, "Source change preserves match but invalidates live facts");
        Check(game.Accept(Input(new TableObservation([], true), 7)).Outcome == InputOutcome.Stale, "Previous source cannot return late");
        Console.WriteLine("PASS event admission/reduction: open frame, shot, duplicates/conflicts, timestamps, timelines and deterministic reconstruction");
    }
    public static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
