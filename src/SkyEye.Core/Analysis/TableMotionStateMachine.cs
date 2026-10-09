namespace SkyEye.Core.Analysis;

/// <summary>
/// A diagnostic-only motion phase reducer. It reports whether the seeded table image is
/// changing; it does not infer ball identity, pots, turns, or mutate canonical GameState.
/// </summary>
public sealed class TableMotionStateMachine
{
    const double MotionThresholdPixelsPerFrame = 0.55;
    const double StrongMotionPixelsPerFrame = 2.5;
    const double MultiRegionMotionPixelsPerFrame = 0.9;
    const long SettledQuietUs = 700_000;
    const long WindowUs = 2_000_000;

    readonly Dictionary<string, MotionStreak> streaks = new(StringComparer.Ordinal);
    readonly Queue<WindowSample> window = new();
    MotionAnalysisPhase phase = MotionAnalysisPhase.Unknown;
    long? stateSinceUs, lastMotionUs, quietSinceUs;

    public MotionAnalysisSnapshot Reset()
    {
        streaks.Clear();
        window.Clear();
        phase = MotionAnalysisPhase.Unknown;
        stateSinceUs = lastMotionUs = quietSinceUs = null;
        return MotionAnalysisSnapshot.Unknown with { Evidence = "Source gap or scope reset; waiting for fresh paired frames" };
    }

    public MotionAnalysisSnapshot Observe(MotionMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        long now = measurement.SourceTimeUs;
        var templates = measurement.Regions.Where(region => region.TemplateStatus == "matched"
            && region.TemplateDxPixels is { } dx && double.IsFinite(dx)
            && region.TemplateDyPixels is { } dy && double.IsFinite(dy)).ToArray();
        int total = measurement.Regions.Count;
        int required = total == 0 ? 1 : Math.Min(total, Math.Max(3, (int)Math.Ceiling(total * .35)));
        double[] displacements = templates.Select(region => Math.Sqrt(
            Math.Pow(region.TemplateDxPixels!.Value, 2) + Math.Pow(region.TemplateDyPixels!.Value, 2))).ToArray();
        int movingRegions = 0;
        double peak = 0;
        int index = 0;
        foreach (var region in templates)
        {
            double dx = region.TemplateDxPixels!.Value, dy = region.TemplateDyPixels!.Value;
            double displacement = displacements[index++];
            peak = Math.Max(peak, displacement);
            bool aboveNoise = displacement >= MotionThresholdPixelsPerFrame;
            if (aboveNoise) movingRegions++;
            if (!streaks.TryGetValue(region.Id, out var previous)) previous = default;
            if (aboveNoise && previous.HasDirection
                && previous.Dx * dx + previous.Dy * dy > 0
                && previous.Streak < 8)
                streaks[region.Id] = new(dx, dy, previous.Streak + 1, true);
            else if (aboveNoise)
                streaks[region.Id] = new(dx, dy, 1, true);
            else
                streaks[region.Id] = default;
        }
        var liveIds = templates.Select(region => region.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in streaks.Keys.Where(id => !liveIds.Contains(id)).ToArray()) streaks.Remove(id);

        bool persistentSingleRegion = streaks.Values.Any(streak => streak.Streak >= 3);
        bool movement = peak >= StrongMotionPixelsPerFrame
            || displacements.Count(value => value >= MultiRegionMotionPixelsPerFrame) >= 2
            || persistentSingleRegion;

        window.Enqueue(new(now, peak, movingRegions));
        while (window.Count > 0 && (now - window.Peek().SourceTimeUs > WindowUs || window.Count > 120)) window.Dequeue();

        string evidence;
        if (templates.Length < required)
        {
            SetPhase(MotionAnalysisPhase.Unknown, now);
            quietSinceUs = null;
            evidence = $"Template coverage insufficient ({templates.Length}/{total}; need {required})";
        }
        else if (movement)
        {
            SetPhase(MotionAnalysisPhase.Moving, now);
            lastMotionUs = now;
            quietSinceUs = null;
            evidence = peak >= StrongMotionPixelsPerFrame
                ? $"Strong ball-patch displacement ({peak:F2} px/frame)"
                : persistentSingleRegion
                    ? "Ball-patch displacement persisted in one region"
                    : $"Ball-patch displacement in {displacements.Count(value => value >= MultiRegionMotionPixelsPerFrame)} regions";
        }
        else
        {
            if (phase is MotionAnalysisPhase.Unknown or MotionAnalysisPhase.Moving)
            {
                SetPhase(MotionAnalysisPhase.Settling, now);
                quietSinceUs = now;
            }
            else if (phase == MotionAnalysisPhase.Settling && quietSinceUs is { } quietAt
                && now - quietAt >= SettledQuietUs)
                SetPhase(MotionAnalysisPhase.Settled, now);
            evidence = phase == MotionAnalysisPhase.Settled
                ? "Ball-patch positions quiet for at least 700 ms"
                : "No supported ball-patch movement above threshold";
        }

        return new(phase, stateSinceUs, lastMotionUs,
            quietSinceUs is { } start ? Math.Max(0, now - start) / 1000d : null,
            templates.Length, total, movingRegions, templates.Length == 0 ? null : peak,
            window.Count == 0 ? null : window.Max(sample => sample.PeakDisplacementPixels),
            window.Count == 0 ? 0 : window.Max(sample => sample.MovingRegions), window.Count, evidence);
    }

    void SetPhase(MotionAnalysisPhase next, long atSourceTimeUs)
    {
        if (phase == next) return;
        phase = next;
        stateSinceUs = atSourceTimeUs;
    }

    readonly record struct MotionStreak(double Dx, double Dy, int Streak, bool HasDirection);
    readonly record struct WindowSample(long SourceTimeUs, double PeakDisplacementPixels, int MovingRegions);
}
