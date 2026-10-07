namespace SkyEye.Core.Application;

public enum AdviceTargetGroup { Open, Red, Yellow, Black }
public sealed record AdvicePreferences(AdviceTargetGroup TargetGroup);
public sealed record PresentationPreferences(bool ShotChoicesEnabled)
{
    public bool PhoneCorrected { get; init; } = true;
    public bool TvCorrected { get; init; } = true;
    public SurfacePresentation Phone { get; init; } = new(ShotChoicesEnabled ? ShotDisplayMode.Cycling : ShotDisplayMode.Live);
    public SurfacePresentation Tv { get; init; } = new(ShotChoicesEnabled ? ShotDisplayMode.Cycling : ShotDisplayMode.Live);
}

/// <summary>Canonical, immutable app snapshot. Platform credentials never belong here.
/// Connection, table and game projections are added with their verified command slices.</summary>
public sealed record SkyEyeState(long Version, AdvicePreferences Advice, PresentationPreferences Presentation)
{
    public CameraConnection Camera { get; init; } = CameraConnection.Empty;
    public ShotChoiceCatalog ShotChoices { get; init; } = ShotChoiceCatalog.Empty;
    public TableObservation? Observation { get; init; }
    public SkyEye.GameState.GameSnapshot Game { get; init; } = SkyEye.GameState.GameSnapshot.Empty;
    // Recovered history is authoritative but its last table image is not current live evidence.
    public bool GameTableCurrent { get; init; }
    public string? GameError { get; init; }
    public MatchView Match { get; init; } = MatchView.Empty;
    public bool CanManageMatch => Camera.ConnectionId == Guid.Empty || Camera.CanControl;
    public bool HasFreshGameTable(DateTimeOffset now) => GameTableCurrent && Game.Table.LastSettled is { } last
        && (now - last.At).TotalSeconds is >= 0 and <= 6;
    public static SkyEyeState Empty { get; } = new(0, new(AdviceTargetGroup.Open), new(false));
}
