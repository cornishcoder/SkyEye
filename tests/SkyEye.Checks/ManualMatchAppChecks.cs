using SkyEye.Core.Application;
using G = SkyEye.GameState;

static class ManualMatchAppChecks
{
    public static async Task Run()
    {
        var app = new AppCoordinator();
        async Task<CommandReceipt> Send(G.GameInput input, string frame = "one", long? version = null)
        {
            var evidence = new G.GameInputEnvelope(Guid.NewGuid(), "match", frame, DateTimeOffset.UnixEpoch, input, Provenance: G.Provenance.PlayerConfirmed);
            return await app.DispatchAsync(new(evidence.Id, CommandOrigin.Touch, 0, new SubmitGameInput(evidence, version ?? app.State.Game.Version)));
        }
        void Check(bool yes, string message) { if (!yes) throw new Exception(message); }
        Check((await Send(new G.StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3))).Succeeded, "Local setup uses game version independently of camera/app polling");
        Check((await Send(new G.StartFrame("rich"))).Succeeded, "First declared frame opens");
        Check((await Send(new G.DeclareColours("rich", G.BallColour.Red))).Succeeded && app.State.Advice.TargetGroup == AdviceTargetGroup.Red,
            "Current player's declared red group drives advice filter");
        Check((await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, new SetTargetGroup(AdviceTargetGroup.Yellow)))).Outcome == CommandOutcome.Rejected
            && app.State.Advice.TargetGroup == AdviceTargetGroup.Red, "Legacy group preferences cannot override the active match's canonical declaration");
        Check((await Send(new G.PlayerCorrection(new G.ChangeTurn("mike")))).Succeeded && app.State.Advice.TargetGroup == AdviceTargetGroup.Yellow,
            "Changing player projects the complementary yellow target and invalidates choices");
        Check((await Send(new G.PlayerCorrection(new G.SetFrameWinner("rich")))).Succeeded, "Award first frame");
        Check((await Send(new G.StartFrame("mike"), "two")).Succeeded && app.State.Advice.TargetGroup == AdviceTargetGroup.Open, "New frame resets only current groups");
        Check((await Send(new G.PlayerCorrection(new G.SetFrameWinner("rich")), "two")).Succeeded, "Award second frame");
        Check((await Send(new G.PlayerCorrection(new G.SetFrameWinner("mike")))).Succeeded && app.State.Match.Score == "Rich 1–1 Mike",
            "Earlier frame correction updates read-only phone projection to 1-1");
        var undo = app.State.Match.UndoInputId!.Value;
        Check((await Send(new G.UndoDecision(undo))).Succeeded && app.State.Match.Score == "Rich 2–0 Mike", "Projected undo restores the previous result");
        Check(!(await Send(new G.PlayerCorrection(new G.SetFoul(true)), version: 0)).Succeeded, "Stale rendered game version refuses mutation");
        var guest = new AppCoordinator(cameraControls: new GuestCamera());
        await guest.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 0, new ConnectCamera("camera:8765")));
        var guestInput = new G.GameInputEnvelope(Guid.NewGuid(), "guest", "frame", DateTimeOffset.UnixEpoch,
            new G.StartMatch([new("a", "A"), new("b", "B")], 2));
        Check((await guest.DispatchAsync(new(guestInput.Id, CommandOrigin.Touch, guest.State.Version, new SubmitGameInput(guestInput, 0)))).Outcome == CommandOutcome.Rejected,
            "A connected guest cannot turn local manual permission into host game mutation");
        Console.WriteLine("PASS manual match application: local setup, game-version fences, declared group/player filters, score projection and undo");
    }
    sealed class GuestCamera : ICameraControlEffect
    {
        public Task<CameraControlResult> ExecuteAsync(Guid id, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
            => Task.FromResult(new CameraControlResult(admitted with { Reachability = CameraReachability.Reachable, Armed = true, Capture = SkyEye.Core.CaptureState.Active,
                Ownership = CameraOwnership.Guest, HostOwner = "other", HostGeneration = 1, SessionId = Guid.NewGuid().ToString("N"), CaptureRevision = 1 }, CommandOutcome.Applied, "Guest fixture"));
    }
}
