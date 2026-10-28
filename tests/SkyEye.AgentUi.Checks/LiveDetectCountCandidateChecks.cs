using System.Text.Json;
using SkyEye.Companion.Hybrid;

internal static class LiveDetectCountCandidateChecks
{
    static readonly LiveDetectCounts Full = new(7, 7, 1, 1, 0);
    static readonly LiveDetectCounts OneYellowDown = new(7, 6, 1, 1, 0);

    public static void Run()
    {
        var recovered = new LiveDetectCountCandidateTracker();
        Require(recovered.Observe(1_000_000, Full, true) is null, "first clear inventory establishes baseline");
        Require(recovered.Observe(2_400_000, OneYellowDown, false) is null, "uncertain decrease is ignored");
        Require(recovered.Observe(3_800_000, OneYellowDown, true) is null, "first recovered clear decrease is only pending");
        var candidate = recovered.Observe(5_200_000, OneYellowDown, true);
        Require(candidate is { Before: var before, After: var after } && before == Full && after == OneYellowDown,
            "repeated clear decrease across brief uncertainty becomes a candidate from the trusted baseline");

        var transient = new LiveDetectCountCandidateTracker();
        transient.Observe(1_000_000, Full, true);
        transient.Observe(2_400_000, OneYellowDown, false);
        Require(transient.Observe(3_800_000, Full, true) is null
            && transient.Observe(5_200_000, Full, true) is null,
            "transient uncertain loss that recovers creates no candidate");

        var adviceUnavailable = new LiveDetectCountCandidateTracker();
        adviceUnavailable.Observe(1_000_000, Full, true);
        var noCue = new LiveDetectCounts(7, 6, 1, 0, 0);
        Require(adviceUnavailable.Observe(2_400_000, noCue, false) is null
            && adviceUnavailable.Observe(3_800_000, noCue, false) is null,
            "broad uncertainty suppresses candidates even when visible layout is clear");

        var changing = new LiveDetectCountCandidateTracker();
        changing.Observe(1_000_000, Full, true);
        changing.Observe(2_400_000, OneYellowDown, true);
        var mixedIncrease = new LiveDetectCounts(8, 6, 1, 1, 0);
        Require(changing.Observe(3_800_000, mixedIncrease, true) is null
            && changing.Observe(5_200_000, mixedIncrease, true) is null,
            "mixed count increase and decrease resets the baseline without a pot candidate");

        var unknown = new LiveDetectCountCandidateTracker();
        unknown.Observe(1_000_000, Full, true);
        var unknownChanged = new LiveDetectCounts(7, 6, 1, 1, 1);
        Require(unknown.Observe(2_400_000, unknownChanged, true) is null
            && unknown.Observe(3_800_000, unknownChanged, true) is null,
            "unknown-colour change cannot be called a pot");

        var expired = new LiveDetectCountCandidateTracker();
        expired.Observe(1_000_000, Full, true);
        expired.Observe(17_000_001, OneYellowDown, false);
        Require(expired.Observe(17_100_000, OneYellowDown, true) is null
            && expired.Observe(18_500_000, OneYellowDown, true) is null,
            "unclear interval beyond the baseline lifetime starts a fresh inventory");

        var confirmation = new LiveDetectCountCandidateTracker();
        confirmation.Observe(1_000_000, Full, true);
        confirmation.Observe(2_400_000, OneYellowDown, true);
        Require(confirmation.Observe(8_000_001, OneYellowDown, true) is null
            && confirmation.Observe(9_400_001, OneYellowDown, true) is not null,
            "a stale pending sample needs a fresh repeated clear confirmation");

        var cumulative = new LiveDetectCountCandidateTracker();
        cumulative.Observe(1_000_000, Full, true);
        var yellowAndCueDown = new LiveDetectCounts(7, 5, 1, 0, 0);
        cumulative.Observe(2_400_000, OneYellowDown, true);
        Require(cumulative.Observe(3_800_000, yellowAndCueDown, true) is null,
            "additional decrease replaces the pending target without discarding its baseline");
        var confirmedCumulative = cumulative.Observe(5_200_000, yellowAndCueDown, true);
        Require(confirmedCumulative is { Before: var cumulativeBefore, After: var cumulativeAfter }
            && cumulativeBefore == Full && cumulativeAfter == yellowAndCueDown,
            "repeated cumulative decrease reports all changes from the pre-motion inventory");

        Console.WriteLine("PASS Live Detect candidate baseline survives brief uncertainty, requires repeated clear counts, and expires safely");
    }

    public static void ReplaySummary(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var tracker = new LiveDetectCountCandidateTracker();
        var candidates = new List<(long SourceTimeUs, string Clip, LiveDetectCountCandidate Candidate)>();
        foreach (var row in document.RootElement.GetProperty("tableStateCounts").EnumerateArray())
        {
            var value = row.GetProperty("counts");
            var counts = new LiveDetectCounts(value.GetProperty("red").GetInt32(),
                value.GetProperty("yellow").GetInt32(), value.GetProperty("black").GetInt32(),
                value.GetProperty("cue").GetInt32(), value.GetProperty("other").GetInt32());
            bool candidateEligible = !row.GetProperty("layoutUncertain").GetBoolean()
                && !row.GetProperty("obstructed").GetBoolean()
                && !row.GetProperty("uncertain").GetBoolean();
            if (tracker.Observe(row.GetProperty("sourceTimeUs").GetInt64(), counts, candidateEligible) is { } candidate)
                candidates.Add((row.GetProperty("sourceTimeUs").GetInt64(), row.GetProperty("clipName").GetString() ?? "unknown", candidate));
        }

        Console.WriteLine($"Replayed {document.RootElement.GetProperty("uniqueTimelineSampleCount").GetInt32()} exact Pixel 11 timeline inventories through the production candidate tracker.");
        Console.WriteLine($"Candidate count: {candidates.Count}; canonical GameState mutation: none (candidate-only tracker).");
        foreach (var item in candidates)
        {
            var before = item.Candidate.Before;
            var after = item.Candidate.After;
            Console.WriteLine($"{item.Clip} at source {item.SourceTimeUs}: {Describe(before, after)}");
        }
    }

    static string Describe(LiveDetectCounts before, LiveDetectCounts after)
    {
        var changes = new List<string>();
        Add("red", before.Reds, after.Reds);
        Add("yellow", before.Yellows, after.Yellows);
        Add("black", before.Black, after.Black);
        Add("cue ball", before.Cue, after.Cue);
        return changes.Count == 0 ? "no named count decrease" : string.Join("; ", changes);

        void Add(string kind, int prior, int current)
        {
            if (current < prior) changes.Add($"{kind} {prior} → {current}");
        }
    }

    static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
