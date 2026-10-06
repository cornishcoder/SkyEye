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
        Check(GameJson.WriteState(GameSession.Restore(JsonSerializer.Deserialize<ImmutableArray<EventEnvelope>>(json)).State) == GameJson.WriteState(game.State), "Ordered history reconstructs deterministically");
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
    public static void Corrections()
    {
        var game = YellowPot();
        var original = game.History.Single(e => e.Event is BallPottedEvent);
        game = Apply(game, Input(new PlayerCorrection(new SetBallPresence("Y3", BallPresence.OnTable, original.EventId)), 7));
        Check(game.State.Table.Remaining(BallColour.Yellow) == 1 && game.State.Frame!.Pots.Count == 0,
            "Player correction repairs the inferred pot, remaining count and visit outcome");
        Check(game.State.Table.Balls["Y3"].Presence.Source == Provenance.PlayerCorrected && game.History.Contains(original), "Original evidence remains explainable");
        game = Apply(game, Input(new TableObservation([new("R1", BallColour.Red, new(.2, .5), .9)], true), 8));
        Check(game.State.Table.Balls["Y3"].Presence.Value == BallPresence.OnTable, "Machine disappearance cannot undo the same-shot player correction");
        game = Apply(game, Input(new SkyCorrection(new AssignColour("rich", BallColour.Yellow)), 9) with { Confidence = .78 });
        Check(game.State.Frame!.Colours["rich"] is { Source: Provenance.Inferred, Confidence: .78 }, "Machine assignment retains uncertainty");
        game = Apply(game, Input(new PlayerCorrection(new AssignColour("rich", BallColour.Yellow)), 10));
        Check(game.State.Frame!.Colours["rich"].Source == Provenance.PlayerConfirmed, "I'm yellows confirms the uncertain assignment");
        game = Apply(game, Input(new SkyCorrection(new AssignColour("rich", BallColour.Red)), 11));
        Check(game.State.Frame!.Colours["rich"].Value == BallColour.Yellow, "Machine inference cannot override player confirmation");
        game = Apply(game, Input(new PlayerCorrection(new SetFoul(false)), 12));
        Check(game.State.Frame!.Foul!.Value == false, "That wasn't a foul records the player correction");
        game = Apply(game, Input(new PlayerCorrection(new ChangeTurn("mike")), 13));
        Check(game.State.Frame!.CurrentPlayer.Value == "mike" && game.State.Frame.Visit == 2 && game.State.Frame.Foul is null, "His turn starts a visit with unknown foul state");
        game = Apply(game, Input(new ShotStarted("next-shot"), 14));
        game = Apply(game, Input(new PossiblePocketEvent("next-shot", "Y3", Pocket.TopLeft), 15) with { Confidence = .9 });
        // A player-confirmed identity can support a later shot without invented detector confidence.
        game = Apply(game, Input(new ShotEnded("next-shot"), 16));
        game = Apply(game, Input(new TableObservation([new("R1", BallColour.Red, new(.2, .5), .9)], true), 17));
        Check(game.State.Table.Balls["Y3"].Presence.Value == BallPresence.Potted, "A later actual shot releases same-shot presence protection");
        var json = JsonSerializer.Serialize(game.History);
        Check(GameJson.WriteState(GameSession.Restore(JsonSerializer.Deserialize<ImmutableArray<EventEnvelope>>(json)).State) == GameJson.WriteState(game.State), "Amended history reproduces corrected state deterministically");
        var later = YellowPot();
        var earlierPot = later.History.Single(e => e.Event is BallPottedEvent);
        later = Apply(later, Input(new TableObservation([new("Y3", BallColour.Yellow, new(.5, .5), .9), new("R1", BallColour.Red, new(.2, .5), .9)], true), 7));
        later = Apply(later, Input(new ShotStarted("later"), 8));
        later = Apply(later, Input(new PossiblePocketEvent("later", "Y3", Pocket.TopLeft), 9) with { Confidence = .9 });
        later = Apply(later, Input(new ShotEnded("later"), 10));
        later = Apply(later, Input(new TableObservation([new("R1", BallColour.Red, new(.2, .5), .9)], true), 11));
        later = Apply(later, Input(new PlayerCorrection(new SetBallPresence("Y3", BallPresence.OnTable, earlierPot.EventId)), 12));
        Check(later.State.Table.Balls["Y3"].Presence.Value == BallPresence.Potted && later.State.Frame!.Pots.Count == 1,
            "Correcting an earlier pot preserves the later real pot and repairs dependent ledger counts");
        game = Apply(game, Input(new PlayerCorrection(new SetFrameWinner("rich")), 18));
        game = Apply(game, Input(new PlayerCorrection(new SetFrameWinner("rich")), 19));
        Check(game.State.Match!.Score("rich") == 1, "Duplicate winner declarations do not count twice");
        game = Apply(game, Input(new PlayerCorrection(new SetFrameWinner("mike")), 20));
        Check(game.State.Match!.Score("rich") == 0 && game.State.Match.Score("mike") == 1, "That's my frame repairs the declared scoreboard");
        Console.WriteLine("PASS first-class corrections: amended pot/visit, confirmed colour, foul/turn/winner, machine precedence, later shot and deterministic rebuild");
    }
    public static void EvidenceBoundaries()
    {
        var game = YellowPot();
        Check(game.Accept(Input(new TableObservation([], true), 6)).Outcome == InputOutcome.Stale,
            "The same decoded frame cannot be resubmitted under a new evidence ID");
        Check(game.Accept(Input(new TableSettled(), 8) with { SourceTimeUs = 1 }).Outcome == InputOutcome.Stale,
            "Older source PTS is stale even when wall-clock time is newer");
        Check(game.Accept(Input(new TableSettled(), 8) with { Confidence = double.NaN }).Outcome == InputOutcome.Rejected, "Invalid confidence is refused");
        Check(game.Accept(Input(new TableObservation([new("bad", BallColour.Yellow, new(2, 0))], true), 8)).Outcome == InputOutcome.Rejected,
            "Domain positions are bounded independently of CV");
        Check(game.Accept(Input(new PlayerCorrection(new SetBallPresence("Y3", BallPresence.OnTable, Guid.NewGuid())), 8)).Outcome == InputOutcome.Rejected,
            "A correction cannot reference an unrelated event");
        var conflict = Apply(OpenTable(), Input(new ShotStarted("shot"), 3));
        conflict = Apply(conflict, Input(new PossiblePocketEvent("shot", "Y3", Pocket.TopLeft), 4) with { Confidence = .9 });
        conflict = Apply(conflict, Input(new PossiblePocketEvent("shot", "Y3", Pocket.BottomRight), 5) with { Confidence = .9 });
        conflict = Apply(conflict, Input(new ShotEnded("shot"), 6));
        conflict = Apply(conflict, Input(new TableObservation([new("R1", BallColour.Red, new(.2, .5), .9)], true), 7));
        Check(!conflict.History.Any(e => e.Event is BallPottedEvent), "Conflicting pocket evidence cannot become a confident pot");
        try { GameSession.Restore(game.History.SetItem(0, game.History[0] with { SchemaVersion = 99 })); throw new Exception("Invalid schema accepted"); }
        catch (ArgumentException) { }
        var reconnected = Apply(game, Input(new SelectSource(), 8, Source with { Generation = 1, Connection = "new-connection" }));
        Check(reconnected.State.Table.Uncertain && reconnected.State.Table.LastSettled is null, "A new connection can restart observation generation without promoting old live facts");
        var unknown = Apply(OpenTable(), Input(new BallObserved(new("unclassified", BallColour.Unknown, new(.5, .5))), 3));
        Check(unknown.State.Table.Uncertain && unknown.State.Table.Remaining(BallColour.Red) is null, "An individual unknown-colour signal cannot claim reliable counts");
        var excessive = Enumerable.Range(0, 17).Select(i => new ObservedBall("false-circle-" + i, BallColour.Red, new(.1 + i * .01, .5))).ToImmutableArray();
        var imperfect = Apply(OpenTable(), Input(new TableObservation(excessive, true), 3));
        Check(imperfect.State.Table.Uncertain && imperfect.State.Table.Balls.Count == 2,
            "Excess detections remain timestamped uncertain evidence rather than inventing a 17-ball table");
        var seed = OpenTable();
        var repeated = Enumerable.Range(0, 9993).Select(i => seed.History[2] with { Sequence = i + 4, EventId = Guid.NewGuid(), InputId = Guid.NewGuid(),
            At = DateTimeOffset.UnixEpoch.AddSeconds(i + 3), SourceTimeUs = (i + 3) * 1_000_000L, Event = new TableSettledEvent() });
        var nearlyFull = GameSession.Restore(seed.History.AddRange(repeated));
        nearlyFull = Apply(nearlyFull, Input(new ShotStarted("last-shot"), 20000));
        nearlyFull = Apply(nearlyFull, Input(new PossiblePocketEvent("last-shot", "Y3", Pocket.TopLeft), 20001) with { Confidence = .9 });
        nearlyFull = Apply(nearlyFull, Input(new ShotEnded("last-shot"), 20002));
        var full = nearlyFull.Accept(Input(new TableObservation([new("R1", BallColour.Red, new(.2, .5), .9)], true), 20003));
        Check(full.Outcome == InputOutcome.Rejected && full.Session.History.Length == 9999,
            "Capacity refusal is atomic for observation plus inferred pot and resolution, never a partial history");
        Console.WriteLine("PASS evidence boundaries: same-frame replay, PTS, invalid confidence/coordinates, amendment references, conflicting pockets and reconnect generation");
    }
}
