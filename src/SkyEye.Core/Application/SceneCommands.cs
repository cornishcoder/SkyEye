using System.Collections.Immutable;

namespace SkyEye.Core.Application;

public enum SceneTarget { Phone, Tv, Both }
public enum ShotDisplayMode { Live, Cycling, Pinned, Score, Replay }
[Flags]
public enum ShotAnnotations
{
    None = 0, CueBallPath = 1, ObjectBallPath = 2, TargetBall = 4, GhostBall = 8, Pocket = 16, ShotLabel = 32,
    AimCorridor = 64, ContactPoint = 128, CushionContacts = 256, CueBallProjectedPath = 512, Provenance = 1024,
    Lines = CueBallPath | ObjectBallPath | AimCorridor | GhostBall | ContactPoint | Pocket,
    WhiteDirection = CueBallPath | ContactPoint | CueBallProjectedPath | Provenance,
    All = 2047
}
public sealed record SurfacePresentation(ShotDisplayMode Mode, string? ShotId = null, long TableVersion = 0)
{
    public ShotAnnotations Annotations { get; init; } = ShotAnnotations.All;
    public long Revision { get; init; }
    public string? ReplayId { get; init; }
    public static SurfacePresentation Live { get; } = new(ShotDisplayMode.Live);
}
public sealed record ShotChoice(string Id, string Type, int Pocket);
public sealed record ShotChoiceCatalog(CameraScope? Source, long TableVersion, AdviceTargetGroup Group,
    DateTimeOffset CapturedAt, DateTimeOffset ExpiresAt, ImmutableArray<ShotChoice> Choices)
{
    public static ShotChoiceCatalog Empty { get; } = new(null, 0, AdviceTargetGroup.Open, default, default, []);
    public bool Matches(CameraConnection camera, AdviceTargetGroup group, DateTimeOffset now) => camera.CanControl
        && camera.Capture == CaptureState.Active && Source == camera.Scope && Group == group && now < ExpiresAt;
}
public sealed record SetShotDisplay(SceneTarget Target, ShotDisplayMode Mode) : SkyEyeCommand;
public sealed record PinShot(SceneTarget Target, long TableVersion, string ShotId) : SkyEyeCommand;
public sealed record ShowShot(SceneTarget Target, long TableVersion, int Number) : SkyEyeCommand;
public sealed record ShowAnnotation(SceneTarget Target, long TableVersion, string ShotId, ShotAnnotations Annotations) : SkyEyeCommand;
public sealed record ClearAnnotations(SceneTarget Target) : SkyEyeCommand;
public sealed record SetTableCorrection(SceneTarget Target, bool Enabled) : SkyEyeCommand;
public sealed record PublishReplayPresentation(SceneTarget Target, Guid ConnectionId, string ReplayId) : SkyEyeCommand;
// System-only publication of references from the validated table pipeline; geometry stays native.
public sealed record PublishShotCatalog(ShotChoiceCatalog Catalog) : SkyEyeCommand;

public sealed partial class AppCoordinator
{
    SkyEyeState? SceneCommand(CommandEnvelope envelope)
    {
        var presentation = state.Presentation;
        switch (envelope.Command)
        {
            case ClearAnnotations clear when Enum.IsDefined(clear.Target):
                return state with { Presentation = ForTarget(presentation, clear.Target, SurfacePresentation.Live) };
            case SetTableCorrection correction when Enum.IsDefined(correction.Target) && (correction.Target == SceneTarget.Phone || correction.Enabled):
                return state with { Presentation = presentation with {
                    PhoneCorrected = correction.Target is SceneTarget.Phone or SceneTarget.Both ? correction.Enabled : presentation.PhoneCorrected,
                    TvCorrected = correction.Target is SceneTarget.Tv or SceneTarget.Both ? correction.Enabled : presentation.TvCorrected } };
            case PublishReplayPresentation replay when envelope.Origin == CommandOrigin.System && Enum.IsDefined(replay.Target)
                && replay.ConnectionId == state.Camera.ConnectionId && Guid.TryParseExact(replay.ReplayId, "N", out _):
                return state with { Presentation = ForTarget(presentation, replay.Target, new(ShotDisplayMode.Replay) { ReplayId = replay.ReplayId }) };
            case ShowShot numberedShot when Enum.IsDefined(numberedShot.Target):
                var numbered = state.ShotChoices;
                if (!numbered.Matches(state.Camera, state.Advice.TargetGroup, sceneClock.GetUtcNow()) || numberedShot.TableVersion != numbered.TableVersion
                    || numberedShot.Number < 1 || numberedShot.Number > numbered.Choices.Length) return null;
                return state with { Presentation = ForTarget(presentation, numberedShot.Target, new(ShotDisplayMode.Pinned, numbered.Choices[numberedShot.Number - 1].Id, numberedShot.TableVersion)) };
            case ShowAnnotation annotation when Enum.IsDefined(annotation.Target) && annotation.Annotations != ShotAnnotations.None && (annotation.Annotations & ~ShotAnnotations.All) == 0:
                var annotated = state.ShotChoices;
                if (!annotated.Matches(state.Camera, state.Advice.TargetGroup, sceneClock.GetUtcNow()) || annotation.TableVersion != annotated.TableVersion
                    || !annotated.Choices.Any(c => c.Id == annotation.ShotId)) return null;
                return state with { Presentation = ForTarget(presentation, annotation.Target, new(ShotDisplayMode.Pinned, annotation.ShotId, annotation.TableVersion) { Annotations = annotation.Annotations }) };
            case SetShotDisplay show when Enum.IsDefined(show.Target) && show.Mode is ShotDisplayMode.Live or ShotDisplayMode.Cycling:
                return state with { Presentation = ForTarget(presentation, show.Target, new(show.Mode)) };
            case PinShot pin when Enum.IsDefined(pin.Target):
                var catalog = state.ShotChoices;
                if (!catalog.Matches(state.Camera, state.Advice.TargetGroup, sceneClock.GetUtcNow()) || pin.TableVersion != catalog.TableVersion
                    || !catalog.Choices.Any(c => c.Id == pin.ShotId)) return null;
                return state with { Presentation = ForTarget(presentation, pin.Target, new(ShotDisplayMode.Pinned, pin.ShotId, pin.TableVersion)) };
            case PublishShotCatalog publish when envelope.Origin == CommandOrigin.System && ValidCatalog(publish.Catalog):
                var next = publish.Catalog;
                SurfacePresentation Retain(SurfacePresentation surface) => surface.Mode != ShotDisplayMode.Pinned ||
                    surface.TableVersion == next.TableVersion && next.Choices.Any(c => c.Id == surface.ShotId) ? surface : SurfacePresentation.Live;
                return state with { ShotChoices = next, Presentation = WithSurfaces(presentation, Retain(presentation.Phone), Retain(presentation.Tv)) };
            default: return null;
        }
    }
    bool ValidCatalog(ShotChoiceCatalog catalog)
    {
        if (catalog is null) return false;
        if (catalog == ShotChoiceCatalog.Empty) return true;
        var now = sceneClock.GetUtcNow();
        return catalog.TableVersion > 0 && catalog.Matches(state.Camera, state.Advice.TargetGroup, now)
            && (now - catalog.CapturedAt).TotalSeconds is >= -2 and <= 6 && catalog.ExpiresAt == catalog.CapturedAt.AddSeconds(15)
            && !catalog.Choices.IsDefault && catalog.Choices.Length <= 3
            && catalog.Choices.All(c => c is not null && c.Id is { Length: > 0 and <= 160 } && c.Pocket is >= 0 and < 6 && c.Type is "direct" or "bank" or "kick" or "bank-and-kick")
            && catalog.Choices.Select(c => c.Id).Distinct().Count() == catalog.Choices.Length;
    }
    static PresentationPreferences ForTarget(PresentationPreferences value, SceneTarget target, SurfacePresentation surface)
        => WithSurfaces(value, target is SceneTarget.Phone or SceneTarget.Both ? surface : value.Phone, target is SceneTarget.Tv or SceneTarget.Both ? surface : value.Tv);
    static PresentationPreferences WithSurfaces(PresentationPreferences value, SurfacePresentation phone, SurfacePresentation tv)
    {
        phone = phone with { Revision = value.Phone.Revision }; tv = tv with { Revision = value.Tv.Revision };
        if (phone != value.Phone) phone = phone with { Revision = checked(value.Phone.Revision + 1) };
        if (tv != value.Tv) tv = tv with { Revision = checked(value.Tv.Revision + 1) };
        return value with { Phone = phone, Tv = tv, ShotChoicesEnabled = phone.Mode is ShotDisplayMode.Cycling or ShotDisplayMode.Pinned || tv.Mode is ShotDisplayMode.Cycling or ShotDisplayMode.Pinned };
    }
    static SkyEyeState ClearShotCatalog(SkyEyeState value)
    {
        var p = value.Presentation;
        return value with { ShotChoices = ShotChoiceCatalog.Empty, Presentation = WithSurfaces(p,
            p.Phone.Mode == ShotDisplayMode.Pinned ? SurfacePresentation.Live : p.Phone,
            p.Tv.Mode == ShotDisplayMode.Pinned ? SurfacePresentation.Live : p.Tv) };
    }
}
