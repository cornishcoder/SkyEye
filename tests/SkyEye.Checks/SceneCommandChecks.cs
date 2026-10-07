using SkyEye.Core;
using SkyEye.Core.Application;

static class SceneCommandChecks
{
    public static async Task Run()
    {
        static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
        var restored = new AppCoordinator(phoneCorrected:false);
        Check(!restored.State.Presentation.PhoneCorrected && restored.State.Presentation.TvCorrected
            && restored.State.Presentation.Phone.Mode==ShotDisplayMode.Live && restored.State.ShotChoices==ShotChoiceCatalog.Empty,
            "Restored correction flags cannot restore transient shots or change independent surface defaults");
        await restored.DispatchAsync(new(Guid.NewGuid(),CommandOrigin.Touch,restored.State.Version,new SetTableCorrection(SceneTarget.Phone,true)));
        await restored.DispatchAsync(new(Guid.NewGuid(),CommandOrigin.Touch,restored.State.Version,new ClearAnnotations(SceneTarget.Both)));
        Check(restored.State.Presentation.PhoneCorrected && restored.State.Presentation.TvCorrected,
            "Independent correction choices survive clearing HUD advice");
        Check(!(await restored.DispatchAsync(new(Guid.NewGuid(),CommandOrigin.Sky,restored.State.Version,new SetTableCorrection(SceneTarget.Tv,false)))).Succeeded
            && !(await restored.DispatchAsync(new(Guid.NewGuid(),CommandOrigin.Touch,restored.State.Version,new SetTableCorrection(SceneTarget.Both,false)))).Succeeded
            && restored.State.Presentation.TvCorrected,
            "Touch and Sky cannot select an uncorrected live TV route");
        var clock = new Clock(); var app = new AppCoordinator(cameraControls: new Camera(), clock: clock);
        Task<CommandReceipt> Send(SkyEyeCommand command, CommandOrigin origin = CommandOrigin.Touch) => app.DispatchAsync(new(Guid.NewGuid(), origin, app.State.Version, command));
        await Send(new ConnectCamera("camera:8765"));
        await Send(new SetShotDisplay(SceneTarget.Tv, ShotDisplayMode.Cycling));
        Check(app.State.Presentation.Phone.Mode == ShotDisplayMode.Live && app.State.Presentation.Tv.Mode == ShotDisplayMode.Cycling, "TV choices must not change phone");
        var catalog = new ShotChoiceCatalog(app.State.Camera.Scope, 7, AdviceTargetGroup.Open, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(15), [new("7:r1:0", "direct", 0), new("7:r2:1", "bank", 1)]);
        Check(!(await Send(new PublishShotCatalog(catalog))).Succeeded, "touch cannot publish its own candidate proof");
        Check((await Send(new PublishShotCatalog(catalog), CommandOrigin.System)).Succeeded, "validated system catalog accepted");
        await Send(new ClearAnnotations(SceneTarget.Both));
        Check(app.State.ShotChoices==catalog && !app.State.Presentation.ShotChoicesEnabled,
            "A fresh catalogue remains selectable while both surfaces show clean live footage");
        var phoneRevision = app.State.Presentation.Phone.Revision;
        Check((await Send(new ShowShot(SceneTarget.Tv, 7, 2), CommandOrigin.Sky)).Succeeded
            && app.State.Presentation.Tv.ShotId == "7:r2:1" && app.State.Presentation.Phone.Revision == phoneRevision, "Sky selects number two on TV without changing phone revision");
        Check((await Send(new ShowAnnotation(SceneTarget.Phone, 7, "7:r2:1", ShotAnnotations.CueBallPath), CommandOrigin.Sky)).Succeeded
            && app.State.Presentation.Phone.Annotations == ShotAnnotations.CueBallPath && app.State.Presentation.Tv.Annotations == ShotAnnotations.All, "annotation subset is independently scoped");
        Check((await Send(new ShowAnnotation(SceneTarget.Phone,7,"7:r2:1",ShotAnnotations.WhiteDirection))).Succeeded
            && app.State.Presentation.Phone.Annotations==ShotAnnotations.WhiteDirection && app.State.Presentation.Tv.Annotations==ShotAnnotations.All,
            "Touch white-direction preset uses the same scoped semantic command as Sky");
        Check((await Send(new ShowAnnotation(SceneTarget.Tv,7,"7:r2:1",ShotAnnotations.Lines),CommandOrigin.Sky)).Succeeded
            && app.State.Presentation.Phone.Annotations==ShotAnnotations.WhiteDirection && app.State.Presentation.Tv.Annotations==ShotAnnotations.Lines,
            "Independent TV minimal preset does not change phone projection");
        Check(!(await Send(new ShowShot(SceneTarget.Phone, 6, 2))).Succeeded && !(await Send(new ShowShot(SceneTarget.Tv, 7, 4))).Succeeded, "stale numbers and missing numbers rejected");
        await Send(new ClearAnnotations(SceneTarget.Phone), CommandOrigin.Sky);
        Check(app.State.Presentation.Phone.Mode == ShotDisplayMode.Live && app.State.Presentation.Tv.Mode == ShotDisplayMode.Pinned, "clearing phone retains selected TV shot");
        await Send(new SetShotDisplay(SceneTarget.Tv, ShotDisplayMode.Cycling));
        Check(!(await Send(new PublishShotCatalog(catalog with { Choices = [null!] }), CommandOrigin.System)).Succeeded, "malformed catalog rejected without throwing");
        Check(!(await Send(new PinShot(SceneTarget.Phone, 6, "7:r1:0"))).Succeeded && !(await Send(new PinShot(SceneTarget.Phone, 7, "7:missing"))).Succeeded, "wrong version or missing candidate rejected");
        var pin = new CommandEnvelope(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, new PinShot(SceneTarget.Phone, 7, "7:r1:0"));
        Check((await app.DispatchAsync(pin)).Succeeded && app.State.Presentation.Phone.Mode == ShotDisplayMode.Pinned && app.State.Presentation.Tv.Mode == ShotDisplayMode.Cycling, "pin is target-specific");
        Check((await app.DispatchAsync(pin)).Duplicate, "pin replay is deduplicated");
        var version = app.State.Version;
        Check(!(await Send(new SetShotDisplay(SceneTarget.Both, ShotDisplayMode.Score))).Succeeded && app.State.Version == version, "no invented score without ledger");
        clock.Advance(15);
        Check(!(await Send(new PinShot(SceneTarget.Tv, 7, "7:r1:0"))).Succeeded, "expired shot cannot be pinned");
        await Send(new PublishShotCatalog(ShotChoiceCatalog.Empty), CommandOrigin.System);
        Check(app.State.Presentation.Phone.Mode == ShotDisplayMode.Live && app.State.Presentation.Tv.Mode == ShotDisplayMode.Cycling, "clearing proof removes pins but preserves cycle preference");
        clock.Advance(1);
        catalog = catalog with { CapturedAt = clock.GetUtcNow(), ExpiresAt = clock.GetUtcNow().AddSeconds(15) };
        await Send(new PublishShotCatalog(catalog), CommandOrigin.System); await Send(new PinShot(SceneTarget.Both, 7, "7:r2:1"));
        await Send(new SetTargetGroup(AdviceTargetGroup.Yellow));
        Check(app.State.ShotChoices == ShotChoiceCatalog.Empty && app.State.Presentation.Phone.Mode == ShotDisplayMode.Live && app.State.Presentation.Tv.Mode == ShotDisplayMode.Live, "group change clears incompatible pins/catalog");
        await Send(new SetTargetGroup(AdviceTargetGroup.Open)); await Send(new PublishShotCatalog(catalog), CommandOrigin.System);
        await Send(new PinShot(SceneTarget.Both, 7, "7:r1:0"));
        await Send(new ConnectCamera("other:8765"));
        Check(app.State.ShotChoices == ShotChoiceCatalog.Empty && app.State.Presentation.Phone.Mode == ShotDisplayMode.Live, "camera replacement clears scoped pins");
        Check(!(await Send(new PublishShotCatalog(catalog), CommandOrigin.System)).Succeeded, "late old-camera catalog rejected");
        await Send(new ShowShotChoices(true));
        Check(app.State.Presentation.Phone.Mode == ShotDisplayMode.Cycling && app.State.Presentation.Tv.Mode == ShotDisplayMode.Cycling, "legacy choices retain both-surface behavior");
        var calibration = new TableCalibration([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
        var shot = new Shot("1:r:0", "r", 0, new(.7, .4), new(.8, .4), new(2, 0), .5, "fixture", 1)
            { CuePath = [new(.2, .2), new(.7, .4)], TargetPath = [new(.8, .4), new(2, 0)] };
        var scene = SkyEye.Core.Analysis.ShotSceneProjection.Create(shot, calibration, 1, 2, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), "Pinned fixture", true, (ShotAnnotations)63);
        Check(Math.Abs(scene.Paths[0].Points[0].X - .1) < 1e-10 && Math.Abs(scene.Paths[0].Points[0].Y - .2) < 1e-10
            && scene.Paths.All(p => p.Selected) && scene.Markers.Length == 3 && scene.TableVersion == 1, "shot scene uses calibrated original-image coordinates, pin styling and table provenance");
        var path = SkyEye.Core.Analysis.ShotSceneProjection.Create(shot, calibration, 2, 2, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(5), "Label", true, ShotAnnotations.CueBallPath);
        Check(path.Paths.Length == 1 && path.Markers.Length == 0 && path.Caption == "", "cue-path-only annotation excludes unrelated markers and labels");
        var replayRequest = new PublishReplayPresentation(SceneTarget.Tv, app.State.Camera.ConnectionId, Guid.NewGuid().ToString("N"));
        Check(!(await Send(replayRequest, CommandOrigin.Sky)).Succeeded && (await Send(replayRequest, CommandOrigin.System)).Succeeded, "only validated native replay preparation marks replay state");
        Check(app.State.Presentation.Tv.Mode == ShotDisplayMode.Replay && app.State.Presentation.Phone.Mode == ShotDisplayMode.Cycling, "replay retains independent phone choices");
        Console.WriteLine("PASS target-specific live/cycle/pin/clear, scoped catalog, expired/stale references, duplicate pins, group/camera invalidation and score gate");
    }
    sealed class Clock : TimeProvider { DateTimeOffset now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => now; public void Advance(int seconds) => now = now.AddSeconds(seconds); }
    sealed class Camera : ICameraControlEffect
    {
        public Task<CameraControlResult> ExecuteAsync(Guid id, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
            => Task.FromResult(new CameraControlResult(admitted with { Reachability = CameraReachability.Reachable, Armed = true, Capture = CaptureState.Active,
                Ownership = CameraOwnership.Owned, HostOwner = "fixture", HostGeneration = 1, SessionId = Guid.NewGuid().ToString("N"), CaptureRevision = 1 }, CommandOutcome.Applied, "Fixture observed."));
    }
}
