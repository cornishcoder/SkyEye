using System.Collections.Immutable;
using SkyEye.GameState;

namespace SkyEye.Core.Rules;

/// <summary>Pure downstream consumer of canonical state and player-declared facts.</summary>
public static class InternationalEightball
{
    public const string Profile = "wepf-international-2c-2023-05-v1";
    public const string Name = "International Eightball 2c · May 2023";
    public const string Source = "https://www.wepf.org/docs/International%20Eightball%20Rules%20(Revision%202c)%20-%20A4%20Booklet.pdf";

    public static RulePosition Evaluate(GameSnapshot state, DeclaredShot shot)
    {
        if (state.Match is null || state.Frame is null || state.Frame.Winner is not null || state.Match.Finished) throw new ArgumentException("Use an active frame.");
        if (shot is null || !Enum.IsDefined(shot.Legality) || shot.RedsPotted is < 0 or > 7 || shot.YellowsPotted is < 0 or > 7
            || shot.RedsBefore is < 0 or > 7 || shot.YellowsBefore is < 0 or > 7 || shot.FirstContact is { } first && !Enum.IsDefined(first)
            || shot.CalledGroup is { } called && called is not (BallColour.Red or BallColour.Yellow)
            || shot.CueBallPotted && shot.CueBallOffTable) throw new ArgumentException("Check the declared counts and shot facts.");
        var frame = state.Frame;
        string shooter = frame.CurrentPlayer.Value, opponent = state.Match.Players.Single(p => p.Id != shooter).Id;
        BallColour? group = frame.Colours.TryGetValue(shooter, out var assigned) ? assigned.Value : null;
        int? redBefore = shot.RedsBefore ?? frame.Rules?.Reds, yellowBefore = shot.YellowsBefore ?? frame.Rules?.Yellows;
        if (redBefore < shot.RedsPotted || yellowBefore < shot.YellowsPotted) throw new ArgumentException("Potted counts exceed the confirmed remaining balls.");
        int? reds = redBefore - shot.RedsPotted, yellows = yellowBefore - shot.YellowsPotted;
        bool breakComplete = frame.Rules?.BreakComplete ?? false;
        RulePosition Result(string player, bool? foul, CueBallEntitlement hand, string? winner, bool pending, string reason, params string[] clauses)
        {
            var playerGroup = group is null ? null : player == shooter ? group : group == BallColour.Red ? BallColour.Yellow : BallColour.Red;
            int? remaining = playerGroup == BallColour.Red ? reds : playerGroup == BallColour.Yellow ? yellows : null;
            var target = winner is not null ? LegalTarget.Finished : pending ? LegalTarget.Unknown : playerGroup is null ? LegalTarget.OpenColours
                : remaining == 0 ? LegalTarget.Black : playerGroup == BallColour.Red ? LegalTarget.Red : LegalTarget.Yellow;
            return new RulePosition(Profile, player, group, reds, yellows, hand, target, foul, winner, breakComplete, pending, reason, clauses.ToImmutableArray())
            { BlackNeedsRespot = shot.Break && shot.BlackPotted && shot.LegalBreak == true };
        }
        RulePosition Pending(string reason, params string[] clauses) => Result(shooter, null, CueBallEntitlement.None, null, true, reason, clauses);
        if (shot.Break)
        {
            if (breakComplete) throw new ArgumentException("A break is already recorded. Undo or correct it before recording another.");
            if (!frame.OpenTable) return Pending("A break needs an open table; review the declared colours.", "4h");
            if (shot.LegalBreak != true) return Pending(shot.LegalBreak == false ? "Re-rack required; opponent chooses who breaks. Resolve this manually." : "Declare whether the break was legal.", "4f", "4g");
            if (shot.Legality == DeclaredLegality.Unknown) return Pending("Declare any other break foul before changing the turn.", "4j");
            if (shot.Legality == DeclaredLegality.LossOfFrame) return Pending("Break frame-loss declarations need manual referee review.", "4");
            breakComplete = true;
            if (shot.CueBallPotted && shot.Legality != DeclaredLegality.StandardFoul)
                return Result(opponent, false, CueBallEntitlement.Baulk, null, false, "Incoming player has one visit with cue ball in hand from baulk.", "4j(1)");
            if (shot.CueBallOffTable || shot.Legality == DeclaredLegality.StandardFoul)
                return Result(opponent, true, CueBallEntitlement.Anywhere, null, false, "Incoming player has one visit with cue ball in hand anywhere.", "4j(2)", "6e");
            bool continueBreak = shot.Legality == DeclaredLegality.Legal && shot.RedsPotted + shot.YellowsPotted > 0;
            return Result(continueBreak ? shooter : opponent, false, CueBallEntitlement.None, null, false,
                shot.BlackPotted ? "Re-spot the black; groups remain open." : "Legal break recorded; groups remain open.", "4h", "4i");
        }
        breakComplete = true; // The player may start recording in the middle of an existing frame.
        if (shot.Legality == DeclaredLegality.LossOfFrame)
            return Result(opponent, true, CueBallEntitlement.None, opponent, false, "Declared frame-loss foul awards the frame to the opponent.", "6f");
        bool foul = shot.CueBallPotted || shot.CueBallOffTable || shot.Legality == DeclaredLegality.StandardFoul;
        if (!foul && shot.Legality == DeclaredLegality.Legal && group is not null && shot.FirstContact is { } contact)
        {
            int? beforeOwn = group == BallColour.Red ? redBefore : yellowBefore;
            if (contact == BallColour.Black && beforeOwn is null)
                return Pending("Confirm the group's before-count to establish whether black was on at first contact.", "3", "6p");
            var on = beforeOwn == 0 ? BallColour.Black : group;
            if (contact != on) throw new ArgumentException("The declared first contact contradicts legal-shot status. Review the legality or contact.");
        }
        if (shot.BlackPotted)
        {
            if (foul || shot.Legality == DeclaredLegality.LossOfTurn)
                return Result(opponent, true, CueBallEntitlement.None, opponent, false, "Black potted on a foul or non-legal winning shot; opponent wins.", "6f(3)");
            int? ownBefore = group == BallColour.Red ? redBefore : group == BallColour.Yellow ? yellowBefore : null;
            if (ownBefore > 0) return Result(opponent, true, CueBallEntitlement.None, opponent, false, "The group was not cleared before the black shot; opponent wins.", "3", "6f(4)");
            if (group is null || ownBefore is null || shot.Legality == DeclaredLegality.Unknown)
                return Pending("Confirm assigned colours, cleared group before this shot and complete shot legality before awarding the black.", "3", "7");
            return Result(shooter, false, CueBallEntitlement.None, shooter, false, "A separately potted legal black wins the frame.", "3", "7");
        }
        if (foul) return Result(opponent, true, CueBallEntitlement.Anywhere, null, false, "Standard foul: opponent has one visit with cue ball in hand anywhere.", "6e");
        if (shot.Legality == DeclaredLegality.Unknown) return Pending("Shot legality is unresolved; turn and foul decisions remain pending.", "6p");
        if (shot.Legality == DeclaredLegality.LossOfTurn) return Result(opponent, false, CueBallEntitlement.None, null, false, "Declared loss of turn; cue ball stays where it lies.", "6d");
        if (group is null && shot.RedsPotted + shot.YellowsPotted > 0)
        {
            if (shot.CallRequired is null) return Pending("Confirm whether the open-table group needed a call.", "6a(5)");
            if (shot.CallRequired == true)
            {
                if (shot.CalledGroup is null) return Result(opponent, false, CueBallEntitlement.None, null, false, "Required group call was omitted; table remains open.", "6a(5b)");
                if (shot.FirstContact is null) return Pending("Confirm first contact with the called group.", "6a(5c)");
                if (shot.FirstContact != shot.CalledGroup || (shot.CalledGroup == BallColour.Red ? shot.RedsPotted : shot.YellowsPotted) == 0)
                    return Result(opponent, false, CueBallEntitlement.None, null, false, "The called group was not legally potted; table remains open.", "6a(5c-d)");
                group = shot.CalledGroup;
            }
            else if (shot.RedsPotted > 0 && shot.YellowsPotted > 0)
            {
                if (shot.FirstContact is not (BallColour.Red or BallColour.Yellow)) return Pending("Both groups were potted; confirm the first contacted group.", "6a(4)");
                group = shot.FirstContact;
            }
            else group = shot.RedsPotted > 0 ? BallColour.Red : BallColour.Yellow;
        }
        bool ownPot = group == BallColour.Red ? shot.RedsPotted > 0 : group == BallColour.Yellow && shot.YellowsPotted > 0;
        return Result(ownPot ? shooter : opponent, false, CueBallEntitlement.None, null, false,
            ownPot ? "Legal on-group pot: player continues." : "No on-group pot: opponent takes the table.", "6a", "6b", "6c", "6d");
    }
}
