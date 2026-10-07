namespace SkyEye.Core.Analysis;

/// <summary>Different policies govern legacy observation, review, advice lifetime
/// and continuous evidence. Verification values are provisional until O08 holdout acceptance.</summary>
public static class ObservationPolicy
{
    public const double LegacyObservationGapSeconds = 15;
    public const double ReviewFreshnessSeconds = 6;
    public const double CatalogueLifetimeSeconds = 15;
    public const double MinimumStillSeconds = 2;
    public const double MaximumVerificationGapSeconds = .8;
    public const double VerificationTimeoutSeconds = 4;
    public const double MaximumFinalAgeMs = 1500;
    public const double MaximumClockSampleAgeMs = 2500;
    public const int MinimumVerificationSamples = 5;
    public const int MaximumVerificationSamples = 6;
    public const double SampleIntervalSeconds = .5;
    public const string VerificationConfiguration = "o08-provisional-v1";
}

public enum TableMotion { Unknown, Still, Moving }
public enum LayoutQuality { Unknown, Clear, Ambiguous, Obstructed }
public enum ObservationReason
{
    None, Checking, InsufficientEvidence, DetectionAmbiguous, Obstructed,
    Moving, LightingChanged, CalibrationChanged, FrameUnavailable, FrameTooOld,
    ClockUntrusted, SourceChanged, SampleGap, Cancelled, TimedOut
}
public sealed record RecognitionTelemetry(string Branch, int RawCircleCount, int FinalCircleCount,
    string[] RawLabels, double[] Gains, int ClothHue, string[] Warnings);
public sealed record LayoutChangeTelemetry(int PreviousCount, int CurrentCount, bool SpatiallyCompatible, bool LabelsCompatible);
