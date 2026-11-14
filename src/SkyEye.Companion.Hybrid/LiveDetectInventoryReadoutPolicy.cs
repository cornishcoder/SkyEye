namespace SkyEye.Companion.Hybrid;

/// <summary>
/// Keeps a history-conditioned settled event separate from a clear current-inventory claim.
/// </summary>
internal static class LiveDetectInventoryReadoutPolicy
{
    public static bool IsCurrentInventoryClear(bool uncertain, bool obstructed,
        bool layoutUncertain, LiveDetectCounts counts) =>
        !uncertain && !obstructed && !layoutUncertain && counts.Other == 0 && counts.Total > 0;

    public static LiveDetectCounts? SettledCurrentInventory(bool currentInventoryClear,
        LiveDetectEventBatch eventBatch) =>
        currentInventoryClear && eventBatch.SettledBaselineMatchesCurrent
            ? eventBatch.SettledBaseline
            : null;

    /// <summary>A settled-baseline summary that names a black or cue ball the baseline formed without
    /// as "not seen", never as absent from the table (D2).</summary>
    public static string BaselineSummary(LiveDetectCounts counts, bool blackNotSeen, bool cueNotSeen) =>
        $"{counts.Reds} reds · {counts.Yellows} yellows · "
        + (blackNotSeen && counts.Black == 0 ? "black not seen" : $"{counts.Black} black") + " · "
        + (cueNotSeen && counts.Cue == 0 ? "cue ball not seen" : $"{counts.Cue} cue ball{(counts.Cue == 1 ? "" : "s")}")
        + (counts.Other > 0 ? $" · {counts.Other} unknown" : "");
}
