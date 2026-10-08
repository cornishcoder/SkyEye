using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core.Application;
using SkyEye.Core.Rules;
using SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

public sealed record DashboardScenarioSnapshot(Guid ScenarioId, string Preset, long Version,
    GameSnapshot Game, MatchView Match, int EventCount, ImmutableArray<DashboardScenarioEvent> Events,
    DashboardScenarioProposal? PendingProposal, DashboardScenarioRulePrompt? RulePrompt, string? Error);
public sealed record DashboardScenarioEvent(long Sequence, Guid InputId, string FrameId, DateTimeOffset At,
    string Provenance, string Event, bool ScenarioTest);
public sealed record DashboardScenarioChoice(string Id, string Label, string Description, bool Recommended);
public sealed record DashboardScenarioProposal(Guid Id, string Kind, string Title, string Summary, string Recommendation,
    ImmutableArray<DashboardScenarioEvent> Events, string ProposedScore, string ProposedTurn,
    ImmutableArray<DashboardScenarioChoice> Choices, RulePosition? RulePosition = null);
public sealed record DashboardScenarioRulePrompt(string ShotId, string ShooterId, string ShooterName,
    int MovedBallCount, int PotCount, bool NoPotAfterMovement, string RulesProfile);
public sealed record DashboardScenarioResetRequest(Guid ScenarioId, long ExpectedVersion, string Preset);
public sealed record DashboardScenarioResetResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);
public sealed record DashboardScenarioCommandRequest(Guid ScenarioId, long ExpectedVersion, Guid OperationId, string Action,
    string? PlayerOne = null, string? PlayerTwo = null, int Target = 3, string? PlayerId = null,
    string? Colour = null, string? BallId = null, double? X = null, double? Y = null,
    double? Confidence = null, bool? Foul = null, bool Settled = true, bool Uncertain = false,
    string? ShotId = null, string? Pocket = null);
public sealed record DashboardScenarioCommandResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);
public sealed record DashboardScenarioReviewRequest(Guid ScenarioId, long ExpectedVersion, Guid ProposalId, string ChoiceId);
public sealed record DashboardScenarioReviewResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);
public sealed record DashboardScenarioRuleRequest(Guid ScenarioId, long ExpectedVersion, Guid OperationId,
    string Legality, int RedsPotted, int YellowsPotted, bool BlackPotted = false, bool CueBallPotted = false,
    bool CueBallOffTable = false, string? FirstContact = null, bool? CallRequired = null, string? CalledGroup = null,
    int? RedsBefore = null, int? YellowsBefore = null, bool Break = false, bool? LegalBreak = null);
public sealed record DashboardScenarioRuleResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);

/// <summary>Owns a disposable, separately journaled GameState scenario for the Lab dashboard.</summary>
public sealed class DashboardScenarioWorkspace
{
    readonly object gate = new();
    readonly string root;
    readonly string activePath;
    Guid scenarioId;
    string preset = "empty";
    GameJournalStore journal = null!;
    GameSession session = GameSession.Empty;
    string? error;
    GameSession? pendingSession;
    DashboardScenarioProposal? pendingProposal;
    readonly Dictionary<string, GameSession> pendingChoices = new(StringComparer.Ordinal);
    EvidenceSource ScenarioSource => new("dashboard-scenario", "scenario-" + scenarioId.ToString("N"), 1, "scenario-table-2x1");

    public DashboardScenarioWorkspace(string directory)
    {
        root = Path.GetFullPath(directory);
        activePath = Path.Combine(root, "active-scenario.txt");
        LoadActive();
    }

    public DashboardScenarioSnapshot Snapshot()
    {
        lock (gate) return Current();
    }

    public DashboardScenarioResetResponse Reset(DashboardScenarioResetRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (request.Preset is not ("empty" or "clean-rack" or "mid-match"))
                return new(false, "Choose a supported scenario preset.", Current());

            try
            {
                var nextId = Guid.NewGuid();
                var nextJournal = Open(nextId);
                var nextSession = GameSession.Empty;
                nextSession = Seed(nextSession, nextJournal, nextId, request.Preset);
                var scenarioDirectory = Path.Combine(root, nextId.ToString("N"));
                Directory.CreateDirectory(scenarioDirectory);
                File.WriteAllText(Path.Combine(scenarioDirectory, "preset.txt"), request.Preset);
                WriteActive(nextId);
                scenarioId = nextId;
                preset = request.Preset;
                journal = nextJournal;
                session = nextSession;
                ClearPending();
                error = null;
                return new(true, "Scenario reset to " + PresetName(preset) + ". Live match unchanged.", Current());
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                return new(false, "Scenario could not be reset. The previous scenario remains available.", Current());
            }
        }
    }

    public DashboardScenarioCommandResponse Apply(DashboardScenarioCommandRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (pendingProposal is not null)
                return new(false, "Choose an action for the pending event before adding another one.", Current());
            if (request.OperationId == Guid.Empty)
                return new(false, "A unique operation ID is required.", Current());

            var matchId = session.State.Match?.Id ?? "scenario-" + scenarioId.ToString("N");
            var frameId = session.State.Frame?.Id ?? session.State.Match?.Frames.LastOrDefault()?.Id ?? "scenario-setup";
            GameInput input;
            EvidenceSource? source = null;
            long? sourceTimeUs = null;
            double? confidence = null;
            var provenance = Provenance.PlayerConfirmed;
            try
            {
                switch (request.Action)
                {
                    case "start-match":
                        if (string.IsNullOrWhiteSpace(request.PlayerOne) || string.IsNullOrWhiteSpace(request.PlayerTwo))
                            return new(false, "Enter a name for both players.", Current());
                        matchId = "scenario-" + Guid.NewGuid().ToString("N");
                        frameId = "scenario-setup";
                        input = new StartMatch([new("p1", request.PlayerOne.Trim()), new("p2", request.PlayerTwo.Trim())], request.Target);
                        break;
                    case "start-frame":
                        if (session.State.Match is not { } activeMatch)
                            return new(false, "Start a match before starting a frame.", Current());
                        var frameNumber = activeMatch.Frames.Length + 1;
                        frameId = activeMatch.Id + "-frame-" + frameNumber;
                        input = new StartFrame(request.PlayerId ?? activeMatch.Players[0].Id);
                        break;
                    case "change-turn":
                        input = new PlayerCorrection(new ChangeTurn(RequiredPlayer(request.PlayerId)));
                        break;
                    case "declare-open":
                        input = new DeclareColours();
                        break;
                    case "assign-group":
                        var group = ParseColour(request.Colour);
                        if (group is not (BallColour.Red or BallColour.Yellow))
                            return new(false, "Choose reds or yellows for the first group.", Current());
                        input = new DeclareColours(RequiredPlayer(request.PlayerId), group);
                        break;
                    case "set-foul":
                        if (request.Foul is null) return new(false, "Choose foul or no foul.", Current());
                        input = new PlayerCorrection(new SetFoul(request.Foul.Value));
                        break;
                    case "frame-winner":
                        input = new PlayerCorrection(new SetFrameWinner(RequiredPlayer(request.PlayerId)));
                        break;
                    case "finish-match":
                        input = new FinishMatch();
                        break;
                    case "undo-last":
                        if (MatchView.From(session).UndoInputId is not { } undoId)
                            return new(false, "There is no supported player decision to undo.", Current());
                        var target = session.History.Last(item => item.InputId == undoId);
                        matchId = target.MatchId;
                        frameId = target.FrameId;
                        input = new UndoDecision(undoId);
                        provenance = Provenance.PlayerCorrected;
                        break;
                    case "ball-upsert":
                        if (string.IsNullOrWhiteSpace(request.BallId) || request.X is not { } x || request.Y is not { } y
                            || !double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1
                            || request.Confidence is { } ballConfidence && (!double.IsFinite(ballConfidence) || ballConfidence is < 0 or > 1))
                            return new(false, "Enter a ball ID, table position from 0 to 1, and optional confidence from 0 to 1.", Current());
                        var colour = ParseColour(request.Colour);
                        input = new BallObserved(new(request.BallId.Trim(), colour, new(x, y), request.Confidence));
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence;
                        break;
                    case "ball-move":
                        if (string.IsNullOrWhiteSpace(request.BallId) || request.X is not { } movedX || request.Y is not { } movedY
                            || !double.IsFinite(movedX) || !double.IsFinite(movedY) || movedX is < 0 or > 1 || movedY is < 0 or > 1
                            || !session.State.Table.Balls.TryGetValue(request.BallId.Trim(), out var movedBall)
                            || movedBall.Presence.Value != BallPresence.OnTable
                            || request.Confidence is { } moveConfidence && (!double.IsFinite(moveConfidence) || moveConfidence is < 0 or > 1))
                            return new(false, "Choose a known ball and enter its new table position from 0 to 1.", Current());
                        input = new BallMoved(request.BallId.Trim(), new(movedX, movedY));
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence ?? 0.95;
                        provenance = Provenance.Observed;
                        break;
                    case "ball-missing":
                        input = new BallMissing(RequiredText(request.BallId, "Choose a ball first."));
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence ?? 0.95;
                        provenance = Provenance.Inferred;
                        break;
                    case "table-state":
                        var onTable = session.State.Table.Balls.Values
                            .Where(ball => ball.Presence.Value == BallPresence.OnTable)
                            .OrderBy(ball => ball.Id, StringComparer.Ordinal)
                            .Select(ball => new ObservedBall(ball.Id, ball.Colour, ball.Position, ball.Presence.Confidence))
                            .ToImmutableArray();
                        input = new SkyEye.GameState.TableObservation(onTable, request.Settled, request.Uncertain);
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = 0.95;
                        provenance = Provenance.PlayerConfirmed;
                        break;
                    case "shot-start":
                        input = new ShotStarted(string.IsNullOrWhiteSpace(request.ShotId) ? "scenario-shot-" + Guid.NewGuid().ToString("N") : request.ShotId.Trim());
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = 0.95;
                        break;
                    case "shot-end":
                        if (session.State.Shot is not { Ended: false } activeShot)
                            return new(false, "There is no active shot to end.", Current());
                        input = new ShotEnded(activeShot.Id);
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = 0.95;
                        break;
                    case "pocket-approach":
                        if (session.State.Shot is not { Ended: false } approachShot)
                            return new(false, "Start a shot before adding a pocket approach.", Current());
                        if (!Enum.TryParse<Pocket>(request.Pocket, true, out var pocket) || !Enum.IsDefined(pocket))
                            return new(false, "Choose a table pocket.", Current());
                        input = new PossiblePocketEvent(approachShot.Id, RequiredText(request.BallId, "Choose a ball first."), pocket);
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence ?? 0.95;
                        break;
                    default:
                        return new(false, "Choose a supported scenario action.", Current());
                }
            }
            catch (ArgumentException e)
            {
                return new(false, e.Message, Current());
            }

            var envelope = new GameInputEnvelope(request.OperationId, matchId, frameId, DateTimeOffset.UtcNow,
                input, source, sourceTimeUs, provenance, confidence);
            var result = session.Accept(envelope);
            if (result.Outcome != InputOutcome.Applied)
                return new(false, result.Message, Current());

            if (request.Action is "ball-move" or "ball-missing" or "table-state" or "shot-end" or "pocket-approach")
            {
                var added = result.Session.History.Skip(session.History.Length).ToArray();
                var hasPot = added.Any(item => item.Event is BallPottedEvent);
                GameSession? uncertain = null;
                if (hasPot && input is SkyEye.GameState.TableObservation observation)
                {
                    var alternate = session.Accept(envelope with
                    {
                        Id = Guid.NewGuid(),
                        At = DateTimeOffset.UtcNow.AddTicks(1),
                        SourceTimeUs = NextSourceTime(),
                        Input = observation with { Uncertain = true }
                    });
                    if (alternate.Outcome == InputOutcome.Applied) uncertain = alternate.Session;
                }
                pendingSession = result.Session;
                pendingProposal = CreateEvidenceProposal(request.Action, result.Session, added, hasPot, uncertain is not null);
                pendingChoices.Clear();
                pendingChoices["accept"] = result.Session;
                if (uncertain is not null) pendingChoices["keep-uncertain"] = uncertain;
                error = null;
                return new(true, "Event preview ready. Choose what the scenario should do.", Current());
            }

            try { journal.Save(session, result.Session); }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                return new(false, "The scenario could not be saved; no state change was published.", Current());
            }
            session = result.Session;
            preset = "custom";
            error = null;
            try
            {
                var presetPath = Path.Combine(root, scenarioId.ToString("N"), "preset.txt");
                File.WriteAllText(presetPath + ".tmp", preset);
                File.Move(presetPath + ".tmp", presetPath, overwrite: true);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
            { error = "The scenario state is saved; its custom label will be refreshed on the next edit."; }
            return new(true, "Scenario action recorded. Live match unchanged.", Current());
        }
    }

    public DashboardScenarioReviewResponse Resolve(DashboardScenarioReviewRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (pendingProposal is null || pendingSession is null || pendingProposal.Id != request.ProposalId)
                return new(false, "That event proposal has expired. Refresh the scenario and try again.", Current());

            if (request.ChoiceId == "edit-facts" && pendingProposal.Kind == "rules")
            {
                ClearPending();
                return new(true, "The scenario is unchanged. Review the shot facts and evaluate again.", Current());
            }

            var knownChoice = request.ChoiceId is "reject" || pendingChoices.ContainsKey(request.ChoiceId);
            var selected = pendingChoices.GetValueOrDefault(request.ChoiceId);
            if (!knownChoice)
                return new(false, "Choose one of the actions shown for this event.", Current());

            if (selected is not null)
            {
                try { journal.Save(session, selected); }
                catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
                { return new(false, "The reviewed event could not be saved; the scenario is unchanged.", Current()); }
                session = selected;
                preset = "custom";
                SavePresetLabel();
            }

            var message = request.ChoiceId switch
            {
                "accept" => pendingProposal.Kind == "rules" ? "Rules recommendation accepted into the scenario journal." : "Event accepted into the scenario journal.",
                "keep-uncertain" => "Missing evidence kept, with the table marked uncertain; no pot was recorded.",
                var choice when choice.StartsWith("turn:", StringComparison.Ordinal) => "Manual turn choice recorded in the scenario journal.",
                _ => "Proposal discarded; the scenario state is unchanged."
            };
            ClearPending();
            return new(true, message, Current());
        }
    }

    public DashboardScenarioRuleResponse EvaluateRules(DashboardScenarioRuleRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (pendingProposal is not null)
                return new(false, "Review the pending event before evaluating shot rules.", Current());
            if (request.OperationId == Guid.Empty)
                return new(false, "A unique operation ID is required.", Current());
            if (GetRulePrompt() is not { } prompt)
                return new(false, "Finish and settle a shot before evaluating its rules.", Current());
            if (!Enum.TryParse<DeclaredLegality>(request.Legality, true, out var legality) || !Enum.IsDefined(legality)
                || !ParseOptionalColour(request.FirstContact, out var firstContact)
                || !ParseOptionalColour(request.CalledGroup, out var calledGroup))
                return new(false, "Choose a supported legality, first contact and called group.", Current());

            var facts = new DeclaredShot(request.Break, legality, request.RedsPotted, request.YellowsPotted,
                request.BlackPotted, request.CueBallPotted, request.CueBallOffTable, request.LegalBreak,
                firstContact, request.CallRequired, calledGroup, request.RedsBefore, request.YellowsBefore);
            RulePosition ruling;
            try { ruling = InternationalEightball.Evaluate(session.State, facts); }
            catch (ArgumentException e) { return new(false, e.Message, Current()); }

            var matchId = session.State.Match!.Id;
            var frameId = session.State.Frame!.Id;
            var envelope = new GameInputEnvelope(request.OperationId, matchId, frameId, DateTimeOffset.UtcNow,
                new ApplyShotRuling(new(session.State.Frame.CurrentPlayer.Value, facts, ruling)),
                Provenance: Provenance.PlayerConfirmed);
            var result = session.Accept(envelope);
            if (result.Outcome != InputOutcome.Applied)
                return new(false, result.Message, Current());

            var added = result.Session.History.Skip(session.History.Length).ToArray();
            pendingSession = result.Session;
            var proposedMatch = MatchView.From(result.Session);
            var nextPlayer = result.Session.State.Frame?.CurrentPlayer.Value;
            var nextName = nextPlayer is null ? "No player" : proposedMatch.Name(nextPlayer);
            var choices = ImmutableArray.Create(
                new DashboardScenarioChoice("accept", ruling.Pending ? "Record pending rule decision" : "Accept rules recommendation",
                    ruling.Reason + (ruling.Clauses.Length == 0 ? "" : " · rule " + string.Join(", ", ruling.Clauses)), true),
                new DashboardScenarioChoice("reject", "Keep current state", "Discard this rule proposal and leave the frame unchanged.", false))
                .AddRange(result.Session.State.Match!.Players.Select(player => new DashboardScenarioChoice(
                    "turn:" + player.Id, "Set turn to " + player.Name, "Apply this explicit player correction instead of the rules recommendation.", false)))
                .Add(new DashboardScenarioChoice("edit-facts", "Review shot facts", "Keep the frame unchanged and adjust the declared facts.", false));
            pendingChoices.Clear();
            pendingChoices["accept"] = result.Session;
            foreach (var player in result.Session.State.Match.Players)
            {
                var correction = new GameInputEnvelope(Guid.NewGuid(), matchId, frameId, DateTimeOffset.UtcNow,
                    new PlayerCorrection(new ChangeTurn(player.Id)), Provenance: Provenance.PlayerCorrected);
                var alternate = session.Accept(correction);
                if (alternate.Outcome == InputOutcome.Applied) pendingChoices["turn:" + player.Id] = alternate.Session;
            }
            pendingProposal = new(Guid.NewGuid(), "rules", "Rule decision awaiting your choice",
                $"{prompt.ShooterName}'s shot · {prompt.MovedBallCount} ball(s) moved · {prompt.PotCount} pot(s) currently recorded.",
                ruling.Reason + (ruling.Pending ? " More declared facts may be needed." : $" Proposed next player: {nextName}."),
                added.Select(ToScenarioEvent).ToImmutableArray(), proposedMatch.Score,
                nextPlayer is null ? "—" : proposedMatch.Name(nextPlayer), choices, ruling);
            return new(true, "Rules engine preview ready. Accept, keep the current state or revise the facts.", Current());
        }
    }

    long NextSourceTime() => (session.History.LastOrDefault(item => item.Source == ScenarioSource)?.SourceTimeUs ?? 0) + 1_000_000;

    string RequiredPlayer(string? value)
    {
        if (session.State.Match is not { } match || !match.Players.Any(player => player.Id == value))
            throw new ArgumentException("Choose one of the players in this match.");
        return value!;
    }

    static string RequiredText(string? value, string message)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();

    static BallColour ParseColour(string? value)
        => Enum.TryParse<BallColour>(value, true, out var colour) && Enum.IsDefined(colour)
            ? colour : throw new ArgumentException("Choose a supported ball colour.");

    void LoadActive()
    {
        lock (gate)
        {
            Directory.CreateDirectory(root);
            if (File.Exists(activePath))
            {
                var value = File.ReadAllText(activePath).Trim();
                if (!Guid.TryParseExact(value, "N", out scenarioId))
                {
                    error = "The saved scenario index is invalid. Reset the scenario to start a new one.";
                    scenarioId = Guid.NewGuid();
                    journal = Open(scenarioId);
                    session = GameSession.Empty;
                    return;
                }
                journal = Open(scenarioId);
                try
                {
                    session = journal.Load();
                    var presetPath = Path.Combine(root, scenarioId.ToString("N"), "preset.txt");
                    preset = File.Exists(presetPath) ? File.ReadAllText(presetPath).Trim() : "empty";
                }
                catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
                {
                    session = GameSession.Empty;
                    error = "Saved scenario history is unavailable. Reset it to create a clean Lab scenario.";
                }
                return;
            }

            scenarioId = Guid.NewGuid();
            journal = Open(scenarioId);
            session = GameSession.Empty;
            preset = "empty";
            WriteActive(scenarioId);
        }
    }

    DashboardScenarioSnapshot Current() => new(scenarioId, preset, session.State.Version,
        session.State, MatchView.From(session), session.History.Length,
        session.History.TakeLast(40).Reverse().Select(ToScenarioEvent).ToImmutableArray(),
        pendingProposal, GetRulePrompt(), error);

    DashboardScenarioProposal CreateEvidenceProposal(string action, GameSession proposed, EventEnvelope[] added,
        bool inferredPot, bool hasUncertainChoice)
    {
        var events = added.Select(ToScenarioEvent).ToImmutableArray();
        var pots = added.Where(item => item.Event is BallPottedEvent)
            .Select(item => (BallPottedEvent)item.Event).ToArray();
        var summary = action switch
        {
            "ball-missing" => "A ball disappeared from the observed inventory. This is missing evidence, not proof of a pot.",
            "pocket-approach" => "A ball approached a pocket during the active shot. This is evidence only.",
            "ball-move" => "A new position was recorded for a ball during the scenario.",
            "shot-end" => "The active shot was marked ended. The table must settle before an outcome can be inferred.",
            _ => "A table-state observation was checked by the canonical GameState reducer."
        };
        var recommendation = inferredPot
            ? "GameState can infer a pot from the missing ball, pocket approach, shot end and settled certain table. Review the evidence before recording it."
            : action == "ball-missing"
                ? "Keep the ball in the inventory as Missing and mark the table uncertain, or reject the observation."
                : action == "table-state" && proposed.State.Shot is { Ended: true, Resolved: true }
                    ? "The shot resolved with no pot recorded. Review the shot facts to ask the rules engine whether the turn changes."
                    : "Review the evidence before it enters the scenario history.";
        var choices = ImmutableArray.CreateBuilder<DashboardScenarioChoice>();
        choices.Add(new("accept", inferredPot ? "Accept inferred pot" : action == "ball-missing" ? "Accept missing evidence" : "Accept event",
            inferredPot ? string.Join(" · ", pots.Select(pot => $"{pot.BallId} to {pot.Pocket}")) : "Record the proposed typed GameState event.", true));
        if (hasUncertainChoice)
            choices.Add(new("keep-uncertain", "Keep it missing; mark table uncertain", "Save the missing-ball evidence without inferring a pot or resolving the shot.", false));
        choices.Add(new("reject", "Reject event", "Keep the current scenario state and discard this observation.", false));
        var match = MatchView.From(proposed);
        return new(Guid.NewGuid(), "evidence", "Game event awaiting your choice", summary, recommendation,
            events, match.Score, match.CurrentPlayer is null ? "—" : match.Name(match.CurrentPlayer), choices.ToImmutable());
    }

    DashboardScenarioRulePrompt? GetRulePrompt()
    {
        if (pendingProposal is not null || session.State.Shot is not { Ended: true, Resolved: true } shot
            || session.State.Frame is null || session.State.Match is null) return null;
        var completed = session.History.LastOrDefault(item => item.Event is ShotResolvedEvent && item.FrameId == session.State.Frame.Id);
        if (completed is null || session.History.Any(item => item.Sequence > completed.Sequence && item.FrameId == completed.FrameId
            && item.Event is RulesShotRecordedEvent or TurnChangedEvent or FrameWonEvent)) return null;
        var moved = shot.Before.Values.Count(before => before.Presence.Value == BallPresence.OnTable
            && session.State.Table.Balls.TryGetValue(before.Id, out var after) && after.Presence.Value == BallPresence.OnTable
            && before.Position is { } oldPosition && after.Position is { } newPosition
            && Math.Sqrt(Math.Pow(oldPosition.X - newPosition.X, 2) + Math.Pow(oldPosition.Y - newPosition.Y, 2)) > .005);
        var pots = session.State.Frame.Pots.Values.Count(pot => pot.ShotId == shot.Id);
        var shooter = session.State.Frame.CurrentPlayer.Value;
        return new(shot.Id, shooter, MatchView.From(session).Name(shooter), moved, pots, moved > 0 && pots == 0,
            InternationalEightball.Profile);
    }

    static DashboardScenarioEvent ToScenarioEvent(EventEnvelope item)
        => new(item.Sequence, item.InputId, item.FrameId, item.At, item.Provenance.ToString(), item.Event.GetType().Name, true);

    static bool ParseOptionalColour(string? value, out BallColour? colour)
    {
        colour = null;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Enum.TryParse<BallColour>(value, true, out var parsed) || !Enum.IsDefined(parsed)) return false;
        colour = parsed;
        return true;
    }

    void ClearPending()
    {
        pendingSession = null;
        pendingProposal = null;
        pendingChoices.Clear();
    }

    void SavePresetLabel()
    {
        try
        {
            var path = Path.Combine(root, scenarioId.ToString("N"), "preset.txt");
            File.WriteAllText(path + ".tmp", preset);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        { error = "The scenario state is saved; its custom label will be refreshed on the next edit."; }
    }

    GameJournalStore Open(Guid id) => new(Path.Combine(root, id.ToString("N")));

    void WriteActive(Guid id)
    {
        var temporary = activePath + ".tmp";
        File.WriteAllText(temporary, id.ToString("N"));
        File.Move(temporary, activePath, overwrite: true);
    }

    static GameSession Seed(GameSession game, GameJournalStore store, Guid id, string selectedPreset)
    {
        if (selectedPreset == "empty") return game;
        var matchId = "scenario-" + id.ToString("N");
        var frameId = "frame-1";
        var now = DateTimeOffset.UtcNow;
        var step = 0;
        var source = new EvidenceSource("dashboard-scenario", "scenario-" + id.ToString("N"), 1, "scenario-table-2x1");

        void Apply(GameInput input, string targetFrame, EvidenceSource? evidenceSource = null, long? sourceTimeUs = null,
            Provenance provenance = Provenance.PlayerConfirmed, double? confidence = null)
        {
            var before = game;
            var envelope = new GameInputEnvelope(Guid.NewGuid(), matchId, targetFrame,
                now.AddMilliseconds(step++), input, evidenceSource, sourceTimeUs, provenance, confidence);
            var result = game.Accept(envelope);
            if (result.Outcome != InputOutcome.Applied) throw new InvalidOperationException(result.Message);
            store.Save(before, result.Session);
            game = result.Session;
        }

        Apply(new StartMatch([new Player("rich", "Rich"), new Player("mike", "Mike")], 3), frameId);
        Apply(new StartFrame("rich"), frameId);

        if (selectedPreset == "mid-match")
        {
            Apply(new PlayerCorrection(new SetFrameWinner("rich")), frameId);
            frameId = "frame-2";
            Apply(new StartFrame("mike"), frameId);
            Apply(new DeclareColours("rich", BallColour.Red), frameId);
        }

        Apply(new SelectSource(), frameId, source, 0, Provenance.Observed);
        Apply(new SkyEye.GameState.TableObservation(CleanRack(), Settled: true), frameId, source, 1_000_000,
            Provenance.Observed, confidence: 0.95);

        if (selectedPreset == "mid-match")
            Apply(new ShotStarted("scenario-shot-1"), frameId, source, 2_000_000, Provenance.Observed, confidence: 0.95);
        return game;
    }

    static ImmutableArray<ObservedBall> CleanRack()
    {
        var result = ImmutableArray.CreateBuilder<ObservedBall>(16);
        result.Add(new("cue-ball", BallColour.Cue, new(.22, .5), .95));
        var redId = 1;
        var yellowId = 1;
        var index = 0;
        var nonBlackIndex = 0;
        for (var row = 0; row < 5; row++)
        for (var column = 0; column < 5 - row; column++)
        {
            var colour = index == 7 ? BallColour.Black : nonBlackIndex++ % 2 == 0 ? BallColour.Red : BallColour.Yellow;
            var ballId = colour == BallColour.Black ? "black-8" : colour == BallColour.Red ? "red-" + redId++ : "yellow-" + yellowId++;
            var x = .68 + row * .025;
            var y = .38 + column * .05 + row * .025;
            result.Add(new(ballId, colour, new(x, y), .95));
            index++;
        }
        return result.MoveToImmutable();
    }

    static string PresetName(string value) => value switch
    {
        "clean-rack" => "clean rack",
        "mid-match" => "mid-match",
        _ => "blank"
    };
}
