// T2 `detect`: runs the linked detector over rows of the T1 corpus manifest
// (tests/fixtures/detection-corpus/manifest.json) and writes one JSONL line per frame in the
// shape tools/evidence/score_detections.py --detections reads ({"sha256", "balls":[[kind,x,y]]}),
// plus provenance fields the scorer ignores.
//
// Settings:
//   production (default)  ShellTable.cs: Width 800, PackedColourBoundarySearchExtentFraction .65
//   receipt               the row's receipt analyzerConfigurationIdentity.Settings (capture-time settings)
//   FILE.json             a VisionSettings JSON object, for ablations
// Rows with role holdout-excluded or split Holdout are never opened.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyEye.Core;
using SkyEye.Vision.Android;

static class DetectCommand
{
    static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public static int Run(Dictionary<string, string> o)
    {
        if (!o.TryGetValue("manifest", out var manifestPath) || !o.TryGetValue("output", out var outputPath))
        {
            Console.Error.WriteLine("detect needs --manifest and --output");
            return 2;
        }
        string root = o.GetValueOrDefault("root", ".");
        string settingsMode = o.GetValueOrDefault("settings", "production");
        var roles = o.GetValueOrDefault("roles", "positive,empty-control").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var names = o.TryGetValue("names", out var n) ? n.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet() : null;
        int limit = o.TryGetValue("limit", out var l) ? int.Parse(l) : int.MaxValue;
        if (o.TryGetValue("threads", out var threads)) Org.Opencv.Core.Core.NumThreads = int.Parse(threads);

        VisionSettings? fixedSettings = settingsMode switch
        {
            "production" => Production(),
            "receipt" => null,
            _ => JsonSerializer.Deserialize<VisionSettings>(File.ReadAllText(settingsMode))
                 ?? throw new InvalidDataException("Empty settings file: " + settingsMode),
        };
        var analyzers = new Dictionary<string, VisionAnalyzer>();
        VisionAnalyzer AnalyzerFor(VisionSettings s)
        {
            string key = JsonSerializer.Serialize(s);
            if (!analyzers.TryGetValue(key, out var a)) analyzers[key] = a = new VisionAnalyzer(s);
            return a;
        }

        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        var frames = manifest["frames"]!.AsArray();
        var skipped = new Dictionary<string, int>();
        void Skip(string why) => skipped[why] = skipped.GetValueOrDefault(why) + 1;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var output = new StreamWriter(outputPath);
        int written = 0, failed = 0;
        var watch = Stopwatch.StartNew();
        Console.WriteLine($"detect: manifest {manifestPath}, settings {settingsMode}, roles {string.Join(",", roles)}, OpenCV {Org.Opencv.Core.Core.Version}");
        if (fixedSettings is not null) Console.WriteLine("configuration " + AnalyzerFor(fixedSettings).ConfigurationIdentity);

        foreach (var row in frames)
        {
            if (written + failed >= limit) break;
            string role = Str(row, "role") ?? "";
            string split = Str(row, "split") ?? "";
            if (role == "holdout-excluded" || split.Equals("Holdout", StringComparison.OrdinalIgnoreCase)) { Skip("holdout (never opened)"); continue; }
            if (!roles.Contains(role)) { Skip("role not selected"); continue; }
            if (names is not null && !RowNames(row).Any(names.Contains)) { Skip("name not selected"); continue; }
            if (row!["pixelPresent"]?.GetValue<bool>() != true) { Skip("pixel file not present"); continue; }
            var cornerArray = row["calibration"]?["corners"] as JsonArray;
            if (cornerArray is null || cornerArray.Count != 4) { Skip("no calibration corners"); continue; }

            string sha256 = Str(row, "sha256")!;
            string path = Path.Combine(root, Str(row, "path")!);
            var result = new JsonObject
            {
                ["sha256"] = sha256, ["sha1"] = Str(row, "sha1"), ["path"] = Str(row, "path"), ["role"] = role, ["split"] = split,
                ["names"] = new JsonArray(RowNames(row).Select(x => (JsonNode)x).ToArray()),
            };
            try
            {
                byte[] image = File.ReadAllBytes(path);
                string actual = Convert.ToHexStringLower(SHA256.HashData(image));
                if (actual != sha256) throw new InvalidDataException($"pixel SHA-256 {actual} does not match manifest");

                var corners = cornerArray.Select(c => new Point(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToArray();
                string? receiptIdentity = ReceiptIdentity(root, row);
                VisionSettings settings = fixedSettings ?? ReceiptSettings(receiptIdentity)
                    ?? throw new InvalidDataException("receipt has no analyzerConfigurationIdentity");
                var analyzer = AnalyzerFor(settings);
                var vision = analyzer.Analyze(image, new TableCalibration(corners));

                result["balls"] = new JsonArray(vision.Balls.Select(b => (JsonNode)new JsonArray(
                    b.Kind.ToString().ToLowerInvariant(), Math.Round(b.Position.X, 4), Math.Round(b.Position.Y, 4))).ToArray());
                result["uncertain"] = vision.Uncertain;
                result["layoutUncertain"] = vision.HasUncertainLayout;
                result["warnings"] = new JsonArray(vision.Warnings.Select(w => (JsonNode)w).ToArray());
                result["runtime"] = vision.Runtime;
                result["elapsedMs"] = Math.Round(vision.ElapsedMs, 1);
                result["configurationIdentity"] = analyzer.ConfigurationIdentity;
                result["receiptConfigurationIdentity"] = receiptIdentity;
                result["algorithmMatchesReceipt"] = receiptIdentity is null ? null : Algorithm(receiptIdentity) == Algorithm(analyzer.ConfigurationIdentity);
                result["settingsMatchReceipt"] = receiptIdentity is null ? null : JsonNode.DeepEquals(JsonNode.Parse(receiptIdentity), JsonNode.Parse(analyzer.ConfigurationIdentity));
                written++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or OpenCvSharp.OpenCVException)
            {
                result["error"] = ex.GetType().Name + ": " + ex.Message;
                failed++;
            }
            output.WriteLine(result.ToJsonString(Compact));
            if ((written + failed) % 25 == 0) Console.WriteLine($"  {written + failed} frames, {watch.Elapsed.TotalSeconds:F0} s");
        }

        Console.WriteLine($"detect: wrote {written} frames, {failed} failed (error field set), {watch.Elapsed.TotalSeconds:F0} s -> {outputPath}");
        foreach (var (why, count) in skipped.OrderBy(k => k.Key)) Console.WriteLine($"  skipped {count}: {why}");
        return failed == 0 ? 0 : 1;
    }

    static VisionSettings Production() => new(Width: 800) { PackedColourBoundarySearchExtentFraction = .65 };

    static VisionSettings? ReceiptSettings(string? identity)
    {
        if (identity is null) return null;
        var settings = JsonNode.Parse(identity)?["Settings"];
        return settings is null ? null : settings.Deserialize<VisionSettings>();
    }

    static string? ReceiptIdentity(string root, JsonNode row)
    {
        string? receipt = Str(row["calibration"], "receipt") ?? Str(row["storedResult"], "receipt");
        if (receipt is null) return null;
        string path = Path.Combine(root, receipt);
        if (!File.Exists(path)) return null;
        var node = JsonNode.Parse(File.ReadAllText(path))?["analyzerConfigurationIdentity"];
        return node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node?.ToJsonString();
    }

    static string? Algorithm(string identity) => JsonNode.Parse(identity)?["Algorithm"]?.GetValue<string>();

    static IEnumerable<string> RowNames(JsonNode? row) =>
        (row?["names"] as JsonArray ?? []).Select(x => x!.ToString())
        .Concat((row?["frameIds"] as JsonArray ?? []).Select(x => x!.ToString()));

    static string? Str(JsonNode? node, string key) => node?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
