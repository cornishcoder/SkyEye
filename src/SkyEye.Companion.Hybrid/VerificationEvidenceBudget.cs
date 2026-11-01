using SkyEye.Core.Analysis;

namespace SkyEye.Companion.Hybrid;

internal static class VerificationEvidenceBudget
{
    const long MiB = 1024 * 1024;

    public const long MaximumBytes = 256 * MiB;
    public const long MaximumFrameBytes = 8 * MiB;
    public const long FrameWriteOverheadBytes = 512 * 1024;
    public const long FrameReservationBytes = 2 * MiB + FrameWriteOverheadBytes;
    public const long CaptureReservationBytes = FrameReservationBytes * ObservationPolicy.MaximumCapturedVerificationSamples;

    public static bool CanReserveCapture(long retainedBytes) => retainedBytes >= 0
        && retainedBytes <= MaximumBytes - CaptureReservationBytes;

    public static bool CanWriteFrame(long retainedBytes, long pixelBytes) => retainedBytes >= 0
        && pixelBytes >= 0 && pixelBytes <= MaximumFrameBytes
        && retainedBytes <= MaximumBytes - pixelBytes - FrameWriteOverheadBytes;
}
