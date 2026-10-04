using System.Collections.Immutable;

namespace SkyEye.Core.Application;

public enum SceneTarget { Phone, Tv, Both }
public enum ShotDisplayMode { Live, Cycling, Pinned, Score }
public sealed record SurfacePresentation(ShotDisplayMode Mode, string? ShotId = null, long TableVersion = 0)
{
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
// System-only publication of references from the validated table pipeline; geometry stays native.
public sealed record PublishShotCatalog(ShotChoiceCatalog Catalog) : SkyEyeCommand;

public sealed partial class AppCoordinator
{
    SkyEyeState? SceneCommand(CommandEnvelope envelope)
    {
        var presentation = state.Presentation;
        switch (envelope.Command)
        {
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
            && !catalog.Choices.IsDefault && catalog.Choices.Length <= 3 && catalog.Choices.Select(c => c.Id).Distinct().Count() == catalog.Choices.Length
            && catalog.Choices.All(c => c is not null && c.Id is { Length: > 0 and <= 160 } && c.Pocket is >= 0 and < 6 && c.Type is "direct" or "bank" or "kick" or "bank-and-kick");
    }
    static PresentationPreferences ForTarget(PresentationPreferences value, SceneTarget target, SurfacePresentation surface)
        => WithSurfaces(value, target is SceneTarget.Phone or SceneTarget.Both ? surface : value.Phone, target is SceneTarget.Tv or SceneTarget.Both ? surface : value.Tv);
    static PresentationPreferences WithSurfaces(PresentationPreferences value, SurfacePresentation phone, SurfacePresentation tv)
        => value with { Phone = phone, Tv = tv, ShotChoicesEnabled = phone.Mode != ShotDisplayMode.Live || tv.Mode != ShotDisplayMode.Live };
    static SkyEyeState ClearShotCatalog(SkyEyeState value)
    {
        var p = value.Presentation;
        return value with { ShotChoices = ShotChoiceCatalog.Empty, Presentation = WithSurfaces(p,
            p.Phone.Mode == ShotDisplayMode.Pinned ? SurfacePresentation.Live : p.Phone,
            p.Tv.Mode == ShotDisplayMode.Pinned ? SurfacePresentation.Live : p.Tv) };
    }
}
