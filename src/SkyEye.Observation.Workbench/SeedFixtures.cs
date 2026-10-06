using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Observation;

static class SeedFixtures
{
    public static void Write(string root, string destination)
    {
        var files = new CorpusFiles(root);
        var directory = files.Resolve(destination);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Seed destination contains files; choose an empty corpus version.");
        Directory.CreateDirectory(directory);
        void Json<T>(string name, T value) => File.WriteAllText(Path.Combine(directory, name), CorpusJson.Write(value) + "\n");
        void Index(string name, IEnumerable<IndexedFrame> frames) => File.WriteAllText(Path.Combine(directory, name),
            string.Join("\n", frames.Select(f => JsonSerializer.Serialize(f, new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false }))) + "\n");
        string PathOf(string file) => destination.TrimEnd('/','\\') + "/" + file;
        var image = File.ReadAllBytes(files.Resolve("poolgame/pixel-table.jpg"));
        var hash = CorpusFiles.Hash(image);
        using var manual = JsonDocument.Parse(File.ReadAllBytes(files.Resolve("poolgame/pixel-table.json")));
        var truth = manual.RootElement;
        var calibration = new TableCalibration(truth.GetProperty("calibration").EnumerateArray().Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
        int width = truth.GetProperty("width").GetInt32(), height = truth.GetProperty("height").GetInt32();
        var labelBalls = truth.GetProperty("expected").EnumerateArray().Select((b,i) =>
        {
            var p = b.GetProperty("image"); var table = calibration.Map(new(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()));
            var colour = b.GetProperty("kind").GetString()!;
            return new BallLabel(colour + "-truth-" + i, colour, table.X, table.Y, LabelVisibility.Visible);
        }).ToImmutableArray();
        var realFrame = new IndexedFrame("pixel-table-regression", 0, null, new("pixel-10", "unknown-still-time", 1, "manual-pixel-table-2026-10-03"),
            "poolgame/pixel-table.jpg", hash, PixelEncoding.Jpeg, width, height);
        Index("still.jsonl", [realFrame]);
        Json("still-labels.json", new SequenceLabels(1, "pixel-table-still", "Original human fixture labels; workbench table transform", realFrame.Scope.Calibration, true,
            [new(realFrame.SampleId, hash, "table-2x1", labelBalls, "Derived by the existing calibration from human image centres; a tuned still, not held-out shot truth.")], []));
        var storedPath = files.Resolve("poolgame/device-vision-reference.json");
        using var stored = JsonDocument.Parse(File.ReadAllBytes(storedPath));
        var detected = stored.RootElement.GetProperty("analyzed");
        var detectedBalls = detected.GetProperty("Balls").EnumerateArray().Select(b => new DetectedBall(b.GetProperty("Id").GetString()!, b.GetProperty("Kind").GetString()!,
            b.GetProperty("Position").GetProperty("X").GetDouble(), b.GetProperty("Position").GetProperty("Y").GetDouble(), b.GetProperty("Confidence").GetDouble())).ToImmutableArray();
        Json("stored-detections.json", new[] { new StoredDetection(realFrame.SampleId, hash, "table-2x1", "Archived C# OpenCV " + detected.GetProperty("Runtime").GetString(),
            "Repo-associated initial pixel-table device result; source report SHA256 " + CorpusFiles.Hash(File.ReadAllBytes(storedPath)), detected.GetProperty("Uncertain").GetBoolean(), detectedBalls) });

        var scope = new FrameScope("synthetic-camera", "timeline-one", 1, "synthetic-cal-v1");
        var frames = new List<IndexedFrame>();
        void Frame(string sample, long id, long pts, FrameScope frameScope, long? embedded = null)
        {
            var pixels = new byte[64]; BinaryPrimitives.WriteInt64LittleEndian(pixels, embedded ?? id);
            string file = sample + ".gray"; File.WriteAllBytes(Path.Combine(directory,file), pixels);
            frames.Add(new("contract-" + sample, id, pts, frameScope, PathOf(file), CorpusFiles.Hash(pixels), PixelEncoding.Gray8, 8, 8, true));
        }
        Frame("start",1,1000,scope); Frame("next",2,34000,scope); Frame("duplicate",2,34000,scope);
        Frame("backward",0,2000,scope); Frame("gap",8,1000000,scope);
        Frame("reset",1,0,scope with { Timeline = "timeline-two", Generation = 2 });
        Frame("bad-pair",2,10000,scope with { Timeline = "timeline-two", Generation = 2 },99);
        Index("contract.jsonl",frames);
        Json("contract-labels.json", new SequenceLabels(1,"receipt-contracts","Synthetic generator v1",scope.Calibration,false,[],[]));
        var realSource = new MediaSource("pixel-10", "2026-10-03 mounted Pixel camera still; exact source PTS unavailable",realFrame.PixelFile,hash,width,height,SourceClock.Unknown,realFrame.Scope.Calibration);
        var syntheticSource = new MediaSource("synthetic-camera","Generated receipt-contract pixels; no physical shot",frames[0].PixelFile,frames[0].PixelSha256,8,8,SourceClock.SyntheticMicroseconds,scope.Calibration,30);
        var manifest = new CorpusManifest(1,"skyeye-observation-seed-v1",
            [new("pixel-table-still","pixel-layout-tuning-2026-10-03",CorpusKind.Real,CorpusSplit.Regression,SequencePurpose.StillRegression,realSource,
                PathOf("still.jsonl"),PathOf("still-labels.json"),"Existing tuned fixture. No exact source timestamp or temporal ground truth.",["tuning","still","not-heldout"]),
             new("receipt-contracts","synthetic-contract-v1",CorpusKind.Synthetic,CorpusSplit.Regression,SequencePurpose.Contract,syntheticSource,
                PathOf("contract.jsonl"),PathOf("contract-labels.json"),"Injected duplicate/backward/gap/reset/mismatched pairing; no tracking accuracy evidence.",["synthetic","contracts"])]);
        Json("manifest.json",manifest);
        CorpusValidation.Validate(manifest, files);
        Console.WriteLine("Seeded " + directory);
    }
}
