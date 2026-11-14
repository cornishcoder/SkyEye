using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Companion.Hybrid;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

/// <summary>D3 slice 3: settled-change rules in shadow (design 2.3 and 5.2). Policy checks on synthetic
/// evidence, and the golden shadow test: the reducer's outcomes and events are identical with and without
/// evidence attached.</summary>
internal static class LiveDetectSettledChangeChecks
{
    static readonly Guid Epoch = Guid.NewGuid();

    public static void Run()
    {
        PolicyRules();
        int passes = 0, proposals = 0, events = 0;
        var kinds = new HashSet<string>();
        foreach (int seed in Enumerable.Range(1, 40))
            foreach (var options in new[] { LiveDetectReducerOptions.Default, LiveDetectReducerOptions.Previous with { SettledChange = LiveDetectSettledChangeMode.Shadow } })
            {
                passes += GoldenShadow(GenerateObservations(seed, 400), options, seed, out int p, out int e, kinds);
                proposals += p; events += e;
            }
        // The test is only meaningful if the streams exercise the reducer and the shadow rules.
        Require(events > 1000 && proposals > 1000 && kinds.IsSupersetOf(["hold", "departure_support", "unexplained_change"]),
            $"Golden streams must exercise the reducer and shadow rules (events {events}, proposals {proposals}, kinds {string.Join(",", kinds)})");
        Console.WriteLine($"PASS D3 settled-change rules in shadow: hold, no hold near change, chain breaks, black/cue inference, departure support, pocket regions, not-seen states; golden shadow test identical over {passes} generated passes ({events} table events, {proposals} shadow proposals of kinds {string.Join(", ", kinds.Order())})");
    }

    /// <summary>Replays every run in a saved Live Detect log without shadow evidence, with any real logged
    /// shadow evidence, and with random shadow evidence, failing on any difference in outcome, events, clips or baseline.</summary>
    public static void GoldenShadowLog(string path)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var runs = new Dictionary<Guid, List<LiveDetectTableEventObservation>>();
        var lastEvidenceByRun = new Dictionary<Guid, SettledChangeEvidence?>();
        int loggedShadowFrames = 0;
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            LiveDetectLogRecord? record;
            try { record = JsonSerializer.Deserialize<LiveDetectLogRecord>(line, options); } catch (JsonException) { continue; }
            if (record?.Kind != "frame" || record.Frame is not { } frame) continue;
            if (!runs.TryGetValue(record.RunId, out var list)) runs[record.RunId] = list = [];
            var layout = LiveDetectTableEventTrackerChecks.CaptureLayoutForReplay(frame.Balls);
            var counts = new LiveDetectCounts(frame.Reds, frame.Yellows, frame.Black, frame.Cue, frame.Other);
            var source = new EvidenceSource("replay-camera", frame.Timeline, 1, frame.CalibrationId, frame.CameraConnectionId.ToString("N"));
            SettledChangePass? loggedPass = null;
            if (frame.SettledChange is { } sc)
            {
                loggedShadowFrames++;
                if (sc.Evidence is not null) lastEvidenceByRun[record.RunId] = sc.Evidence;
                else if (sc.EvidenceSequence is null) lastEvidenceByRun[record.RunId] = null;
                lastEvidenceByRun.TryGetValue(record.RunId, out var carriedEvidence);
                var state = Enum.TryParse<SettledChangeState>(sc.State, out var parsed) ? parsed : SettledChangeState.Settled;
                loggedPass = new(state, sc.MotionFraction, sc.StillPasses, sc.ComputeMs,
                    sc.EvidenceSequence is not null ? carriedEvidence : null, sc.EvidenceIsNew, sc.UnavailableReason);
            }
            list.Add(new(source, frame.CameraConnectionId, frame.MediaGeneration, frame.FrameId, frame.CapturedAt,
                frame.SourceTimeUs, frame.Timeline, frame.CalibrationId, counts, layout, frame.Obstructed, frame.Uncertain,
                frame.LayoutUncertain, frame.SpatiallyCompatible == false, frame.PackedRackEvidence,
                SettledChange: loggedPass));
        }
        int frames = 0, proposals = 0, events = 0, loggedIntact = 0, loggedProposals = 0;
        var loggedComparisons = new List<string>();
        foreach (var (runId, observations) in runs)
        {
            frames += GoldenShadow(observations, LiveDetectReducerOptions.Default, runId.GetHashCode(), out int p, out int e, [],
                out int runLoggedIntact, out int runLoggedProposals, loggedComparisons);
            proposals += p; events += e;
            loggedIntact += runLoggedIntact; loggedProposals += runLoggedProposals;
        }
        string loggedSummary = loggedShadowFrames > 0
            ? $"; logged shadow replay: {loggedShadowFrames} frames, {loggedIntact} intact passes, {loggedProposals} proposals [{string.Join("; ", loggedComparisons)}]"
            : "";
        Console.WriteLine($"PASS golden shadow {Path.GetFileName(path)}: {runs.Count} runs, {frames} frames, {events} table events identical with and without shadow evidence ({proposals} random shadow proposals computed{loggedSummary})");
    }

    static int GoldenShadow(IReadOnlyList<LiveDetectTableEventObservation> observations, LiveDetectReducerOptions options, int seed,
        out int proposals, out int events, HashSet<string> kinds) =>
        GoldenShadow(observations, options, seed, out proposals, out events, kinds, out _, out _, null);

    static int GoldenShadow(IReadOnlyList<LiveDetectTableEventObservation> observations, LiveDetectReducerOptions options, int seed,
        out int proposals, out int events, HashSet<string> kinds, out int loggedIntact, out int loggedProposals, List<string>? loggedComparisons)
    {
        var plain = new LiveDetectTableEventTracker(options);
        var shadow = new LiveDetectTableEventTracker(options);
        var logged = new LiveDetectTableEventTracker(options);
        var rng = new Random(seed);
        long sequence = 0; proposals = 0; events = 0; loggedIntact = 0; loggedProposals = 0;
        for (int i = 0; i < observations.Count; i++)
        {
            var observation = observations[i];
            var a = plain.Observe(observation with { SettledChange = null });
            var b = shadow.Observe(observation with { SettledChange = RandomPass(rng, observation.SourceTimeUs, ref sequence) });
            var c = logged.Observe(observation);
            string left = Project(a), right = Project(b), real = Project(c);
            if (left != right)
                throw new InvalidOperationException($"Shadow evidence changed the reducer at pass {i} (seed {seed}):\n{left}\n{right}");
            if (left != real)
                throw new InvalidOperationException($"Logged shadow evidence changed the reducer at pass {i}:\n{left}\n{real}");
            if (a.SettledChange is not null) throw new InvalidOperationException("No evidence must give no shadow output");
            proposals += b.SettledChange?.Proposals.Length ?? 0; events += a.TableEvents.Length;
            if (c.SettledChange is { ChainIntact: true }) loggedIntact++;
            loggedProposals += c.SettledChange?.Proposals.Length ?? 0;
            if (loggedComparisons is not null && c.SettledChange is { EvidenceIsNew: true } sc)
            {
                string propText = sc.Proposals.IsEmpty ? "0"
                    : string.Join(",", sc.Proposals.Select(p => $"{p.Kind}@({p.TableX:0.000},{p.TableY:0.000})"));
                loggedComparisons.Add($"seq {sc.EvidenceSequence}: intact={sc.ChainIntact} len={sc.ChainLength} proposals={propText}");
            }
            foreach (var p in b.SettledChange?.Proposals ?? []) kinds.Add(p.Kind);
        }
        return observations.Count;
    }

    static string Project(LiveDetectEventBatch batch) => JsonSerializer.Serialize(new
    {
        batch.CheckOutcome,
        Events = batch.TableEvents.Select(e => e with { EventId = Guid.Empty, ActivityId = e.ActivityId is null ? null : Guid.Empty }),
        Clip = batch.ClipActivity is { } clip ? clip with { Id = Guid.Empty } : null,
        batch.ClipEventType, batch.ClipEndSourceTimeUs, batch.SettledBaseline, batch.SettledBaselineAt,
        batch.SettledBaselineMatchesCurrent, batch.BaselineMatchSamples, batch.BaselineBlackNotSeen, batch.BaselineCueNotSeen,
        batch.PocketRegionUncertain, batch.PocketRegionBalls
    });

    // ---- Policy rules on hand-built evidence -------------------------------------------------------

    static void PolicyRules()
    {
        var red = new SettledBallPosition(BallColour.Red, .5, .5);      // table (1.0, 0.5)
        var black = new SettledBallPosition(BallColour.Black, .74, .067); // table (1.48, 0.067), the cushion miss spot
        var yellow = new SettledBallPosition(BallColour.Yellow, .25, .3);
        ImmutableArray<SettledBallPosition> baseline = [red, yellow, black];

        LiveDetectSettledChangeContext Context(ImmutableArray<SettledBallPosition> current, bool advanced = false, bool obstructed = false,
            bool blackNotSeen = false, ImmutableArray<LiveDetectUncertainRegion> pockets = default, ImmutableArray<SettledBallPosition>? baselineLayout = null)
            => new(true, advanced, 1_000_000, baselineLayout ?? baseline, current, true, obstructed, pockets.IsDefault ? [] : pockets, blackNotSeen, false, 5_000_000);

        // U1 hold: the red is not detected, the chain is intact and nothing changed there.
        var policy = new LiveDetectSettledChangePolicy();
        policy.Observe(Context([red, yellow, black], advanced: true), null);
        var shadow = policy.Observe(Context([yellow, black]), Pass(Evidence(1)));
        Require(shadow.ChainIntact && shadow.Proposals.Single().Kind == "hold" && shadow.Proposals[0].Colour == "red"
            && !shadow.Proposals[0].Applied && shadow.BallStates.Single().State == "held", "U1: a missing ball with no change nearby is held, not applied");

        // U1 refused when anything changed near the ball, even a change too small to be a region.
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context([yellow, black]), Pass(Evidence(1, grid: [(1.0, .5)])));
        Require(!shadow.Proposals.Any(p => p.Kind == "hold"), "U1: a change-grid bit near the ball refuses the hold");

        // U1 refused under obstruction.
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context([yellow, black], obstructed: true), Pass(Evidence(1)));
        Require(!shadow.Proposals.Any(p => p.Kind == "hold"), "U1: obstruction refuses the hold");

        // Chain: a gated link, a sequence gap, a new epoch, or a first link that does not start from the baseline view.
        foreach (var (name, second) in new (string, SettledChangeEvidence)[] {
            ("gated", Evidence(2, gates: ["lighting_drift"])), ("gap", Evidence(3)), ("epoch", Evidence(2) with { Epoch = Guid.NewGuid() }),
            ("no reference", Evidence(2) with { Status = SettledChangeStatus.NoReference, Comparison = null }) })
        {
            policy = new(); policy.Observe(Context(baseline, advanced: true), null);
            policy.Observe(Context(baseline), Pass(Evidence(1)));
            shadow = policy.Observe(Context([yellow, black]), Pass(second));
            Require(!shadow.ChainIntact && !shadow.Proposals.Any(p => p.Kind == "hold"), $"Chain breaks on {name}; no hold");
        }
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context([yellow, black]), Pass(Evidence(1) with { ReferenceSegmentStartUs = 2_000_000, ReferenceSegmentEndUs = 3_000_000 }));
        Require(!shadow.ChainIntact, "Chain: the first link must start from the settled view the baseline was taken in");

        // A baseline advance restarts the chain.
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        policy.Observe(Context(baseline), Pass(Evidence(1, gates: ["lighting_drift"])));
        shadow = policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context([yellow, black]), Pass(Evidence(2) with { ReferenceSegmentStartUs = 500_000, ReferenceSegmentEndUs = 1_500_000 }));
        Require(shadow.ChainIntact, "A new baseline restarts a broken chain");

        // Initial NoReference (Sequence 1) after baseline formation records the epoch without breaking the chain;
        // a 3-sample baseline window [s1..s3] or moving BaselineSourceTimeUs on ordinary passes links cleanly.
        policy = new();
        policy.Observe(new(true, true, 1_000_000, baseline, baseline, true, false, [], false, false, 1_700_000, 1_700_000), null);
        shadow = policy.Observe(new(true, false, 1_000_000, baseline, baseline, true, false, [], false, false, 2_200_000, 1_700_000),
            Pass(new(Epoch, 1, SettledChangeStatus.NoReference, [], 0, 0, 0, 0, 1_350_000, 2_200_000, null)));
        Require(!shadow.ChainIntact && shadow.ChainLength == 0, "Initial NoReference has no comparison link yet");
        shadow = policy.Observe(new(true, false, 1_350_000, baseline, [yellow, black], true, false, [], false, false, 4_500_000, 1_700_000),
            Pass(Evidence(2) with { ReferenceSegmentStartUs = 1_350_000, ReferenceSegmentEndUs = 3_000_000, CurrentWindowStartUs = 3_300_000, CurrentWindowEndUs = 4_500_000 }));
        Require(shadow.ChainIntact && shadow.ChainLength == 1 && shadow.Proposals.Single().Kind == "hold",
            "Initial NoReference + 3-sample baseline window + moving BaselineSourceTimeUs keeps chain intact and holds missing red");
        shadow = policy.Observe(new(true, false, 1_700_000, baseline, [yellow, black], true, false, [], false, false, 6_500_000, 1_700_000),
            Pass(Evidence(3) with { ReferenceSegmentStartUs = 3_300_000, ReferenceSegmentEndUs = 5_000_000, CurrentWindowStartUs = 5_300_000, CurrentWindowEndUs = 6_500_000 }));
        Require(shadow.ChainIntact && shadow.ChainLength == 2, "Second comparison links onto the chain while BaselineSourceTimeUs moves");
        shadow = policy.Observe(new(true, false, 1_700_000, baseline, [yellow, black], true, false, [], false, false, 7_000_000, 1_700_000),
            new(SettledChangeState.Moving, .02, 0, 1, null, false));
        Require(!shadow.ChainIntact && shadow.Proposals.IsEmpty, "Active motion suspends chainIntact and refuses hold until the motion settles");

        // Transition comparison: when a shot settles, the 3-frame layout baseline advances (~0.7 s) before the
        // 1.2 s settled-change comparison arrives (~1.2 s). The transition link keeps the chain intact, allows U2
        // inference, explains pre-shot departures, and is replaced when the first post-shot comparison arrives.
        ImmutableArray<SettledBallPosition> postShot = [red, new(BallColour.Yellow, .4, .6)];
        shadow = policy.Observe(new(true, true, 10_000_000, postShot, postShot, true, false, [], false, false, 10_700_000, 10_700_000), null);
        shadow = policy.Observe(new(true, false, 10_000_000, postShot, postShot, true, false, [], false, false, 11_200_000, 10_700_000),
            Pass(Evidence(4, regions: [
                Region(.5, .3, ChangeDirection.Departure), // pre-shot yellow departure at (0.5, 0.3)
                Region(.8, .6, ChangeDirection.Arrival),   // post-shot yellow arrival at (0.8, 0.6)
                Region(1.48, .067, ChangeDirection.Arrival, darkAfter: .7) // black arrival at cushion miss spot
            ], grid: [(.5, .3), (.8, .6), (1.48, .067)]) with
            {
                ReferenceSegmentStartUs = 5_300_000, ReferenceSegmentEndUs = 8_000_000,
                CurrentWindowStartUs = 10_400_000, CurrentWindowEndUs = 11_200_000
            }));
        Require(shadow.ChainIntact && shadow.ChainLength == 1
            && shadow.Proposals.Single().Kind == "infer_black"
            && !shadow.Proposals.Any(p => p.Kind == "unexplained_change"),
            "Transition comparison after BaselineAdvanced keeps chain intact, infers black, and explains pre/post-shot balls");
        shadow = policy.Observe(new(true, false, 10_000_000, postShot, [postShot[1]], true, false, [], false, false, 13_500_000, 10_700_000),
            Pass(Evidence(5) with { ReferenceSegmentStartUs = 10_700_000, ReferenceSegmentEndUs = 12_000_000, CurrentWindowStartUs = 12_300_000, CurrentWindowEndUs = 13_500_000 }));
        Require(shadow.ChainIntact && shadow.ChainLength == 1 && shadow.Proposals.Single().Kind == "hold",
            "First post-shot comparison replaces the transition link and starts the post-shot chain");

        // U3: a ball-sized region at a missing ball's position supports a departure; no hold.
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context([yellow, black]), Pass(Evidence(1, regions: [Region(1.0, .5, ChangeDirection.Departure)], grid: [(1.0, .5)])));
        Require(shadow.Proposals.Any(p => p.Kind == "departure_support" && p.Colour == "red") && !shadow.Proposals.Any(p => p.Kind == "hold"),
            "U3: a departure region supports the ball having left");

        // U2: one dark arrival with no detection nearby infers the black, pending the recheck.
        ImmutableArray<SettledBallPosition> noBlack = [red, yellow];
        policy = new(); policy.Observe(Context(noBlack, advanced: true, baselineLayout: noBlack), null);
        shadow = policy.Observe(Context(noBlack, baselineLayout: noBlack), Pass(Evidence(1, regions: [Region(1.48, .067, ChangeDirection.Arrival, darkAfter: .7)], grid: [(1.48, .067)])));
        var infer = shadow.Proposals.SingleOrDefault(p => p.Kind == "infer_black");
        Require(infer is not null && infer.Detail!.Contains("recheck_pending") && shadow.BallStates.Any(s => s.State == "inferred" && s.Colour == "black"),
            "U2: one dark arrival infers the black (recheck pending), shown as inferred");
        // Two candidates give nothing.
        policy = new(); policy.Observe(Context(noBlack, advanced: true, baselineLayout: noBlack), null);
        shadow = policy.Observe(Context(noBlack, baselineLayout: noBlack), Pass(Evidence(1, regions: [
            Region(1.48, .067, ChangeDirection.Arrival, darkAfter: .7), Region(.5, .9, ChangeDirection.Unclear, darkAfter: .6)])));
        Require(!shadow.Proposals.Any(p => p.Kind == "infer_black"), "U2: two candidates are ambiguous");
        // Not dark enough gives nothing; a departure gives nothing.
        policy = new(); policy.Observe(Context(noBlack, advanced: true, baselineLayout: noBlack), null);
        shadow = policy.Observe(Context(noBlack, baselineLayout: noBlack), Pass(Evidence(1, regions: [Region(1.48, .067, ChangeDirection.Arrival, darkAfter: .25)])));
        Require(!shadow.Proposals.Any(p => p.Kind == "infer_black"), "U2: needs the dark appearance hint");
        // A held black elsewhere blocks inference.
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context([red, yellow]), Pass(Evidence(1, regions: [Region(.3, .9, ChangeDirection.Arrival, darkAfter: .7)], grid: [(.3, .9)])));
        Require(shadow.Proposals.Any(p => p.Kind == "hold" && p.Colour == "black") && !shadow.Proposals.Any(p => p.Kind == "infer_black"),
            "U2: a held black elsewhere blocks a second, inferred black");

        // U5: a D2 not-seen black is upgraded by inference and says so; otherwise it stays not_seen.
        policy = new(); policy.Observe(Context(noBlack, advanced: true, blackNotSeen: true, baselineLayout: noBlack), null);
        shadow = policy.Observe(Context(noBlack, blackNotSeen: true, baselineLayout: noBlack), Pass(Evidence(1, regions: [Region(1.48, .067, ChangeDirection.Unclear, darkAfter: .7)])));
        Require(shadow.Proposals.Single(p => p.Kind == "infer_black").Detail!.Contains("upgrades_not_seen"), "U5: inference upgrades a not-seen black");
        policy = new(); policy.Observe(Context(noBlack, advanced: true, blackNotSeen: true, baselineLayout: noBlack), null);
        shadow = policy.Observe(Context(noBlack, blackNotSeen: true, baselineLayout: noBlack), Pass(Evidence(1)));
        Require(shadow.BallStates.Any(s => s.Colour == "black" && s.State == "not_seen"), "U5: without evidence the black stays not seen");
        // A not-seen black that leaves (cushion-black session e7276502, step 2): a dark departure where it stood,
        // and the black now detected elsewhere, is departure support for the black, not an unexplained change.
        var movedBlack = new SettledBallPosition(BallColour.Black, .5, .3);   // table (1.0, 0.3)
        policy = new(); policy.Observe(Context(noBlack, advanced: true, blackNotSeen: true, baselineLayout: noBlack), null);
        shadow = policy.Observe(Context([red, yellow, movedBlack], blackNotSeen: true, baselineLayout: noBlack),
            Pass(Evidence(1, regions: [Region(1.92, .5, ChangeDirection.Unclear, darkAfter: .18, darkBefore: .48)])));
        Require(shadow.Proposals is [{ Kind: "departure_support", Colour: "black", Detail: "not_seen_ball_left" }],
            $"A not-seen black leaving is departure support ({string.Join(",", shadow.Proposals.Select(p => p.Kind + "/" + p.Detail))})");
        policy = new(); policy.Observe(Context(noBlack, advanced: true, blackNotSeen: true, baselineLayout: noBlack), null);
        shadow = policy.Observe(Context(noBlack, blackNotSeen: true, baselineLayout: noBlack),
            Pass(Evidence(1, regions: [Region(1.92, .5, ChangeDirection.Unclear, darkAfter: .18, darkBefore: .48)])));
        Require(shadow.Proposals is [{ Kind: "unexplained_change" }],
            "The same dark departure with no black seen anywhere stays unexplained");

        // U6 still check: the black drops out of detection while the table is still. No change at its baseline
        // spot contradicts a pot; a change there does not; a gated check says so.
        SettledChangePass StillPass(SettledChangeComparison comparison) =>
            new(SettledChangeState.Settled, 0, 9, 1, null, false) { StillCheck = new(1, 2, 3, 4, comparison) };
        LiveDetectSettledChangePolicy stillPolicy; LiveDetectSettledChangeShadow stillShadow;
        var blankCheck = Evidence(1).Comparison!;
        var changedCheck = Evidence(1, grid: [(1.48, .067)]).Comparison!;
        stillPolicy = new(); stillPolicy.Observe(Context(baseline, advanced: true), null);
        stillShadow = stillPolicy.Observe(Context([red, yellow]), StillPass(blankCheck));
        Require(stillShadow.Proposals.Any(p => p.Kind == "pot_contradicted" && p.Colour == "black" && Math.Abs(p.TableX - 1.48) < 1e-9),
            "U6: a still check with no change at the missing black's spot contradicts a pot");
        stillPolicy = new(); stillPolicy.Observe(Context(baseline, advanced: true), null);
        stillShadow = stillPolicy.Observe(Context([red, yellow]), StillPass(changedCheck));
        Require(stillShadow.Proposals.Any(p => p.Kind == "still_check_changed" && p.Colour == "black") && !stillShadow.Proposals.Any(p => p.Kind == "pot_contradicted"),
            "U6: a change at the missing black's spot does not contradict the pot");
        stillPolicy = new(); stillPolicy.Observe(Context(baseline, advanced: true), null);
        stillShadow = stillPolicy.Observe(Context([red, yellow]), StillPass(blankCheck with { GateReasons = ["lighting_drift"] }));
        Require(stillShadow.Proposals.Any(p => p.Kind == "still_check_gated") && !stillShadow.Proposals.Any(p => p.Kind == "pot_contradicted"),
            "U6: a gated still check does not contradict anything");
        stillPolicy = new(); stillPolicy.Observe(Context(baseline, advanced: true), null);
        stillShadow = stillPolicy.Observe(Context([red, yellow, black]), StillPass(blankCheck));
        Require(!stillShadow.Proposals.Any(p => p.Kind is "pot_contradicted" or "still_check_changed"),
            "U6: nothing to say when no baseline ball is missing");
        var stillJson = JsonSerializer.Serialize(LiveDetectLogSettledChange.From("shadow", StillPass(blankCheck), stillShadow, null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Require(stillJson.Contains("\"stillCheck\""), "U6: the still check is logged");

        // Device run 65ccfdec (0.8.65): a not-seen state carried NaN coordinates and System.Text.Json threw
        // ArgumentException writing the log record, which stopped Live Detect. It must serialise and round-trip.
        var notSeenLog = LiveDetectLogSettledChange.From("shadow", Pass(Evidence(1)), shadow, null);
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        string? notSeenJson = null;
        try { notSeenJson = JsonSerializer.Serialize(notSeenLog, webJson); } catch (ArgumentException) { }
        Require(notSeenJson is not null
            && JsonSerializer.Deserialize<LiveDetectLogSettledChange>(notSeenJson, webJson)?.BallStates?
                .Single(s => s.State == "not_seen") is { Colour: "black", TableX: null, TableY: null },
            "U5: a not-seen ball state is logged without coordinates (no NaN in the log record)");

        // U4: a D1 pocket region is unchanged, or changed, since the baseline.
        ImmutableArray<LiveDetectUncertainRegion> pocket = [new(2.0, 0.0, .102)];
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context(baseline, pockets: pocket), Pass(Evidence(1)));
        Require(shadow.Proposals.Any(p => p.Kind == "pocket_unchanged"), "U4: no change in the pocket region");
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context(baseline, pockets: pocket), Pass(Evidence(1, grid: [(1.97, .03)])));
        Require(shadow.Proposals.Any(p => p.Kind == "pocket_changed"), "U4: a change inside the pocket region");

        // Unexplained ball-sized change is logged; it creates nothing else.
        policy = new(); policy.Observe(Context(baseline, advanced: true), null);
        shadow = policy.Observe(Context(baseline), Pass(Evidence(1, regions: [Region(1.5, .5, ChangeDirection.Unclear)])));
        Require(shadow.Proposals.Single().Kind == "unexplained_change", "An unexplained ball-sized change is logged only");

        // Reducer: no evidence, no shadow output; Off ignores evidence.
        var tracker = new LiveDetectTableEventTracker(LiveDetectReducerOptions.Default with { SettledChange = LiveDetectSettledChangeMode.Off });
        var observations = GenerateObservations(3, 60);
        long seq = 0; var rng = new Random(3);
        Require(observations.All(o => tracker.Observe(o with { SettledChange = RandomPass(rng, o.SourceTimeUs, ref seq) }).SettledChange is null),
            "Mode Off computes nothing");
    }

    static SettledChangePass Pass(SettledChangeEvidence evidence) =>
        new(SettledChangeState.Settled, 0, 3, 1, evidence, true);

    static SettledChangeEvidence Evidence(long sequence, SettledChangeRegion[]? regions = null, (double X, double Y)[]? grid = null, string[]? gates = null)
    {
        var bits = new byte[100];
        foreach (var (x, y) in grid ?? [])
        {
            int col = Math.Clamp((int)((1 - y) * 20), 0, 19), row = Math.Clamp((int)(x * 20), 0, 39), bit = row * 20 + col;
            bits[bit / 8] |= (byte)(1 << (bit % 8));
        }
        var comparison = new SettledChangeComparison(gates ?? [], .01, 0, .05, regions?.Length ?? 0, regions ?? [], bits, 20, 40, 1);
        long start = sequence == 1 ? 500_000 : 1_500_000 + sequence * 1_000_000;
        return new(Epoch, sequence, SettledChangeStatus.Compared, gates ?? [], start, start + 1_000_000, start + 600_000,
            start + 1_000_000, start + 2_000_000, start + 2_600_000, comparison);
    }

    static SettledChangeRegion Region(double x, double y, ChangeDirection direction, double darkAfter = 0, double whiteAfter = 0, double darkBefore = 0) =>
        new(x, y, 0, 0, "cushion", 1, 1, 0, 1, true, null, direction, direction == ChangeDirection.Arrival ? .9 : .5, .1, darkBefore, darkAfter, 0, whiteAfter);

    // ---- Generated observation sequences and random evidence --------------------------------------

    static SettledChangePass? RandomPass(Random rng, long timeUs, ref long sequence)
    {
        if (rng.NextDouble() < .3) return null;
        bool isNew = rng.NextDouble() < .3;
        SettledChangeEvidence? evidence = null;
        if (isNew || rng.NextDouble() < .5)
        {
            if (isNew) sequence += rng.NextDouble() < .9 ? 1 : 2;
            int n = rng.Next(0, 4);
            var regions = Enumerable.Range(0, n).Select(_ => new SettledChangeRegion(rng.NextDouble() * 2, rng.NextDouble(), 0, 0,
                rng.NextDouble() < .5 ? "cushion" : "table", rng.NextDouble() * 3, rng.NextDouble() * 2, rng.NextDouble() * .1, rng.NextDouble(),
                rng.NextDouble() < .6, rng.NextDouble() < .2 ? "rail" : null, (ChangeDirection)rng.Next(3),
                rng.NextDouble(), rng.NextDouble(), rng.NextDouble(), rng.NextDouble(), rng.NextDouble(), rng.NextDouble())).ToArray();
            var bits = new byte[100]; rng.NextBytes(bits); for (int i = 0; i < bits.Length; i++) bits[i] &= (byte)(rng.Next(256) & rng.Next(256));
            string[] gates = rng.NextDouble() < .15 ? ["lighting_drift"] : [];
            var status = rng.NextDouble() < .15 ? SettledChangeStatus.NoReference : SettledChangeStatus.Compared;
            long refStart = timeUs - rng.Next(1, 20) * 1_000_000L;
            evidence = new(Epoch, sequence, status, gates, refStart, refStart + rng.Next(0, 10) * 1_000_000L, refStart, refStart + 500_000,
                timeUs - 1_000_000, timeUs, status == SettledChangeStatus.Compared ? new(gates, .01, 0, .1, n, regions, bits, 20, 40, 1) : null);
        }
        var passState = isNew && evidence is not null || rng.NextDouble() < .6
            ? SettledChangeState.Settled : (SettledChangeState)rng.Next(4);
        return new(passState, rng.NextDouble() * .01, rng.Next(0, 6), rng.NextDouble() * 20, evidence, isNew && evidence is not null);
    }

    /// <summary>A long, varied observation stream: stable stretches, moves, pots, returns, missed black/cue,
    /// obstruction, broad uncertainty, D1 pocket regions, decoder restarts and timeline changes.</summary>
    static List<LiveDetectTableEventObservation> GenerateObservations(int seed, int count)
    {
        var rng = new Random(seed);
        var balls = new List<SettledBallPosition>();
        void Rack()
        {
            balls.Clear();
            foreach (var (colour, n) in new[] { (BallColour.Red, 7), (BallColour.Yellow, 7), (BallColour.Black, 1), (BallColour.Cue, 1) })
                for (int i = 0; i < n; i++) balls.Add(new(colour, .05 + rng.NextDouble() * .9, .05 + rng.NextDouble() * .9));
        }
        Rack();
        var list = new List<LiveDetectTableEventObservation>();
        string timeline = "t1"; long generation = 1, time = 1_000_000; var connection = Guid.NewGuid();
        for (int i = 0; i < count; i++)
        {
            time += 350_000 + rng.Next(-100_000, 100_000);
            double r = rng.NextDouble();
            if (r < .03 && balls.Count > 2) balls.RemoveAt(rng.Next(balls.Count));                                   // pot
            else if (r < .08) { int k = rng.Next(balls.Count); balls[k] = balls[k] with { X = rng.NextDouble(), Y = rng.NextDouble() }; } // move
            else if (r < .09) Rack();
            else if (r < .095) generation++;
            else if (r < .097) timeline = "t" + rng.Next(2, 9);
            var seen = balls.Where(b => !(b.Colour is BallColour.Black or BallColour.Cue && rng.NextDouble() < .15)).ToList();  // missed black/cue
            bool obstructed = rng.NextDouble() < .05, uncertain = rng.NextDouble() < .1;
            bool pocket = !obstructed && rng.NextDouble() < .1;
            var counts = new LiveDetectCounts(seen.Count(b => b.Colour == BallColour.Red), seen.Count(b => b.Colour == BallColour.Yellow),
                seen.Count(b => b.Colour == BallColour.Black), seen.Count(b => b.Colour == BallColour.Cue), 0);
            var source = new EvidenceSource("generated", timeline, generation, "cal", connection.ToString("N"));
            list.Add(new(source, connection, generation, i, DateTimeOffset.UnixEpoch.AddMicroseconds(time), time, timeline, "cal",
                counts, [.. seen], obstructed, uncertain || pocket, uncertain || pocket, rng.NextDouble() < .1, false,
                pocket ? [new LiveDetectUncertainRegion(2, 0, .102)] : default, pocket ? uncertain : null));
        }
        return list;
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
