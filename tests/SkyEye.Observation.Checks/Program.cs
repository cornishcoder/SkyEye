using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    var recordedAnalyzer = "{\"Algorithm\":\"contract-v1\",\"Settings\":{\"Width\":800}}";
    var alternateAnalyzer = "{\"Algorithm\":\"contract-v2\",\"Settings\":{\"Width\":800}}";
    var comparisonLockBytes = System.Text.Encoding.UTF8.GetBytes(CorpusJson.Write(comparisonLock) + "\n");
    var comparisonLockSha = CorpusFiles.Hash(comparisonLockBytes);
    O08ReplayProvenance ReplayMetadata(IndexedFrame f, bool layoutUncertain = false) => new(
        comparisonManifest.Id, comparisonLock.ManifestSha256, comparisonLockSha, positiveSequence.Id, "Holdout",
        f.FrameId, f.SourceTimeUs!.Value, f.Scope, f.PixelSha256, new string('d', 64), new string('e', 64),
        new string('f', 64), new string('c', 64), "screening", "contract-device", 5, false,
        O08ReplayExport.WarmupTreatment, recordedAnalyzer, recordedAnalyzer,
        CorpusFiles.Hash(System.Text.Encoding.UTF8.GetBytes(recordedAnalyzer)),
        CorpusFiles.Hash(System.Text.Encoding.UTF8.GetBytes(recordedAnalyzer)), true, 10 + f.FrameId,
        false, layoutUncertain, false);
    var comparisonDetections = positiveFrames.Select((f, i) => new StoredDetection(f.SampleId, f.PixelSha256,
        "table-2x1", "fixture-v1", "synthetic contract", i == 2,
        i == 2 ? [new("cue", "cue", 1, .5, null), new("duplicate", "red", 1.01, .5, null),
            new("other-extra", "red", 1.8, .9, null)] : [new("cue", "cue", 1, .5, null)])
        { O08Replay = ReplayMetadata(positiveFrames[i], i == 2) }).ToImmutableArray();
    O09CandidateBinding Binding(string detectionSha, string exportSha) => new(2, exportSha, comparisonManifest.Id,
        comparisonLock.ManifestSha256, comparisonLockSha, "Holdout", true, "detections.json", detectionSha);
    var comparisonCandidates = new[] {
        new O09CandidateInput("current", new string('a', 64), comparisonDetections, Binding(new string('a', 64), new string('1', 64))),
        new O09CandidateInput("candidate", new string('b', 64), comparisonDetections, Binding(new string('b', 64), new string('2', 64))),
        new O09CandidateInput("control", new string('c', 64), comparisonDetections, Binding(new string('c', 64), new string('3', 64))) };
    var comparison = O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout, comparisonCandidates, .027, comparisonLockSha);
    var comparisonFrames = comparison.Candidates[0].Frames;
    Check(comparison.SchemaVersion == 4 && comparisonFrames.Length == positiveFrames.Length
        && comparisonFrames[0].FrameId == positiveFrames[0].FrameId && comparisonFrames[0].SourceTimeUs == positiveFrames[0].SourceTimeUs
        && comparisonFrames[0].Scope == positiveFrames[0].Scope && comparisonFrames[0].PixelSha256 == positiveFrames[0].PixelSha256,
        "Comparison retains every reviewed frame's exact source receipt");
    Check(comparisonFrames[2].Score is { Uncertain: true, FalsePositives: 2, MissedVisible: 0 }
        && comparisonFrames[2].DuplicateExtraIds.SequenceEqual(["duplicate"])
        && comparisonFrames[2].OtherExtraIds.SequenceEqual(["other-extra"])
        && comparisonFrames[0].Score is { FalsePositives: 0, MissedVisible: 0 }
        && comparison.Candidates[0].FailureSamples.Length == 1,
        "Comparison reports duplicate and other extras separately alongside per-frame scores");
    Check(comparison.SplitLockSha256 == comparisonLockSha
        && comparison.Candidates[0].ReplayConfiguration is { CaptureConfigurationExactFrames: 5, CaptureConfigurationMismatchFrames: 0 }
        && comparison.Candidates[0].ReplayConfiguration is { Modes.Length: 1, DeviceSerials.Length: 1, BatchSizes.Length: 1, WarmupTreatments.Length: 1 }
        && comparison.Candidates[0].Overall is { TimedFrames: 5, AnalysisWallP50Ms: 13.0, AnalysisWallP95Ms: 15.0, DuplicateFalsePositives: 1, OtherFalsePositives: 1 }
        && comparisonFrames[2].O08Replay is { LayoutUncertain: true, Obstructed: false },
        "O09 binds replay rows to the exact split lock and reports uncertainty plus offline timing separately");
    var differentSettings = comparisonCandidates.Select(c => c.Name == "candidate" ? c with { Detections = c.Detections.Select(d =>
        d with { O08Replay = d.O08Replay! with { ReplayConfiguration = alternateAnalyzer,
            ReplayConfigurationSha256 = CorpusFiles.Hash(System.Text.Encoding.UTF8.GetBytes(alternateAnalyzer)), CaptureConfigurationExact = false } }).ToImmutableArray() } : c).ToArray();
    var comparedSettings = O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout, differentSettings, .027, comparisonLockSha);
    Check(comparedSettings.Candidates.Single(c => c.Name == "candidate").ReplayConfiguration is
        { CaptureConfigurationExactFrames: 0, CaptureConfigurationMismatchFrames: 5, ReplayConfigurations.Length: 1 },
        "Candidate setting differences remain visible in the O09 report instead of being silently called matched");
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout,
        [comparisonCandidates[0] with { Binding = comparisonCandidates[0].Binding! with { DetectionSha256 = new string('0', 64) } },
            comparisonCandidates[1], comparisonCandidates[2]], .027, comparisonLockSha));
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout,
        [comparisonCandidates[0] with { Binding = comparisonCandidates[0].Binding! with { SchemaVersion = 1 } },
            comparisonCandidates[1], comparisonCandidates[2]], .027, comparisonLockSha));
    var reorderedCandidates = comparisonCandidates.Select(c => c with { Detections = c.Detections.Reverse().ToImmutableArray() }).ToArray();
    Check(CorpusJson.Write(comparison) == CorpusJson.Write(O09Comparison.Compare(comparisonManifest, comparisonLock, files,
        CorpusSplit.Holdout, reorderedCandidates, .027, comparisonLockSha)), "Per-frame comparison is deterministic across detection ordering");
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout,
        [comparisonCandidates[0] with { Detections = comparisonDetections.RemoveAt(0) }, comparisonCandidates[1], comparisonCandidates[2]], .027, comparisonLockSha));
    File.WriteAllText(Path.Combine(root, "positive-labels.json"), CorpusJson.Write(positiveLabels with { Reviewed = false }));
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest, comparisonLock, files, CorpusSplit.Holdout, comparisonCandidates, .027, comparisonLockSha));
    File.WriteAllText(Path.Combine(root, "positive-labels.json"), CorpusJson.Write(positiveLabels));
    var mismatchedIndex = Path.Combine(root, "mismatched-index.jsonl");
    File.WriteAllLines(mismatchedIndex, positiveFrames.Select((f, i) => JsonSerializer.Serialize(i == 0 ? f with { PixelSha256 = new string('0', 64) } : f,
        new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false })));
    Throws<InvalidDataException>(() => O09Comparison.Compare(comparisonManifest with { Sequences = [positiveSequence with { IndexFile = "mismatched-index.jsonl" }] },
        comparisonLock, files, CorpusSplit.Holdout, comparisonCandidates, .027, comparisonLockSha));
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
    const string replayConfiguration = "{\"Algorithm\":\"synthetic-contract\",\"Settings\":{\"Width\":800}}";
    var captureFrames = frames.Take(2).Append(Frame("after", 3, 67000)).ToArray();
    foreach (var f in captureFrames)
    {
        File.Copy(Path.Combine(root, f.PixelFile), Path.Combine(captureFolder, f.PixelFile));
        object indexed = f.FrameId == 1
            ? new { f.SampleId, f.FrameId, f.SourceTimeUs, f.Scope, f.PixelFile, f.PixelSha256, f.Encoding, f.Width, f.Height,
                f.EmbeddedFrameId, analyzerPixelSource = "decoded-bitmap-lossless" }
            : f; // Also retain coverage for older receipts without the extra field.
        File.WriteAllText(Path.Combine(captureFolder, f.SampleId + ".json"), JsonSerializer.Serialize(new { indexedFrame = indexed,
            calibrationCorners = new[] { new { x = 0d, y = 0d }, new { x = 1d, y = 0d }, new { x = 1d, y = 1d }, new { x = 0d, y = 1d } },
            analyzerConfigurationIdentity = replayConfiguration, detectionRequested = f.FrameId != 2 }, CorpusJson.Options));
    }
    var secondScope = scope with { Timeline = "timeline-second" };
    var secondCaptureFolder = Path.Combine(root, "capture", "window2"); Directory.CreateDirectory(secondCaptureFolder);
    var secondCaptureFrames = new[] { Frame("other-start", 10, 1000, secondScope),
        Frame("other-next", 11, 34000, secondScope), Frame("other-after", 12, 67000, secondScope) };
    foreach (var f in secondCaptureFrames)
    {
        File.Copy(Path.Combine(root, f.PixelFile), Path.Combine(secondCaptureFolder, f.PixelFile));
        File.WriteAllText(Path.Combine(secondCaptureFolder, f.SampleId + ".json"), JsonSerializer.Serialize(new { indexedFrame = f,
            calibrationCorners = new[] { new { x = 0d, y = 0d }, new { x = 1d, y = 0d }, new { x = 1d, y = 1d }, new { x = 0d, y = 1d } },
            analyzerConfigurationIdentity = replayConfiguration, detectionRequested = f.FrameId != 11 }, CorpusJson.Options));
    }
    var importedPath = VerificationCaptureImport.Import(Path.Combine(root, "capture"), Path.Combine(root, "imported"));
    var imported = CorpusJson.Read<CorpusManifest>(importedPath);
    var importedAudit = CorpusValidation.Validate(imported, new CorpusFiles(Path.Combine(root, "imported")));
    Check(importedAudit.HeldOutVerificationSequences == 0 && imported.Sequences.Single(s => s.Id == "window").Session == scope.Timeline
        && imported.Sequences.Single(s => s.Id == "window2").Session == secondScope.Timeline,
        "Imported paired snapshots stay unreviewed development material; related windows retain a common session");
    Check(File.ReadAllText(Path.Combine(root, "imported", "window", "start.receipt.json")).Contains("decoded-bitmap-lossless"),
        "Current capture pixel-source provenance stays in the copied receipt without becoming a reviewed label");
    var importedFiles = new CorpusFiles(Path.Combine(root, "imported"));
    var importedLock = SplitLock.Freeze(imported, importedFiles, importedPath);
    var runRoot = Path.Combine(root, "test-replay"); Directory.CreateDirectory(Path.Combine(runRoot, "source-receipts"));
    var replayFrames = captureFrames;
    foreach (var f in replayFrames) File.Copy(Path.Combine(captureFolder, f.SampleId + ".json"), Path.Combine(runRoot, "source-receipts", f.SampleId + ".json"));
    string ReceiptHash(IndexedFrame f) => CorpusFiles.Hash(File.ReadAllBytes(Path.Combine(runRoot, "source-receipts", f.SampleId + ".json")));
    var replaySamples = replayFrames.Select((f, i) => new { frameId = f.FrameId, sourceTimeUs = f.SourceTimeUs,
        pixelSha256 = f.PixelSha256, receiptSha256 = ReceiptHash(f), detectionRequested = i != 1,
        analysis800Metrics = i == 1 ? null : new { wallMs = i == 0 ? 42.0 : 36.0 } }).ToArray();
    var replayReport = new { completed = true, mode = "probe-only", independentFrames = false, recordedAnalyzerConfigurationIdentity = replayConfiguration,
        replayAnalyzerConfigurationIdentity = replayConfiguration, samples = replaySamples,
        fullAnalysis800 = new { observations = new object[] {
            new { configuredWidth = 800, circleCount = 1, uncertain = false,
                layoutUncertain = false, obstructed = true, circles = new[] { new { ballId = "cue-0", kind = "cue", x = 1.0, y = .5 } } },
            new { configuredWidth = 800, circleCount = 1, uncertain = false,
                layoutUncertain = true, obstructed = false, circles = new[] { new { ballId = "red-2", kind = "red", x = .7, y = .4 } } }
        } } };
    var replayReportPath = Path.Combine(runRoot, "report.json"); File.WriteAllText(replayReportPath, CorpusJson.Write(replayReport));
    var replayManifestPath = Path.Combine(runRoot, "manifest.json");
    var replayManifest = new { mode = "probe-only", deviceSerial = "synthetic-device", independentFrames = false,
        apkSha256 = new string('a', 64), report = "report.json",
        reportSha256 = CorpusFiles.Hash(File.ReadAllBytes(replayReportPath)), sourceFrames = replayFrames.Select(f => new {
            frameId = f.FrameId, pixelFile = f.PixelFile, sha256 = f.PixelSha256, receiptSha256 = ReceiptHash(f),
            receiptArtifact = "source-receipts/" + f.SampleId + ".json" }).ToArray() };
    File.WriteAllText(replayManifestPath, CorpusJson.Write(replayManifest));
    var splitLockPath = Path.Combine(root, "imported.lock.json");
    File.WriteAllText(splitLockPath, CorpusJson.Write(importedLock) + "\n");
    var splitLockBytes = File.ReadAllBytes(splitLockPath);
    var splitLockSha = CorpusFiles.Hash(splitLockBytes);
    var converted = O08ReplayExport.Convert(imported, importedPath, importedLock, importedFiles, "window", runRoot, splitLockBytes);
    Check(!converted.LabelsReviewed && converted.Frames.Length == 3 && converted.Detections.Length == 2
        && converted.SplitLockSha256 == splitLockSha
        && converted.Frames[0] is { DetectionRequested: true, AnalysisWallMs: 42.0, AnalysisUncertain: false, Obstructed: true }
        && converted.Frames[1] is { DetectionRequested: false, AnalysisWallMs: null, AnalysisUncertain: null }
        && converted.Frames[2] is { DetectionRequested: true, AnalysisWallMs: 36.0, LayoutUncertain: true }
        && converted.Detections[0] is { SampleId: "start", Uncertain: true, Balls.Length: 1 }
        && converted.Detections[0].O08Replay is { AnalysisUncertain: false, LayoutUncertain: false, Obstructed: true, SplitLockSha256: var recordedLock } && recordedLock == splitLockSha
        && converted.Detections[1] is { SampleId: "after", Uncertain: true, Balls.Length: 1 }
        && converted.Detections[1].O08Replay is { AnalysisUncertain: false, LayoutUncertain: true, Obstructed: false },
        "Bound O08 export preserves unreviewed status, binds split/config/timing provenance, maps requested observations around skips, and treats layout/obstruction uncertainty conservatively");
    var exportDirectory = Path.Combine(root, "exported-window"); Directory.CreateDirectory(exportDirectory);
    var primaryFrames = converted.Frames.Take(2).ToImmutableArray();
    var primaryDetections = converted.Detections.Take(1).Select(d => d with
        { O08Replay = d.O08Replay! with { BatchSize = 2 } }).ToImmutableArray();
    var primaryDetectionBytes = System.Text.Encoding.UTF8.GetBytes(CorpusJson.Write(primaryDetections) + "\n");
    File.WriteAllBytes(Path.Combine(exportDirectory, "detections.json"), primaryDetectionBytes);
    var primaryArtifact = new O08ExportArtifactManifest(2, converted.CorpusId, converted.ManifestSha256,
        converted.SplitLockSha256, converted.SequenceId, converted.Split, converted.LabelsReviewed,
        converted.ReplayManifestSha256, converted.ReplayReportSha256, converted.ApkSha256, converted.Mode,
        converted.DeviceSerial, 2, converted.IndependentFrames, converted.WarmupTreatment,
        converted.RecordedConfiguration, converted.ReplayConfiguration, converted.CaptureConfigurationExact,
        "detections.json", CorpusFiles.Hash(primaryDetectionBytes), primaryFrames,
        "Offline width-800 synthetic first batch.");
    File.WriteAllText(Path.Combine(exportDirectory, "manifest.json"), CorpusJson.Write(primaryArtifact) + "\n");
    var chunkDirectory = Path.Combine(root, "exported-window-chunk"); Directory.CreateDirectory(chunkDirectory);
    var chunkReplayManifestSha = new string('6', 64); var chunkReplayReportSha = new string('5', 64);
    var chunkDetection = converted.Detections[1] with { O08Replay = converted.Detections[1].O08Replay! with
        { ReplayManifestSha256 = chunkReplayManifestSha, ReplayReportSha256 = chunkReplayReportSha,
            DeviceSerial = "synthetic-device-chunk", BatchSize = 1 } };
    var chunkDetectionBytes = System.Text.Encoding.UTF8.GetBytes(CorpusJson.Write(new[] { chunkDetection }) + "\n");
    File.WriteAllBytes(Path.Combine(chunkDirectory, "detections.json"), chunkDetectionBytes);
    var chunkArtifact = new O08ExportArtifactManifest(2, converted.CorpusId, converted.ManifestSha256,
        converted.SplitLockSha256, converted.SequenceId, converted.Split, converted.LabelsReviewed,
        chunkReplayManifestSha, chunkReplayReportSha, converted.ApkSha256, converted.Mode,
        "synthetic-device-chunk", 1, converted.IndependentFrames, converted.WarmupTreatment,
        converted.RecordedConfiguration, converted.ReplayConfiguration, converted.CaptureConfigurationExact,
        "detections.json", CorpusFiles.Hash(chunkDetectionBytes), [converted.Frames[2]],
        "Offline width-800 synthetic second batch from the same sequence.");
    File.WriteAllText(Path.Combine(chunkDirectory, "manifest.json"), CorpusJson.Write(chunkArtifact) + "\n");
    string ImportedReceiptHash(IndexedFrame f) => CorpusFiles.Hash(File.ReadAllBytes(Path.Combine(root, "imported", "window2", f.SampleId + ".receipt.json")));
    var secondReplayManifestSha = new string('8', 64); var secondReplayReportSha = new string('9', 64); var secondApkSha = new string('7', 64);
    var secondExportFrames = secondCaptureFrames.Select((f, i) => new O08ExportFrame(f.SampleId, f.FrameId, f.SourceTimeUs!.Value,
        f.Scope, f.PixelSha256, ImportedReceiptHash(f), i != 1, i == 1 ? null : 30 + i,
        i == 1 ? null : false, i == 1 ? null : false, i == 1 ? null : false) { SequenceId = "window2" }).ToImmutableArray();
    var secondDetections = secondExportFrames.Where(f => f.DetectionRequested).Select(f => new StoredDetection(f.SampleId,
        f.PixelSha256, "table-2x1", "o08-777777777777", "synthetic merge test", false,
        [new("cue-" + f.FrameId, "cue", 1, .5, null)])
        { O08Replay = new O08ReplayProvenance(imported.Id, importedLock.ManifestSha256, splitLockSha, "window2", "Development",
            f.FrameId, f.SourceTimeUs, f.Scope, f.PixelSha256, f.ReceiptSha256, secondReplayManifestSha, secondReplayReportSha,
            secondApkSha, "screening", "test-device", 3, false, O08ReplayExport.WarmupTreatment,
            replayConfiguration, replayConfiguration,
            CorpusFiles.Hash(System.Text.Encoding.UTF8.GetBytes(replayConfiguration)),
            CorpusFiles.Hash(System.Text.Encoding.UTF8.GetBytes(replayConfiguration)), true, f.AnalysisWallMs!.Value,
            false, false, false) }).ToImmutableArray();
    var secondExportDirectory = Path.Combine(root, "exported-window2"); Directory.CreateDirectory(secondExportDirectory);
    var secondDetectionBytes = System.Text.Encoding.UTF8.GetBytes(CorpusJson.Write(secondDetections) + "\n");
    File.WriteAllBytes(Path.Combine(secondExportDirectory, "detections.json"), secondDetectionBytes);
    var secondArtifact = new O08ExportArtifactManifest(2, imported.Id, importedLock.ManifestSha256, splitLockSha,
        "window2", "Development", false, secondReplayManifestSha, secondReplayReportSha, secondApkSha, "screening",
        "test-device", 3, false, O08ReplayExport.WarmupTreatment, replayConfiguration, replayConfiguration, true,
        "detections.json", CorpusFiles.Hash(secondDetectionBytes), secondExportFrames, "Offline width-800 synthetic second-sequence export.");
    File.WriteAllText(Path.Combine(secondExportDirectory, "manifest.json"), CorpusJson.Write(secondArtifact) + "\n");
    File.WriteAllText(Path.Combine(exportDirectory, "manifest.json"), CorpusJson.Write(primaryArtifact with { SchemaVersion = 1 }) + "\n");
    Throws<InvalidDataException>(() => O08ReplayExport.Merge(imported, importedPath, importedLock, importedFiles,
        [exportDirectory], splitLockBytes));
    File.WriteAllText(Path.Combine(exportDirectory, "manifest.json"), CorpusJson.Write(primaryArtifact) + "\n");
    var merged = O08ReplayExport.Merge(imported, importedPath, importedLock, importedFiles,
        [exportDirectory, chunkDirectory, secondExportDirectory], splitLockBytes);
    var mergedDetectionBytes = System.Text.Encoding.UTF8.GetBytes(CorpusJson.Write(merged.Detections) + "\n");
    Check(merged.Manifest is { SequenceCount: 2, FrameCount: 6, RequestedCount: 4, SkippedCount: 2 }
        && merged.Detections.Length == 4 && merged.Manifest.DetectionSha256 == CorpusFiles.Hash(mergedDetectionBytes)
        && merged.Detections[^1].O08Replay?.Scope == secondScope,
        "O08 merge verifies source artifact hashes and combines distinct sequences with exact requested/skipped frame provenance");
    var archivedReceipt = Path.Combine(runRoot, "source-receipts", "start.json");
    File.AppendAllText(archivedReceipt, " ");
    Throws<InvalidDataException>(() => O08ReplayExport.Convert(imported, importedPath, importedLock, importedFiles, "window", runRoot, splitLockBytes));
    File.Copy(Path.Combine(captureFolder, "start.json"), archivedReceipt, true);
    var copiedReceipt = Path.Combine(root, "imported", "window", "start.receipt.json");
    File.AppendAllText(copiedReceipt, " ");
    Throws<InvalidDataException>(() => O08ReplayExport.Convert(imported, importedPath, importedLock, importedFiles, "window", runRoot, splitLockBytes));
    File.Copy(Path.Combine(captureFolder, "start.json"), copiedReceipt, true);
    File.AppendAllText(replayReportPath, " ");
    Throws<InvalidDataException>(() => O08ReplayExport.Convert(imported, importedPath, importedLock, importedFiles, "window", runRoot, splitLockBytes));
    File.WriteAllText(replayReportPath, CorpusJson.Write(replayReport));
    void RejectChangedReport(Action<JsonNode> change)
    {
        var changed = JsonNode.Parse(CorpusJson.Write(replayReport))!; change(changed);
        File.WriteAllText(replayReportPath, changed.ToJsonString());
        var changedManifest = JsonNode.Parse(CorpusJson.Write(replayManifest))!;
        changedManifest["reportSha256"] = CorpusFiles.Hash(File.ReadAllBytes(replayReportPath));
        File.WriteAllText(replayManifestPath, changedManifest.ToJsonString());
        Throws<InvalidDataException>(() => O08ReplayExport.Convert(imported, importedPath, importedLock, importedFiles, "window", runRoot, splitLockBytes));
        File.WriteAllText(replayReportPath, CorpusJson.Write(replayReport));
        File.WriteAllText(replayManifestPath, CorpusJson.Write(replayManifest));
    }
    RejectChangedReport(r => r["samples"]![0]!["sourceTimeUs"] = 1001L);
    RejectChangedReport(r => r["samples"]![0]!["detectionRequested"] = false);
    RejectChangedReport(r => r["fullAnalysis800"]!["observations"]![0]!["circleCount"] = 2);
    RejectChangedReport(r => r["recordedAnalyzerConfigurationIdentity"] = "different configuration");
    File.AppendAllText(Path.Combine(root, "imported", "window", "labels.json"), " ");
    Throws<InvalidDataException>(() => O08ReplayExport.Convert(imported, importedPath, importedLock, importedFiles, "window", runRoot, splitLockBytes));
    File.WriteAllText(Path.Combine(root, "imported", "window", "labels.json"), CorpusJson.Write(CorpusJson.Read<SequenceLabels>(Path.Combine(root, "imported", "window", "labels.json"))));
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
