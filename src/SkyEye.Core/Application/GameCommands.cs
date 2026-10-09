using SkyEye.GameState;

namespace SkyEye.Core.Application;

public sealed record SubmitGameInput(GameInputEnvelope Input, long? ExpectedGameVersion = null) : SkyEyeCommand;

public sealed partial class AppCoordinator
{
    readonly IGameJournal? gameJournal;
    GameSession game = GameSession.Empty;
    readonly bool gameUnavailable;

    // The existing application writer serializes game proposals, journal publication and notifications.
    CommandReceipt GameCommand(CommandEnvelope envelope, SubmitGameInput command)
    {
        if (gameUnavailable) return Receipt(envelope, CommandOutcome.Failed, state.GameError!);
        if (command.Input?.Input is EstablishSettledBaseline && envelope.Origin != CommandOrigin.System)
            return Receipt(envelope, CommandOutcome.Rejected, "Starting inventory is established by the verified observation path.");
        bool declared = command.Input?.Input is StartMatch or StartFrame or FinishMatch or DeclareColours or UndoDecision
            or PlayerCorrection or AcceptSettledInventory or CorrectSettledInventory or RejectSettledInventory;
        bool trustedRuling = command.Input?.Input is ApplyShotRuling && envelope.Origin == CommandOrigin.System;
        if (command.Input?.Input is ApplyShotRuling && !trustedRuling) return Receipt(envelope, CommandOutcome.Rejected, "Submit declared facts through the rules command.");
        bool local = command.Input?.Source is null && state.Camera.ConnectionId == Guid.Empty
            && (declared && envelope.Origin is CommandOrigin.Touch or CommandOrigin.Sky || trustedRuling);
        if (command.Input is null || envelope.OperationId != command.Input.Id || !(local || state.Camera.CanControl)
            || envelope.Origin == CommandOrigin.Vision && (declared
                || command.Input.Provenance is Provenance.PlayerConfirmed or Provenance.PlayerCorrected)
            || command.Input.Source is { } scope && (scope.Camera != state.Camera.CameraIdentity
                || scope.Connection is not null && scope.Connection != state.Camera.ConnectionId.ToString("N")))
            return Receipt(envelope, CommandOutcome.Rejected, "Manage a local match before connecting, or become camera host. Use an explicit command with its matching operation ID.");
        var proposal = game.Accept(command.Input);
        if (proposal.Outcome == InputOutcome.Duplicate) return Receipt(envelope, CommandOutcome.Unchanged, proposal.Message) with { Duplicate = true };
        if (proposal.Outcome != InputOutcome.Applied) return Receipt(envelope, proposal.Outcome switch
        { InputOutcome.Stale => CommandOutcome.Stale, InputOutcome.Conflict => CommandOutcome.Conflict, _ => CommandOutcome.Rejected }, proposal.Message);
        if (command.ExpectedGameVersion is { } expected ? expected != game.State.Version : envelope.ExpectedVersion != state.Version)
            return Receipt(envelope, CommandOutcome.Stale, "The match changed; use its current controls.");
        try { gameJournal?.Save(game, proposal.Session); }
        catch (IOException) { return Receipt(envelope, CommandOutcome.Failed, "Game history could not be saved. Previous state retained."); }
        Volatile.Write(ref game, proposal.Session);
        var group = game.State.Frame?.Rules is { } rules ? rules.Target switch
            { LegalTarget.Red => AdviceTargetGroup.Red, LegalTarget.Yellow => AdviceTargetGroup.Yellow, LegalTarget.Black => AdviceTargetGroup.Black, _ => AdviceTargetGroup.Open }
            : game.State.Frame is { Winner: null } frame && game.State.Match is { Finished: false }
            && frame.Colours.TryGetValue(frame.CurrentPlayer.Value, out var assigned)
                ? assigned.Value == BallColour.Red ? AdviceTargetGroup.Red : assigned.Value == BallColour.Yellow ? AdviceTargetGroup.Yellow : AdviceTargetGroup.Open
                : AdviceTargetGroup.Open;
        bool reviewedTablePublished = command.Input.Input is AcceptSettledInventory or CorrectSettledInventory;
        var updated = ClearShotCatalog(state) with { Version = checked(state.Version + 1), Game = game.State, Match = MatchView.From(game), Advice = new(group),
            GameTableCurrent = reviewedTablePublished
                ? !game.State.Table.Uncertain && game.State.Table.Motion == TableMotion.Settled
                : command.Input.Input is not (StartMatch or StartFrame or SelectSource or FinishMatch or UndoDecision or ApplyShotRuling)
                    && state.GameTableCurrent && !game.State.Table.Uncertain && game.State.Table.Motion == TableMotion.Settled };
        Volatile.Write(ref state, updated); Notify(state);
        return Receipt(envelope, CommandOutcome.Applied, proposal.Message);
    }

    SkyEyeState AcceptGameObservation(SkyEyeState next, TableObservation observation, Guid operationId, bool verified)
    {
        if (gameUnavailable || game.State.Frame is null || game.State.Frame.Winner is not null || observation.GameEvidence is null) return next;
        var source = observation.GameEvidence.Source;
        var proposed = game;
        if (proposed.State.Table.Source is { } old && old != source)
        {
            var selected = proposed.Accept(new(Guid.NewGuid(), proposed.State.Match!.Id, proposed.State.Frame!.Id, observation.CapturedAt,
                new SelectSource(), source, observation.SourceTimeUs));
            if (!selected.Accepted) return next with { GameTableCurrent = false, GameError = selected.Message };
            proposed = selected.Session;
        }
        GameInputEnvelope input;
        if (proposed.State.Table.LastSettled is null && proposed.State.Shot is null
            && observation.Outcome == ObservationOutcome.Ready && verified)
            input = GameObservationAdapter.EstablishBaseline(proposed.State, observation, operationId);
        else if (proposed.State.Table.GroupCounts.Count > 0 && proposed.State.Table.Source == source)
            // Retain A until a reviewed A/B outcome is admitted. Fresh detector IDs
            // cannot replace aggregate inventory or silently reset a shot.
            return next with { GameTableCurrent = observation.Outcome == ObservationOutcome.Ready
                && observation.GameEvidence.Balls.All(b => b.Colour != BallColour.Unknown)
                && SettledTableEvidence.TryCapture(proposed.State, out var before)
                && before.Counts == GameObservationAdapter.Counts(observation.GameEvidence) };
        else input = GameObservationAdapter.Translate(proposed.State, observation, operationId);
        var accepted = proposed.Accept(input);
        if (!accepted.Accepted) return next with { GameTableCurrent = false, GameError = accepted.Message };
        try { gameJournal?.Save(game, accepted.Session); }
        catch (IOException) { return next with { GameTableCurrent = false, GameError = "Observation received but game history could not be saved." }; }
        Volatile.Write(ref game, accepted.Session);
        return next with { Game = game.State, Match = MatchView.From(game), GameTableCurrent = !game.State.Table.Uncertain && game.State.Table.Motion == TableMotion.Settled, GameError = null };
    }
}
