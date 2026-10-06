using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkyEye.GameState;

public enum InputOutcome { Applied, Duplicate, Stale, Rejected, Conflict }
public sealed record InputResult(InputOutcome Outcome, string Message, GameSession Session)
{
    public bool Accepted => Outcome is InputOutcome.Applied or InputOutcome.Duplicate;
}

/// <summary>Immutable proposal. The application persists a successful replacement before publishing it.</summary>
public sealed class GameSession
{
    public ImmutableArray<EventEnvelope> History { get; }
    public GameSnapshot State { get; }
    public static GameSession Empty { get; } = new([]);
    GameSession(ImmutableArray<EventEnvelope> history) { History = history; State = GameReducer.Rebuild(history); }
    public static GameSession Restore(IEnumerable<EventEnvelope> events)
    {
        var history = events.ToImmutableArray();
        if (history.Length > 10000 || history.Any(e => e is null || e.Event is null || e.EventId == Guid.Empty || e.InputId == Guid.Empty
                || e.InputFingerprint is not { Length: 64 } || !e.InputFingerprint.All(Uri.IsHexDigit) || !Text(e.MatchId)
                || !Enum.IsDefined(e.Provenance) || !Confidence(e.Confidence) || e.SourceTimeUs is < 0 || e.Source is { } source && !ValidSource(source))
            || history.GroupBy(e => e.InputId).Any(g => g.Select(e => e.InputFingerprint).Distinct().Count() != 1)
            || history.Select(e => e.EventId).Distinct().Count() != history.Length) throw new ArgumentException("Invalid event identities.");
        foreach (var correction in history.Where(e => e.Event is StateCorrectedEvent { CorrectedPot: not null }))
        {
            var c = (StateCorrectedEvent)correction.Event;
            if (correction.Provenance < Provenance.PlayerConfirmed || !history.Any(e => e.EventId == c.CorrectedPot && e.Sequence < correction.Sequence
                && e.MatchId == correction.MatchId && e.FrameId == correction.FrameId && e.Event is BallPottedEvent p && p.BallId == c.BallId))
                throw new ArgumentException("Invalid amendment reference.");
        }
        var undoTargets = new HashSet<Guid>();
        foreach (var undo in history.Where(e => e.Event is DecisionUndoneEvent))
        {
            var target = ((DecisionUndoneEvent)undo.Event).InputId;
            if (!undoTargets.Add(target) || !history.Any(e => e.InputId == target && e.Sequence < undo.Sequence
                && e.MatchId == undo.MatchId && CanUndo(e))) throw new ArgumentException("Invalid undo reference.");
        }
        return new(history);
    }

    public InputResult Accept(GameInputEnvelope input)
    {
        InputResult Refuse(InputOutcome outcome, string message) => new(outcome, message, this);
        if (input is null || input.Input is null || input.Id == Guid.Empty || !Text(input.MatchId) || !Enum.IsDefined(input.Provenance)
            || !Confidence(input.Confidence) || input.SourceTimeUs is < 0 || input.Source is { } source && !ValidSource(source))
            return Refuse(InputOutcome.Rejected, "Invalid input envelope.");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            input.MatchId, input.FrameId, input.At, input.Source, input.SourceTimeUs, input.Provenance, input.Confidence,
            Input = JsonSerializer.SerializeToElement(input.Input, input.Input.GetType())
        }))));
        var previous = History.FirstOrDefault(e => e.InputId == input.Id);
        if (previous is not null) return Refuse(previous.InputFingerprint == fingerprint ? InputOutcome.Duplicate : InputOutcome.Conflict,
            previous.InputFingerprint == fingerprint ? "Input already applied." : "Input ID reused with different evidence.");
        if (History.Length >= 10000) return Refuse(InputOutcome.Rejected, "Game history is full; archive before continuing.");
        if (input.Input is StartMatch m)
        {
            if (State.Match is { Finished: false, Winner: null } || History.Any(e => e.Event is MatchStartedEvent && e.MatchId == input.MatchId)
                || m.Players.IsDefault || m.Players.Length != 2 || m.Players.Any(p => p is null || !Text(p.Id) || !Text(p.Name) || p.Name.Length > 40 || p.Name.Any(char.IsControl))
                || m.Players.Select(p => p.Id).Distinct().Count() != 2 || m.Target is < 1 or > 100)
                return Refuse(InputOutcome.Rejected, "Declare two distinct players and a positive target for a new match.");
            return Commit(input, fingerprint, [new MatchStartedEvent(m.Players, m.Target)]);
        }
        if (State.Match?.Id != input.MatchId || !Text(input.FrameId)) return Refuse(InputOutcome.Stale, "Input belongs to another match/frame.");
        if (input.Input is UndoDecision undo)
        {
            if (History.Any(e => e.Event is DecisionUndoneEvent u && u.InputId == undo.InputId)
                || !History.Any(e => e.InputId == undo.InputId && e.MatchId == input.MatchId && CanUndo(e)))
                return Refuse(InputOutcome.Rejected, "Choose an existing player decision that has not already been undone.");
            return Commit(input with { Provenance = Provenance.PlayerCorrected }, fingerprint, [new DecisionUndoneEvent(undo.InputId)]);
        }
        if (input.Input is FinishMatch)
        {
            if (State.Match.Finished) return Refuse(InputOutcome.Rejected, "The match is already finished.");
            return Commit(input with { Provenance = Provenance.PlayerConfirmed }, fingerprint, [new MatchFinishedEvent()]);
        }
        if (input.Input is StartFrame f)
        {
            if (State.Match.Finished || !PlayerExists(f.CurrentPlayer) || State.Frame is { Winner: null }
                || State.Match.Frames.Any(frame => frame.Id == input.FrameId) || State.Match.Winner is not null)
                return Refuse(InputOutcome.Rejected, "Declare a player and a new frame after resolving the previous frame.");
            return Commit(input, fingerprint, [new FrameStartedEvent(f.CurrentPlayer)]);
        }
        bool historicalWinner = input.Input is PlayerCorrection { Correction: SetFrameWinner }
            && State.Match.Frames.Any(frame => frame.Id == input.FrameId);
        if (State.Frame?.Id != input.FrameId && !historicalWinner)
            return Refuse(InputOutcome.Stale, "Input belongs to an inactive frame.");
        if (input.Input is DeclareColours groups)
        {
            if (State.Match.Finished || State.Frame!.Winner is not null || !(groups.PlayerId is null && groups.Colour is null
                || groups.PlayerId is not null && PlayerExists(groups.PlayerId) && groups.Colour is BallColour.Red or BallColour.Yellow))
                return Refuse(InputOutcome.Rejected, "Declare open table or the current frame's complementary colours.");
            return Commit(input with { Provenance = Provenance.PlayerConfirmed }, fingerprint, [new ColoursDeclaredEvent(groups.PlayerId, groups.Colour)]);
        }
        if (input.Input is PlayerCorrection or SkyCorrection)
        {
            var correction = input.Input is PlayerCorrection player ? player.Correction : ((SkyCorrection)input.Input).Correction;
            var provenance = input.Input is PlayerCorrection ? Provenance.PlayerCorrected : Provenance.Inferred;
            GameEvent? amended = correction switch
            {
                AssignColour c when PlayerExists(c.PlayerId) && c.Colour is BallColour.Red or BallColour.Yellow => new ColourAssignedEvent(c.PlayerId, c.Colour),
                ChangeTurn c when PlayerExists(c.PlayerId) => new TurnChangedEvent(c.PlayerId),
                SetFoul c => new FoulRecordedEvent(c.Foul),
                SetFrameWinner c when PlayerExists(c.PlayerId) => new FrameWonEvent(c.PlayerId),
                SetBallPresence c when Enum.IsDefined(c.Presence) && State.Table.Balls.ContainsKey(c.BallId)
                    && (c.CorrectedPot is null || History.Any(e => e.EventId == c.CorrectedPot && e.FrameId == input.FrameId
                        && e.Event is BallPottedEvent p && p.BallId == c.BallId) && c.Presence != BallPresence.Potted)
                    => new StateCorrectedEvent(c.BallId, c.Presence, c.CorrectedPot),
                _ => null
            };
            if (amended is null) return Refuse(InputOutcome.Rejected, "Unsupported correction or invalid fact reference.");
            if (correction is AssignColour assigned && input.Input is PlayerCorrection
                && (!State.Frame!.Colours.TryGetValue(assigned.PlayerId, out var existing) || existing.Value == assigned.Colour))
                provenance = Provenance.PlayerConfirmed;
            if (input.Input is SkyCorrection && correction is SetBallPresence { CorrectedPot: not null })
                return Refuse(InputOutcome.Rejected, "A machine cannot revoke a committed pot; ask the player.");
            return Commit(input with { Provenance = provenance }, fingerprint, [amended]);
        }
        if (State.Match.Finished || State.Frame!.Winner is not null) return Refuse(InputOutcome.Stale, "The frame or match has ended.");
        if (input.Input is SelectSource)
        {
            if (input.Source is null || input.Source == State.Table.Source || State.Table.Source is { } old && input.Source.Camera == old.Camera
                && input.Source.Connection == old.Connection && input.Source.Generation < old.Generation)
                return Refuse(InputOutcome.Rejected, "Select a new current source explicitly.");
            return Commit(input, fingerprint, [new SourceSelectedEvent()]);
        }
        if (input.Source is null || State.Table.Source is { } current && current != input.Source)
            return Refuse(InputOutcome.Stale, "Evidence belongs to another source; select it explicitly first.");
        var last = History.LastOrDefault(e => e.FrameId == input.FrameId && e.Source == input.Source);
        if (last is not null && (input.At < last.At || input.SourceTimeUs is { } pts && last.SourceTimeUs is { } previousPts && pts < previousPts))
            return Refuse(InputOutcome.Stale, "Evidence is older than the accepted source watermark.");
        if (input.Input is TableObservation && History.LastOrDefault(e => e.FrameId == input.FrameId && e.Source == input.Source
                && e.Event is TableObservedEvent) is { } lastTable && (input.At <= lastTable.At
                    || input.SourceTimeUs is { } tablePts && lastTable.SourceTimeUs is { } oldTablePts && tablePts <= oldTablePts))
            return Refuse(InputOutcome.Stale, "This table frame was already observed.");
        GameEvent? change = input.Input switch
        {
            TableObservation o when ValidObservation(o) && !(last?.Event is TableObservedEvent && input.At == last.At)
                => new TableObservedEvent(o.Balls, o.Settled, o.Uncertain || !o.IdentitiesResolved || o.Balls.Length > 16 || o.Balls.Any(b => b.Colour == BallColour.Unknown),
                    o.IdentitiesResolved && o.Balls.Length <= 16),
            ShotStarted s when Text(s.ShotId) && CanStartShot(s.ShotId) => new ShotStartedEvent(s.ShotId),
            MotionStarted s when Text(s.ShotId) && CanStartShot(s.ShotId) => new ShotStartedEvent(s.ShotId),
            ShotEnded s when State.Shot is { Ended: false } shot && shot.Id == s.ShotId => new ShotEndedEvent(s.ShotId),
            MotionStopped s when State.Shot is { Ended: false } shot && shot.Id == s.ShotId => new ShotEndedEvent(s.ShotId),
            TableSettled when State.Table.Balls.Count > 0 => new TableSettledEvent(),
            PossiblePocketEvent p when State.Shot is { Ended: false } shot && shot.Id == p.ShotId && shot.Before.ContainsKey(p.BallId)
                && Enum.IsDefined(p.Pocket) => new PocketApproachedEvent(p.ShotId, p.BallId, p.Pocket),
            BallObserved b when ValidBall(b.Ball) => new BallEvidenceEvent(b.Ball, BallPresence.OnTable),
            BallAppeared b when ValidBall(b.Ball) => new BallEvidenceEvent(b.Ball, BallPresence.OnTable),
            BallMoved b when State.Table.Balls.TryGetValue(b.BallId, out var ball) && ValidPosition(b.Position)
                => new BallEvidenceEvent(new(ball.Id, ball.Colour, b.Position, input.Confidence), BallPresence.OnTable),
            BallMissing b when State.Table.Balls.TryGetValue(b.BallId, out var ball)
                => new BallEvidenceEvent(new(ball.Id, ball.Colour, ball.Position, input.Confidence), BallPresence.Missing),
            _ => null
        };
        return change is null ? Refuse(InputOutcome.Rejected, "Unsupported, invalid or phase-incompatible evidence.") : Commit(input, fingerprint, [change]);
    }
    bool PlayerExists(string id) => State.Match!.Players.Any(p => p.Id == id);
    public static bool CanUndo(EventEnvelope e) => e.Provenance >= Provenance.PlayerConfirmed
        && e.Event is ColourAssignedEvent or ColoursDeclaredEvent or TurnChangedEvent or FoulRecordedEvent or FrameWonEvent or StateCorrectedEvent;
    bool CanStartShot(string id) => State.Table.Motion == TableMotion.Settled && !State.Table.Uncertain
        && (State.Shot is null || State.Shot.Ended) && !History.Any(e => e.FrameId == State.Frame!.Id && e.Event is ShotStartedEvent s && s.ShotId == id);
    static bool ValidObservation(TableObservation o) => !o.Balls.IsDefault && o.Balls.Length <= 64
        && o.Balls.All(ValidBall)
        && o.Balls.Select(b => b.Id).Distinct().Count() == o.Balls.Length;
    static bool ValidPosition(TablePosition? p) => p is null || double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1;
    static bool ValidBall(ObservedBall b) => b is not null && Text(b.Id) && Enum.IsDefined(b.Colour) && Confidence(b.Confidence) && ValidPosition(b.Position);
    static bool Confidence(double? value) => value is null || double.IsFinite(value.Value) && value is >= 0 and <= 1;
    static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128;
    static bool ValidSource(EvidenceSource s) => Text(s.Camera) && Text(s.Timeline) && Text(s.Calibration) && s.Generation > 0;
    InputResult Commit(GameInputEnvelope input, string fingerprint, IEnumerable<GameEvent> changes)
    {
        var events = changes.Select((e, i) => new EventEnvelope(1, History.Length + i + 1, Guid.NewGuid(), input.Id, fingerprint,
            input.MatchId, input.FrameId, input.At, input.Source, input.SourceTimeUs, input.Provenance, input.Confidence, e)).ToImmutableArray();
        var history = History.AddRange(events);
        var proposed = new GameSession(history);
        if (input.Input is TableObservation or TableSettled or ShotEnded or MotionStopped
            && proposed.State is { Shot: { Ended: true, Resolved: false } shot, Table: { Motion: TableMotion.Settled, Uncertain: false } table })
        {
            foreach (var missing in table.Balls.Values.OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                // This conservative vertical slice needs supplied pocket and identity confidence.
                // It is an inferred outcome, never a calibrated probability or a rule decision.
                if (missing.Presence.Value != BallPresence.Missing || !shot.Before.TryGetValue(missing.Id, out var before)
                    || before.Presence.Value != BallPresence.OnTable || before.Presence.Source < Provenance.PlayerConfirmed && before.Presence.Confidence is not >= .8
                    || !shot.Approaches.TryGetValue(missing.Id, out var pocket) || pocket.Ambiguous || pocket.Confidence is not >= .8) continue;
                var pot = new BallPottedEvent(shot.Id, missing.Id, pocket.Pocket, missing.Colour == BallColour.Cue,
                    [before.Presence.EvidenceId, pocket.EvidenceId, missing.Presence.EvidenceId]);
                history = history.Add(new(1, history.Length + 1, Guid.NewGuid(), input.Id, fingerprint, input.MatchId, input.FrameId, input.At,
                    input.Source, input.SourceTimeUs, Provenance.Inferred, Math.Min(before.Presence.Confidence ?? pocket.Confidence.Value, pocket.Confidence.Value), pot));
            }
            history = history.Add(new(1, history.Length + 1, Guid.NewGuid(), input.Id, fingerprint, input.MatchId, input.FrameId, input.At,
                input.Source, input.SourceTimeUs, input.Provenance, input.Confidence, new ShotResolvedEvent()));
            proposed = new(history);
        }
        return proposed.History.Length > 10000
            ? new(InputOutcome.Rejected, "Game history has insufficient capacity for this complete transition.", this)
            : new(InputOutcome.Applied, "Game state updated.", proposed);
    }
}
