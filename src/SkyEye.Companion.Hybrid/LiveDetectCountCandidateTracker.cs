namespace SkyEye.Companion.Hybrid;

internal readonly record struct LiveDetectCounts(int Reds, int Yellows, int Black, int Cue, int Other)
{
    public string Summary => $"{Reds} reds · {Yellows} yellows · {Black} black · {Cue} cue ball{(Cue == 1 ? "" : "s")}"
        + (Other > 0 ? $" · {Other} unknown" : "");

    public int Total => Reds + Yellows + Black + Cue + Other;
}

internal readonly record struct LiveDetectCountCandidate(LiveDetectCounts Before, LiveDetectCounts After);

/// <summary>
/// Holds a recent fully certain inventory across brief unclear frames and emits only
/// repeated, lower-count layouts as advisory candidates. It never changes game state.
/// </summary>
internal sealed class LiveDetectCountCandidateTracker
{
    internal const long MaximumBaselineAgeUs = 15_000_000;
    internal const long MaximumConfirmationGapUs = 5_000_000;

    LiveDetectCounts? baseline;
    long baselineSourceTimeUs;
    LiveDetectCounts? pending;
    long pendingSourceTimeUs;
    long lastSourceTimeUs = long.MinValue;

    public LiveDetectCountCandidate? Observe(long sourceTimeUs, LiveDetectCounts counts, bool candidateEligible)
    {
        if (sourceTimeUs <= lastSourceTimeUs)
        {
            Reset();
            return null;
        }
        lastSourceTimeUs = sourceTimeUs;

        if (baseline is not null && sourceTimeUs - baselineSourceTimeUs > MaximumBaselineAgeUs)
            ClearBaseline();

        if (!candidateEligible)
        {
            pending = null;
            return null;
        }

        if (baseline is not { } trusted)
        {
            SetBaseline(counts, sourceTimeUs);
            return null;
        }

        if (counts == trusted)
        {
            pending = null;
            baselineSourceTimeUs = sourceTimeUs;
            return null;
        }

        if (!IsNamedCountDecrease(trusted, counts))
        {
            // A clear increase, mixed increase/decrease, or unknown-colour change
            // makes the previous inventory unsuitable as a pot-candidate baseline.
            SetBaseline(counts, sourceTimeUs);
            return null;
        }

        if (pending is { } expected && counts == expected
            && sourceTimeUs - pendingSourceTimeUs <= MaximumConfirmationGapUs)
        {
            var candidate = new LiveDetectCountCandidate(trusted, counts);
            SetBaseline(counts, sourceTimeUs);
            return candidate;
        }

        // A further decrease before confirmation is compared with the same
        // trusted pre-motion inventory; confirmation still requires a repeat.
        pending = counts;
        pendingSourceTimeUs = sourceTimeUs;
        return null;
    }

    public void Reset()
    {
        ClearBaseline();
        lastSourceTimeUs = long.MinValue;
    }

    void SetBaseline(LiveDetectCounts counts, long sourceTimeUs)
    {
        baseline = counts;
        baselineSourceTimeUs = sourceTimeUs;
        pending = null;
    }

    void ClearBaseline()
    {
        baseline = null;
        baselineSourceTimeUs = 0;
        pending = null;
        pendingSourceTimeUs = 0;
    }

    static bool IsNamedCountDecrease(LiveDetectCounts before, LiveDetectCounts after) =>
        after.Reds <= before.Reds && after.Yellows <= before.Yellows
        && after.Black <= before.Black && after.Cue <= before.Cue
        && after.Other == before.Other
        && (after.Reds < before.Reds || after.Yellows < before.Yellows
            || after.Black < before.Black || after.Cue < before.Cue);
}
