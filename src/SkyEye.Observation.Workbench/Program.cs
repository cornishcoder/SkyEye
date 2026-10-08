using SkyEye.Observation;
using System.Collections.Immutable;
using System.Security.Cryptography;

try
{
    if (args.Length == 3 && args[0] == "seed") { SeedFixtures.Write(args[1], args[2]); return; }
    if (args.Length == 3 && args[0] == "import-verification") { Console.WriteLine(VerificationCaptureImport.Import(args[1], args[2])); return; }
    if (args.Length == 4 && args[0] == "import-diagnostic-labels")
    { Console.WriteLine(DiagnosticLabelCaptureImport.Import(args[1], args[2], args[3])); return; }
    if (args.Length == 4 && args[0] == "import-diagnostic-shots")
    { Console.WriteLine(DiagnosticLabelCaptureImport.ImportAssociationSequences(args[1], args[2], args[3])); return; }
    if (args.Length == 0 || args[0] is not ("validate" or "replay" or "freeze" or "baseline" or "verification" or "compare" or "export-yolo" or "association"))
        throw new ArgumentException("Usage: validate|replay|freeze|baseline|verification|compare|export-yolo|association --root DIR --manifest FILE [--output FILE] [--output-dir DIR] [--lock FILE] [--detections FILE] [--requests FILE] [--current FILE --learned FILE --hybrid FILE --split Development|Holdout] [--tolerance NUMBER] [association: --candidate-name NAME --max-balls INTEGER --max-speed NUMBER --association-slack NUMBER --move-start NUMBER --move-stop NUMBER --truth-move-start NUMBER --truth-move-stop NUMBER --max-gap-ms INTEGER --ambiguity-margin NUMBER --colour-penalty NUMBER [--viewer-sequence ID --html-output FILE]] | import-diagnostic-labels CAPTURE_ROOT OUTPUT_ROOT ASSIGNMENTS.json | import-diagnostic-shots CAPTURE_ROOT OUTPUT_ROOT ASSIGNMENTS.json");
    var options = new Dictionary<string, string>();
    for (int i = 1; i < args.Length; i += 2)
        if (i + 1 == args.Length || args[i] is not ("--root" or "--manifest" or "--output" or "--lock" or "--detections" or "--requests" or "--tolerance"
            or "--current" or "--learned" or "--hybrid" or "--split" or "--output-dir" or "--candidate-name" or "--max-speed"
            or "--association-slack" or "--move-start" or "--move-stop" or "--truth-move-start" or "--truth-move-stop" or "--max-balls"
            or "--max-gap-ms" or "--ambiguity-margin" or "--colour-penalty" or "--viewer-sequence" or "--html-output")
            || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown, duplicate or incomplete option.");
    if (args[0] != "association" && (options.ContainsKey("--viewer-sequence") || options.ContainsKey("--html-output")))
        throw new ArgumentException("--viewer-sequence and --html-output are available only for O04 association.");
    string Required(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException("Missing " + name);
    var manifestPath = Path.GetFullPath(Required("--manifest"));
    var files = new CorpusFiles(Required("--root"));
    var manifest = CorpusJson.Read<CorpusManifest>(manifestPath);
    var audit = CorpusValidation.Validate(manifest, files);
    var frozen = SplitLock.Freeze(manifest, files, manifestPath);
    if ((args[0] is "compare" or "export-yolo" or "association") && !options.ContainsKey("--lock"))
        throw new ArgumentException("O09/O04 comparison requires --lock to freeze the session split and labels.");
    if (options.TryGetValue("--lock", out var lockPath)) SplitLock.Verify(CorpusJson.Read<FrozenCorpus>(lockPath), frozen);
    string? htmlOutputPath = null;
    string? htmlOutput = null;
    object result = args[0] switch
    {
        "validate" => audit,
        "freeze" => frozen,
        "replay" => new { schemaVersion = 1, corpus = manifest.Id, frozen.ManifestSha256, audit,
            sequences = manifest.Sequences.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => FrameReplay.Run(s, files)).ToArray() },
        "baseline" => Baseline(),
        "verification" => new { schemaVersion = 1, corpus = manifest.Id, frozen.ManifestSha256,
            requestSha256 = CorpusFiles.Hash(File.ReadAllBytes(Required("--requests"))),
            provenance = "Recorded request scoring; offline timing is not live acceptance. Preserve per-session and per-stratum denominators.",
            windows = VerificationEvaluation.Score(manifest, files, CorpusJson.Read<VerificationWindow[]>(Required("--requests"))) },
        "compare" => Compare(),
        "export-yolo" => ExportYolo(),
        "association" => Association(),
        _ => throw new ArgumentException("Unknown command")
    };
    object ExportYolo()
    {
        var outputDirectory = Path.GetFullPath(Required("--output-dir"));
        var report = O09YoloDatasetExport.Export(manifest, frozen, files, outputDirectory);
        return new { directory = outputDirectory, report };
    }
    object Compare()
    {
        var split = Required("--split").ToLowerInvariant() switch
        {
            "development" => CorpusSplit.Development,
            "holdout" => CorpusSplit.Holdout,
            _ => throw new ArgumentException("O09 --split must be Development or Holdout.")
        };
        if (!options.ContainsKey("--tolerance")) throw new ArgumentException("O09 comparison requires a precommitted --tolerance appropriate to the frozen coordinate space.");
        double tolerance = double.Parse(Required("--tolerance"), System.Globalization.CultureInfo.InvariantCulture);
        O09CandidateInput Candidate(string name, string key)
        {
            var path = Path.GetFullPath(Required(key));
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 256L * 1024 * 1024) throw new InvalidDataException("Candidate output is missing or exceeds the 256 MiB read bound: " + key);
            var bytes = File.ReadAllBytes(path);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var detections = (System.Text.Json.JsonSerializer.Deserialize<StoredDetection[]>(bytes, CorpusJson.Options)
                ?? throw new InvalidDataException("Candidate output is empty: " + key)).ToImmutableArray();
            return new(name, sha, detections);
        }
        return O09Comparison.Compare(manifest, frozen, files, split,
            [Candidate("current", "--current"), Candidate("learned", "--learned"), Candidate("hybrid", "--hybrid")], tolerance);
    }
    object Association()
    {
        var split = Required("--split").ToLowerInvariant() switch
        {
            "development" => CorpusSplit.Development,
            "holdout" => CorpusSplit.Holdout,
            _ => throw new ArgumentException("O04 association --split must be Development or Holdout.")
        };
        string detectionPath = Path.GetFullPath(Required("--detections"));
        var info = new FileInfo(detectionPath);
        if (!info.Exists || info.Length > 256L * 1024 * 1024)
            throw new InvalidDataException("O04 candidate output is missing or exceeds the 256 MiB read bound.");
        var bytes = File.ReadAllBytes(detectionPath);
        var candidate = System.Text.Json.JsonSerializer.Deserialize<StoredDetection[]>(bytes, CorpusJson.Options)
            ?? throw new InvalidDataException("O04 candidate output is empty.");
        double Number(string key) => double.Parse(Required(key), System.Globalization.CultureInfo.InvariantCulture);
        long gapMs = long.Parse(Required("--max-gap-ms"), System.Globalization.CultureInfo.InvariantCulture);
        if (gapMs is < 1 or > 15_000) throw new ArgumentException("O04 --max-gap-ms must be from 1 through 15000.");
        int maximumBalls = int.Parse(Required("--max-balls"), System.Globalization.CultureInfo.InvariantCulture);
        var tuning = new O04AssociationOptions(Number("--tolerance"), Number("--max-speed"),
            Number("--association-slack"), Number("--move-start"), Number("--move-stop"),
            Number("--truth-move-start"), Number("--truth-move-stop"), maximumBalls, checked(gapMs * 1000),
            Number("--ambiguity-margin"), Number("--colour-penalty"));
        string candidateName = options.GetValueOrDefault("--candidate-name")
            ?? candidate.Where(d => d is not null).Select(d => d.Model).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).FirstOrDefault()
            ?? "candidate";
        bool hasViewerSequence = options.TryGetValue("--viewer-sequence", out var viewerSequence);
        bool hasHtmlOutput = options.TryGetValue("--html-output", out var htmlPath);
        if (hasViewerSequence != hasHtmlOutput)
            throw new ArgumentException("O04 HTML output requires both --viewer-sequence and --html-output.");
        var report = O04AssociationReplay.Replay(manifest, frozen, files, split, candidate,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), candidateName, tuning,
            hasViewerSequence ? viewerSequence : null);
        if (hasHtmlOutput)
        {
            htmlOutputPath = Path.GetFullPath(htmlPath!);
            htmlOutput = O04AssociationHtml.Render(report, files);
        }
        return report;
    }
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
    var reportOutputPath = options.TryGetValue("--output", out var output)
        ? Path.GetFullPath(output) : null;
    if (reportOutputPath is not null && htmlOutputPath is not null
        && string.Equals(reportOutputPath, htmlOutputPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        throw new ArgumentException("The JSON report and HTML viewer must use different output files.");
    if (reportOutputPath is not null && File.Exists(reportOutputPath))
        throw new IOException("Report/lock output exists; choose a new path to preserve earlier evidence.");
    if (htmlOutputPath is not null && File.Exists(htmlOutputPath))
        throw new IOException("HTML viewer output exists; choose a new path to preserve earlier evidence.");
    if (reportOutputPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportOutputPath)!);
        File.WriteAllText(reportOutputPath, json + "\n");
        Console.WriteLine("Wrote " + reportOutputPath);
    }
    else Console.WriteLine(json);
    if (htmlOutputPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(htmlOutputPath)!);
        File.WriteAllText(htmlOutputPath, htmlOutput!, new System.Text.UTF8Encoding(false));
        Console.WriteLine("Wrote " + htmlOutputPath);
    }
}
catch (Exception e) when (e is ArgumentException or IOException or InvalidDataException or System.Text.Json.JsonException or FormatException or InvalidOperationException or OverflowException)
{
    Console.Error.WriteLine(e.Message); Environment.ExitCode = 1;
}
