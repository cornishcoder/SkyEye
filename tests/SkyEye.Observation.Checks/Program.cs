using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Observation;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

var root = Path.Combine(Path.GetTempPath(), "skyeye-observation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var files = new CorpusFiles(root);
    var scope = new FrameScope("camera", "timeline", 1, "cal-v1");
    var frames = new List<IndexedFrame>();
    IndexedFrame Frame(string name, long id, long pts, FrameScope? source = null, long? pixelId = null)
    {
        var pixels = new byte[64]; BinaryPrimitives.WriteInt64LittleEndian(pixels, pixelId ?? id);
        File.WriteAllBytes(Path.Combine(root, name + ".gray"), pixels);
        return new(name, id, pts, source ?? scope, name + ".gray", CorpusFiles.Hash(pixels), PixelEncoding.Gray8, 8, 8, true);
    }
    frames.Add(Frame("start", 1, 1000)); frames.Add(Frame("next", 2, 34000));
    frames.Add(Frame("duplicate", 2, 34000)); frames.Add(Frame("backward", 0, 2000));
    frames.Add(Frame("gap", 8, 1000000)); frames.Add(Frame("reset", 1, 0, scope with { Timeline = "new", Generation = 2 }));
    frames.Add(Frame("wrong-pixels", 2, 10000, scope with { Timeline = "new", Generation = 2 }, 99));
    File.WriteAllLines(Path.Combine(root, "frames.jsonl"), frames.Select(f => JsonSerializer.Serialize(f, new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false })));
    var source = new MediaSource("camera", "Synthetic receipt fixture", "start.gray", frames[0].PixelSha256, 8, 8, SourceClock.SyntheticMicroseconds, "cal-v1", 30);
    var sequence = new CorpusSequence("contracts", "session", CorpusKind.Synthetic, CorpusSplit.Regression, SequencePurpose.Contract, source, "frames.jsonl", "labels.json", "Not real shots", []);
    File.WriteAllText(Path.Combine(root, "labels.json"), CorpusJson.Write(new SequenceLabels(1, "contracts", "fixture", "cal-v1", false, [], [])));
    var manifest = new CorpusManifest(1, "test", [sequence]);
    Check(!CorpusJson.Write(manifest).Contains('\r'), "Frozen JSON uses LF on every platform");
    var audit = CorpusValidation.Validate(manifest, files);
    Check(!audit.AccuracyGateReady && audit.RealShotSequences == 0, "Synthetic contracts cannot satisfy real accuracy coverage");
    var report = FrameReplay.Run(sequence, files);
    Check(report.Processed == 4 && report.Rejected == 3 && report.ScopeResets == 1 && report.GapResets == 1, "Drops/gaps/resets have exact accounting");
    Check(report.MissingSourceFrameIds == 5, "Missing indexed frame IDs are counted separately from rejected samples");
    Check(report.Receipts[^1].Status == ReplayStatus.PixelIdMismatch, "Payload frame identity must match the indexed receipt");
    Check(CorpusJson.Write(report) == CorpusJson.Write(FrameReplay.Run(sequence, files)), "Fixed inputs/config reproduce byte-identical reports");
    Throws<InvalidDataException>(() => files.Resolve("../outside"));
    var real = sequence with { Kind = CorpusKind.Real, Purpose = SequencePurpose.Shot, Split = CorpusSplit.Holdout };
    Throws<InvalidDataException>(() => CorpusValidation.Validate(manifest with { Sequences = [sequence, real with { Id = "held" }] }, files));
    // O08 holdout purpose is explicit and cannot inflate O01 shot counts.
    File.WriteAllLines(Path.Combine(root, "verification.jsonl"), frames.Take(2).Select(f => JsonSerializer.Serialize(f, new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false })));
    var verificationLabels = new SequenceLabels(1, "contracts", "reviewer", "cal-v1", true,
        frames.Take(2).Select(f => new FrameLabels(f.SampleId, f.PixelSha256, "table-2x1", [new("C", "cue", 1, .5, LabelVisibility.Visible)], "contract only")).ToImmutableArray(),
        [new("still", 1000, 34000, "confirmed", [], "contract only")]);
    File.WriteAllText(Path.Combine(root, "verification-labels.json"), CorpusJson.Write(verificationLabels));
    var verification = real with { Purpose = SequencePurpose.StillVerification, Source = source with { Clock = SourceClock.RecordingMicroseconds }, IndexFile = "verification.jsonl", LabelsFile = "verification-labels.json" };
    var verificationAudit = CorpusValidation.Validate(manifest with { Sequences = [verification] }, files);
    Check(verificationAudit.RealShotSequences == 0 && verificationAudit.HeldOutVerificationSequences == 1 && !verificationAudit.AccuracyGateReady,
        "Reviewed verification holdout has its own denominator and proves no shot accuracy");
    Throws<InvalidDataException>(() => CorpusValidation.Validate(manifest with { Sequences = [verification with { Source = source }] }, files));
    Throws<InvalidDataException>(() => CorpusValidation.Validate(manifest with { Sequences = [verification with { Purpose = SequencePurpose.NegativeVerification }] }, files));
    File.WriteAllLines(Path.Combine(root, "negative-index.jsonl"), frames.Take(2).Select(f => JsonSerializer.Serialize(f with { FrameId = 1, SourceTimeUs = 1000 }, new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false })));
    File.WriteAllText(Path.Combine(root, "verification-labels.json"), CorpusJson.Write(verificationLabels with { Intervals = [new("source-invalid", 1000, 34000, "confirmed", [], "Duplicate source receipt contract")] }));
    var negative = verification with { Purpose = SequencePurpose.NegativeVerification, IndexFile = "negative-index.jsonl" };
    Check(CorpusValidation.Validate(manifest with { Sequences = [negative] }, files).HeldOutVerificationSequences == 1,
        "Explicit reviewed source-invalid negatives can preserve frozen/duplicate receipts for rejection evaluation");
    var observed = new SkyEye.Core.Application.TableObservation(new(Guid.NewGuid(), "camera", "session", 1, 1), 1, 1, "timeline", DateTimeOffset.UtcNow,
        null, 0, 0, 0, 2300, 1, SkyEye.Core.Application.ObservationOutcome.Ready, "synthetic-contract")
        { Reason = SkyEye.Core.Analysis.ObservationReason.None };
    var request = new VerificationWindow("window", negative.Id, "next", Guid.NewGuid(), 1000, 34000,
        SkyEye.Core.Application.AdviceTargetGroup.Open, VerificationTimingKind.OfflineReplay, observed);
    var scoredRequest = VerificationEvaluation.Score(manifest with { Sequences = [negative] }, files, [request]).Single();
    Check(scoredRequest.Unsafe && scoredRequest.FalseReady && !scoredRequest.ValidProof && scoredRequest.Timely is null,
        "Unsafe published Ready is counted even without valid proof; offline replay cannot pass live timing");
    var refusedRequest = request with { Id = "refused", OperationId = Guid.NewGuid(), TimingKind = VerificationTimingKind.LiveRecorded,
        Result = observed with { Outcome = SkyEye.Core.Application.ObservationOutcome.Unclear } };
    var refusalScore = VerificationEvaluation.Score(manifest with { Sequences = [negative] }, files, [refusedRequest]).Single();
    Check(!refusalScore.FalseReady && refusalScore.Timely == true, "Recorded negative refusal retains its live timing denominator");
    Throws<InvalidDataException>(() => VerificationEvaluation.Score(manifest with { Sequences = [negative] }, files, [request, request]));
    Throws<InvalidDataException>(() => CorpusValidation.Validate(manifest with { Sequences = [negative with { Purpose = SequencePurpose.StillVerification }] }, files));
    // A complete safe publication must score its final geometry, not just its Ready flag.
    var positiveFrames = Enumerable.Range(0, 5).Select(i => Frame("positive-" + i, i + 1, 1000 + i * 500000)).ToArray();
    File.WriteAllLines(Path.Combine(root, "positive-index.jsonl"), positiveFrames.Select(f => JsonSerializer.Serialize(f, new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false })));
    var positiveLabels = verificationLabels with {
        Frames = positiveFrames.Select(f => new FrameLabels(f.SampleId, f.PixelSha256, "table-2x1",
            [new("C", "cue", 1, .5, LabelVisibility.Visible) { RadiusX = .027, RadiusY = .027 }], "synthetic contract only")).ToImmutableArray(),
        Intervals = [new("still", 1000, 2001000, "confirmed", [], "synthetic contract only")] };
    File.WriteAllText(Path.Combine(root, "positive-labels.json"), CorpusJson.Write(positiveLabels));
    var positiveSequence = verification with { IndexFile = "positive-index.jsonl", LabelsFile = "positive-labels.json" };
    var positiveSamples = positiveFrames.Select(f => new VerificationSample(f.FrameId, f.SourceTimeUs!.Value, 100, 50, 10, 5, 50,
        new(TableMotion.Still, ObservationReason.None, true, 0, 0, 0, null))).ToArray();
    var positiveResult = observed with { SourceTimeUs = 2001000, FrameAgeMs = 100, Motion = TableMotion.Still, Quality = LayoutQuality.Clear,
        DecodedReceipt = new(1, 5, 2001000, 2001000000, 8, 8), DetectionEvidence = new(5, 2001000, 100),
        Verification = new(request.OperationId, ObservationPolicy.VerificationConfiguration, "cal-v1", "open", 1, 1000, 2001000, 5, 500, 50, positiveSamples),
        GameEvidence = new(new("camera", "timeline", 1, "cal-v1", "contract"), [new("C", SkyEye.GameState.BallColour.Cue, new(.5, .5), 1)]) };
    var positiveRequest = request with { FinalSampleId = "positive-4", ToUs = 2001000, Result = positiveResult };
    var positiveScore = VerificationEvaluation.Score(manifest with { Sequences = [positiveSequence] }, files, [positiveRequest]).Single();
    Check(positiveScore.ValidProof && positiveScore.CorrectLayout == true && !positiveScore.FalseReady,
        "Complete safe proof scores matching reviewed final geometry in the shared coordinate space");
    // Synthetic contract data exercises report plumbing, not detector accuracy.
    var comparisonManifest = manifest with { Sequences = [positiveSequence] };
    var comparisonManifestPath = Path.Combine(root, "comparison-manifest.json");
    File.WriteAllText(comparisonManifestPath, CorpusJson.Write(comparisonManifest));
    CorpusValidation.Validate(comparisonManifest, files);
    var comparisonLock = SplitLock.Freeze(comparisonManifest, files, comparisonManifestPath);
    var comparisonDetections = positiveFrames.Select((f, i) => new StoredDetection(f.SampleId, f.PixelSha256,
        "table-2x1", "fixture-v1", "synthetic contract", i == 2,
        i == 2 ? [new("extra", "red", 1.8, .9, null)] : [new("cue", "cue", 1, .5, null)])).ToImmutableArray();
    var comparisonCandidates = new[] {
        new O09CandidateInput("current", new string('a', 64), comparisonDetections),
        new O09CandidateInput("candidate", new string('b', 64), comparisonDetections),
        new O09CandidateInput("control", new string('c', 64), comparisonDetections) };
    var comparison = O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout, comparisonCandidates, .027);
    var comparisonFrames = comparison.Candidates[0].Frames;
    Check(comparison.SchemaVersion == 2 && comparisonFrames.Length == positiveFrames.Length
        && comparisonFrames[0].FrameId == positiveFrames[0].FrameId && comparisonFrames[0].SourceTimeUs == positiveFrames[0].SourceTimeUs
        && comparisonFrames[0].Scope == positiveFrames[0].Scope && comparisonFrames[0].PixelSha256 == positiveFrames[0].PixelSha256,
        "Comparison retains every reviewed frame's exact source receipt");
    Check(comparisonFrames[2].Score is { Uncertain: true, FalsePositives: 1, MissedVisible: 1 }
        && comparisonFrames[0].Score is { FalsePositives: 0, MissedVisible: 0 }
        && comparison.Candidates[0].FailureSamples.Length == 1,
        "Comparison retains successful and uncertain failed frame scores alongside failure summaries");
    var reorderedCandidates = comparisonCandidates.Select(c => c with { Detections = c.Detections.Reverse().ToImmutableArray() }).ToArray();
    Check(CorpusJson.Write(comparison) == CorpusJson.Write(O09Comparison.Compare(comparisonManifest, comparisonLock, files,
        CorpusSplit.Holdout, reorderedCandidates, .027)), "Per-frame comparison is deterministic across detection ordering");
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout,
        [comparisonCandidates[0] with { Detections = comparisonDetections.RemoveAt(0) }, comparisonCandidates[1], comparisonCandidates[2]], .027));
    File.WriteAllText(Path.Combine(root, "positive-labels.json"), CorpusJson.Write(positiveLabels with { Reviewed = false }));
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout, comparisonCandidates, .027));
    File.WriteAllText(Path.Combine(root, "positive-labels.json"), CorpusJson.Write(positiveLabels));
    var mismatchedIndex = Path.Combine(root, "mismatched-index.jsonl");
    File.WriteAllLines(mismatchedIndex, positiveFrames.Select((f, i) => JsonSerializer.Serialize(i == 0 ? f with { PixelSha256 = new string('0', 64) } : f,
        new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false })));
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest with { Sequences = [positiveSequence with { IndexFile = "mismatched-index.jsonl" }] },
        comparisonLock, files, CorpusSplit.Holdout, comparisonCandidates, .027));
    Check(VerificationEvaluation.Score(manifest with { Sequences = [positiveSequence] }, files,
        [positiveRequest with { Result = observed }]).Single().FalseReady,
        "A safe-window Ready without proof remains in the false Ready denominator");
    var misplaced = positiveResult with { GameEvidence = positiveResult.GameEvidence! with { Balls = [new("C", SkyEye.GameState.BallColour.Cue, new(.52, .5), 1)] } };
    Check(VerificationEvaluation.Score(manifest with { Sequences = [positiveSequence] }, files, [positiveRequest with { Result = misplaced }]).Single().FalseReady,
        "A valid publication proof cannot excuse an incorrect final ball position");
    File.WriteAllText(Path.Combine(root, "verification-labels.json"), CorpusJson.Write(verificationLabels with { Reviewed = false }));
    Throws<InvalidDataException>(() => CorpusValidation.Validate(manifest with { Sequences = [verification] }, files));
    var lockPath = Path.Combine(root, "manifest.json"); File.WriteAllText(lockPath, CorpusJson.Write(manifest));
    var frozen = SplitLock.Freeze(manifest, files, lockPath);
    SplitLock.Verify(frozen, SplitLock.Freeze(manifest, files, lockPath));
    File.AppendAllText(Path.Combine(root, "labels.json"), " ");
    Throws<InvalidDataException>(() => SplitLock.Verify(frozen, SplitLock.Freeze(manifest, files, lockPath)));
    var truth = new FrameLabels("start", frames[0].PixelSha256, "table-2x1",
        [new("R1", "red", .2, .3, LabelVisibility.Visible), new("Y1", "yellow", .5, .6, LabelVisibility.Visible), new("hidden", "red", .8, .8, LabelVisibility.Occluded)], "fixture");
    var detection = new StoredDetection("start", frames[0].PixelSha256, "table-2x1", "fixture-v1", "Synthetic scoring example", true,
        [new("matched", "yellow", .2, .3, .8), new("extra", "red", 1.5, .9, .9)]);
    var score = DetectorBaseline.Score(truth, detection, .027);
    Check(score.SpatialTruePositives == 1 && score.FalsePositives == 1 && score.MissedVisible == 1 && score.ColourCorrect == 0,
        "Visible ground truth, wrong colours, extra circles and misses remain separate");
    Throws<InvalidDataException>(() => DetectorBaseline.Score(truth, detection with { PixelSha256 = new string('0',64) }, .027));
    var radiiTruth = truth with { Balls = [truth.Balls[0] with { RadiusX = .02, RadiusY = .01 }] };
    var radiiDetection = detection with { Balls = [new("measured", "red", .205, .3, .9)] };
    var radiiScore = DetectorBaseline.Score(radiiTruth, radiiDetection, .027);
    Check(radiiScore.MatchesWithReviewedRadii == 1 && Math.Abs(radiiScore.CentreErrorInRadiiP95!.Value - .25) < 1e-8,
        "Centre error is scaled by independently labelled ellipse radii; unlabelled radii remain unavailable");
    // A nearest-first greedy match loses B: A must be reassigned to the alternative.
    var crowdedTruth = truth with { Balls = [new("A", "red", .20, .3, LabelVisibility.Visible), new("B", "yellow", .24, .3, LabelVisibility.Visible)] };
    var crowdedDetection = detection with { Balls = [new("shared", "yellow", .22, .3, null), new("alternative", "red", .17, .3, null)] };
    var crowded = DetectorBaseline.Score(crowdedTruth, crowdedDetection, .035);
    Check(crowded.SpatialTruePositives == 2 && crowded.FalsePositives == 0 && crowded.MissedVisible == 0,
        "Maximum association avoids the greedy crowded-ball miss");
    Check(CorpusJson.Write(crowded) == CorpusJson.Write(DetectorBaseline.Score(crowdedTruth,
        crowdedDetection with { Balls = crowdedDetection.Balls.Reverse().ToImmutableArray() }, .035)),
        "Stored detection order cannot change association or metrics");
    var captureFolder = Path.Combine(root, "capture", "window"); Directory.CreateDirectory(captureFolder);
    foreach (var f in frames.Take(2))
    {
        File.Copy(Path.Combine(root, f.PixelFile), Path.Combine(captureFolder, f.PixelFile));
        object indexed = f.FrameId == 1
            ? new { f.SampleId, f.FrameId, f.SourceTimeUs, f.Scope, f.PixelFile, f.PixelSha256, f.Encoding, f.Width, f.Height,
                f.EmbeddedFrameId, analyzerPixelSource = "decoded-bitmap-lossless" }
            : f; // Also retain coverage for older receipts without the extra field.
        File.WriteAllText(Path.Combine(captureFolder, f.SampleId + ".json"), JsonSerializer.Serialize(new { indexedFrame = indexed }, CorpusJson.Options));
    }
    var importedPath = VerificationCaptureImport.Import(Path.Combine(root, "capture"), Path.Combine(root, "imported"));
    var imported = CorpusJson.Read<CorpusManifest>(importedPath);
    var importedAudit = CorpusValidation.Validate(imported, new CorpusFiles(Path.Combine(root, "imported")));
    Check(importedAudit.HeldOutVerificationSequences == 0 && imported.Sequences[0].Session == scope.Timeline,
        "Imported paired snapshots stay unreviewed development material; related windows retain a common session");
    Check(File.ReadAllText(Path.Combine(root, "imported", "window", "start.receipt.json")).Contains("decoded-bitmap-lossless"),
        "Current capture pixel-source provenance stays in the copied receipt without becoming a reviewed label");
    Throws<IOException>(() => VerificationCaptureImport.Import(Path.Combine(root, "capture"), Path.Combine(root, "imported")));
    var captureReceipt = Path.Combine(captureFolder, "start.json");
    File.WriteAllText(captureReceipt, File.ReadAllText(captureReceipt).Replace("analyzerPixelSource", "unexpectedCaptureField"));
    Throws<JsonException>(() => VerificationCaptureImport.Import(Path.Combine(root, "capture"), Path.Combine(root, "invalid-import")));
    // Minimal SOF fixture checks the dimension contract without image decoding or CV.
    var jpeg = new byte[] { 0xff,0xd8,0xff,0xc0,0,11,8,0,8,0,8,1,1,0x11,0,0xff,0xd9 };
    var jpegFrame = frames[0] with { Encoding = PixelEncoding.Jpeg, EmbeddedFrameId = false };
    PixelDimensions.Verify(jpegFrame, jpeg);
    Throws<InvalidDataException>(() => PixelDimensions.Verify(jpegFrame with { Width = 9 }, jpeg));
    Throws<InvalidDataException>(() => PixelDimensions.Verify(jpegFrame, jpeg[..8]));
    var collision = Frame("receipt-conflict", 1, 1001, pixelId: 1);
    File.WriteAllLines(Path.Combine(root, "conflicts.jsonl"), new[] { frames[0], collision }.Select(f => JsonSerializer.Serialize(f, new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false })));
    Check(FrameReplay.Run(sequence with { IndexFile = "conflicts.jsonl" }, files).Receipts[1].Status == ReplayStatus.PairConflict,
        "A frame ID cannot name another timestamp/payload receipt");
    File.WriteAllBytes(Path.Combine(root, "next.gray"), new byte[64]);
    Throws<InvalidDataException>(() => FrameReplay.Run(sequence, files));
    Console.WriteLine("PASS observation corpus/replay: deterministic receipts, duplicates/backwards, gaps/scope resets, pixel-ID pairing, checksums, split leakage and synthetic coverage limits");
}
finally
{
    if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
        || !Path.GetFileName(root).StartsWith("skyeye-observation-")) throw new InvalidOperationException("Unsafe test cleanup.");
    Directory.Delete(root, true);
}
static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
