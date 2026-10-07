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
        var drift = good with { Balls = good.Balls.Select(b => b with { Position = b.Position + new Point(.02, 0) }).ToArray() };
        Check(VerificationConsensus.Resolve([good, good, drift]) is null, "Fixed anchor prevents cumulative centre drift");
        PixelChecks();
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
        var id = Guid.NewGuid(); var verification = session.VerifyAsync(id, 1);
        Check(ReferenceEquals(verification, session.VerifyAsync(id, 1)), "Compatible in-flight verification shares one owner");
        await verification;
        Check(session.State.Observation is { Outcome: ObservationOutcome.Ready, Motion: TableMotion.Still, Verification.IndependentFrames: 5 }, "Five independent source samples establish still/clear readiness");
        Check(frames.Reads == 5 && frames.Disposals == 5 && analyzer.Detections == 3 && analyzer.Disposals == 1, "Bounded anchors, exact bitmap/worker disposal");
        var observation = session.State.Observation!;
        var choices = session.ComputeChoices(session.State.Generation, observation.TableVersion);
        Check(ReferenceEquals(choices, session.ComputeChoices(session.State.Generation, observation.TableVersion)) && analyzer.Detections == 3, "Choices computed once without new perception");
        session.Invalidate(); frames.Duplicate = true;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.SourceChanged && session.State.Observation.Outcome != ObservationOutcome.Ready, "Duplicate receipts cannot build a span"); frames.Duplicate = false;
        session.Invalidate(); frames.ClockAge = 3000;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.ClockUntrusted, "Stale mapping fails even for freshly copied pixels"); frames.ClockAge = 1;
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
        await Until(() => frames.Disposals == disposed + 1);
        Check(session.State.Observation is null, "Uncooperative late acquisition cannot publish and its pixels are disposed"); frames.Hold = null;

        session.Invalidate(); analyzer.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); analyzer.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var changing = session.VerifyAsync(Guid.NewGuid(), 1); await analyzer.Entered.Task;
        session.Bind(camera, "yellow"); await Cancelled(changing);
        Check(session.State.Observation is null, "Group changes cancel verification and reject late results");
        int workers = analyzer.Disposals; analyzer.Hold.SetResult(); await Until(() => analyzer.Disposals == workers + 1); analyzer.Hold = null;
        session.Invalidate(); frames.Hold = new(TaskCreationOptions.RunContinuationsAsynchronously); frames.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeout = session.VerifyAsync(Guid.NewGuid(), 1); await frames.Entered.Task; await timeout;
        Check(session.State.Observation is { Reason: ObservationReason.TimedOut, Outcome: ObservationOutcome.Unsettled }, "Uncooperative source reaches the hard deadline with an honest result");
        int lateDisposals = frames.Disposals; frames.Hold.SetResult(); await Until(() => frames.Disposals == lateDisposals + 1); frames.Hold = null;
        session.Invalidate(); frames.AgeMs = 1450; analyzer.DelayMs = 100;
        await session.VerifyAsync(Guid.NewGuid(), 1);
        Check(session.State.Observation?.Reason == ObservationReason.FrameTooOld, "Freshness fails when processing pushes the final sample over the publication budget"); frames.AgeMs = 0; analyzer.DelayMs = 0;
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
        Check(new VerificationPixels(w, h).Measure(new byte[w * h], true, null).Motion == TableMotion.Unknown, "Textureless coverage never implies stillness");
        var lighting = new VerificationPixels(w, h); lighting.Measure(gray, true, null);
        Check(lighting.Measure(gray.Select(b => (byte)(b + 20)).ToArray(), true, null).Reason == ObservationReason.LightingChanged, "Broad exposure change has its own reason");
        var pose = new VerificationPixels(w, h); pose.Measure(gray, true, null);
        var shifted = Enumerable.Range(0, w * h).Select(i => gray[(i / w) * w + (i % w + 2) % w]).ToArray();
        Check(pose.Measure(shifted, true, null).Reason == ObservationReason.CalibrationChanged, "Global camera shift invalidates calibration");
    }
    static async Task Cancelled(Task task) { try { await task; } catch (OperationCanceledException) { return; } throw new Exception("Expected cancellation"); }
    static async Task Until(Func<bool> condition) { for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(10); Check(condition(), "Late resource cleanup completes"); }
    sealed class Frames : ITableFrameSource, ITableVerificationFrameSource
    {
        public int Reads, Disposals; public bool Duplicate, Gap; public double ClockAge = 1, AgeMs;
        public bool FinalCurrent = true;
        public Task RefreshVerificationClockAsync(CancellationToken cancellation) => Task.CompletedTask;
        public Task<bool> IsVerificationSourceCurrentAsync(CameraScope source, long generation, string timeline, CancellationToken cancellation) => Task.FromResult(FinalCurrent);
        public TaskCompletionSource? Hold, Entered;
        public async Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
        {
            int index = ++Reads; var hold = Hold; if (hold is not null) { Entered!.TrySetResult(); await hold.Task; }
            var pts = index * (Gap ? 1000000L : 500000L);
            return new([], DateTimeOffset.UtcNow.AddMilliseconds(-AgeMs), "fixture") { SourceTimeUs = Duplicate ? 0 : pts,
                ClockSampleAgeMs = ClockAge, ClockUncertaintyMs = 1, Image = new Image(this), DecodedReceipt = new(1, Duplicate ? 1 : index, pts, index, 640, 360) };
        }
    }
    sealed class Image(Frames frames) : ITableFrameImage { public byte[] EncodeForReview() => []; public void Dispose() => Interlocked.Increment(ref frames.Disposals); }
    sealed class Analyzer : ITableAnalyzer, ITableVerificationAnalyzer
    {
        public int Detections, Disposals, DelayMs; public TaskCompletionSource? Hold, Entered;
        public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default) => Vision();
        public Point[]? FindCorners(byte[] jpeg) => null;
        public ITableVerificationWorker CreateVerification(TableCalibration calibration) => new Worker(this);
        sealed class Worker(Analyzer owner) : ITableVerificationWorker
        {
            int count;
            public VerificationMeasurement Measure(TableFrame frame, bool detect, CancellationToken cancellation)
            {
                if (owner.Hold is { } hold) { owner.Entered!.TrySetResult(); hold.Task.GetAwaiter().GetResult(); }
                if (owner.DelayMs > 0) Thread.Sleep(owner.DelayMs);
                if (detect) Interlocked.Increment(ref owner.Detections);
                return new(count++ == 0 ? TableMotion.Unknown : TableMotion.Still, count == 1 ? ObservationReason.InsufficientEvidence : ObservationReason.None, true, 0, 0, 0, detect ? Vision() : null);
            }
            public void Dispose() => Interlocked.Increment(ref owner.Disposals);
        }
    }
    sealed class Calibration : ICalibrationStore { public Point[]? Load(string id) => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)]; public void Save(string id, Point[] corners) { } }
}
