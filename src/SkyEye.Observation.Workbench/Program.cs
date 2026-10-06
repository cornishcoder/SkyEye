using SkyEye.Observation;

try
{
    if (args.Length == 3 && args[0] == "seed") { SeedFixtures.Write(args[1], args[2]); return; }
    if (args.Length == 0 || args[0] is not ("validate" or "replay" or "freeze" or "baseline"))
        throw new ArgumentException("Usage: validate|replay|freeze|baseline --root DIR --manifest FILE [--output FILE] [--lock FILE] [--detections FILE] [--tolerance NUMBER]");
    var options = new Dictionary<string, string>();
    for (int i = 1; i < args.Length; i += 2)
        if (i + 1 == args.Length || args[i] is not ("--root" or "--manifest" or "--output" or "--lock" or "--detections" or "--tolerance")
            || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown, duplicate or incomplete option.");
    string Required(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException("Missing " + name);
    var manifestPath = Path.GetFullPath(Required("--manifest"));
    var files = new CorpusFiles(Required("--root"));
    var manifest = CorpusJson.Read<CorpusManifest>(manifestPath);
    var audit = CorpusValidation.Validate(manifest, files);
    var frozen = SplitLock.Freeze(manifest, files, manifestPath);
    if (options.TryGetValue("--lock", out var lockPath)) SplitLock.Verify(CorpusJson.Read<FrozenCorpus>(lockPath), frozen);
    object result = args[0] switch
    {
        "validate" => audit,
        "freeze" => frozen,
        "replay" => new { schemaVersion = 1, corpus = manifest.Id, frozen.ManifestSha256, audit,
            sequences = manifest.Sequences.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => FrameReplay.Run(s, files)).ToArray() },
        "baseline" => Baseline(),
        _ => throw new ArgumentException("Unknown command")
    };
    object Baseline()
    {
        var detections = CorpusJson.Read<StoredDetection[]>(Required("--detections"));
        double tolerance = double.Parse(options.GetValueOrDefault("--tolerance", "0.027"), System.Globalization.CultureInfo.InvariantCulture);
        var scored = new List<BaselineScore>();
        var unscored = new List<string>();
        foreach (var sequence in manifest.Sequences.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(sequence.LabelsFile));
            foreach (var frame in labels.Frames)
                if (detections.SingleOrDefault(d => d.SampleId == frame.SampleId) is { } detected) scored.Add(DetectorBaseline.Score(frame, detected, tolerance));
                else unscored.Add(frame.SampleId);
        }
        if (scored.Count != detections.Length) throw new InvalidDataException("Each stored detection must match one labelled indexed sample.");
        return new { schemaVersion = 1, corpus = manifest.Id, audit, tolerance, detectionSha256 = CorpusFiles.Hash(File.ReadAllBytes(Required("--detections"))),
            labelledFrames = scored.Count + unscored.Count, scoredFrames = scored.Count, unscoredSampleIds = unscored.ToArray(),
            provenance = "Scoring stored results, not re-running native CV or measuring held-out accuracy", frames = scored.ToArray() };
    }
    var json = CorpusJson.Write(result);
    if (options.TryGetValue("--output", out var output))
    {
        var path = Path.GetFullPath(output);
        if (File.Exists(path)) throw new IOException("Report/lock output exists; choose a new path to preserve earlier evidence.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json + "\n");
        Console.WriteLine("Wrote " + path);
    }
    else Console.WriteLine(json);
}
catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or System.Text.Json.JsonException or FormatException or InvalidOperationException)
{
    Console.Error.WriteLine(e.Message); Environment.ExitCode = 1;
}
