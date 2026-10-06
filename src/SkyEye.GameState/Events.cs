using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SkyEye.GameState;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MatchStartedEvent), "match-started")]
[JsonDerivedType(typeof(FrameStartedEvent), "frame-started")]
[JsonDerivedType(typeof(TableObservedEvent), "table-observed")]
[JsonDerivedType(typeof(ShotStartedEvent), "shot-started")]
[JsonDerivedType(typeof(ShotEndedEvent), "shot-ended")]
[JsonDerivedType(typeof(TableSettledEvent), "table-settled")]
[JsonDerivedType(typeof(PocketApproachedEvent), "pocket-approached")]
[JsonDerivedType(typeof(BallPottedEvent), "ball-potted")]
[JsonDerivedType(typeof(ColourAssignedEvent), "colour-assigned")]
[JsonDerivedType(typeof(TurnChangedEvent), "turn-changed")]
[JsonDerivedType(typeof(FoulRecordedEvent), "foul-recorded")]
[JsonDerivedType(typeof(FrameWonEvent), "frame-won")]
[JsonDerivedType(typeof(StateCorrectedEvent), "state-corrected")]
public abstract record GameEvent;
public sealed record MatchStartedEvent(ImmutableArray<Player> Players, int Target) : GameEvent;
public sealed record FrameStartedEvent(string CurrentPlayer) : GameEvent;
public sealed record TableObservedEvent(ImmutableArray<ObservedBall> Balls, bool Settled, bool Uncertain) : GameEvent;
public sealed record ShotStartedEvent(string ShotId) : GameEvent;
public sealed record ShotEndedEvent(string ShotId) : GameEvent;
public sealed record TableSettledEvent : GameEvent;
public sealed record PocketApproachedEvent(string ShotId, string BallId, Pocket Pocket) : GameEvent;
public sealed record BallPottedEvent(string ShotId, string BallId, Pocket Pocket, bool CueBall, ImmutableArray<Guid> Evidence) : GameEvent;
public sealed record ColourAssignedEvent(string PlayerId, BallColour Colour) : GameEvent;
public sealed record TurnChangedEvent(string PlayerId) : GameEvent;
public sealed record FoulRecordedEvent(bool Foul) : GameEvent;
public sealed record FrameWonEvent(string PlayerId) : GameEvent;
public sealed record StateCorrectedEvent(string BallId, BallPresence Presence, Guid? CorrectedPot) : GameEvent;
public sealed record EventEnvelope(int SchemaVersion, long Sequence, Guid EventId, Guid InputId, string InputFingerprint,
    string MatchId, string FrameId, DateTimeOffset At, EvidenceSource? Source, long? SourceTimeUs,
    Provenance Provenance, double? Confidence, GameEvent Event);
