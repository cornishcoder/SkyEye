using System.Collections.Immutable;

namespace SkyEye.GameState;

/// <summary>Pure reduction of meaningful events in authoritative sequence order.</summary>
public static class GameReducer
{
    public static GameSnapshot Rebuild(IEnumerable<EventEnvelope> events)
    {
        var history = events.ToArray();
        var amendments = history.Where(e => e.Event is StateCorrectedEvent { CorrectedPot: not null })
            .GroupBy(e => ((StateCorrectedEvent)e.Event).CorrectedPot!.Value).ToDictionary(g => g.Key, g => g.Last());
        var state = GameSnapshot.Empty;
        foreach (var e in history)
        {
            if (e.SchemaVersion != 1 || e.Sequence != state.Version + 1) throw new ArgumentException("Invalid event schema or sequence.");
            // A historical amendment replaces the fact at its original position.
            // Later real shots still reduce after it; arrival of the amendment cannot erase them.
            if (e.Event is BallPottedEvent pot && amendments.TryGetValue(e.EventId, out var correction))
            {
                var amended = (StateCorrectedEvent)correction.Event;
                var before = state.Shot!.Before[pot.BallId];
                var ball = before with { Presence = Fact(amended.Presence, correction), ProtectedThroughShot = state.Shot.Id };
                state = state with { Table = state.Table with { Balls = state.Table.Balls.SetItem(ball.Id, ball) } };
            }
            else state = Apply(state, e);
            state = state with { Version = e.Sequence };
        }
        return state;
    }

    static Fact<T> Fact<T>(T value, EventEnvelope e) => new(value, e.Provenance, e.Confidence, e.InputId);
    static GameSnapshot Apply(GameSnapshot s, EventEnvelope e)
    {
        switch (e.Event)
        {
            case MatchStartedEvent m:
                return new GameSnapshot(e.Sequence, new(e.MatchId, m.Players, m.Target, ImmutableDictionary<string, Fact<string>>.Empty), null, TableState.Empty, null)
                { Archives = s.Match is null ? s.Archives : s.Archives.Add(new(s.Match, e.At)) };
            case MatchFinishedEvent:
                return s with { Match = s.Match! with { Finished = true, FinishedAt = e.At },
                    Table = s.Table with { Motion = TableMotion.Unknown, Uncertain = true }, Shot = null };
            case FrameStartedEvent f:
                return s with { Frame = new(e.FrameId, Fact(f.CurrentPlayer, e), 1, ImmutableDictionary<string, Fact<BallColour?>>.Empty,
                    null, null, ImmutableDictionary<Guid, Pot>.Empty), Table = TableState.Empty, Shot = null,
                    Match = s.Match! with { Frames = s.Match.Frames.Select(frame => frame with { Closed = true }).ToImmutableArray()
                        .Add(new(e.FrameId, s.Match.Frames.Length + 1, f.CurrentPlayer, e.At, null, false)) } };
            case SourceSelectedEvent:
                return s with { Table = s.Table with { Source = e.Source, Motion = TableMotion.Unknown, LastSettled = null, Uncertain = true }, Shot = null };
            case TableObservedEvent o:
                if (!o.IdentitiesResolved) return s with { Table = s.Table with { Source = e.Source, Motion = TableMotion.Unknown, Uncertain = true } };
                var balls = s.Table.Balls;
                var present = o.Balls.Select(b => b.Id).ToHashSet();
                foreach (var old in balls.Values)
                    if (!present.Contains(old.Id) && old.Presence.Value != BallPresence.Potted && !Protected(old, s))
                        balls = balls.SetItem(old.Id, old with { Presence = Fact(BallPresence.Missing, e) });
                foreach (var b in o.Balls)
                    if (!balls.TryGetValue(b.Id, out var old) || !Protected(old, s))
                        balls = balls.SetItem(b.Id, new(b.Id, b.Colour, b.Position, new(BallPresence.OnTable, e.Provenance, b.Confidence ?? e.Confidence, e.InputId)));
                return s with { Table = new(balls, o.Settled ? TableMotion.Settled : TableMotion.Unknown, e.Source,
                    o.Settled && !o.Uncertain ? new(e.InputId, e.At, e.SourceTimeUs, e.Source!) : s.Table.LastSettled, o.Uncertain) };
            case ShotStartedEvent start:
                return s with { Shot = new(start.ShotId, s.Table.Balls, ImmutableDictionary<string, PocketApproach>.Empty, false, false),
                    Table = s.Table with { Motion = TableMotion.Moving }, Frame = s.Frame! with { Foul = null } };
            case ShotEndedEvent:
                return s with { Shot = s.Shot! with { Ended = true }, Table = s.Table with { Motion = TableMotion.Unknown } };
            case TableSettledEvent:
                return s with { Table = s.Table with { Motion = TableMotion.Settled,
                    LastSettled = !s.Table.Uncertain ? new(e.InputId, e.At, e.SourceTimeUs, e.Source!) : s.Table.LastSettled } };
            case PocketApproachedEvent p:
                var ambiguous = s.Shot!.Approaches.TryGetValue(p.BallId, out var previous) && (previous.Ambiguous || previous.Pocket != p.Pocket);
                return s with { Shot = s.Shot with { Approaches = s.Shot.Approaches.SetItem(p.BallId, new(p.BallId, p.Pocket, e.Confidence, e.InputId, ambiguous)) } };
            case BallEvidenceEvent b:
                if (s.Table.Balls.TryGetValue(b.Ball.Id, out var protectedBall) && Protected(protectedBall, s)) return s;
                var known = new KnownBall(b.Ball.Id, b.Ball.Colour, b.Ball.Position, new(b.Presence, e.Provenance, b.Ball.Confidence ?? e.Confidence, e.InputId));
                return s with { Table = s.Table with { Balls = s.Table.Balls.SetItem(known.Id, known), Source = e.Source,
                    Uncertain = s.Table.Uncertain || b.Presence == BallPresence.Missing || known.Colour == BallColour.Unknown } };
            case BallPottedEvent p:
                var ball = s.Table.Balls[p.BallId];
                var presence = Fact(BallPresence.Potted, e);
                return s with { Table = s.Table with { Balls = s.Table.Balls.SetItem(ball.Id, ball with { Presence = presence, Position = null }) },
                    Frame = s.Frame! with { Pots = s.Frame.Pots.Add(e.EventId, new(e.EventId, p.ShotId, p.BallId, p.Pocket, presence)) } };
            case ShotResolvedEvent:
                return s with { Shot = s.Shot! with { Resolved = true } };
            case ColourAssignedEvent c:
                if (s.Frame!.Colours.TryGetValue(c.PlayerId, out var colour) && colour.Source > e.Provenance) return s;
                return s with { Frame = s.Frame with { Colours = s.Frame.Colours.SetItem(c.PlayerId, Fact<BallColour?>(c.Colour, e)) } };
            case TurnChangedEvent t:
                if (s.Frame!.CurrentPlayer.Source > e.Provenance) return s;
                return s with { Frame = s.Frame with { CurrentPlayer = Fact(t.PlayerId, e), Visit = s.Frame.Visit + (s.Frame.CurrentPlayer.Value == t.PlayerId ? 0 : 1),
                    Foul = s.Frame.CurrentPlayer.Value == t.PlayerId ? s.Frame.Foul : null } };
            case FoulRecordedEvent f:
                if (s.Frame!.Foul is { } foul && foul.Source > e.Provenance) return s;
                return s with { Frame = s.Frame with { Foul = Fact<bool?>(f.Foul, e) } };
            case FrameWonEvent f:
                if (s.Match!.FrameWinners.TryGetValue(e.FrameId, out var winner) && winner.Source > e.Provenance) return s;
                var result = Fact(f.PlayerId, e);
                return s with { Frame = s.Frame!.Id == e.FrameId ? s.Frame with { Winner = result } : s.Frame,
                    Match = s.Match with { FrameWinners = s.Match.FrameWinners.SetItem(e.FrameId, result),
                        Frames = s.Match.Frames.Select(frame => frame.Id == e.FrameId ? frame with { Winner = f.PlayerId, Closed = true } : frame).ToImmutableArray() } };
            case StateCorrectedEvent { CorrectedPot: not null }:
                return s; // Applied at the amended event's sequence above.
            case StateCorrectedEvent c:
                var corrected = s.Table.Balls[c.BallId];
                if (corrected.Presence.Source > e.Provenance && Protected(corrected, s)) return s;
                return s with { Table = s.Table with { Balls = s.Table.Balls.SetItem(c.BallId,
                    corrected with { Presence = Fact(c.Presence, e), ProtectedThroughShot = s.Shot?.Id ?? "" }) } };
            default: throw new ArgumentException("Unsupported event.");
        }
    }
    static bool Protected(KnownBall ball, GameSnapshot state) => ball.ProtectedThroughShot is not null
        && (state.Shot is null || ball.ProtectedThroughShot == state.Shot.Id);
}
