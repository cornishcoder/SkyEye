namespace SkyEye.Companion.Hybrid;

// The platform-neutral UI checks compile SkyVoiceGameTools without linking the
// Android ShellMotion and ShellVisionDashboard files that own these payloads.
public sealed record SettledTableCandidateDisplay(Guid ActivityWindowId,
    TestOutcomeRecommendation? Recommendation, object? Before, object? After, object? RulesAdvice);

public sealed record TestOutcomeRecommendation(string Kind);

public sealed record VisionDashboardOutcomeReviewResponse(bool Accepted, string Message, long? GameVersion = null);
