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
    var audit = CorpusValidation.Validate(manifest, files);
    Check(!audit.AccuracyGateReady && audit.RealShotSequences == 0, "Synthetic contracts cannot satisfy real accuracy coverage");
    var report = FrameReplay.Run(sequence, files);
    Check(report.Processed == 4 && report.Rejected == 3 && report.ScopeResets == 1 && report.GapResets == 1, "Drops/gaps/resets have exact accounting");
    Check(report.Receipts[^1].Status == ReplayStatus.PixelIdMismatch, "Payload frame identity must match the indexed receipt");
    Check(CorpusJson.Write(report) == CorpusJson.Write(FrameReplay.Run(sequence, files)), "Fixed inputs/config reproduce byte-identical reports");
    Throws<InvalidDataException>(() => files.Resolve("../outside"));
    var real = sequence with { Kind = CorpusKind.Real, Purpose = SequencePurpose.Shot, Split = CorpusSplit.Holdout };
    Throws<InvalidDataException>(() => CorpusValidation.Validate(manifest with { Sequences = [sequence, real with { Id = "held" }] }, files));
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
