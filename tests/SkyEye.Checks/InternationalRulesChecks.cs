using SkyEye.GameState;
using SkyEye.Core.Rules;

static class InternationalRulesChecks
{
    public static void Run()
    {
        var game = GameSession.Empty;
        GameSession Apply(GameInput input)
        {
            var result = game.Accept(new(Guid.NewGuid(), "match", "frame", DateTimeOffset.UnixEpoch, input, Provenance: Provenance.PlayerConfirmed));
            Check(result.Accepted, result.Message); return game = result.Session;
        }
        Apply(new StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3)); Apply(new StartFrame("rich"));
        var open = game.State;
        RulePosition Rule(DeclaredShot shot, GameSnapshot? state = null) => InternationalEightball.Evaluate(state ?? open, shot);
        var breakPot = Rule(new(true, DeclaredLegality.Legal, RedsPotted: 1, LegalBreak: true, RedsBefore: 7, YellowsBefore: 7));
        Check(breakPot.Player == "rich" && breakPot.PlayerGroup is null && breakPot.Reds == 6, "4h: break pot retains turn without assigning groups");
        var breakBlack = Rule(new(true, DeclaredLegality.Legal, BlackPotted: true, LegalBreak: true));
        Check(breakBlack.Player == "mike" && breakBlack.Winner is null && breakBlack.BlackNeedsRespot, "4i: black-only break changes turn and needs re-spotting");
        var breakScratch = Rule(new(true, DeclaredLegality.Legal, CueBallPotted: true, LegalBreak: true));
        Check(breakScratch.CueBall == CueBallEntitlement.Baulk && breakScratch.Foul == false && breakScratch.Player == "mike", "4j(1): legal-break in-off is loss of turn with baulk placement");
        Check(Rule(new(true, DeclaredLegality.Legal, CueBallOffTable: true, LegalBreak: true)).CueBall == CueBallEntitlement.Anywhere,
            "4j(2): cue ball off-table on break gives anywhere placement");
        Check(Rule(new(true, DeclaredLegality.Legal, LegalBreak: false)).Pending, "4g: illegal break waits for re-rack/breaker choice");
        Check(Rule(new(false, DeclaredLegality.Unknown, RedsPotted: 1)).Pending, "Unknown legality cannot silently award groups or a turn");
        Check(Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, CallRequired: null)).Pending, "Unresolved required-call context remains pending");
        var single = Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, CallRequired: false));
        Check(single.PlayerGroup == BallColour.Red && single.Player == "rich", "6a: an explicit legal open-table red assigns reds");
        Check(Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, YellowsPotted: 1, CallRequired: false)).Pending,
            "6a(4): mixed open-table pots need first-contact group");
        Check(Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, YellowsPotted: 1, CallRequired: false, FirstContact: BallColour.Yellow)).PlayerGroup == BallColour.Yellow,
            "6a(4): mixed pots assign the confirmed first contacted group");
        Check(Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, CallRequired: true)).Player == "mike",
            "6a(5): an omitted required call loses turn and leaves open table");
        Check(Rule(new(false, DeclaredLegality.StandardFoul, RedsPotted: 1)).PlayerGroup is null,
            "6a(6): foul pots cannot decide groups");
        Apply(new DeclareColours("rich", BallColour.Red)); var red = game.State;
        Check(Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, YellowsPotted: 1), red).Player == "rich", "6c: legal combination with own pot continues");
        Check(Rule(new(false, DeclaredLegality.Legal, YellowsPotted: 1), red).Player == "mike", "6d: opponent-only pot loses turn without ball-in-hand");
        Check(Rule(new(false, DeclaredLegality.Legal), red).Player == "mike", "6b: legal miss changes turn");
        var scratch = Rule(new(false, DeclaredLegality.Legal, CueBallPotted: true), red);
        Check(scratch.Player == "mike" && scratch.Foul == true && scratch.CueBall == CueBallEntitlement.Anywhere, "6e: scratch gives one visit and anywhere ball-in-hand");
        Check(Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, RedsBefore: 1, YellowsBefore: 4), red).Target == LegalTarget.Black,
            "Clearing the confirmed last red makes black the next target");
        Check(Rule(new(false, DeclaredLegality.Legal, BlackPotted: true), red).Pending, "Unknown before-count cannot prove legal black eligibility");
        Check(Rule(new(false, DeclaredLegality.Legal, RedsPotted: 1, BlackPotted: true, RedsBefore: 1), red).Winner == "mike",
            "3/6f(4): last group ball and black in the same shot loses the frame");
        Check(Rule(new(false, DeclaredLegality.Legal, BlackPotted: true, RedsBefore: 0), red).Winner == "rich", "7: separately potted legal black wins");
        Check(Rule(new(false, DeclaredLegality.StandardFoul, BlackPotted: true, RedsBefore: 0), red).Winner == "mike", "6f(3): foul on black loses frame");
        try { Rule(new(false, DeclaredLegality.Legal, RedsPotted: 2, RedsBefore: 1), red); throw new Exception("Impossible count accepted"); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS International 2c declarations: break exceptions, open-table calls/groups, combinations, turn/foul, counts/black and uncertainty");
    }
    static void Check(bool yes, string message) { if (!yes) throw new Exception(message); }
}
