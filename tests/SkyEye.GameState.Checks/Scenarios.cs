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
    public static GameSession YellowPot(double pocketConfidence = .92)
    {
        var game = OpenTable();
        game = Apply(game, Input(new ShotStarted("shot"), 3));
        game = Apply(game, Input(new PossiblePocketEvent("shot", "Y3", Pocket.BottomRight), 4) with { Confidence = pocketConfidence });
        game = Apply(game, Input(new ShotEnded("shot"), 5));
        return Apply(game, Input(new TableObservation([new("R1", BallColour.Red, new(.2, .5), .9)], true), 6));
    }
    public static void PotStory()
    {
        var game = YellowPot();
        var pot = game.History.Single(e => e.Event is BallPottedEvent);
        Check(pot.Event is BallPottedEvent { BallId: "Y3", Pocket: Pocket.BottomRight, CueBall: false } p && p.Evidence.Length == 3,
            "When a yellow approaches a known pocket and disappears after the shot, the history cites before/trajectory/after");
        Check(game.State.Table.Remaining(BallColour.Yellow) == 0 && game.State.Frame!.Pots.Count == 1 && game.State.Frame.OpenTable,
            "Then table and visit reflect one inferred pot without inventing colour/rule decisions");
        Check(pot.Provenance == Provenance.Inferred && pot.Confidence == .9, "Pot retains conservative evidence confidence and inferred provenance");
        game = Apply(game, Input(new TableSettled(), 7));
        Check(game.History.Count(e => e.Event is BallPottedEvent) == 1, "Repeated settlement cannot pot the same ball twice");
        var uncertain = YellowPot(.3);
        Check(!uncertain.History.Any(e => e.Event is BallPottedEvent) && uncertain.State.Table.Remaining(BallColour.Yellow) is null,
            "Weak pocket evidence leaves a missing ball unresolved");
        var missing = Apply(Apply(OpenTable(), Input(new ShotStarted("shot"), 3)), Input(new ShotEnded("shot"), 4));
        missing = Apply(missing, Input(new TableObservation([new("R1", BallColour.Red, new(.2, .5), .9)], true), 5));
        Check(!missing.History.Any(e => e.Event is BallPottedEvent), "Disappearance alone is never a pot");
        var obscured = Apply(Apply(OpenTable(), Input(new ShotStarted("shot"), 3)), Input(new PossiblePocketEvent("shot", "Y3", Pocket.BottomRight), 4) with { Confidence = .9 });
        obscured = Apply(Apply(obscured, Input(new ShotEnded("shot"), 5)), Input(new TableObservation([], true, true), 6));
        Check(!obscured.History.Any(e => e.Event is BallPottedEvent) && obscured.State.Table.Remaining(BallColour.Red) is null, "Occlusion keeps the shot uncertain");
        Console.WriteLine("PASS yellow-pot story: before/pocket/after evidence, inferred visit/table facts, weak confidence, disappearance and obstruction");
    }
}
