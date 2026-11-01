namespace SkyEye.Companion.Hybrid;

public enum CalibrationReconnectDecision
{
    RequirePoseConfirmation,
    KeepSavedCalibration,
    ApplyFreshProposal,
    RequireManualCalibration
}

public static class CalibrationReconnectPolicy
{
    public static bool RequiresPoseConfirmation(bool hasSavedCalibration, bool poseVerifiedForConnection)
        => hasSavedCalibration && !poseVerifiedForConnection;

    public static CalibrationReconnectDecision Decide(bool hasSavedCalibration, bool poseVerifiedForConnection,
        bool hasValidProposal)
        => RequiresPoseConfirmation(hasSavedCalibration, poseVerifiedForConnection)
            ? CalibrationReconnectDecision.RequirePoseConfirmation
            : hasSavedCalibration ? CalibrationReconnectDecision.KeepSavedCalibration
            : hasValidProposal ? CalibrationReconnectDecision.ApplyFreshProposal
            : CalibrationReconnectDecision.RequireManualCalibration;
}

public sealed class FramingGeometryFence
{
    (int Width, int Height, string Configuration)? known;
    public bool Observe(int width, int height, string configuration)
    {
        if (width <= 0 || height <= 0) return false;
        var next = (width, height, configuration);
        bool changed = known is { } previous && previous != next;
        known = next; return changed;
    }
}
