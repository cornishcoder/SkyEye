using System.Collections.Immutable;

namespace SkyEye.GameState;

public enum Provenance { Observed, Inferred, PlayerConfirmed, PlayerCorrected }
public enum BallColour { Cue, Red, Yellow, Black, Unknown }
public enum BallPresence { Unknown, OnTable, Missing, Potted }
public enum TableMotion { Unknown, Moving, Settled }
public enum Pocket { TopLeft, TopMiddle, TopRight, BottomLeft, BottomMiddle, BottomRight }
public sealed record Fact<T>(T Value, Provenance Source, double? Confidence, Guid EvidenceId);
public sealed record Player(string Id, string Name);
// Normalized playable rectangle: (0,0) top left; (1,1) bottom right.
public sealed record TablePosition(double X, double Y);
// Source time is microseconds on this source's monotonic media timeline only.
public sealed record EvidenceSource(string Camera, string Timeline, long Generation, string Calibration, string? Connection = null);
public sealed record ObservedBall(string Id, BallColour Colour, TablePosition? Position, double? Confidence = null);
public sealed record KnownBall(string Id, BallColour Colour, TablePosition? Position, Fact<BallPresence> Presence, string? ProtectedThroughShot = null);
public sealed record SettledObservation(Guid EvidenceId, DateTimeOffset At, long? SourceTimeUs, EvidenceSource Source);
public sealed record Pot(Guid EventId, string ShotId, string BallId, Pocket Pocket, Fact<BallPresence> Presence);
public sealed record PocketApproach(string BallId, Pocket Pocket, double? Confidence, Guid EvidenceId, bool Ambiguous = false);
public sealed record ShotState(string Id, ImmutableDictionary<string, KnownBall> Before,
    ImmutableDictionary<string, PocketApproach> Approaches, bool Ended, bool Resolved);

public sealed record MatchState(string Id, ImmutableArray<Player> Players, int Target,
    ImmutableDictionary<string, Fact<string>> FrameWinners)
{
    public ImmutableArray<FrameSummary> Frames { get; init; } = [];
    public bool Finished { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public int Score(string playerId) => FrameWinners.Values.Count(w => w.Value == playerId);
    public string? Winner => Players.FirstOrDefault(p => Score(p.Id) >= Target)?.Id;
}
public sealed record FrameSummary(string Id, int Number, string Starter, DateTimeOffset StartedAt, string? Winner, bool Closed);
public sealed record ArchivedMatch(MatchState Match, DateTimeOffset ArchivedAt);
public sealed record FrameState(string Id, Fact<string> CurrentPlayer, int Visit,
    ImmutableDictionary<string, Fact<BallColour?>> Colours, Fact<bool?>? Foul,
    Fact<string>? Winner, ImmutableDictionary<Guid, Pot> Pots)
{
    public bool OpenTable => Colours.Count == 0;
}
public sealed record TableState(ImmutableDictionary<string, KnownBall> Balls, TableMotion Motion,
    EvidenceSource? Source, SettledObservation? LastSettled, bool Uncertain)
{
    public static TableState Empty { get; } = new(ImmutableDictionary<string, KnownBall>.Empty, TableMotion.Unknown, null, null, true);
    public int? Remaining(BallColour colour) => Uncertain || Balls.Values.Any(b => b.Colour == colour && b.Presence.Value is BallPresence.Missing or BallPresence.Unknown)
        ? null : Balls.Values.Count(b => b.Colour == colour && b.Presence.Value == BallPresence.OnTable);
    public BallPresence Black => Balls.Values.FirstOrDefault(b => b.Colour == BallColour.Black)?.Presence.Value ?? BallPresence.Unknown;
}
public sealed record GameSnapshot(long Version, MatchState? Match, FrameState? Frame, TableState Table, ShotState? Shot)
{
    public ImmutableArray<ArchivedMatch> Archives { get; init; } = [];
    public static GameSnapshot Empty { get; } = new(0, null, null, TableState.Empty, null);
}
