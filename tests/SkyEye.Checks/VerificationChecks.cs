using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class VerificationChecks
{
    static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
    static VisionResult Vision(Ball[]? balls = null, bool uncertain = false) => new(balls ?? [new("cue", "cue", new(1.8, .5), 1), new("red", "red", new(1, .5), 1), new("black", "black", new(.2, .8), 1)], uncertain, [], 1, 3, true, "fixture", [1, 1, 1], 100, false, []);
    public static async Task Run()
    {
        var good = Vision();
        var flip = good with { Balls = good.Balls.Select(b => b.Kind == "red" ? b with { Kind = "yellow" } : b).ToArray() };
        Check(VerificationConsensus.Resolve([good, flip, good])?.Single(b => b.Kind == "red") is not null, "Spatial association tolerates one colour flip without calling it motion");
        Check(VerificationConsensus.Resolve([good, good, flip]) is null, "Final conflicting label cannot override majority evidence");
        var missing = good with { Balls = good.Balls.Take(2).ToArray() };
        Check(VerificationConsensus.Resolve([good, missing, good]) is null, "A dropout needs an independent current recovery sample");
        Check(VerificationConsensus.Resolve([good, missing, good, good])?.Length == 3, "A recovered dropout has current support, no stale carry-forward");
        Check(VerificationConsensus.Resolve([good, good, missing]) is null, "Persistent missing body cannot silently remove a ball");
        Check(VerificationConsensus.Resolve([good, good with { Balls = [.. good.Balls, new("extra", "red", new(.7, .3), 1)] }, good]) is null, "Unexplained extra bodies remain unresolved");
        Check(VerificationConsensus.Resolve([good, good, good with { Uncertain = true }]) is null,
            "Stable motion evidence never makes an uncertain final layout ready");
        var noCue = good with { Balls = good.Balls.Where(b => b.Kind != "cue").ToArray(), Uncertain = true, LayoutUncertain = false };
        Check(VerificationConsensus.Resolve([noCue, noCue, noCue])?.Length == 2,
            "A verified cue-absent layout can be ready when its visible bodies and locations are clear");
        var noBlack = good with { Balls = good.Balls.Where(b => b.Kind != "black").ToArray(), Uncertain = true, LayoutUncertain = false };
        Check(VerificationConsensus.Resolve([noBlack, noBlack, noBlack])?.Length == 2,
            "A verified black-absent layout can be ready independently of game composition");
        var drift = good with { Balls = good.Balls.Select(b => b with { Position = b.Position + new Point(.02, 0) }).ToArray() };
        Check(VerificationConsensus.Resolve([good, good, drift]) is null, "Fixed anchor prevents cumulative centre drift");
        PixelChecks();
        BoundaryChecks();
        var cream = new HsvPixel(20, 110, 240); var dark = new HsvPixel(20, 210, 25);
        var patch = Enumerable.Repeat(cream, 70).Concat(Enumerable.Repeat(dark, 30)).ToArray();
        Check(BallClassifier.Classify(patch).Kind == "cue" && BallClassifier.ClassifyWithRim(patch, Enumerable.Repeat(dark, 30).ToArray()).Kind == "black",
            "Cream patch needs independently dark rim/body evidence for local black recovery");
        Check(BallClassifier.ClassifyWithRim(patch, Enumerable.Repeat(cream, 30).ToArray()).Kind == "cue", "Inventory alone cannot turn a second cue into black");
        Check(BallClassifier.ClassifyWithRim(Enumerable.Repeat(cream, 100).ToArray(), Enumerable.Repeat(dark, 30).ToArray()).Kind == "cue", "An outside shadow cannot manufacture a black body");

        var camera = new CameraConnection(Guid.NewGuid(), new string('A', 64), CameraReachability.Reachable, true, CaptureState.Active,
            CameraOwnership.Owned, "fixture", 1, Guid.NewGuid().ToString("N"), 1);
        var frames = new Frames(); var analyzer = new Analyzer();
        using var session = new TableAnalysisSession(frames, analyzer, new Calibration());
        session.Bind(camera, "open"); session.RestoreCalibration();
        var id = Guid.NewGuid(); var verification = session.VerifyAsync(id, 1, reviewFrame: true);
        Check(ReferenceEquals(verification, session.VerifyAsync(id, 1, reviewFrame: true)), "Compatible in-flight verification shares one owner");
        await verification;
        await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Outcome: ObservationOutcome.Ready, Motion: TableMotion.Still, Verification.IndependentFrames: 5 }, "Five independent source samples establish still/clear readiness");
        Check(frames.Reads == 5 && frames.Disposals == 5 && analyzer.Detections == 3 && analyzer.Disposals == 1, "Bounded anchors, exact bitmap/worker disposal");
        Check(session.State.Jpeg is { Length: 1 } && frames.Encodings == 3, "Explicit burst review retains only the latest anchor's owned image");
        var observation = session.State.Observation!;
        var choices = session.ComputeChoices(session.State.Generation, observation.TableVersion);
        Check(ReferenceEquals(choices, session.ComputeChoices(session.State.Generation, observation.TableVersion)) && analyzer.Detections == 3, "Choices computed once without new perception");

        var transientFrames = new Frames(); var transientAnalyzer = new Analyzer { DetectedBalls = PackedRackFixture.Balls };
        transientAnalyzer.InsufficientEvidenceAtCalls.UnionWith([2, 3]);
        using var transientSession = new TableAnalysisSession(transientFrames, transientAnalyzer, new Calibration());
        transientSession.Bind(camera, "open"); transientSession.RestoreCalibration();
        await transientSession.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !transientSession.HasPendingWork);
        Check(transientSession.State.Observation is { Outcome: ObservationOutcome.Ready, Motion: TableMotion.Still,
                Quality: LayoutQuality.Clear, Reason: ObservationReason.None, Verification.IndependentFrames: 7,
                Verification.CandidateSpanStartSampleIndex: 2, Verification.CandidateSpanIndependentFrames: 5,
                Verification.ConsensusConflict: null }
            && transientFrames.Reads == 7 && transientAnalyzer.Detections == 4,
            "Two transient insufficient-evidence intervals restart the candidate span; only five later samples with two seconds of stillness and fresh detection consensus become ready");

        var retryFrames = new Frames(); var retryAnalyzer = new Analyzer { DetectedBalls = PackedRackFixture.Balls };
        retryAnalyzer.UncertainAtDetections.UnionWith([3, 4]);
        using var retrySession = new TableAnalysisSession(retryFrames, retryAnalyzer, new Calibration());
        retrySession.Bind(camera, "open"); retrySession.RestoreCalibration();
        await retrySession.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !retrySession.HasPendingWork);
        var retryTable = retrySession.State.Table;
        Check(retrySession.State.Observation is { Outcome: ObservationOutcome.Ready, Motion: TableMotion.Still,
                Quality: LayoutQuality.Clear, Reason: ObservationReason.None, Verification.IndependentFrames: 8,
                Verification.CandidateSpanStartSampleIndex: 0, Verification.CandidateSpanIndependentFrames: 8,
                Verification.ConsensusConflict: null }
            && retryTable is { Confirmed: true, Balls: { Length: 16 } }
            && retryFrames.Reads == 8 && retryAnalyzer.Detections == 5,
            "After uncertain samples delay consensus, an uncertain last scheduled detection gets one fresh full-frame retry before the recovered inventory is judged");

        var packedFrames = new Frames(); var packedAnalyzer = new Analyzer { DetectedBalls = PackedRackFixture.Balls };
        using var packedSession = new TableAnalysisSession(packedFrames, packedAnalyzer, new Calibration());
        packedSession.Bind(camera, "open"); packedSession.RestoreCalibration();
        await packedSession.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !packedSession.HasPendingWork);
        var packedObservation = packedSession.State.Observation!; var packedTable = packedSession.State.Table!;
        Check(packedObservation is { Outcome: ObservationOutcome.Ready, Motion: TableMotion.Still, Quality: LayoutQuality.Clear,
                Reason: ObservationReason.None, Verification.IndependentFrames: 5, Verification.CandidateSpanIndependentFrames: 5,
                Verification.ConsensusConflict: null }
            && packedTable is { Settled: true, Occluded: false, Confirmed: true } && !packedSession.PhysicallyObstructed
            && packedTable.Balls.Length == 16 && packedTable.Balls.Count(b => b.Kind == "red") == 7
            && packedTable.Balls.Count(b => b.Kind == "yellow") == 7 && packedTable.Balls.Count(b => b.Kind == "cue") == 1
            && packedTable.Balls.Count(b => b.Kind == "black") == 1,
            "The saved clear/still sixteen-ball receipt establishes ready inventory without planner spacing");
        Check(!ShotPlanner.HasSafeBallSpacing(packedTable) && !ShotPlanner.Eligible(packedTable, DateTimeOffset.UtcNow)
            && ShotPlanner.Suggest(packedTable, DateTimeOffset.UtcNow).Length == 0,
            "The planner rejects advice for the packed receipt and returns no candidates");
        var noBlackTable = new TableState(1, DateTimeOffset.UtcNow,
            [new("cue", "cue", new(1.8, .5), 1), new("red", "red", new(1, .5), 1)],
            "red", true, false, true, "fixture-no-black");
        Check(!ShotPlanner.Eligible(noBlackTable, DateTimeOffset.UtcNow),
            "Advice remains unavailable when the verified layout has no black ball");

        var noCueFrames = new Frames();
        var noCueAnalyzer = new Analyzer
        {
            DetectedBalls = good.Balls.Where(b => b.Kind != "cue").ToArray(),
            LayoutUncertain = true,
            ClearLayoutWithIncompleteInventory = true
        };
        using var noCueSession = new TableAnalysisSession(noCueFrames, noCueAnalyzer, new Calibration());
        noCueSession.Bind(camera, "open"); noCueSession.RestoreCalibration();
        await noCueSession.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !noCueSession.HasPendingWork);
        Check(noCueSession.State.Observation is { Outcome: ObservationOutcome.Ready, Quality: LayoutQuality.Clear, Motion: TableMotion.Still }
            && noCueSession.State.Vision is { Uncertain: true, HasUncertainLayout: false }
            && noCueSession.State.Table is { Confirmed: true, Occluded: false }
            && !ShotPlanner.Eligible(noCueSession.State.Table, DateTimeOffset.UtcNow),
            "Stable cue-absent observations can be ready while their incomplete inventory remains blocked from shot advice");
        var packedEffect = new TableObservationEffect(() => packedSession, () => 1, () => true);
        var packedRequest = new ObserveTable(camera.Scope!, packedSession.State.Generation, 1, IncludeChoices: true,
            Mode: ObservationMode.VerifyForAdvice);
        var effectResult = await packedEffect.ExecuteAsync(packedRequest, Guid.NewGuid(), CancellationToken.None);
        Check(effectResult.Outcome == ObservationOutcome.Ready && effectResult.Choices is null
            && !packedSession.State.ChoicesComputed && packedSession.State.Table is { Confirmed: true, Occluded: false },
            "IncludeChoices does not run planning inside the observation effect or delay its ready result");
        packedObservation = effectResult;
        packedTable = packedSession.State.Table!;
        var packedChoices = packedSession.ComputeChoices(packedSession.State.Generation, packedObservation.TableVersion);
        Check(packedChoices.Length == 0 && packedSession.State.Observation?.Outcome == ObservationOutcome.Ready
            && packedSession.State.Table is { Settled: true, Occluded: false, Confirmed: true, Version: var packedVersion }
            && packedVersion == packedTable.Version,
            "An unavailable planner result cannot demote or revise the verified table state");
        bool correctionRejected = false;
        try
        {
            var correction = string.Join('\n', PackedRackFixture.Balls.Select(b => $"{b.Kind} {b.Position.X:R} {b.Position.Y:R}"));
            packedSession.Correct(packedTable.Version, correction);
        }
        catch (ArgumentException) { correctionRejected = true; }
        Check(correctionRejected && packedSession.State.Observation?.Outcome == ObservationOutcome.Ready
            && packedSession.State.Table?.Version == packedTable.Version,
            "A rejected overlapping manual correction leaves the independently verified inventory ready");

        packedSession.Invalidate(); packedFrames.Reads = 0; packedAnalyzer.MeasurementCalls = 0; packedAnalyzer.Detections = 0;
        packedAnalyzer.MovingAtCall = 3;
        await packedSession.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !packedSession.HasPendingWork);
        Check(packedSession.State.Observation is { Outcome: ObservationOutcome.Unsettled, Motion: TableMotion.Moving, Reason: ObservationReason.Moving }
            && packedSession.State.Table is { Settled: false, Confirmed: false }
            && ShotPlanner.Suggest(packedSession.State.Table, DateTimeOffset.UtcNow).Length == 0,
            "Positive movement still blocks readiness and advice for the same full inventory");
        packedAnalyzer.MovingAtCall = 0; packedAnalyzer.DropLastDetectedBall = true;
        packedSession.Invalidate(); packedFrames.Reads = 0; packedAnalyzer.MeasurementCalls = 0; packedAnalyzer.Detections = 0;
        await packedSession.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !packedSession.HasPendingWork);
        Check(packedSession.State.Observation is { Outcome: ObservationOutcome.Unclear, Quality: LayoutQuality.Ambiguous,
                Reason: ObservationReason.DetectionAmbiguous, Verification.ConsensusConflict: not null }
            && packedSession.State.Table is { Confirmed: false },
            $"A persistent incomplete final detection remains unclear and cannot establish confirmed inventory ({packedSession.State.Observation?.Outcome}/{packedSession.State.Observation?.Quality}/{packedSession.State.Observation?.Reason}; conflict={packedSession.State.Observation?.Verification?.ConsensusConflict}; balls={packedSession.State.Table?.Balls.Length}; confirmed={packedSession.State.Table?.Confirmed}; calls={packedAnalyzer.MeasurementCalls}/{packedAnalyzer.Detections})");

        session.Invalidate(); frames.Reads = 0; analyzer.CreatedWorkers = 0; analyzer.ObstructionIssued = false;
        analyzer.ObstructFirstDetectedFrame = true;
        await session.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Outcome: ObservationOutcome.Ready, Motion: TableMotion.Still, Reason: ObservationReason.None,
            Verification.IndependentFrames: 7, Verification.CandidateSpanStartSampleIndex: 1, Verification.CandidateSpanIndependentFrames: 6 },
            "A transient obstructed bootstrap restarts the stillness anchor and recovers only after a fresh stable span with detector consensus");
        Check(frames.Reads == 7 && analyzer.CreatedWorkers == 1 && analyzer.ObstructionResets == 1,
            "Obstruction recovery stays inside the bounded seven-frame budget and resets the unsafe anchor");
        analyzer.ObstructFirstDetectedFrame = false;
        session.Invalidate(); frames.Reads = 0; analyzer.CreatedWorkers = 0; analyzer.MeasurementCalls = 0; analyzer.ObstructionResets = 0; analyzer.ObstructionIssued = false;
        analyzer.ObstructAtCall = 3;
        await session.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Outcome: ObservationOutcome.Unsettled, Motion: TableMotion.Unknown,
            Reason: ObservationReason.InsufficientEvidence, Verification.IndependentFrames: 7,
            Verification.CandidateSpanStartSampleIndex: 3, Verification.CandidateSpanIndependentFrames: 4 },
            "An obstruction after an initial clear span prevents combining old samples with a too-short recovery span");
        Check(frames.Reads == 7 && analyzer.ObstructionResets == 1, "Late obstruction recovery remains within the bounded burst");
        analyzer.ObstructAtCall = 0;
        session.Invalidate(); frames.Reads = 0; analyzer.CreatedWorkers = 0; analyzer.ObstructionIssued = false;
        analyzer.ObstructEveryDetectedFrame = true;
        await session.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Outcome: ObservationOutcome.Unclear, Motion: TableMotion.Unknown, Quality: LayoutQuality.Obstructed,
            Reason: ObservationReason.Obstructed, Verification.IndependentFrames: 7, Verification.CandidateSpanIndependentFrames: 0 }
            && session.State.Table is { Occluded: true, Confirmed: false },
            "Persistent obstruction consumes the bounded burst but never becomes still, clear or ready");
        Check(frames.Reads == 7, "Persistent obstruction retries only within the seven-frame bound");
        analyzer.ObstructEveryDetectedFrame = false;
        session.Invalidate(); analyzer.LayoutUncertain = true;
        await session.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Outcome: ObservationOutcome.Unclear, Motion: TableMotion.Still,
            Quality: LayoutQuality.Ambiguous, Reason: ObservationReason.DetectionAmbiguous,
            Verification.IndependentFrames: 8, Verification.CandidateSpanIndependentFrames: 8 },
            "A settled but ambiguous layout reports stillness while remaining ineligible for advice");
        var ambiguousObservation = session.State.Observation!;
        var stillDetectionMessage = AppCoordinator.VerificationMessage(ambiguousObservation);
        Check(ambiguousObservation.Outcome == ObservationOutcome.Unclear
            && stillDetectionMessage.Contains("Motion stayed still in the valid samples", StringComparison.Ordinal)
            && stillDetectionMessage.Contains("No shot choices were returned", StringComparison.Ordinal)
            && !stillDetectionMessage.Contains("Stillness could not be verified", StringComparison.Ordinal),
            "Valid still-motion samples are reported separately from unresolved detection without promoting readiness");
        var historicalAmbiguous = HistoricalVerificationReport.From(ambiguousObservation, DateTimeOffset.UtcNow,
            publishedAsCurrent: true, CommandOutcome.Applied);
        Check(AppCoordinator.VerificationMessage(historicalAmbiguous).Contains("Motion stayed still in the valid samples", StringComparison.Ordinal),
            "Historical Home feedback retains the same-run still-motion evidence");
        var radiusObservation = ambiguousObservation with
        {
            Verification = ambiguousObservation.Verification! with
            { ConsensusConflict = new("radius-inconsistent", 3, "red-0") }
        };
        Check(AppCoordinator.VerificationMessage(radiusObservation).Contains("boundary radius evidence disagreed", StringComparison.Ordinal),
            "A same-run radius conflict is described from its recorded consensus reason");
        Check(AppCoordinator.VerificationMessage(historicalAmbiguous with
                { ConsensusConflict = new("radius-inconsistent", 3, "red-0") })
                .Contains("boundary radius evidence disagreed", StringComparison.Ordinal),
            "Historical Home feedback names a recorded radius conflict");
        var unknownMotionMessage = AppCoordinator.VerificationMessage(ambiguousObservation with { Motion = TableMotion.Unknown });
        var staleMotionMessage = AppCoordinator.VerificationMessage(ambiguousObservation with { FrameAgeMs = ObservationPolicy.MaximumFinalAgeMs + 1 });
        var noEvidenceMessage = AppCoordinator.VerificationMessage(ambiguousObservation with { Verification = null });
        var obstructedMessage = AppCoordinator.VerificationMessage(ambiguousObservation with
            { Motion = TableMotion.Unknown, Quality = LayoutQuality.Obstructed, Reason = ObservationReason.Obstructed });
        var invalidCoverage = ambiguousObservation.Verification! with
        {
            Samples = ambiguousObservation.Verification.Samples.Select((sample, index) => index == 1
                ? sample with { Measurement = sample.Measurement with { CoverageValid = false } }
                : sample).ToArray()
        };
        var invalidCoverageMessage = AppCoordinator.VerificationMessage(ambiguousObservation with { Verification = invalidCoverage });
        var staleClock = ambiguousObservation.Verification! with
        { FinalClockSampleAgeMs = ObservationPolicy.MaximumClockSampleAgeMs + 1 };
        var staleClockMessage = AppCoordinator.VerificationMessage(ambiguousObservation with { Verification = staleClock });
        Check(!unknownMotionMessage.Contains("still", StringComparison.OrdinalIgnoreCase)
            && !staleMotionMessage.Contains("still", StringComparison.OrdinalIgnoreCase)
            && !noEvidenceMessage.Contains("still", StringComparison.OrdinalIgnoreCase)
            && !obstructedMessage.Contains("still", StringComparison.OrdinalIgnoreCase)
            && !invalidCoverageMessage.Contains("still", StringComparison.OrdinalIgnoreCase)
            && !staleClockMessage.Contains("still", StringComparison.OrdinalIgnoreCase)
            && AppCoordinator.VerificationMessage(ObservationReason.DetectionAmbiguous) == "Table detection is unclear. Review the balls or retry.",
            "Unknown, obstructed, stale, missing or uncovered motion evidence never receives a stillness claim");
        analyzer.LayoutUncertain = false;
        session.Invalidate(); analyzer.Failure = new InvalidDataException("fixture");
        await session.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Reason: ObservationReason.AnalysisFailed, Verification.Failure.Stage: "analysis" },
            "Worker failures retain their stage instead of blaming live playback"); analyzer.Failure = null;
        session.Invalidate(); session.VerificationCapture = _ => throw new IOException("fixture");
        await session.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Reason: ObservationReason.CaptureFailed, Verification.Failure.Stage: "capture" },
            "Diagnostic capture failure is distinct from acquisition and analysis"); session.VerificationCapture = null;
        session.Invalidate(); frames.Failure = new FrameAcquisitionException(ObservationReason.ClockUntrusted, "unmapped-source-clock");
        await session.VerifyAsync(Guid.NewGuid(), 1); await Until(() => !session.HasPendingWork);
        Check(session.State.Observation is { Reason: ObservationReason.ClockUntrusted, Verification.Failure.Code: "unmapped-source-clock" },
            "Typed native acquisition reason survives publication"); frames.Failure = null;
        session.Invalidate(); frames.Duplicate = true;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.SourceChanged && session.State.Observation.Outcome != ObservationOutcome.Ready, "Duplicate receipts cannot build a span"); frames.Duplicate = false;
        session.Invalidate(); frames.ClockAge = 3000;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.ClockUntrusted, "Stale mapping fails even for freshly copied pixels"); frames.ClockAge = 1;
        Check(session.State.Observation?.Verification?.RejectedSample?.ClockSampleAgeMs == 3000, "Refused frame metadata survives as rejected evidence, never as a valid sample");
        session.Invalidate(); frames.Gap = true;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.SampleGap, "Legacy fifteen-second gap never substitutes for continuous evidence"); frames.Gap = false;
        session.Invalidate(); frames.AgeMs = 1600;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.FrameTooOld, "Age is checked before costly detection"); frames.AgeMs = 0;

        session.Invalidate(); frames.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); frames.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource(); var cancelled = session.VerifyAsync(Guid.NewGuid(), 1, cancel.Token);
        await frames.Entered.Task; cancel.Cancel();
        await Cancelled(cancelled); int disposed = frames.Disposals; frames.Hold.SetResult();
        await Until(() => frames.Disposals == disposed + 1 && !session.HasPendingWork);
        Check(session.State.Observation is null, "Uncooperative late acquisition cannot publish and its pixels are disposed"); frames.Hold = null;

        session.Invalidate(); analyzer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); analyzer.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var changing = session.VerifyAsync(Guid.NewGuid(), 1); await analyzer.Entered.Task;
        session.Bind(camera, "yellow"); await Cancelled(changing);
        Check(session.State.Observation is null, "Group changes cancel verification and reject late results");
        int workers = analyzer.Disposals; analyzer.Hold.SetResult(); await Until(() => analyzer.Disposals == workers + 1 && !session.HasPendingWork); analyzer.Hold = null;
        session.Invalidate(); frames.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); frames.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = session.VerifyAsync(Guid.NewGuid(), 1); await frames.Entered.Task; await timeout;
        Check(session.State.Observation is { Reason: ObservationReason.TimedOut, Outcome: ObservationOutcome.Unsettled }, "Uncooperative source reaches the hard deadline with an honest result");
        int readsAtTimeout = frames.Reads;
        try { _ = session.VerifyAsync(Guid.NewGuid(), 1); throw new Exception("Expected retiring source refusal"); } catch (InvalidOperationException) { }
        Check(frames.Reads == readsAtTimeout, "A timed-out uncooperative read cannot build a hidden acquisition queue");
        int lateDisposals = frames.Disposals; frames.Hold.SetResult(); await Until(() => frames.Disposals == lateDisposals + 1 && !session.HasPendingWork); frames.Hold = null;
        session.Invalidate(); frames.AgeMs = 1450; analyzer.DelayMs = 100;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.FrameTooOld && session.State.Observation.Outcome != ObservationOutcome.Ready
            && session.State.Table is { Confirmed: false }, "Freshness fails when processing pushes the final sample over the publication budget"); frames.AgeMs = 0; analyzer.DelayMs = 0;
        session.Invalidate(); frames.FinalCurrent = false;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.SourceChanged && session.State.Choices.Length == 0, "Decoder/timeline change after final CV blocks publication");
        Console.WriteLine("PASS verification: independent pixels/motion, conservative consensus/dropout/current support, source span/gap/clock/age, bounded anchors, coalescing, cancellation/late disposal and group fences");
    }
    static void PixelChecks()
    {
        const int w = 128, h = 256;
        var gray = Enumerable.Range(0, w * h).Select(i => (byte)(80 + i % w % 4 * 12)).ToArray();
        var motion = new VerificationPixels(w, h);
        Check(motion.Measure(gray, true, null).Motion == TableMotion.Unknown, "A seed cannot establish motion");
        Check(motion.Measure(gray, true, null).Motion == TableMotion.Still, "Supported identical textured views prove local stillness");
        var moved = gray.ToArray(); for (int y = 40; y < 48; y++) for (int x = 40; x < 48; x++) moved[y * w + x] = 200;
        Check(motion.Measure(moved, true, null).Reason == ObservationReason.Moving, "Unrecognized local motion is detected without a settled layout");
        var subtle = gray.ToArray(); for (int y = 40; y < 44; y++) subtle[y * w + 40] = 200;
        var uncertainMotion = new VerificationPixels(w, h); uncertainMotion.Measure(gray, true, null);
        Check(uncertainMotion.Measure(subtle, true, null).Motion == TableMotion.Unknown, "Small unsupported changes cannot imply stillness");
        var supportedPixelNoise = new VerificationMeasurement(TableMotion.Unknown, ObservationReason.InsufficientEvidence,
            true, .19, .006, 24, Vision()) { SensitiveTileChange = .23, PhotometricResidualTileChange = .017 };
        Check(VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise, allTrackedBodiesSupported: true),
            "Complete stable ball-template support absorbs weak local pixel noise across a broad brightness change");
        Check(!VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise, allTrackedBodiesSupported: false),
            "Weak pixel changes cannot establish stillness when any detected body lacks template support");
        Check(VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise with
            { Vision = Vision(uncertain: true), MaximumDetectedBodyDisplacement = 10.4 }, allTrackedBodiesSupported: true),
            "Complete unchanged body templates establish stillness despite ambiguous detector geometry");
        Check(!VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise with
            { Vision = Vision(uncertain: true) }, allTrackedBodiesSupported: false),
            "A missing tracked-body template remains unknown even when the detector is uncertain");
        Check(!VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise with { PhotometricResidualTileChange = .031 }, true),
            "Stronger photometric residual remains unresolved above the supported-noise limit");
        Check(!VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise with { SensitiveTileChange = .301 }, true),
            "Stronger weak-edge change remains unresolved above the supported-noise limit");
        Check(!VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise with { PerimeterChange = .021 }, true),
            "Perimeter or pose change remains unresolved above the supported-noise limit");
        Check(!VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise with { Vision = Vision() with { Obstructed = true } }, true),
            "Pixel-noise tolerance never accepts an obstructed frame");
        Check(!VerificationStillnessPolicy.CanResolveAsStill(supportedPixelNoise with { Reason = ObservationReason.Moving, Motion = TableMotion.Moving }, true),
            "Pixel-noise tolerance never overrides positive motion evidence");
        Check(new VerificationPixels(w, h).Measure(new byte[w * h], true, null).Motion == TableMotion.Unknown, "Textureless coverage never implies stillness");
        Check(new VerificationPixels(w, h).Measure(gray, true, Vision() with { Obstructed = true }).Reason == ObservationReason.Obstructed,
            "A physically obstructed bootstrap frame is refused immediately without waiting for a settled layout");
        var exposure = new VerificationPixels(w, h); exposure.Measure(gray, true, null);
        var brighter = gray.Select(b => (byte)Math.Clamp((int)Math.Round(b * 1.35 + 12), 0, 255)).ToArray();
        var exposureMeasurement = exposure.Measure(brighter, true, null);
        Check(exposureMeasurement.Motion == TableMotion.Still && exposureMeasurement.IlluminationChange > 8,
            "A global exposure shift is normalized without inventing table motion");
        var exposureMotion = new VerificationPixels(w, h); exposureMotion.Measure(gray, true, null);
        var movingUnderExposure = brighter.ToArray();
        for (int y = 40; y < 48; y++) for (int x = 40; x < 48; x++) movingUnderExposure[y * w + x] = 220;
        Check(exposureMotion.Measure(movingUnderExposure, true, null).Reason == ObservationReason.Moving,
            "Photometric normalization retains positive evidence of local movement");
        var pose = new VerificationPixels(w, h); pose.Measure(gray, true, null);
        var shifted = Enumerable.Range(0, w * h).Select(i => gray[(i / w) * w + (i % w + 2) % w]).ToArray();
        var shiftedMeasurement = pose.Measure(shifted, true, null);
        Check(shiftedMeasurement.Reason == ObservationReason.CalibrationChanged,
            $"Global camera shift invalidates calibration (reason={shiftedMeasurement.Reason}, rail={shiftedMeasurement.PerimeterChange:F3}, residual={shiftedMeasurement.PhotometricResidualTileChange:F3})");
        byte[] Body(int cx, byte foreground, bool rgb = false)
        {
            var result = Enumerable.Repeat((byte)100, w * h * (rgb ? 3 : 1)).ToArray();
            for (int y = 3; y < h - 3; y++) for (int x = 3; x < 12; x++)
                for (int c = 0; c < (rgb ? 3 : 1); c++) result[(y * w + x) * (rgb ? 3 : 1) + c] = (byte)(x % 2 == 0 ? 80 : 120);
            for (int y = 95; y <= 105; y++) for (int x = cx - 5; x <= cx + 5; x++)
                if ((x - cx) * (x - cx) + (y - 100) * (y - 100) <= 25)
                    for (int c = 0; c < (rgb ? 3 : 1); c++) result[(y * w + x) * (rgb ? 3 : 1) + c] = foreground;
            return result;
        }
        var lowContrast = new VerificationPixels(w, h); lowContrast.Measure(Body(60, 110), true, null);
        for (int x = 61; x <= 65; x++) Check(lowContrast.Measure(Body(x, 110), true, null).Motion != TableMotion.Still,
            "Low-contrast cumulative movement cannot pass as still");
        var before = Body(60, 110, true); var after = Body(61, 110, true);
        var translation = VerificationTemplate.Compare(before, after, w, h, 60, 100, 12);
        Check(translation is { Dx: > .8 and < 1.2, Dy: > -.2 and < .2 }, "Colour template resolves smooth low-contrast body displacement");
        Check(VerificationTemplate.Compare(before, before, w, h, 60, 100, 12) is { Dx: 0, Dy: 0 }, "A supported unchanged body has zero displacement");
        byte[] Exposure(byte[] input)
        {
            var output = input.ToArray(); double[] gain = [.76, 1.08, 1.35]; double[] offset = [12, 6, 0];
            for (int i = 0; i < output.Length; i++) output[i] = (byte)Math.Clamp((int)Math.Round(input[i] * gain[i % 3] + offset[i % 3]), 0, 255);
            return output;
        }
        Check(VerificationTemplate.Compare(before, Exposure(before), w, h, 60, 100, 12) is { Dx: 0, Dy: 0 },
            "A stationary colour template remains aligned across exposure and white-balance shifts");
        var translatedExposure = VerificationTemplate.Compare(before, Exposure(after), w, h, 60, 100, 12);
        Check(translatedExposure is { Dx: > .8 and < 1.2, Dy: > -.2 and < .2 },
            "Photometric normalization preserves ball displacement evidence");
        var blank = Enumerable.Repeat((byte)100, w * h * 3).ToArray();
        Check(VerificationTemplate.Compare(blank, blank, w, h, 60, 100, 12) is null, "Textureless body template is unknown");
        var chromatic = new VerificationPixels(w, h); var sameGray = Body(60, 100);
        chromatic.Measure(sameGray, true, null, before);
        Check(chromatic.Measure(sameGray, true, null, after).Motion != TableMotion.Still, "Colour-only change cannot disappear in grayscale conversion");
    }
    static void BoundaryChecks()
    {
        var hsvMask = BoundaryColourMask.FromHsvPixels([
            0, 100, 30, 10, 100, 30, 11, 100, 30, 40, 255, 255,
            41, 255, 255, 170, 100, 30, 169, 99, 255, 10, 99, 255,
            179, 100, 29
        ]);
        Check(hsvMask.Matches("red", 0) && !hsvMask.Matches("yellow", 0)
            && hsvMask.Matches("red", 3) && hsvMask.Matches("yellow", 3)
            && !hsvMask.Matches("red", 6) && hsvMask.Matches("yellow", 6)
            && hsvMask.Matches("yellow", 9) && !hsvMask.Matches("red", 9)
            && !hsvMask.Matches("yellow", 12) && hsvMask.Matches("red", 15)
            && !hsvMask.Matches("red", 18) && !hsvMask.Matches("red", 21)
            && !hsvMask.Matches("red", 24),
            "Cached HSV boundary membership preserves inclusive production thresholds and red wraparound");
        Check(!hsvMask.Matches("blue", 0) && !hsvMask.Matches("red", -3)
            && !hsvMask.Matches("red", 1) && !hsvMask.Matches("red", 1000),
            "Cached HSV boundary membership rejects invalid classes and offsets");
        const int width = 160, height = 160;
        var image = new byte[width * height * 3];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            bool body = (x - 80) * (x - 80) + (y - 80) * (y - 80) <= 27 * 27;
            bool cream = (x - 74) * (x - 74) + (y - 74) * (y - 74) <= 13 * 13;
            for (int c = 0; c < 3; c++) image[(y * width + x) * 3 + c] = cream ? (byte)230 : body ? (byte)20 : (byte)110;
        }
        var fit = BallBoundaryRefiner.Refine(image, width, height, new(84, 77), 27);
        Check(fit is not null && (fit.Centre - new Point(80, 80)).Length < 2 && Math.Abs(fit.Radius - 27) < 2,
            "Current outer boundary recovers a displaced proposal without fitting the cream patch");
        var auditedFit = BallBoundaryRefiner.Refine(image, width, height, new(84, 77), 27,
            collectSectorEvidence: true);
        Check(auditedFit is not null && auditedFit.SectorEvidence.Length == 32
            && fit is not null && auditedFit.Centre == fit.Centre && auditedFit.Radius == fit.Radius
            && auditedFit.Score == fit.Score && auditedFit.SupportedSectors == fit.SupportedSectors
            && auditedFit.ProposalFit is { Centre.X: 84, Centre.Y: 77, Radius: 27 }
            && auditedFit.SectorEvidence.Select(s => s.Supported).Count(s => s) == (int)(auditedFit.SupportedSectors * 32),
            "Replay-only sector diagnostics preserve the fit and expose all 32 support decisions");
        var shadowedRed = new byte[width * height * 3];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            double distance = Math.Sqrt((x - 80) * (x - 80) + (y - 80) * (y - 80));
            byte blue = distance <= 22 ? (byte)10 : distance <= 27 ? (byte)35 : (byte)110;
            byte green = distance <= 22 ? (byte)10 : distance <= 27 ? (byte)40 : (byte)110;
            byte red = distance <= 22 ? (byte)180 : distance <= 27 ? (byte)50 : (byte)110;
            int at = (y * width + x) * 3;
            shadowedRed[at] = blue; shadowedRed[at + 1] = green; shadowedRed[at + 2] = red;
        }
        var shadowFit = BallBoundaryRefiner.Refine(shadowedRed, width, height, new(80, 80), 22,
            expectedKind: "red");
        Check(shadowFit is { SupportedSectors: >= ObservationPolicy.MinimumSupportedBoundaryFraction }
            && Math.Abs(shadowFit.Radius - 22) < 1.5,
            "Recognized red boundary stays on the paint edge when a dark shadow separates it from cloth");
        var handCoveredRed = (byte[])shadowedRed.Clone();
        for (int y = 45; y <= 80; y++) for (int x = 45; x < 115; x++)
        {
            int at = (y * width + x) * 3;
            handCoveredRed[at] = 70; handCoveredRed[at + 1] = 120; handCoveredRed[at + 2] = 180;
        }
        var handFit = BallBoundaryRefiner.Refine(handCoveredRed, width, height, new(80, 80), 22,
            expectedKind: "red");
        Check(handFit is null || handFit.SupportedSectors < ObservationPolicy.MinimumSupportedBoundaryFraction,
            "Recognized-colour paint-edge scoring still rejects a half-covered red body");
        var maskedNeighbour = new byte[width * height * 3];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int dx = x - 80, dy = y - 80; double radius = Math.Sqrt(dx * dx + dy * dy);
            bool body = radius <= 27; double angle = Math.Atan2(dy, dx);
            int sector = ((int)Math.Floor((angle + Math.PI) * 16 / Math.PI)) & 31;
            byte value = body ? (byte)20 : (byte)110;
            if (sector < 13 && radius is >= 22 and <= 38) value = 230;
            int at = (y * width + x) * 3; maskedNeighbour[at] = maskedNeighbour[at + 1] = maskedNeighbour[at + 2] = value;
        }
        var maskedFit = BallBoundaryRefiner.Refine(maskedNeighbour, width, height, new(80, 80), 27, collectSectorEvidence: true);
        Check(maskedFit?.ProposalFit is { SupportedSectors: >= ObservationPolicy.MinimumSupportedBoundaryFraction }
            && maskedFit.SupportedSectors >= ObservationPolicy.MinimumSupportedBoundaryFraction,
            "A supported exact seed remains eligible when a neighboring edge masks part of the boundary");
        var partialEvidence = Enumerable.Range(0, 32).Select(index =>
        {
            double angle = index * Math.PI / 16;
            var inner = new Point(8 * Math.Cos(angle), 8 * Math.Sin(angle));
            var outer = new Point(12 * Math.Cos(angle), 12 * Math.Sin(angle));
            bool supported = index < 22;
            return new BoundarySectorEvidence(index, inner, outer, inner, outer,
                supported ? 20 : 3, supported ? 20 : 60, supported ? 18 : 2,
                ExteriorRejected: !supported, Supported: supported);
        }).ToArray();
        var adjacentBallCentres = Enumerable.Range(22, 10).Select(index =>
        {
            double angle = index * Math.PI / 16;
            return new Point(20 * Math.Cos(angle), 20 * Math.Sin(angle));
        }).ToArray();
        Check(BallBoundaryRefiner.HasConservativeOcclusionCoverage(partialEvidence, new(0, 0), 10, adjacentBallCentres),
            "One-sector-short visible support passes only when every rejected arc is covered by a measured neighbor");
        var narrowlyOccludedEvidence = partialEvidence.Select(sector => sector.Index == 0
            ? sector with { Supported = false, ExteriorRejected = true, OuterPoint = new(12, 0) }
            : sector with { Supported = true, ExteriorRejected = false }).ToArray();
        var narrowlyOccludingNeighbour = new Point(20 * Math.Cos(35.25 * Math.PI / 180), 20 * Math.Sin(35.25 * Math.PI / 180));
        Check(BallBoundaryRefiner.HasConservativeOcclusionCoverage(narrowlyOccludedEvidence, new(0, 0), 10,
                [narrowlyOccludingNeighbour]),
            "A measured neighboring-ball edge within the fitted-boundary tolerance explains a narrowly missed sector");
        var outsideNeighbor = new Point(20 * Math.Cos(36.8 * Math.PI / 180), 20 * Math.Sin(36.8 * Math.PI / 180));
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(narrowlyOccludedEvidence, new(0, 0), 10,
                [outsideNeighbor]),
            "A neighboring centre outside the fitted-boundary tolerance does not explain an unsupported sector");
        var packedBlackEvidence = partialEvidence.Select(sector => sector.Index < 14
            ? sector with { Supported = true, ExteriorRejected = false }
            : sector with { Supported = false, ExteriorRejected = true }).ToArray();
        var packedBlackNeighbours = Enumerable.Range(14, 18).Select(index =>
        {
            double angle = index * Math.PI / 16;
            return new Point(20 * Math.Cos(angle), 20 * Math.Sin(angle));
        }).ToArray();
        Check(BallBoundaryRefiner.HasConservativeOcclusionCoverage(packedBlackEvidence, new(0, 0), 10,
                packedBlackNeighbours, 14d / 32),
            "Packed black body accepts 14/32 visible support only when every remaining exterior arc is covered by a fitted neighbor");
        var belowPackedBlackVisibility = packedBlackEvidence.Select(sector => sector.Index == 13
            ? sector with { Supported = false, ExteriorRejected = true } : sector).ToArray();
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(belowPackedBlackVisibility, new(0, 0), 10,
                packedBlackNeighbours, 14d / 32),
            "Packed black body below 14/32 direct visibility remains unknown");
        var uncoveredPackedArc = packedBlackEvidence.Select(sector => sector.Index == 31
            ? sector with { ExteriorRejected = false } : sector).ToArray();
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(uncoveredPackedArc, new(0, 0), 10,
                packedBlackNeighbours, 14d / 32),
            "Packed black body remains unknown when even one of 18 unsupported arcs lacks measured neighbor coverage");
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(partialEvidence, new(0, 0), 10, []),
            "A dark candidate without measured neighboring balls cannot use the occlusion exception");
        var uncoveredArc = partialEvidence.Select(sector => sector.Index == 31
            ? sector with { OuterPoint = new(1000, 1000) } : sector).ToArray();
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(uncoveredArc, new(0, 0), 10, adjacentBallCentres),
            "One unsupported neighboring arc prevents packed-ball acceptance");
        var incompleteOcclusion = partialEvidence.Select(sector => sector.Index == 31
            ? sector with { ExteriorRejected = false } : sector).ToArray();
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(incompleteOcclusion, new(0, 0), 10, adjacentBallCentres),
            "A weak edge sector is not excused as a neighboring-ball occlusion");
        var fewerVisible = partialEvidence.Select(sector => sector.Index == 21
            ? sector with { Supported = false, ExteriorRejected = true } : sector).ToArray();
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(fewerVisible, new(0, 0), 10, adjacentBallCentres),
            "More than one sector below the visible boundary gate remains unknown");
        var obstructedEvidence = partialEvidence.Select(sector => sector.Index < 18
            ? sector with { Supported = true, ExteriorRejected = false }
            : sector with { Supported = false, ExteriorRejected = true }).ToArray();
        Check(!BallBoundaryRefiner.HasConservativeOcclusionCoverage(obstructedEvidence, new(0, 0), 10, []),
            "A true obstruction with fewer than 70% visible sectors stays rejected");
        var strictFit = BallBoundaryRefiner.Refine(image, width, height, new(84, 77), 27, .75);
        Check(strictFit is not null && strictFit.SupportedSectors >= .75 && (strictFit.Centre - new Point(80, 80)).Length < 2,
            "Stronger proposal support still fits the full body rather than the cream patch");
        Check(BallBoundaryRefiner.Refine(new byte[width * height * 3], width, height, new(80, 80), 27) is null,
            "Radius prior cannot invent a ball on a blank image");
        var edgeImage = new byte[200 * 200 * 3];
        Check(BallBoundaryRefiner.Refine(edgeImage, 200, 200, new(170, 100), 20) is null,
            "Refinement safely rejects boundary candidates whose rounded pixel samples fall outside the image");
        Check(BallBoundaryRefiner.Inspect(edgeImage, 200, 200, new(170, 100), 20,
                new(177.56, 100), 20).Length == 0,
            "Boundary inspection safely rejects rounded pixel samples outside the image");
        foreach (int radius in new[] { 20, 33 })
        {
            var differentSize = new byte[width * height * 3];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                bool body = (x - 80) * (x - 80) + (y - 80) * (y - 80) <= radius * radius;
                byte value = body ? (byte)20 : (byte)110;
                int at = (y * width + x) * 3;
                differentSize[at] = differentSize[at + 1] = differentSize[at + 2] = value;
            }
            var sizeFit = BallBoundaryRefiner.Refine(differentSize, width, height, new(80, 80), radius);
            Check(sizeFit is { SupportedSectors: >= ObservationPolicy.MinimumSupportedBoundaryFraction }
                && Math.Abs(sizeFit.Radius - radius) < 1.5,
                $"Independent boundary evidence fits a {radius}-pixel ball");
            foreach (var offset in new[] { new Point(-3, 0), new Point(3, 0), new Point(0, -3), new Point(0, 3) })
            {
                var jitterFit = BallBoundaryRefiner.Refine(differentSize, width, height, new Point(80, 80) + offset, radius);
                Check(jitterFit is { SupportedSectors: >= ObservationPolicy.MinimumSupportedBoundaryFraction }
                    && Math.Abs(jitterFit.Radius - radius) < 2,
                    $"Supported {radius}-pixel boundary survives a three-pixel seed shift");
            }
        }
        var adjacentBodies = new byte[width * height * 3];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            bool left = (x - 55) * (x - 55) + (y - 80) * (y - 80) <= 26 * 26;
            bool right = (x - 105) * (x - 105) + (y - 80) * (y - 80) <= 26 * 26;
            int at = (y * width + x) * 3;
            if (left)
            {
                adjacentBodies[at] = 10;
                adjacentBodies[at + 1] = 20;
                adjacentBodies[at + 2] = 180;
            }
            else if (right)
            {
                adjacentBodies[at] = 5;
                adjacentBodies[at + 1] = 140;
                adjacentBodies[at + 2] = 225;
            }
            else
            {
                adjacentBodies[at] = 90;
                adjacentBodies[at + 1] = 100;
                adjacentBodies[at + 2] = 110;
            }
        }
        var gapFit = BallBoundaryRefiner.Refine(adjacentBodies, width, height, new(80, 80), 26,
            expectedKind: "red");
        Check(gapFit is null || gapFit.SupportedSectors < ObservationPolicy.MinimumSupportedBoundaryFraction,
            "Adjacent ball edges cannot create a supported boundary at the empty gap");
        var motionAnchor = new[]
        {
            new CircleObservation("red-0", new(300, 500), 27, "solid", []) { CandidateBoundarySupport = .75 },
            new CircleObservation("yellow-1", new(650, 900), 27, "solid", []) { CandidateBoundarySupport = .75 }
        };
        var jittered = motionAnchor.Select(c => c with { Centre = c.Centre + new Point(1.5, 0) }).ToArray();
        Check(VerificationObjectMotion.MaximumSupportedDisplacement(motionAnchor, jittered) is < 6,
            "Small supported detector jitter does not count as ball motion");
        var rolled = motionAnchor.Select(c => c.BallId == "red-0" ? c with { Centre = c.Centre + new Point(25, 0) } : c).ToArray();
        Check(VerificationObjectMotion.MaximumSupportedDisplacement(motionAnchor, rolled) is > 6,
            "A slowly rolled ball is positive motion evidence even when pixel templates lose the translation");
        var unsupportedRoll = rolled.Select(c => c.BallId == "red-0" ? c with { CandidateBoundarySupport = .5 } : c).ToArray();
        Check(VerificationObjectMotion.MaximumSupportedDisplacement(motionAnchor, unsupportedRoll) is < 6,
            "Unsupported geometry cannot establish object motion");
        var good = Vision();
        Check(VerificationObjectMotion.IsReliablePositiveEvidence(good, 6.1, 6),
            "An unobstructed supported detection can establish positive movement");
        Check(!VerificationObjectMotion.IsReliablePositiveEvidence(good, 5.9, 6),
            "Sub-threshold detector displacement cannot establish movement");
        Check(!VerificationObjectMotion.IsReliablePositiveEvidence(good with { Obstructed = true }, 56.08, 6),
            "An obstructed packed-table detection cannot turn a missing-ball association into movement");
        Check(!VerificationObjectMotion.IsReliablePositiveEvidence(null, 56.08, 6),
            "Missing detections cannot establish positive movement");
        var supported = good with { Circles = good.Balls.Select(b => new CircleObservation(b.Id, new(100, 100), 27, "solid", [])
            { ProposalRadius = 27, BoundarySupport = 1 }).ToArray() };
        var bad = supported with { Circles = supported.Circles.Select(c => c with { Radius = 18, BoundarySupport = null }).ToArray(), Uncertain = true };
        Check(VerificationConsensus.Resolve([supported, bad, supported, supported]) is not null,
            "An explicitly unsupported middle fit can recover through three supported current appearances");
        Check(VerificationConsensus.Resolve([supported, bad, supported]) is null, "Unsupported middle geometry contributes no vote");
        Check(VerificationConsensus.Resolve([supported, supported, bad], out var conflict) is null && conflict is not null,
            "Final unsupported geometry remains refused with a specific consensus conflict");
        VisionResult RadiusVision(double radius, double? support)
        {
            var body = new Ball("red-0", "red", new(1, .5), .9);
            return Vision([body]) with { Circles = [new CircleObservation(body.Id, new(500, 400), radius, "solid", [])
                { ProposalRadius = support is null ? radius : null, BoundarySupport = support, CandidateBoundarySupport = support }] };
        }
        var partialRadiusStart = RadiusVision(17, 27 / 32d);
        var partialRadiusNear = RadiusVision(18, 29 / 32d);
        var partialRadiusOutlier = RadiusVision(22, 23 / 32d);
        Check(VerificationConsensus.Resolve([partialRadiusStart, partialRadiusNear, partialRadiusOutlier,
                partialRadiusNear, partialRadiusStart])?.Length == 1,
            "One isolated partial-radius outlier is ignored only when adjacent clear fits corroborate stable geometry");
        Check(VerificationConsensus.Resolve([partialRadiusStart, partialRadiusNear, partialRadiusOutlier,
                partialRadiusNear with { Uncertain = true }, partialRadiusStart])?.Length == 1,
            "An unrelated uncertain ball does not erase this body's uniquely matched supported radius evidence");
        Check(VerificationConsensus.Resolve([partialRadiusStart, partialRadiusNear, partialRadiusOutlier,
                partialRadiusOutlier, partialRadiusStart], out var persistentRadiusConflict) is null
            && persistentRadiusConflict?.Code == "radius-inconsistent",
            "A persistent partial-boundary radius shift remains unresolved");
        Check(VerificationConsensus.Resolve([partialRadiusStart, partialRadiusNear, partialRadiusOutlier,
                partialRadiusNear, partialRadiusOutlier, partialRadiusNear, partialRadiusStart], out var repeatedRadiusConflict) is null
            && repeatedRadiusConflict?.Code == "radius-inconsistent",
            "Repeated isolated partial-radius outliers cannot accumulate into consensus");
        Check(VerificationConsensus.Resolve([partialRadiusStart, partialRadiusNear, partialRadiusNear,
                partialRadiusOutlier], out var finalRadiusConflict) is null
            && finalRadiusConflict?.Code == "radius-inconsistent",
            "A final partial-boundary radius outlier remains unresolved");
        var unsupportedRadius = RadiusVision(22, null);
        Check(VerificationConsensus.Resolve([partialRadiusStart, partialRadiusNear, partialRadiusOutlier,
                unsupportedRadius, partialRadiusStart], out var missingNeighbourSupportConflict) is null
            && missingNeighbourSupportConflict?.Code == "radius-inconsistent",
            "A bracket without unique supported radius evidence on both sides remains unresolved");
        Check(VerificationConsensus.Resolve([partialRadiusStart, partialRadiusNear, partialRadiusNear,
                unsupportedRadius], out var missingFinalSupportConflict) is null
            && missingFinalSupportConflict?.Code == "radius-inconsistent",
            "A final unsupported radius proposal remains unresolved");
        var completeRadiusStart = RadiusVision(17, 1);
        var completeRadiusOutlier = RadiusVision(22, 1);
        Check(VerificationConsensus.Resolve([completeRadiusStart, completeRadiusOutlier, completeRadiusStart], out var radiusConflict) is null
            && radiusConflict?.Code == "radius-inconsistent",
            "Fully supported circumference disagreement remains unresolved");
    }
    static async Task Cancelled(Task task) { try { await task; } catch (OperationCanceledException) { return; } throw new Exception("Expected cancellation"); }
    static async Task Until(Func<bool> condition) { for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(10); Check(condition(), "Late resource cleanup completes"); }
    sealed class Frames : ITableFrameSource, ITableVerificationFrameSource
    {
        public int Reads, Disposals, Encodings; public bool Duplicate, Gap; public double ClockAge = 1, AgeMs;
        public bool FinalCurrent = true; public Exception? Failure;
        public Task RefreshVerificationClockAsync(CancellationToken cancellation) => Task.CompletedTask;
        public Task<bool> IsVerificationSourceCurrentAsync(CameraScope source, long generation, string timeline, CancellationToken cancellation) => Task.FromResult(FinalCurrent);
        public TaskCompletionSource? Hold, Entered;
        public async Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
        {
            if (Failure is { } failure) throw failure;
            int index = ++Reads; var hold = Hold; if (hold is not null) { Entered!.TrySetResult(); await hold.Task; }
            var pts = index * (Gap ? 1000000L : 500000L);
            return new([], DateTimeOffset.UtcNow.AddMilliseconds(-AgeMs), "fixture") { SourceTimeUs = Duplicate ? 0 : pts,
                ClockSampleAgeMs = ClockAge, ClockUncertaintyMs = 1, Image = new Image(this), DecodedReceipt = new(1, Duplicate ? 1 : index, pts, index, 640, 360) };
        }
    }
    sealed class Image(Frames frames) : ITableFrameImage { public byte[] EncodeForReview() { Interlocked.Increment(ref frames.Encodings); return [1]; } public void Dispose() => Interlocked.Increment(ref frames.Disposals); }
    sealed class Analyzer : ITableAnalyzer, ITableVerificationAnalyzer
    {
        public int Detections, Disposals, DelayMs, CreatedWorkers, ObstructionResets, MeasurementCalls, ObstructAtCall;
        public bool LayoutUncertain, ObstructFirstDetectedFrame, ObstructEveryDetectedFrame, ObstructionIssued, DropLastDetectedBall;
        public bool ClearLayoutWithIncompleteInventory;
        public int MovingAtCall;
        public HashSet<int> InsufficientEvidenceAtCalls = [];
        public HashSet<int> UncertainAtDetections = [];
        public Ball[]? DetectedBalls;
        public TaskCompletionSource? Hold, Entered; public Exception? Failure;
        VisionResult Detect(Ball[]? balls = null)
        {
            var vision = Vision(balls, LayoutUncertain);
            return ClearLayoutWithIncompleteInventory ? vision with { LayoutUncertain = false } : vision;
        }
        public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default) => Detect();
        public Point[]? FindCorners(byte[] jpeg) => null;
        public ITableVerificationWorker CreateVerification(TableCalibration calibration) { CreatedWorkers++; return new Worker(this); }
        sealed class Worker(Analyzer owner) : ITableVerificationWorker
        {
            int count;
            public VerificationMeasurement Measure(TableFrame frame, bool detect, CancellationToken cancellation)
            {
                int call = Interlocked.Increment(ref owner.MeasurementCalls);
                if (owner.Failure is { } failure) throw failure;
                if (owner.Hold is { } hold) { owner.Entered!.TrySetResult(); hold.Task.GetAwaiter().GetResult(); }
                if (owner.DelayMs > 0) Thread.Sleep(owner.DelayMs);
                if (detect && (owner.ObstructEveryDetectedFrame || call == owner.ObstructAtCall
                    || owner.ObstructFirstDetectedFrame && !owner.ObstructionIssued))
                {
                    owner.ObstructionIssued = true;
                    return new(TableMotion.Unknown, ObservationReason.Obstructed, false, 0, 0, 0, Vision() with { Obstructed = true });
                }
                int sample = count++;
                VisionResult? detected = null;
                if (detect)
                {
                    int detection = Interlocked.Increment(ref owner.Detections);
                    detected = owner.Detect(owner.DetectedBalls);
                    if (owner.DropLastDetectedBall && detection >= 3) detected = detected with { Balls = detected.Balls[..^1] };
                    if (owner.UncertainAtDetections.Contains(detection))
                        detected = detected with { Balls = detected.Balls[..^1], Uncertain = true };
                }
                if (call == owner.MovingAtCall)
                    return new(TableMotion.Moving, ObservationReason.Moving, true, 0, 0, 0, detected);
                if (owner.InsufficientEvidenceAtCalls.Contains(call))
                    return new(TableMotion.Unknown, ObservationReason.InsufficientEvidence, true, 0, .005, .1, detected);
                return new(sample == 0 ? TableMotion.Unknown : TableMotion.Still,
                    sample == 0 ? ObservationReason.InsufficientEvidence : ObservationReason.None, true, 0, 0, 0, detected);
            }
            public void ResetAfterObstruction() { count = 0; Interlocked.Increment(ref owner.ObstructionResets); }
            public void Dispose() => Interlocked.Increment(ref owner.Disposals);
        }
    }
    sealed class Calibration : ICalibrationStore { public Point[]? Load(string id) => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)]; public void Save(string id, Point[] corners) { } }
}
