using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Observation;

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
    // A nearest-first greedy match loses B: A must be reassigned to the alternative.
    var crowdedTruth = truth with { Balls = [new("A", "red", .20, .3, LabelVisibility.Visible), new("B", "yellow", .24, .3, LabelVisibility.Visible)] };
    var crowdedDetection = detection with { Balls = [new("shared", "yellow", .22, .3, null), new("alternative", "red", .17, .3, null)] };
    var crowded = DetectorBaseline.Score(crowdedTruth, crowdedDetection, .035);
    Check(crowded.SpatialTruePositives == 2 && crowded.FalsePositives == 0 && crowded.MissedVisible == 0,
        "Maximum association avoids the greedy crowded-ball miss");
    Check(CorpusJson.Write(crowded) == CorpusJson.Write(DetectorBaseline.Score(crowdedTruth,
        crowdedDetection with { Balls = crowdedDetection.Balls.Reverse().ToImmutableArray() }, .035)),
        "Stored detection order cannot change association or metrics");
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
