using SkyEye.GameState;

namespace SkyEye.Core.Rules;

/// <summary>
/// Applies the product assumption that a stable settled disappearance is a pot.
/// It infers the shot facts that follow from the count change, then delegates the
/// turn/frame consequence to the rules engine. Pocket and exact ball identity are
/// deliberately left unknown.
/// </summary>
public static class SettledOutcomeRuleInference
{
    public static bool TryInfer(GameSnapshot? state, SettledInventoryComparison? comparison,
        out RuleRuling ruling)
    {
        ruling = null!;
        if (state?.Match is not { Finished: false } match || state.Frame is not { Winner: null } frame
            || state.Table.Uncertain || comparison is null
            || !comparison.Context.Game.IsCompatibleWith(state)
            || state.Table.Source != comparison.Context.Before.Source
            || state.Table.LastSettled?.EvidenceId != comparison.Context.Before.EvidenceId) return false;

        var before = comparison.Context.Before.Counts;
        var after = comparison.After.Counts;
        if (!before.IsComplete || !after.IsComplete || comparison.AddedToTable > 0 || comparison.RemovedFromTable == 0)
            return false;

        int reds = comparison.Changes.Where(change => change.Colour == BallColour.Red).Sum(change => change.Removed);
        int yellows = comparison.Changes.Where(change => change.Colour == BallColour.Yellow).Sum(change => change.Removed);
        bool black = before.BlackBalls > after.BlackBalls;
        bool cue = before.CueBalls > after.CueBalls;
        if (reds + yellows + (black ? 1 : 0) + (cue ? 1 : 0) == 0) return false;

        string shooter = frame.CurrentPlayer.Value;
        BallColour? group = frame.Colours.TryGetValue(shooter, out var assigned) ? assigned.Value : frame.Rules?.PlayerGroup;
        int? ownBefore = group switch
        {
            BallColour.Red => before.Reds,
            BallColour.Yellow => before.Yellows,
            _ => null
        };
        bool blackWasOn = (group is BallColour.Red or BallColour.Yellow) && ownBefore == 0;

        BallColour? firstContact = null;
        if (black && blackWasOn) firstContact = BallColour.Black;
        else if ((reds > 0) != (yellows > 0))
        {
            firstContact = frame.OpenTable
                ? reds > 0 ? BallColour.Red : BallColour.Yellow
                : group ?? (reds > 0 ? BallColour.Red : BallColour.Yellow);
        }

        // A black removed before the shooter's group is cleared loses the frame.
        // Otherwise assume a normal legal shot; a missing cue ball is separately
        // represented as a scratch and the rules evaluator applies the foul.
        bool earlyBlack = black && (group is BallColour.Red or BallColour.Yellow) && ownBefore is > 0;
        var legality = earlyBlack
            ? DeclaredLegality.LossOfFrame
            : DeclaredLegality.Legal;
        var facts = new DeclaredShot(Break: false, Legality: legality,
            RedsPotted: reds, YellowsPotted: yellows, BlackPotted: black,
            CueBallPotted: cue,
            FirstContact: firstContact,
            CallRequired: frame.OpenTable && reds + yellows > 0 ? false : null,
            RedsBefore: before.Reds, YellowsBefore: before.Yellows);

        try
        {
            var position = InternationalEightball.Evaluate(state, facts);
            ruling = new(shooter, facts, position);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
