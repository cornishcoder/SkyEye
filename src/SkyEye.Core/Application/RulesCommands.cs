using SkyEye.GameState;
using SkyEye.Core.Rules;

namespace SkyEye.Core.Application;

public sealed record DeclareShotOutcome(string MatchId, string FrameId, long ExpectedGameVersion,
    DeclaredShot Facts, Guid? CorrectsInputId = null) : SkyEyeCommand;

public sealed partial class AppCoordinator
{
    CommandReceipt RulesCommand(CommandEnvelope envelope, DeclareShotOutcome command)
    {
        if (envelope.Origin is not (CommandOrigin.Touch or CommandOrigin.Sky))
            return Receipt(envelope, CommandOutcome.Rejected, "A player must declare the complete shot facts.");
        if (!state.CanManageMatch) return Receipt(envelope, CommandOutcome.Rejected, "Become camera host before changing this game's rules state.");
        var existing = game.History.FirstOrDefault(e => e.InputId == envelope.OperationId);
        if (existing is not null)
        {
            bool same = existing.MatchId == command.MatchId && existing.FrameId == command.FrameId
                && existing.Event is RulesShotRecordedEvent previous && previous.Ruling.Facts == command.Facts && previous.Ruling.ReplacesInputId == command.CorrectsInputId;
            return Receipt(envelope, same ? CommandOutcome.Unchanged : CommandOutcome.Conflict,
                same ? "Shot already recorded." : "Operation ID was used with different shot facts.") with { Duplicate = same };
        }
        if (command.ExpectedGameVersion != game.State.Version) return Receipt(envelope, CommandOutcome.Stale, "The match changed; use its current shot controls.");
        if (command.MatchId != game.State.Match?.Id || command.FrameId != game.State.Frame?.Id)
            return Receipt(envelope, CommandOutcome.Stale, "Use the current match and frame.");
        var before = game.State;
        if (command.CorrectsInputId is { } target)
        {
            var suppressed = GameRevisions.Suppressed(game.History);
            var last = game.History.LastOrDefault(e => e.Event is not DecisionUndoneEvent && !suppressed.Contains(e.InputId));
            if (last?.InputId != target || last.Event is not RulesShotRecordedEvent)
                return Receipt(envelope, CommandOutcome.Rejected, "Only the latest effective shot can be replaced before later decisions.");
            var original = target;
            while (game.History.First(e => e.InputId == original).Event is RulesShotRecordedEvent { Ruling.ReplacesInputId: { } ancestor }) original = ancestor;
            before = GameSession.Restore(game.History.TakeWhile(e => e.InputId != original)).State;
        }
        RulePosition decision;
        try { decision = InternationalEightball.Evaluate(before, command.Facts); }
        catch (ArgumentException e) { return Receipt(envelope, CommandOutcome.Rejected, e.Message); }
        var input = new GameInputEnvelope(envelope.OperationId, command.MatchId, command.FrameId, sceneClock.GetUtcNow(),
            new ApplyShotRuling(new(before.Frame!.CurrentPlayer.Value, command.Facts, decision, command.CorrectsInputId)), Provenance: Provenance.PlayerConfirmed);
        var result = GameCommand(envelope with { Origin = CommandOrigin.System }, new SubmitGameInput(input, command.ExpectedGameVersion));
        return result.Succeeded ? result with { Message = decision.Reason } : result;
    }
}
