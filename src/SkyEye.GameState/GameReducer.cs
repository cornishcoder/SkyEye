using System.Collections.Immutable;

namespace SkyEye.GameState;

/// <summary>Pure reduction of meaningful events in authoritative sequence order.</summary>
public static class GameReducer
{
    public static GameSnapshot Rebuild(IEnumerable<EventEnvelope> events)
    {
        var state = GameSnapshot.Empty;
        foreach (var e in events)
        {
            if (e.SchemaVersion != 1 || e.Sequence != state.Version + 1) throw new ArgumentException("Invalid event schema or sequence.");
            state = Apply(state, e) with { Version = e.Sequence };
        }
        return state;
    }

    static Fact<T> Fact<T>(T value, EventEnvelope e) => new(value, e.Provenance, e.Confidence, e.InputId);
    static GameSnapshot Apply(GameSnapshot s, EventEnvelope e)
    {
        switch (e.Event)
        {
            case MatchStartedEvent m:
                return new(e.Sequence, new(e.MatchId, m.Players, m.Target, ImmutableDictionary<string, Fact<string>>.Empty), null, TableState.Empty, null);
            case FrameStartedEvent f:
                return s with { Frame = new(e.FrameId, Fact(f.CurrentPlayer, e), 1, ImmutableDictionary<string, Fact<BallColour?>>.Empty,
                    null, null, ImmutableDictionary<Guid, Pot>.Empty), Table = TableState.Empty, Shot = null };
            case SourceSelectedEvent:
                return s with { Table = s.Table with { Source = e.Source, Motion = TableMotion.Unknown, LastSettled = null, Uncertain = true }, Shot = null };
            case TableObservedEvent o:
                var balls = s.Table.Balls;
                var present = o.Balls.Select(b => b.Id).ToHashSet();
                foreach (var old in balls.Values)
                    if (!present.Contains(old.Id) && old.Presence.Value != BallPresence.Potted)
                        balls = balls.SetItem(old.Id, old with { Presence = Fact(BallPresence.Missing, e) });
                foreach (var b in o.Balls)
                    balls = balls.SetItem(b.Id, new(b.Id, b.Colour, b.Position, new(BallPresence.OnTable, e.Provenance, b.Confidence ?? e.Confidence, e.InputId)));
                return s with { Table = new(balls, o.Settled ? TableMotion.Settled : TableMotion.Unknown, e.Source,
                    o.Settled && !o.Uncertain ? new(e.InputId, e.At, e.SourceTimeUs, e.Source!) : s.Table.LastSettled, o.Uncertain) };
            case ShotStartedEvent start:
                return s with { Shot = new(start.ShotId, s.Table.Balls, ImmutableDictionary<string, PocketApproach>.Empty, false, false),
                    Table = s.Table with { Motion = TableMotion.Moving } };
            case ShotEndedEvent:
                return s with { Shot = s.Shot! with { Ended = true }, Table = s.Table with { Motion = TableMotion.Unknown } };
            case TableSettledEvent:
                return s with { Table = s.Table with { Motion = TableMotion.Settled,
                    LastSettled = !s.Table.Uncertain ? new(e.InputId, e.At, e.SourceTimeUs, e.Source!) : s.Table.LastSettled } };
            default: throw new ArgumentException("Unsupported event.");
        }
    }
}
