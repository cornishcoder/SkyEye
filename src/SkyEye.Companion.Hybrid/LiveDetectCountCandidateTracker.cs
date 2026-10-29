namespace SkyEye.Companion.Hybrid;

/// <summary>Decoder frame IDs restart on each media generation; camera PTS does not.</summary>
internal sealed class LiveDetectFrameCursor
{
    public long MediaGeneration { get; private set; }
    string? timeline;
    long frameId, sourceTimeUs;

    public bool TryAccept(long generation, string sourceTimeline, long nextFrameId, long nextSourceTimeUs,
        out bool timelineChanged)
    {
        timelineChanged = false;
        if (generation <= 0 || nextFrameId <= 0 || nextSourceTimeUs < 0 || string.IsNullOrWhiteSpace(sourceTimeline)
            || generation < MediaGeneration) return false;
        bool sameTimeline = timeline == sourceTimeline;
        if (timeline is not null && generation == MediaGeneration && !sameTimeline) return false;
        if (sameTimeline && (nextSourceTimeUs <= sourceTimeUs
            || generation == MediaGeneration && nextFrameId <= frameId)) return false;
        timelineChanged = timeline is not null && !sameTimeline;
        MediaGeneration = generation; timeline = sourceTimeline;
        frameId = nextFrameId; sourceTimeUs = nextSourceTimeUs;
        return true;
    }
}

internal readonly record struct LiveDetectCounts(int Reds, int Yellows, int Black, int Cue, int Other)
{
    public string Summary => $"{Reds} reds · {Yellows} yellows · {Black} black · {Cue} cue ball{(Cue == 1 ? "" : "s")}"
        + (Other > 0 ? $" · {Other} unknown" : "");

    public int Total => Reds + Yellows + Black + Cue + Other;
}

internal enum LiveDetectChangeKind { Decrease, Increase, Changed, Racked }

internal readonly record struct LiveDetectCountCandidate(LiveDetectCounts Before, LiveDetectCounts After)
{
    public LiveDetectChangeKind Kind { get; init; }
}

/// <summary>
/// Holds the last fully certain inventory across unclear frames and emits only
/// repeated inventory changes as advisory candidates. Rack events require a supported
/// packed full rack, not just a sixteen-ball count. It never changes game state.
/// </summary>
internal sealed class LiveDetectCountCandidateTracker
{
    internal const long MaximumConfirmationGapUs = 5_000_000;

    LiveDetectCounts? baseline;
    LiveDetectCounts? pending;
    long pendingSourceTimeUs;
    long lastSourceTimeUs = long.MinValue;
    bool baselinePackedRack, pendingPackedRack;

    public LiveDetectCountCandidate? Observe(long sourceTimeUs, LiveDetectCounts counts, bool candidateEligible,
        bool packedRack = false)
    {
        if (sourceTimeUs <= lastSourceTimeUs)
        {
            Reset();
            return null;
        }
        lastSourceTimeUs = sourceTimeUs;

        if (!candidateEligible || counts.Reds is < 0 or > 7 || counts.Yellows is < 0 or > 7
            || counts.Black is < 0 or > 1 || counts.Cue is < 0 or > 1 || counts.Other != 0)
        {
            pending = null;
            return null;
        }
        packedRack = packedRack && counts == new LiveDetectCounts(7, 7, 1, 1, 0);

        if (baseline is not { } trusted)
        {
            // A rack is a current observation, so it can be reported after a
            // long hands-on re-rack without treating its packed layout as a pot.
            SetBaseline(counts, packedRack: false);
            if (packedRack)
            {
                pending = counts; pendingSourceTimeUs = sourceTimeUs; pendingPackedRack = true;
            }
            return null;
        }

        if (counts == trusted && packedRack == baselinePackedRack)
        {
            pending = null;
            baselinePackedRack = packedRack;
            return null;
        }

        if (pending is { } expected && counts == expected
            && packedRack == pendingPackedRack
            && sourceTimeUs - pendingSourceTimeUs <= MaximumConfirmationGapUs)
        {
            if (counts == trusted && !packedRack)
            {
                SetBaseline(counts, packedRack);
                return null;
            }
            var kind = packedRack && !baselinePackedRack ? LiveDetectChangeKind.Racked
                : IsNamedCountDecrease(trusted, counts) ? LiveDetectChangeKind.Decrease
                : IsNamedCountDecrease(counts, trusted) ? LiveDetectChangeKind.Increase
                : LiveDetectChangeKind.Changed;
            var candidate = new LiveDetectCountCandidate(trusted, counts) { Kind = kind };
            SetBaseline(counts, packedRack);
            return candidate;
        }

        // Confirm increases as well as decreases. A transient extra detection must
        // not replace the baseline and make its disappearance look like a pot.
        pending = counts;
        pendingSourceTimeUs = sourceTimeUs;
        pendingPackedRack = packedRack;
        return null;
    }

    public void Reset()
    {
        ClearBaseline();
        lastSourceTimeUs = long.MinValue;
    }

    void SetBaseline(LiveDetectCounts counts, bool packedRack)
    {
        baseline = counts;
        pending = null;
        baselinePackedRack = packedRack;
    }

    void ClearBaseline()
    {
        baseline = null;
        pending = null;
        pendingSourceTimeUs = 0;
        baselinePackedRack = pendingPackedRack = false;
    }

    static bool IsNamedCountDecrease(LiveDetectCounts before, LiveDetectCounts after) =>
        after.Reds <= before.Reds && after.Yellows <= before.Yellows
        && after.Black <= before.Black && after.Cue <= before.Cue
        && after.Other == before.Other
        && (after.Reds < before.Reds || after.Yellows < before.Yellows
            || after.Black < before.Black || after.Cue < before.Cue);
}
