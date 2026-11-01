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
}
