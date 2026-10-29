using SkyEye.Core;

namespace SkyEye.Companion.Hybrid;

internal readonly record struct LiveDetectShotClipWindow(Guid ActivityId, string Timeline,
    long StartSourceTimeUs, long EndSourceTimeUs, int SaveSeconds);

/// <summary>Maps a completed Live Detect activity to a supported recent-clip window.</summary>
internal static class LiveDetectShotClipWindowPolicy
{
    public const int LeadInSeconds = 20;
    public const int PostRollSeconds = 2;
    const int SaveBoundarySafetySeconds = 4;

    public static bool TryCreate(Guid activityId, string timeline, long activityStartSourceTimeUs,
        long settledSourceTimeUs, long saveRequestSourceTimeUs, out LiveDetectShotClipWindow window)
    {
        window = default;
        if (activityId == Guid.Empty || !Guid.TryParseExact(timeline, "N", out _)
            || activityStartSourceTimeUs <= 0 || settledSourceTimeUs < activityStartSourceTimeUs)
            return false;

        long start = Math.Max(0, activityStartSourceTimeUs - LeadInSeconds * 1_000_000L);
        long end = checked(settledSourceTimeUs + PostRollSeconds * 1_000_000L);
        if (saveRequestSourceTimeUs < end) return false;
        long requiredEnd = checked(Math.Max(end, saveRequestSourceTimeUs)
            + SaveBoundarySafetySeconds * 1_000_000L);
        long requiredSeconds = (requiredEnd - start + 999_999) / 1_000_000;
        int saveSeconds = ReplaySelection.Durations.FirstOrDefault(seconds => seconds >= requiredSeconds);
        if (saveSeconds == 0) return false;

        window = new(activityId, timeline, start, end, saveSeconds);
        return true;
    }
}
