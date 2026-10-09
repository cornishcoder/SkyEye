using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SkyEye.GameState;

public sealed record GameInputEnvelope(Guid Id, string MatchId, string FrameId, DateTimeOffset At, GameInput Input,
    EvidenceSource? Source = null, long? SourceTimeUs = null, Provenance Provenance = Provenance.Observed, double? Confidence = null);
public abstract record GameInput;
public sealed record StartMatch(ImmutableArray<Player> Players, int Target) : GameInput;
public sealed record StartFrame(string CurrentPlayer) : GameInput;
public sealed record FinishMatch : GameInput;
public sealed record DeclareColours(string? PlayerId = null, BallColour? Colour = null) : GameInput;
public sealed record UndoDecision(Guid InputId) : GameInput;
public sealed record TableObservation(ImmutableArray<ObservedBall> Balls, bool Settled, bool Uncertain = false, bool IdentitiesResolved = true) : GameInput;
public sealed record MotionStarted(string ShotId) : GameInput;
public sealed record MotionStopped(string ShotId) : GameInput;
public sealed record ShotStarted(string ShotId) : GameInput;
public sealed record ShotEnded(string ShotId) : GameInput;
public sealed record TableSettled : GameInput;
public sealed record SelectSource : GameInput;
public sealed record BallObserved(ObservedBall Ball) : GameInput;
public sealed record BallMoved(string BallId, TablePosition Position) : GameInput;
public sealed record BallMissing(string BallId) : GameInput;
public sealed record BallAppeared(ObservedBall Ball) : GameInput;
public sealed record PossiblePocketEvent(string ShotId, string BallId, Pocket Pocket) : GameInput;
public sealed record PossibleCollision(string ShotId, string FirstBall, string SecondBall) : GameInput;
public sealed record PossibleCushionContact(string ShotId, string BallId) : GameInput;
public sealed record AcceptSettledInventory(int ContractVersion, ActivityWindowContext Context,
    SettledTableEvidence After) : GameInput;
// Typed correction payloads can be supplied by touch, a player or a future Sky adapter.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AssignColour), "colour")]
[JsonDerivedType(typeof(ChangeTurn), "turn")]
[JsonDerivedType(typeof(SetFoul), "foul")]
[JsonDerivedType(typeof(SetBallPresence), "ball")]
[JsonDerivedType(typeof(SetFrameWinner), "frame")]
[JsonDerivedType(typeof(SetInventoryCounts), "inventory-counts")]
public abstract record Correction;
public sealed record AssignColour(string PlayerId, BallColour Colour) : Correction;
public sealed record ChangeTurn(string PlayerId) : Correction;
public sealed record SetFoul(bool Foul) : Correction;
public sealed record SetBallPresence(string BallId, BallPresence Presence, Guid? CorrectedPot = null) : Correction;
public sealed record SetFrameWinner(string PlayerId) : Correction;
public sealed record SetInventoryCounts(SettledTableCounts Counts) : Correction;
public sealed record PlayerCorrection(Correction Correction) : GameInput;
public sealed record SkyCorrection(Correction Correction) : GameInput;
