using SkyEye.Core;
using SkyEye.Core.Application;

static class SceneCommandChecks
{
    public static async Task Run()
    {
        static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
        var clock = new Clock(); var app = new AppCoordinator(cameraControls: new Camera(), clock: clock);
        Task<CommandReceipt> Send(SkyEyeCommand command, CommandOrigin origin = CommandOrigin.Touch) => app.DispatchAsync(new(Guid.NewGuid(), origin, app.State.Version, command));
        await Send(new ConnectCamera("camera:8765"));
        await Send(new SetShotDisplay(SceneTarget.Tv, ShotDisplayMode.Cycling));
        Check(app.State.Presentation.Phone.Mode == ShotDisplayMode.Live && app.State.Presentation.Tv.Mode == ShotDisplayMode.Cycling, "TV choices must not change phone");
        var catalog = new ShotChoiceCatalog(app.State.Camera.Scope, 7, AdviceTargetGroup.Open, clock.GetUtcNow(), clock.GetUtcNow().AddSeconds(15), [new("7:r1:0", "direct", 0), new("7:r2:1", "bank", 1)]);
        Check(!(await Send(new PublishShotCatalog(catalog))).Succeeded, "touch cannot publish its own candidate proof");
        Check((await Send(new PublishShotCatalog(catalog), CommandOrigin.System)).Succeeded, "validated system catalog accepted");
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
