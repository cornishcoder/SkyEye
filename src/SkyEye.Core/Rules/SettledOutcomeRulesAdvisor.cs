using System.Collections.Immutable;
using SkyEye.GameState;

namespace SkyEye.Core.Rules;

/// <summary>A conditional preview from the rules evaluator. It is advice only and is never journalled.</summary>
public sealed record SettledRuleBranch(string Id, string Label, string Condition, string Consequence,
    bool Pending, string? NextPlayer, CueBallEntitlement CueBall, LegalTarget Target,
    ImmutableArray<string> Clauses);

/// <summary>Rules consequences which may follow a settled inventory proposal after missing facts are declared.</summary>
public sealed record SettledOutcomeRulesAdvice(string Status, string Summary,
    ImmutableArray<string> FactsToConfirm, ImmutableArray<SettledRuleBranch> Branches);

/// <summary>
/// Evaluates conditional rule outcomes for one settled count proposal. Detector counts never become declared shot
/// facts here; each branch explicitly states its assumptions and is not applied to canonical state.
/// </summary>
public static class SettledOutcomeRulesAdvisor
{
    public static SettledOutcomeRulesAdvice Evaluate(GameSnapshot? state, SettledInventoryComparison? comparison)
    {
        if (state?.Match is not { Finished: false } match || state.Frame is not { Winner: null } frame
            || comparison is null || !comparison.Context.Game.IsCompatibleWith(state))
            return NotReady("The proposal no longer matches the active player, frame or rules state.",
                "Refresh the table proposal before reviewing rules consequences.");

        var changes = comparison.Changes;
        int redsPotted = changes.Where(change => change.Colour == BallColour.Red).Sum(change => change.Removed);
        int yellowsPotted = changes.Where(change => change.Colour == BallColour.Yellow).Sum(change => change.Removed);
        bool groupAdded = changes.Any(change => change.Colour is BallColour.Red or BallColour.Yellow && change.Added > 0);
        bool cueChanged = changes.Any(change => change.Colour == BallColour.Cue);
        bool blackChanged = changes.Any(change => change.Colour == BallColour.Black);

        if (groupAdded || cueChanged || blackChanged
            || redsPotted + yellowsPotted == 0 && !comparison.LayoutChanged)
            return NotReady("These table changes do not establish a pot or a changed settled layout, so SkyEye will not suggest a turn or foul result.",
                "Confirm what physically happened, then correct or review the table.");

        string shooter = frame.CurrentPlayer.Value;
        string shooterName = match.Players.FirstOrDefault(player => player.Id == shooter)?.Name ?? "Current player";
        bool needsBreakCheck = frame.Rules?.BreakComplete != true;
        var questions = ImmutableArray.CreateBuilder<string>();
        var branches = ImmutableArray.CreateBuilder<SettledRuleBranch>(3);
        if (redsPotted + yellowsPotted == 0)
        {
            questions.Add("Default: treat the stable changed layout as a legal no-pot shot and pass the turn. The player can report a foul or other exception; first contact is not visible to the camera.");
            if (needsBreakCheck) questions.Add("Confirm whether this was the break; these previews assume an ordinary shot.");

            var noPotFacts = new DeclaredShot(Break: false, Legality: DeclaredLegality.Unknown,
                RedsBefore: comparison.Context.Before.Counts.Reds,
                YellowsBefore: comparison.Context.Before.Counts.Yellows);
            AddBranch("legal-no-pot", "If legal with no pot", "ordinary shot; no ball potted; legal first contact and required cushion",
                noPotFacts with { Legality = DeclaredLegality.Legal });
            AddBranch("standard-foul-no-pot", "If it was a standard foul", "ordinary shot; no ball potted; standard foul",
                noPotFacts with { Legality = DeclaredLegality.StandardFoul });
            AddBranch("loss-of-turn-no-pot", "If it was a loss of turn", "ordinary shot; no ball potted; non-foul loss of turn",
                noPotFacts with { Legality = DeclaredLegality.LossOfTurn });

            return new("default-legal", $"Assume a legal no-pot shot for {shooterName}; the turn passes unless the player reports an exception.",
                questions.ToImmutable(), branches.ToImmutable());
        }

        questions.Add("Confirm the removed red/yellow ball(s) were potted; the camera only proves the settled count changed.");
        if (needsBreakCheck) questions.Add("Confirm whether this was the break; these previews assume an ordinary shot.");
        questions.Add("Confirm legality and first contact before recording the shot outcome.");
        if (frame.OpenTable)
            questions.Add("The table is open: confirm whether a group call was required and, if so, the called group and first contact.");

        var before = comparison.Context.Before.Counts;
        var candidateFacts = new DeclaredShot(Break: false, Legality: DeclaredLegality.Unknown,
            RedsPotted: redsPotted, YellowsPotted: yellowsPotted,
            RedsBefore: before.Reds, YellowsBefore: before.Yellows);

        AddBranch("legal", "If the shot was legal", "ordinary shot; removed group balls were potted; legal shot",
            candidateFacts with { Legality = DeclaredLegality.Legal });
        AddBranch("foul", "If it was a standard foul", "ordinary shot; removed group balls were potted; standard foul",
            candidateFacts with { Legality = DeclaredLegality.StandardFoul });
        AddBranch("loss-of-turn", "If it was a loss of turn", "ordinary shot; removed group balls were potted; loss of turn, not a foul",
            candidateFacts with { Legality = DeclaredLegality.LossOfTurn });

        return new("conditional", $"Possible consequences for {shooterName}; these are conditional previews, not recorded facts.",
            questions.ToImmutable(), branches.ToImmutable());

        void AddBranch(string id, string label, string condition, DeclaredShot facts)
        {
            try
            {
                var ruling = InternationalEightball.Evaluate(state, facts);
                string? nextPlayer = match.Players.FirstOrDefault(player => player.Id == ruling.Player)?.Name;
                string consequence = ruling.Pending
                    ? "More facts needed: " + ruling.Reason
                    : FormatConsequence(match, ruling);
                branches.Add(new(id, label, condition, consequence, ruling.Pending, nextPlayer,
                    ruling.CueBall, ruling.Target, ruling.Clauses));
            }
            catch (ArgumentException exception)
            {
                branches.Add(new(id, label, condition, "Rule preview unavailable: " + exception.Message,
                    true, null, CueBallEntitlement.None, LegalTarget.Unknown, []));
            }
        }
    }

    static SettledOutcomeRulesAdvice NotReady(string summary, string question)
        => new("review-needed", summary, [question], []);

    static string FormatConsequence(MatchState match, RulePosition ruling)
    {
        var player = match.Players.FirstOrDefault(candidate => candidate.Id == ruling.Player)?.Name ?? "Unknown player";
        var entitlement = ruling.CueBall switch
        {
            CueBallEntitlement.Anywhere => " · cue ball in hand anywhere",
            CueBallEntitlement.Baulk => " · cue ball in hand from baulk",
            _ => ""
        };
        var target = ruling.Target switch
        {
            LegalTarget.OpenColours => " · groups remain open",
            LegalTarget.Red => " · reds next",
            LegalTarget.Yellow => " · yellows next",
            LegalTarget.Black => " · black next",
            LegalTarget.Finished => " · frame result",
            _ => ""
        };
        var winner = ruling.Winner is null ? "" : $" · frame winner: {match.Players.FirstOrDefault(candidate => candidate.Id == ruling.Winner)?.Name ?? "Unknown player"}";
        return $"{ruling.Reason} Proposed next player: {player}{entitlement}{target}{winner}";
    }
}
