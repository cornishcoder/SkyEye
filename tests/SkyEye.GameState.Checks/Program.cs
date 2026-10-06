using System.Collections.Immutable;
using SkyEye.GameState;

var players = ImmutableArray.Create(new Player("rich", "Rich"), new Player("mike", "Mike"));
var declaration = new GameInputEnvelope(Guid.NewGuid(), "match", "frame", DateTimeOffset.UnixEpoch,
    new StartMatch(players, 3), Provenance: Provenance.PlayerConfirmed);
var frame = new FrameState("frame", new("rich", Provenance.PlayerConfirmed, null, declaration.Id), 1,
    ImmutableDictionary<string, Fact<BallColour?>>.Empty, null, null, ImmutableDictionary<Guid, Pot>.Empty);
Check(frame.OpenTable && frame.CurrentPlayer.Value == "rich" && frame.Foul is null, "Open table and unknown foul are explicit");
var ball = new KnownBall("Y3", BallColour.Yellow, new(.4, .5), new(BallPresence.OnTable, Provenance.Observed, .9, declaration.Id));
var table = TableState.Empty with { Balls = TableState.Empty.Balls.Add(ball.Id, ball), Motion = TableMotion.Settled, Uncertain = false };
Check(table.Remaining(BallColour.Yellow) == 1 && table.Black == BallPresence.Unknown, "Known ball count and unknown black");
Check((table with { Balls = table.Balls.SetItem(ball.Id, ball with { Presence = ball.Presence with { Value = BallPresence.Missing } }) }).Remaining(BallColour.Yellow) is null,
    "Missing evidence is not a proven pot or reliable remaining count");
Check(typeof(GameSnapshot).Assembly.GetReferencedAssemblies().All(a => !a.Name!.Contains("Android") && !a.Name.Contains("SkyEye.Core")), "Domain has no platform/application dependency");
Console.WriteLine("PASS R01 contracts: two players, open frame, settled layout and explicit uncertainty; pure .NET");
Scenarios.Ordering();
Scenarios.PotStory();
Scenarios.Corrections();

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
