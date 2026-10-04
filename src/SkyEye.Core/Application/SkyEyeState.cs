namespace SkyEye.Core.Application;

public enum AdviceTargetGroup { Open, Red, Yellow, Black }
public sealed record AdvicePreferences(AdviceTargetGroup TargetGroup);
public sealed record PresentationPreferences(bool ShotChoicesEnabled);

/// <summary>Canonical, immutable app snapshot. Platform credentials never belong here.
/// Connection, table and game projections are added with their verified command slices.</summary>
public sealed record SkyEyeState(long Version, AdvicePreferences Advice, PresentationPreferences Presentation)
{
    public CameraConnection Camera { get; init; } = CameraConnection.Empty;
    public static SkyEyeState Empty { get; } = new(0, new(AdviceTargetGroup.Open), new(false));
}
