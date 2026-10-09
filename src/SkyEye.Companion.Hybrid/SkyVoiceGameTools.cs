using SkyEye.Core.Application;
using SkyEye.Core.Rules;
using SkyEye.GameState;
using System.Collections.Immutable;

namespace SkyEye.Companion.Hybrid;

/// <summary>Voice adapter for explicit, versioned declarations over canonical GameState.</summary>
public sealed class SkyVoiceGameTools(
    Func<SkyEyeState> current,
    Func<CommandEnvelope, CancellationToken, Task<CommandReceipt>> dispatch,
    Func<string, string, Guid> operationId,
    Func<(bool Available, string Status)>? physicsAvailability = null,
    Func<SettledTableCandidateDisplay?>? liveOutcomeProposal = null,
    Func<Guid, string, int?, int?, int?, int?, CancellationToken, Task<VisionDashboardOutcomeReviewResponse>>? reviewLiveOutcome = null)
{
    public object Read(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var state = current();
        var match = state.Match;
        var game = state.Game;
        var frame = game.Frame;
        var physics = physicsAvailability?.Invoke();
        var outcomeProposal = liveOutcomeProposal?.Invoke();
        return new
        {
            available = match.Id is not null,
            canManage = state.CanManageMatch,
            physicsAvailable = physics?.Available,
            physicsStatus = physics?.Status,
            appVersion = state.Version,
            gameVersion = game.Version,
            error = state.GameError,
            match = match.Id is null ? null : new
            {
                id = match.Id,
                frameId = match.FrameId,
                target = match.Target,
                players = match.Players.Select(player => new
                {
                    player.Name,
                    player.Score,
                    group = player.Group,
                    groupSource = frame?.Colours.TryGetValue(player.Id, out var colour) == true ? colour.Source.ToString() : "Unknown"
                }).ToArray(),
                currentPlayer = frame is null ? null : match.Name(frame.CurrentPlayer.Value),
                turnSource = frame?.CurrentPlayer.Source.ToString() ?? "Unknown",
                foul = match.Foul,
                winner = match.Winner is null ? null : match.Name(match.Winner),
                finished = match.Finished,
                status = match.Status,
                undoInputId = match.UndoInputId,
                correctableShotInputId = match.CorrectableShotInputId,
                latestShotCorrection = match.CorrectableShotInputId is null ? null
                    : "The latest shot can be corrected. If the player reports that it was a standard foul, use record_declared_shot with standard-foul legality, the same pot counts, and replace_latest=true. This re-evaluates from before that shot; do not only flip the current foul flag.",
                rules = match.Rules
            },
            table = new
            {
                motion = game.Table.Motion.ToString(),
                uncertain = game.Table.Uncertain,
                current = state.GameTableCurrent,
                fresh = state.HasFreshGameTable(DateTimeOffset.UtcNow),
                settledAt = game.Table.LastSettled?.At,
                inventory = new
                {
                    reds = game.Table.Remaining(BallColour.Red),
                    yellows = game.Table.Remaining(BallColour.Yellow),
                    cueBalls = game.Table.Remaining(BallColour.Cue),
                    blackBalls = game.Table.Remaining(BallColour.Black),
                    identityFree = game.Table.GroupCounts.Count > 0
                },
                balls = game.Table.Balls.Values.OrderBy(ball => ball.Id).Select(ball => new
                {
                    ball.Id,
                    colour = ball.Colour.ToString(),
                    presence = ball.Presence.Value.ToString(),
                    presenceSource = ball.Presence.Source.ToString(),
                    ball.Position
                }).ToArray()
            },
            pendingTableChange = outcomeProposal is null ? null : new
            {
                candidateId = outcomeProposal.ActivityWindowId,
                status = outcomeProposal.Recommendation?.Kind == "settled-no-pot-shot"
                    ? "stable same-count layout change; legal no-pot is the default, and a reported foul can correct the latest shot"
                    : "awaiting explicit settled inventory review",
                outcomeProposal.Recommendation,
                before = outcomeProposal.Before,
                observed = outcomeProposal.After,
                outcomeProposal.RulesAdvice,
                choices = outcomeProposal.Recommendation?.Kind == "settled-no-pot-shot"
                    ? new[] { "standard-foul-no-pot", "loss-of-turn-no-pot", "correct", "reject" }
                    : new[] { "accept", "correct", "reject" },
                limitation = "Vision supplies settled count and position evidence, not first contact or foul evidence. Treat the shot as legal by default; if the player reports a foul, correct the latest ruling. Pocket identity is not observed."
            },
            limitation = "Stable settled count changes are recorded under the match's pot policy; stable same-count layout changes default to a legal no-pot shot. The player can report an exception, such as a foul, for Sky to correct."
        };
    }

    public async Task<object> ReviewSettledTableChangeAsync(Guid candidateId, string action,
        int? reds, int? yellows, int? cueBalls, int? blackBalls, CancellationToken cancellation)
    {
        var state = current();
        if (!state.CanManageMatch) return Error("This phone must be the camera host to review the table change.", state.Game.Version);
        if (candidateId == Guid.Empty) return Error("Read the current match state and use its live candidate ID.", state.Game.Version);
        var normalizedAction = action?.Trim().ToLowerInvariant() ?? "";
        if (normalizedAction is not ("accept" or "correct" or "reject" or "legal-no-pot"
            or "standard-foul-no-pot" or "loss-of-turn-no-pot"))
            return Error("Choose accept, correct, reject, legal-no-pot, standard-foul-no-pot or loss-of-turn-no-pot.", state.Game.Version);
        if (normalizedAction == "correct"
            && (reds is < 0 or > 7 || yellows is < 0 or > 7 || cueBalls is < 0 or > 1 || blackBalls is < 0 or > 1))
            return Error("Count correction needs 0–7 reds/yellows and 0 or 1 cue ball/black.", state.Game.Version);
        if (reviewLiveOutcome is null) return Error("Live table review is unavailable.", state.Game.Version);
        var result = await reviewLiveOutcome(candidateId, normalizedAction, reds, yellows, cueBalls, blackBalls, cancellation);
        return new { succeeded = result.Accepted, gameVersion = current().Game.Version, message = result.Message };
    }

    public async Task<object> StartMatchAsync(string callId, long expectedGameVersion, string firstName, string secondName, int target, CancellationToken cancellation)
    {
        var state = current();
        if (!CanMutate(state, expectedGameVersion, out var error)) return Error(error, state.Game.Version);
        if (!state.Match.CanStartMatch) return Error("Finish the current match before starting another.", state.Game.Version);
        firstName = firstName?.Trim() ?? "";
        secondName = secondName?.Trim() ?? "";
        if (firstName.Length is < 1 or > 40 || secondName.Length is < 1 or > 40
            || firstName.Any(char.IsControl) || secondName.Any(char.IsControl)
            || string.Equals(firstName, secondName, StringComparison.OrdinalIgnoreCase) || target is < 1 or > 100)
            return Error("Give two distinct player names and a match target from 1 to 100.", state.Game.Version);

        var matchId = TextId(operationId(callId, "match-id"));
        var setupFrameId = TextId(operationId(callId, "setup-frame-id"));
        var players = new[]
        {
            new Player(TextId(operationId(callId, "player-one-id")), firstName),
            new Player(TextId(operationId(callId, "player-two-id")), secondName)
        };
        return await SubmitInputAsync(callId, "start-match", expectedGameVersion, new StartMatch(players.ToImmutableArray(), target),
            cancellation, matchId, setupFrameId);
    }

    public async Task<object> StartFrameAsync(string callId, long expectedGameVersion, string starterName, CancellationToken cancellation)
    {
        var state = current();
        if (!CanMutate(state, expectedGameVersion, out var error)) return Error(error, state.Game.Version);
        if (!state.Match.CanStartFrame || state.Match.Id is null) return Error("Resolve the current frame before starting another.", state.Game.Version);
        if (!TryPlayer(state.Match, starterName, out var player, out error)) return Error(error, state.Game.Version);
        return await SubmitInputAsync(callId, "start-frame", expectedGameVersion, new StartFrame(player!.Id), cancellation,
            state.Match.Id, TextId(operationId(callId, "frame-id")));
    }

    public Task<object> FinishMatchAsync(string callId, long expectedGameVersion, CancellationToken cancellation)
    {
        var state = current();
        if (state.Match.Id is null || state.Match.Finished) return Task.FromResult(Error("There is no unfinished match to finish.", state.Game.Version));
        return SubmitInputAsync(callId, "finish-match", expectedGameVersion, new FinishMatch(), cancellation);
    }

    public async Task<object> DeclareColoursAsync(string callId, long expectedGameVersion, string group, string? playerName, CancellationToken cancellation)
    {
        var state = current();
        if (!CanMutate(state, expectedGameVersion, out var error)) return Error(error, state.Game.Version);
        if (!TryGroup(group, out var colour, out error)) return Error(error, state.Game.Version);
        string? playerId = null;
        if (colour is not null)
        {
            if (!TryPlayer(state.Match, playerName, out var player, out error)) return Error(error, state.Game.Version);
            playerId = player!.Id;
        }
        return await SubmitInputAsync(callId, "declare-colours", expectedGameVersion, new DeclareColours(playerId, colour), cancellation);
    }

    public async Task<object> CorrectAsync(string callId, long expectedGameVersion, string kind, string? playerName,
        string? group, bool? foul, string? ballId, string? presence, CancellationToken cancellation)
    {
        var state = current();
        if (!CanMutate(state, expectedGameVersion, out var error)) return Error(error, state.Game.Version);
        if (state.Match.Id is null || state.Match.FrameId is null) return Error("There is no active match frame to correct.", state.Game.Version);

        GameInput input;
        string action = "correct-" + kind;
        switch (kind?.Trim().ToLowerInvariant())
        {
            case "group":
                if (!TryGroup(group, out var colour, out error)) return Error(error, state.Game.Version);
                if (colour is null) input = new DeclareColours();
                else
                {
                    if (!TryPlayer(state.Match, playerName, out var groupedPlayer, out error)) return Error(error, state.Game.Version);
                    input = new DeclareColours(groupedPlayer!.Id, colour);
                }
                break;
            case "turn":
                if (!TryPlayer(state.Match, playerName, out var turnPlayer, out error)) return Error(error, state.Game.Version);
                input = new PlayerCorrection(new ChangeTurn(turnPlayer!.Id));
                break;
            case "foul":
                if (foul is null) return Error("State whether the declared foul stands.", state.Game.Version);
                input = new PlayerCorrection(new SetFoul(foul.Value));
                break;
            case "winner":
                if (!TryPlayer(state.Match, playerName, out var winner, out error)) return Error(error, state.Game.Version);
                input = new PlayerCorrection(new SetFrameWinner(winner!.Id));
                break;
            case "ball_presence":
                if (string.IsNullOrWhiteSpace(ballId) || !state.Game.Table.Balls.ContainsKey(ballId))
                    return Error("Use a current ball ID from get_match_state.", state.Game.Version);
                if (!Enum.TryParse<BallPresence>(presence, true, out var ballPresence) || ballPresence == BallPresence.Unknown)
                    return Error("Choose OnTable, Missing or Potted for the current ball.", state.Game.Version);
                input = new PlayerCorrection(new SetBallPresence(ballId, ballPresence));
                break;
            default:
                return Error("Supported corrections are group, turn, foul, winner and ball_presence.", state.Game.Version);
        }
        return await SubmitInputAsync(callId, action, expectedGameVersion, input, cancellation);
    }

    public async Task<object> UndoAsync(string callId, long expectedGameVersion, CancellationToken cancellation)
    {
        var state = current();
        if (!CanMutate(state, expectedGameVersion, out var error)) return Error(error, state.Game.Version);
        if (state.Match.Id is null || state.Match.UndoInputId is not { } undo)
            return Error("There is no current player-confirmed decision available to undo.", state.Game.Version);
        return await SubmitInputAsync(callId, "undo-match-decision", expectedGameVersion, new UndoDecision(undo), cancellation);
    }

    public async Task<object> RecordShotAsync(string callId, long expectedGameVersion, DeclaredShot facts, bool replaceLatest, CancellationToken cancellation)
    {
        var state = current();
        if (!CanMutate(state, expectedGameVersion, out var error)) return Error(error, state.Game.Version);
        if (state.Match.Id is null || state.Match.FrameId is null) return Error("There is no active match frame for that shot.", state.Game.Version);
        if (replaceLatest && state.Match.CorrectableShotInputId is null)
            return Error("There is no latest declared shot that can be replaced.", state.Game.Version);
        var command = new DeclareShotOutcome(state.Match.Id, state.Match.FrameId, expectedGameVersion, facts,
            replaceLatest ? state.Match.CorrectableShotInputId : null);
        var id = operationId(callId, replaceLatest ? "replace-shot" : "record-shot");
        var receipt = await dispatch(new(id, CommandOrigin.Sky, state.Version, command), cancellation);
        return Receipt(receipt, current());
    }

    async Task<object> SubmitInputAsync(string callId, string action, long expectedGameVersion, GameInput input,
        CancellationToken cancellation, string? matchId = null, string? frameId = null)
    {
        var state = current();
        if (!CanMutate(state, expectedGameVersion, out var error)) return Error(error, state.Game.Version);
        matchId ??= state.Match.Id;
        frameId ??= state.Match.FrameId;
        if (matchId is null || frameId is null) return Error("Start a match and frame before changing its state.", state.Game.Version);
        var id = operationId(callId, action);
        var envelope = new GameInputEnvelope(id, matchId, frameId, DateTimeOffset.UtcNow, input, Provenance: Provenance.PlayerConfirmed);
        var receipt = await dispatch(new(id, CommandOrigin.Sky, state.Version, new SubmitGameInput(envelope, expectedGameVersion)), cancellation);
        return Receipt(receipt, current());
    }

    static bool CanMutate(SkyEyeState state, long expectedGameVersion, out string error)
    {
        if (!state.CanManageMatch) error = "This phone must be the camera host to change the match.";
        else if (expectedGameVersion < 0 || state.Game.Version != expectedGameVersion)
            error = $"The match changed. Read its current state; the current game version is {state.Game.Version}.";
        else { error = ""; return true; }
        return false;
    }

    static bool TryPlayer(MatchView match, string? name, out MatchPlayerView? player, out string error)
    {
        var matches = match.Players.Where(candidate => string.Equals(candidate.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        player = matches.Length == 1 ? matches[0] : null;
        error = matches.Length switch
        {
            1 => "",
            0 => "Name one of the current players: " + string.Join(" or ", match.Players.Select(candidate => candidate.Name)) + ".",
            _ => "That name matches more than one player. Use a distinct player name."
        };
        return player is not null;
    }

    static bool TryGroup(string? group, out BallColour? colour, out string error)
    {
        colour = group?.Trim().ToLowerInvariant() switch
        {
            "red" or "reds" => BallColour.Red,
            "yellow" or "yellows" => BallColour.Yellow,
            "open" or "open table" => null,
            _ => (BallColour?)BallColour.Unknown
        };
        if (colour == BallColour.Unknown) { error = "Choose red, yellow or open table."; return false; }
        error = ""; return true;
    }

    static object Receipt(CommandReceipt receipt, SkyEyeState state) => new
    {
        succeeded = receipt.Succeeded,
        outcome = receipt.Outcome.ToString(),
        duplicate = receipt.Duplicate,
        gameVersion = state.Game.Version,
        message = receipt.Message
    };

    static object Error(string error, long gameVersion) => new { succeeded = false, outcome = "Rejected", gameVersion, error };
    static string TextId(Guid id) => id.ToString("N");
}
