using System.Text.Json;
using SkyEye.Companion.Hybrid;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

/// <summary>Offline report: for every possible pot the reducer emits on a saved Live Detect log (replayed with
/// the logged settled-change evidence, through the current policy), what D3's shadow output said about that
/// colour over the following passes: held or inferred still on the table (a veto if D3 were allowed to act),
/// or nothing (the pot would stand). Nothing here changes the reducer.
///
///   dotnet run --project tests/SkyEye.AgentUi.Checks -- --pot-veto LOG.jsonl [LOG2.jsonl ...]</summary>
static class LiveDetectPotVetoReport
{
    const int LookaheadPasses = 40;   // about 15 s at Live Detect cadence: covers settle plus one comparison
    static bool Detail;

    public static void Run(IEnumerable<string> arguments)
    {
        Detail = arguments.Contains("--detail");
        var paths = arguments.Where(a => a != "--detail").ToArray();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var seenRuns = new HashSet<Guid>();
        int pots = 0, vetoes = 0, motionVetoes = 0, changeVetoes = 0;
        foreach (var path in paths)
        {
            var runs = new Dictionary<Guid, List<LiveDetectTableEventObservation>>();
            var lastEvidence = new Dictionary<Guid, SettledChangeEvidence?>();
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                LiveDetectLogRecord? record;
                try { record = JsonSerializer.Deserialize<LiveDetectLogRecord>(line, options); } catch (JsonException) { continue; }
                if (record?.Kind != "frame" || record.Frame is not { } frame) continue;
                if (!runs.TryGetValue(record.RunId, out var list)) runs[record.RunId] = list = [];
                SettledChangePass? pass = null;
                if (frame.SettledChange is { } sc)
                {
                    if (sc.Evidence is not null) lastEvidence[record.RunId] = sc.Evidence;
                    else if (sc.EvidenceSequence is null) lastEvidence[record.RunId] = null;
                    lastEvidence.TryGetValue(record.RunId, out var carried);
                    var state = Enum.TryParse<SettledChangeState>(sc.State, out var parsed) ? parsed : SettledChangeState.Settled;
                    pass = new(state, sc.MotionFraction, sc.StillPasses, sc.ComputeMs,
                        sc.EvidenceSequence is not null ? carried : null, sc.EvidenceIsNew, sc.UnavailableReason);
                }
                list.Add(new(new EvidenceSource("replay-camera", frame.Timeline, 1, frame.CalibrationId, frame.CameraConnectionId.ToString("N")),
                    frame.CameraConnectionId, frame.MediaGeneration, frame.FrameId, frame.CapturedAt, frame.SourceTimeUs, frame.Timeline,
                    frame.CalibrationId, new LiveDetectCounts(frame.Reds, frame.Yellows, frame.Black, frame.Cue, frame.Other),
                    LiveDetectTableEventTrackerChecks.CaptureLayoutForReplay(frame.Balls), frame.Obstructed, frame.Uncertain,
                    frame.LayoutUncertain, frame.SpatiallyCompatible == false, frame.PackedRackEvidence, SettledChange: pass));
            }
            foreach (var (runId, observations) in runs)
            {
                if (!seenRuns.Add(runId)) continue;   // the same run can appear in several pulls
                if (!observations.Any(o => o.SettledChange is not null)) continue;
                var tracker = new LiveDetectTableEventTracker(LiveDetectReducerOptions.Default);
                var batches = observations.Select(o => tracker.Observe(o)).ToArray();
                for (int i = 0; i < batches.Length; i++)
                    foreach (var tableEvent in batches[i].TableEvents.Where(e => e.Type == "possible_pot"))
                    {
                        if (tableEvent.Before is not { } before || tableEvent.After is not { } after) continue;
                        var colours = new List<string>();
                        if (after.Reds < before.Reds) colours.Add("red");
                        if (after.Yellows < before.Yellows) colours.Add("yellow");
                        if (after.Black < before.Black) colours.Add("black");
                        if (after.Cue < before.Cue) colours.Add("cue");
                        pots++;
                        string verdict = "none: the pot would stand";
                        // Candidate rule: a ball cannot leave the table without motion. Walk back to the last pass
                        // that still showed the higher count for every dropped colour; if every pass from there to
                        // the pot was Settled (no motion), the decrease is a detection drop, not a pot.
                        int lastHigh = -1;
                        for (int k = i; k >= 0; k--)
                        {
                            var c = observations[k].Counts;
                            if ((!colours.Contains("red") || c.Reds >= before.Reds) && (!colours.Contains("yellow") || c.Yellows >= before.Yellows)
                                && (!colours.Contains("black") || c.Black >= before.Black) && (!colours.Contains("cue") || c.Cue >= before.Cue))
                            { lastHigh = k; break; }
                        }
                        bool motionFree = lastHigh >= 0 && Enumerable.Range(lastHigh, i - lastHigh + 1)
                            .All(k => observations[k].SettledChange is { State: SettledChangeState.Settled });
                        string motion = lastHigh < 0 ? "no earlier high count"
                            : $"last {string.Join("+", colours)} at old count {observations[lastHigh].CapturedAt:HH:mm:ss.f}, " +
                              (motionFree ? "table Settled throughout" : "motion in between");
                        if (motionFree) { motionVetoes++; verdict = "MOTION VETO: dropped while the table was still"; }
                        // Sturdier candidate: the next comparison after the pot shows no change at the ball's last
                        // detected position, so the ball never moved and cannot have been potted.
                        string changeAtLast = "no later comparison";
                        if (lastHigh >= 0)
                        {
                            var lastBalls = observations[lastHigh].Layout.Where(b => colours.Contains(b.Colour.ToString().ToLowerInvariant())).ToArray();
                            for (int k = i; k < Math.Min(batches.Length, i + 2 * LookaheadPasses); k++)
                            {
                                if (observations[k].SettledChange is not { EvidenceIsNew: true, Evidence: { Status: SettledChangeStatus.Compared, Comparison: { } comparison } } c) continue;
                                if (c.Evidence.ReferenceWindowEndUs > observations[lastHigh].SourceTimeUs) { changeAtLast = "next comparison starts after the drop"; break; }
                                bool changed = lastBalls.Any(b => LiveDetectSettledChangePolicy.GridChangedNear(comparison, b.X * 2, b.Y, LiveDetectSettledChangePolicy.Near));
                                changeAtLast = lastBalls.Length == 0 ? "no last position" : changed
                                    ? $"seq {c.Evidence.Sequence}: changed at last position (ball moved)"
                                    : $"seq {c.Evidence.Sequence}: NO change at last position";
                                if (!changed && lastBalls.Length > 0) { changeVetoes++; verdict = "CHANGE VETO: nothing changed where it stood" + (motionFree ? " (also still table)" : ""); }
                                break;
                            }
                        }
                        motion += "; " + changeAtLast;
                        for (int k = i; k < Math.Min(batches.Length, i + LookaheadPasses); k++)
                        {
                            if (k > i && batches[k].TableEvents.Any(e => e.Type is "possible_pot" or "balls_returned_candidate")) break;
                            if (batches[k].SettledChange is not { } shadow) continue;
                            var keep = shadow.BallStates.FirstOrDefault(s => colours.Contains(s.Colour) && s.State is "held" or "inferred");
                            if (keep is null) continue;
                            verdict = (motionFree ? "MOTION VETO and " : "") + $"VETO: {keep.Colour} {keep.State} at ({keep.TableX:0.000},{keep.TableY:0.000}) " +
                                $"{observations[k].CapturedAt:HH:mm:ss} (+{(observations[k].CapturedAt - observations[i].CapturedAt).TotalSeconds:0.0}s)";
                            vetoes++;
                            break;
                        }
                        Console.WriteLine($"{runId.ToString()[..8]} {observations[i].CapturedAt:HH:mm:ss} possible pot {string.Join("+", colours)} " +
                            $"({before.Reds}/{before.Yellows}/{before.Black}/{before.Cue} -> {after.Reds}/{after.Yellows}/{after.Black}/{after.Cue}): {verdict} [{motion}]");
                        if (Detail && !verdict.Contains("VETO"))
                        {
                            // Why no hold: per-pass state over the window (settle state, chain, trust, the colour's state).
                            var lines = new List<string>();
                            for (int k = Math.Max(0, i - 3); k < Math.Min(batches.Length, i + LookaheadPasses); k++)
                            {
                                var o = observations[k]; var s = batches[k].SettledChange;
                                string colourState = s is null ? "-" : string.Join(",", s.BallStates.Where(b => colours.Contains(b.Colour)).Select(b => b.State)) is { Length: > 0 } st ? st : "none";
                                lines.Add($"      {o.CapturedAt:HH:mm:ss.f} {o.SettledChange?.State.ToString() ?? "-",-8} seq {o.SettledChange?.Evidence?.Sequence.ToString() ?? "-",-3} " +
                                    $"intact {s?.ChainIntact.ToString() ?? "-",-5} len {s?.ChainLength.ToString() ?? "-",-2} obstr {o.Obstructed,-5} layoutUnc {o.LayoutUncertain,-5} " +
                                    $"counts {o.Counts.Reds}/{o.Counts.Yellows}/{o.Counts.Black}/{o.Counts.Cue} {string.Join("+", colours)}={colourState} outcome {batches[k].CheckOutcome}");
                            }
                            // Print only rows where something changes, to keep it readable.
                            string? previous = null;
                            foreach (var l in lines) { string key = l[16..]; if (key != previous) Console.WriteLine(l); previous = key; }
                        }
                    }
            }
        }
        Console.WriteLine($"pot-veto: {pots} possible pots; D3 inference contradicts {vetoes}; motion-free drop {motionVetoes}; no change at last position {changeVetoes}");
    }
}
