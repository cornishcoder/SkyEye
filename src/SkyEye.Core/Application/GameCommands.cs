using SkyEye.GameState;

namespace SkyEye.Core.Application;

public sealed record SubmitGameInput(GameInputEnvelope Input) : SkyEyeCommand;

public sealed partial class AppCoordinator
{
    readonly IGameJournal? gameJournal;
    GameSession game = GameSession.Empty;
    readonly bool gameUnavailable;

    // The existing application writer serializes game proposals, journal publication and notifications.
    CommandReceipt GameCommand(CommandEnvelope envelope, SubmitGameInput command)
    {
        if (gameUnavailable) return Receipt(envelope, CommandOutcome.Failed, state.GameError!);
        if (command.Input is null || envelope.OperationId != command.Input.Id || !state.Camera.CanControl
            || envelope.Origin == CommandOrigin.Vision && (command.Input.Input is PlayerCorrection or StartMatch or StartFrame
                || command.Input.Provenance is Provenance.PlayerConfirmed or Provenance.PlayerCorrected)
            || command.Input.Source is { } scope && (scope.Camera != state.Camera.CameraIdentity
                || scope.Connection is not null && scope.Connection != state.Camera.ConnectionId.ToString("N")))
            return Receipt(envelope, CommandOutcome.Rejected, "Only the host can submit an explicit game command with its matching operation ID.");
        var proposal = game.Accept(command.Input);
        if (proposal.Outcome == InputOutcome.Duplicate) return Receipt(envelope, CommandOutcome.Unchanged, proposal.Message) with { Duplicate = true };
        if (proposal.Outcome != InputOutcome.Applied) return Receipt(envelope, proposal.Outcome switch
        { InputOutcome.Stale => CommandOutcome.Stale, InputOutcome.Conflict => CommandOutcome.Conflict, _ => CommandOutcome.Rejected }, proposal.Message);
        if (envelope.ExpectedVersion != state.Version) return Receipt(envelope, CommandOutcome.Stale, "The application changed; submit against its current version.");
        try { gameJournal?.Save(game, proposal.Session); }
        catch (IOException) { return Receipt(envelope, CommandOutcome.Failed, "Game history could not be saved. Previous state retained."); }
        game = proposal.Session;
        var updated = ClearShotCatalog(state) with { Version = checked(state.Version + 1), Game = game.State,
            GameTableCurrent = command.Input.Input is not (StartMatch or StartFrame or SelectSource)
                && state.GameTableCurrent && !game.State.Table.Uncertain && game.State.Table.Motion == TableMotion.Settled };
        Volatile.Write(ref state, updated); Notify(state);
        return Receipt(envelope, CommandOutcome.Applied, proposal.Message);
    }

    SkyEyeState AcceptGameObservation(SkyEyeState next, TableObservation observation, Guid operationId)
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
        var input = GameObservationAdapter.Translate(proposed.State, observation, operationId);
        var accepted = proposed.Accept(input);
        if (!accepted.Accepted) return next with { GameTableCurrent = false, GameError = accepted.Message };
        try { gameJournal?.Save(game, accepted.Session); }
        catch (IOException) { return next with { GameTableCurrent = false, GameError = "Observation received but game history could not be saved." }; }
        game = accepted.Session;
        return next with { Game = game.State, GameTableCurrent = !game.State.Table.Uncertain && game.State.Table.Motion == TableMotion.Settled, GameError = null };
    }
}
