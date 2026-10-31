using System.Text.Json;
using SkyEye.Companion.Hybrid;

internal static class LiveDetectCountCandidateChecks
{
    static readonly LiveDetectCounts Full = new(7, 7, 1, 1, 0);
    static readonly LiveDetectCounts OneYellowDown = new(7, 6, 1, 1, 0);

    public static void Run()
    {
        var cursor = new LiveDetectFrameCursor();
        Require(cursor.TryAccept(6, "camera-a", 5394, 72570736044, out _), "initial decoded frame is admitted");
        Require(cursor.TryAccept(7, "camera-a", 282, 72581337288, out var changedTimeline) && !changedTimeline,
            "decoder recovery admits a restarted frame ID when camera PTS continues");
        Require(!cursor.TryAccept(6, "camera-a", 6000, 72582337288, out _), "late old-decoder frames cannot return");
        Require(!cursor.TryAccept(7, "camera-a", 282, 72581337288, out _)
            && !cursor.TryAccept(7, "camera-a", 283, 72580337288, out _),
            "duplicates and backward camera time remain rejected after recovery");
        Require(!cursor.TryAccept(8, "camera-a", 1, 72580337288, out _),
            "new decoder generation cannot turn an older camera image into fresh evidence");
        Require(cursor.TryAccept(8, "camera-a", 1, 72583337288, out _), "subsequent fresh recovery succeeds");
        Require(!cursor.TryAccept(8, "camera-b", 2, 100, out _), "timeline cannot switch within a decoder generation");
        Require(cursor.TryAccept(9, "camera-b", 1, 100, out changedTimeline) && changedTimeline,
            "a new camera timeline is admitted with an explicit baseline reset signal");

        var initialRack = new LiveDetectCountCandidateTracker();
        Require(initialRack.Observe(1_000_000, Full, true, packedRack: true) is null
            && initialRack.Observe(2_000_000, Full, true, packedRack: true) is null
            && initialRack.Observe(3_000_000, Full, true, packedRack: true) is { Kind: LiveDetectChangeKind.Racked },
            "initial rack is reported only after three supported packed-rack observations");
        initialRack.Observe(4_000_000, OneYellowDown, true);
        initialRack.Observe(5_000_000, OneYellowDown, true);
        Require(initialRack.Observe(6_000_000, OneYellowDown, true) is { Kind: LiveDetectChangeKind.Decrease },
            "a sustained inventory decrease becomes the current baseline before the long obstruction");
        initialRack.Observe(20_000_000, OneYellowDown, false);
        Require(initialRack.Observe(21_000_000, Full, true, packedRack: true) is null
            && initialRack.Observe(22_000_000, Full, true, packedRack: true) is null
            && initialRack.Observe(23_000_000, Full, true, packedRack: true) is { Kind: LiveDetectChangeKind.Racked },
            "a long obstructed re-rack can establish and report a fresh rack while retaining the last clear inventory");

        var recovered = new LiveDetectCountCandidateTracker();
        Require(recovered.Observe(1_000_000, Full, true) is null, "first clear inventory establishes baseline");
        Require(recovered.Observe(2_400_000, OneYellowDown, false) is null, "uncertain decrease is ignored");
        Require(recovered.Observe(3_800_000, OneYellowDown, true) is null, "first recovered clear decrease is only pending");
        Require(recovered.Observe(5_200_000, OneYellowDown, true) is null, "second recovered decrease is still pending");
        var candidate = recovered.Observe(6_600_000, OneYellowDown, true);
        Require(candidate is { Before: var before, After: var after } && before == Full && after == OneYellowDown,
            "repeated clear decrease across brief uncertainty becomes a candidate from the trusted baseline");

        var transient = new LiveDetectCountCandidateTracker();
        transient.Observe(1_000_000, Full, true);
        transient.Observe(2_400_000, OneYellowDown, false);
        Require(transient.Observe(3_800_000, Full, true) is null
            && transient.Observe(5_200_000, Full, true) is null,
            "transient uncertain loss that recovers creates no candidate");

        var twoFrameDropout = new LiveDetectCountCandidateTracker();
        twoFrameDropout.Observe(1_000_000, Full, true);
        Require(twoFrameDropout.Observe(1_400_000, OneYellowDown, true) is null
            && twoFrameDropout.Observe(1_800_000, OneYellowDown, true) is null
            && twoFrameDropout.Observe(2_200_000, Full, true) is null,
            "a two-frame pocket-edge miss that returns to the trusted inventory creates no pot candidate");
        twoFrameDropout.Observe(2_600_000, OneYellowDown, true);
        Require(twoFrameDropout.Observe(3_000_000, OneYellowDown, true) is null
            && twoFrameDropout.Observe(3_400_000, OneYellowDown, true) is { Kind: LiveDetectChangeKind.Decrease },
            "a sustained clear inventory change remains detectable after three samples");

        var adviceUnavailable = new LiveDetectCountCandidateTracker();
        adviceUnavailable.Observe(1_000_000, Full, true);
        var noCue = new LiveDetectCounts(7, 6, 1, 0, 0);
        Require(adviceUnavailable.Observe(2_400_000, noCue, false) is null
            && adviceUnavailable.Observe(3_800_000, noCue, false) is null,
            "an uncertain cue-absent layout cannot establish a candidate");
        adviceUnavailable.Observe(5_200_000, noCue, true);
        Require(adviceUnavailable.Observe(6_600_000, noCue, true) is null
            && adviceUnavailable.Observe(8_000_000, noCue, true) is { Kind: LiveDetectChangeKind.Decrease,
            Before.Cue: 1, After.Cue: 0, Before.Yellows: 7, After.Yellows: 6 },
            "repeated clear cue-absent layout reports cue and colour loss from the pre-motion inventory");
        adviceUnavailable.Observe(9_400_000, OneYellowDown, true);
        Require(adviceUnavailable.Observe(10_800_000, OneYellowDown, true) is null
            && adviceUnavailable.Observe(12_200_000, OneYellowDown, true) is { Kind: LiveDetectChangeKind.Increase,
            Before.Cue: 0, After.Cue: 1 }, "cue return is reported after cue-absent play without restarting");

        var changing = new LiveDetectCountCandidateTracker();
        changing.Observe(1_000_000, Full, true);
        changing.Observe(2_400_000, OneYellowDown, true);
        var mixedIncrease = new LiveDetectCounts(8, 6, 1, 1, 0);
        Require(changing.Observe(3_800_000, mixedIncrease, true) is null
            && changing.Observe(5_200_000, mixedIncrease, true) is null
            && changing.Observe(6_600_000, mixedIncrease, true) is null,
            "an unsupported mixed increase and decrease cannot replace the valid baseline");

        var unknown = new LiveDetectCountCandidateTracker();
        unknown.Observe(1_000_000, Full, true);
        var unknownChanged = new LiveDetectCounts(7, 6, 1, 1, 1);
        Require(unknown.Observe(2_400_000, unknownChanged, true) is null
            && unknown.Observe(3_800_000, unknownChanged, true) is null,
            "unknown-colour change cannot be called a pot");

        var longObstruction = new LiveDetectCountCandidateTracker();
        longObstruction.Observe(1_000_000, Full, true);
        longObstruction.Observe(17_000_001, OneYellowDown, false);
        Require(longObstruction.Observe(17_100_000, OneYellowDown, true) is null
            && longObstruction.Observe(18_500_000, OneYellowDown, true) is null
            && longObstruction.Observe(19_900_000, OneYellowDown, true) is { Kind: LiveDetectChangeKind.Decrease,
                Before: var beforeLongGap, After: var afterLongGap }
            && beforeLongGap == Full && afterLongGap == OneYellowDown,
            "a stable named count decrease after a long obstruction is compared with the last clear inventory");

        var confirmation = new LiveDetectCountCandidateTracker();
        confirmation.Observe(1_000_000, Full, true);
        confirmation.Observe(2_400_000, OneYellowDown, true);
        Require(confirmation.Observe(8_000_001, OneYellowDown, true) is null
            && confirmation.Observe(9_400_001, OneYellowDown, true) is null
            && confirmation.Observe(10_800_001, OneYellowDown, true) is not null,
            "a stale pending sample needs a fresh repeated clear confirmation");

        var cumulative = new LiveDetectCountCandidateTracker();
        cumulative.Observe(1_000_000, Full, true);
        var yellowAndCueDown = new LiveDetectCounts(7, 5, 1, 0, 0);
        cumulative.Observe(2_400_000, OneYellowDown, true);
        Require(cumulative.Observe(3_800_000, yellowAndCueDown, true) is null,
            "additional decrease replaces the pending target without discarding its baseline");
        Require(cumulative.Observe(5_200_000, yellowAndCueDown, true) is null,
            "the new cumulative target still needs three clear samples");
        var confirmedCumulative = cumulative.Observe(6_600_000, yellowAndCueDown, true);
        Require(confirmedCumulative is { Before: var cumulativeBefore, After: var cumulativeAfter }
            && cumulativeBefore == Full && cumulativeAfter == yellowAndCueDown,
            "repeated cumulative decrease reports all changes from the pre-motion inventory");

        var returns = new LiveDetectCountCandidateTracker();
        returns.Observe(1_000_000, OneYellowDown, true);
        Require(returns.Observe(2_000_000, Full, true) is null, "a single increase cannot replace the baseline");
        Require(returns.Observe(3_000_000, OneYellowDown, true) is null
            && returns.Observe(4_000_000, OneYellowDown, true) is null,
            "one false extra ball followed by its disappearance cannot create a false pot");
        returns.Observe(5_000_000, Full, true);
        Require(returns.Observe(6_000_000, Full, true) is null
            && returns.Observe(7_000_000, Full, true) is { Kind: LiveDetectChangeKind.Increase },
            "a repeated return is reported without inventing a rack from counts alone");
        returns.Observe(8_000_000, Full, true, packedRack: true);
        Require(returns.Observe(9_000_000, Full, true, packedRack: true) is null
            && returns.Observe(10_000_000, Full, true, packedRack: true) is { Kind: LiveDetectChangeKind.Racked },
            "a full spread inventory becoming a supported packed rack is reported");
        Require(returns.Observe(11_000_000, Full, true, packedRack: true) is null,
            "same rack is not announced again");
        returns.Observe(12_000_000, Full, true, packedRack: false);
        Require(returns.Observe(13_000_000, Full, true, packedRack: true) is null
            && returns.Observe(14_000_000, Full, true, packedRack: true) is null,
            "one packed-rack telemetry flicker cannot announce a new rack");
        returns.Observe(15_000_000, Full, true, packedRack: false);
        returns.Observe(16_000_000, Full, true, packedRack: false);
        returns.Observe(17_000_000, OneYellowDown, true);
        Require(returns.Observe(18_000_000, OneYellowDown, true) is null
            && returns.Observe(19_000_000, OneYellowDown, true) is { Kind: LiveDetectChangeKind.Decrease },
            "pot candidates continue after the rack is opened without restarting the tracker");
        returns.Observe(20_000_000, Full, true, packedRack: true);
        Require(returns.Observe(21_000_000, Full, true, packedRack: true) is null
            && returns.Observe(22_000_000, Full, true, packedRack: true) is { Kind: LiveDetectChangeKind.Racked },
            "re-rack after a pot creates a new supported baseline in the same run");
        returns.Observe(23_000_000, OneYellowDown, true);
        Require(returns.Observe(24_000_000, OneYellowDown, true) is null
            && returns.Observe(25_000_000, OneYellowDown, true) is { Kind: LiveDetectChangeKind.Decrease },
            "a fresh decrease compares with the new rack rather than the previous frame's baseline");

        var relabel = new LiveDetectCountCandidateTracker();
        relabel.Observe(1_000_000, OneYellowDown, true);
        var mixedValid = new LiveDetectCounts(6, 7, 1, 1, 0);
        relabel.Observe(2_000_000, mixedValid, true);
        Require(relabel.Observe(3_000_000, mixedValid, true) is null
            && relabel.Observe(4_000_000, mixedValid, true) is { Kind: LiveDetectChangeKind.Changed },
            "a repeated mixed change is reported as inventory change rather than a pot");

        Console.WriteLine("PASS Live Detect repeats inventory changes, rejects transient extras, retains clear history, and handles returns and re-racks");
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
                && !row.GetProperty("obstructed").GetBoolean();
            if (tracker.Observe(row.GetProperty("sourceTimeUs").GetInt64(), counts, candidateEligible) is { } candidate)
                candidates.Add((row.GetProperty("sourceTimeUs").GetInt64(), row.GetProperty("clipName").GetString() ?? "unknown", candidate));
        }

        Console.WriteLine($"Replayed {document.RootElement.GetProperty("uniqueTimelineSampleCount").GetInt32()} exact Pixel 11 timeline inventories through the production candidate tracker.");
        Console.WriteLine($"Candidate count: {candidates.Count}; canonical GameState mutation: none (candidate-only tracker).");
        foreach (var item in candidates)
        {
            var before = item.Candidate.Before;
            var after = item.Candidate.After;
            Console.WriteLine($"{item.Clip} at source {item.SourceTimeUs}: {item.Candidate.Kind} · {Describe(before, after)}");
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
            if (current != prior) changes.Add($"{kind} {prior} → {current}");
        }
    }

    static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
