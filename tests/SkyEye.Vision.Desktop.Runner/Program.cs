// T2 desktop detector runner (docs/claude/t2-desktop-runner-design.md).
//
//   parity --image F.png --receipt F.json --proof o08-replay-proof-*.json [--threads 4] [--out result.json]
//
// Runs one frame through the linked detector with the settings the device replay used
// (width 800, packed-colour boundary extent .65, boundary and candidate diagnostics on)
// and compares the circles with samples[0].width800.analysis in the device proof.
// Pass: same circle count, same kinds, one-to-one centres within 1 px at width 800,
// and the same uncertain, layoutUncertain and packedClusterFallback.recovered.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;

var options = ParseOptions(args);
if (args.Length > 0 && args[0] == "parity") return Parity(options);
if (args.Length > 0 && args[0] == "detect") return DetectCommand.Run(options);
if (args.Length > 0 && args[0] == "classify") return ClassifyCommand.Run(options);
if (args.Length > 0 && args[0] == "settled-change") return SettledChangeCommand.Run(options);
if (args.Length > 0 && args[0] == "settled-change-clips") return SettledChangeClipsCommand.Run(options);
if (args.Length > 0 && args[0] == "settled-change-selfcheck") return SettledChangeSelfCheck.Run(options);
if (args.Length > 0 && args[0] == "analyze") return AnalyzeCommand.Run(options);
if (args.Length > 0 && args[0] == "audit")
    return AuditCommand.Run(options, args.Select((a, i) => (a, i)).Where(x => x.a == "--at" && x.i + 1 < args.Length).Select(x => args[x.i + 1]).ToList());
Console.Error.WriteLine("""
    usage:
      parity --image F.png --receipt F.json --proof PROOF.json [--threads N] [--out FILE.json]
      detect --manifest M.json --output OUT.jsonl [--root DIR] [--settings production|receipt|FILE.json]
             [--roles positive,empty-control] [--names 2083,957] [--threads N] [--limit N]
    """);
return 2;

static int Parity(Dictionary<string, string> o)
{
    const int width = 800;
    const double onePixel = 1.0 / width;
    byte[] image = File.ReadAllBytes(o["image"]);
    var proof = JsonNode.Parse(File.ReadAllText(o["proof"]))!;
    var receipt = JsonNode.Parse(File.ReadAllText(o["receipt"]))!;
    var sample = proof["samples"]![0]!;
    string pixelSha = Convert.ToHexStringLower(SHA256.HashData(image));
    string expectedSha = sample["pixelSha256"]!.GetValue<string>();
    Console.WriteLine($"frame {sample["frameId"]}  pixel sha256 {(pixelSha == expectedSha ? "matches proof" : $"DIFFERS: {pixelSha} vs {expectedSha}")}");
    if (pixelSha != expectedSha) return 1;

    var corners = receipt["calibrationCorners"]!.AsArray()
        .Select(p => new Point(p!["x"]!.GetValue<double>(), p["y"]!.GetValue<double>())).ToArray();
    var calibration = new TableCalibration(corners);
    double extent = proof["production800BoundaryExtent"]!.GetValue<double>();
    var settings = new VisionSettings(Width: width, PocketExclusionRadius: proof["pocketExclusionRadius"]!.GetValue<int>())
    {
        LocalIlluminationNormalization = proof["localIlluminationNormalization"]!.GetValue<bool>(),
        VerificationBodyRefinement = proof["bodyRefinement"]!.GetValue<bool>(),
        DarkBodyValueCeiling = proof["darkBodyValueCeiling"]!.GetValue<int>(),
        PackedColourBoundarySearchExtentFraction = extent,
        // Reproduce the proof's algorithm: proofs made before T5 used whole-colour demotion.
        SurplusOnlyInventoryDemotion = ProofAlgorithm(proof).Contains("-surplus-", StringComparison.Ordinal),
        PackedCueHandover = ProofAlgorithm(proof).Contains("-cuehandover", StringComparison.Ordinal),
        ObstructionShadowTolerance = ProofAlgorithm(proof).Contains("-obstruction-shadow", StringComparison.Ordinal),
        PackedClusterLocalUnknowns = ProofAlgorithm(proof).Contains("-packed-local-unknown", StringComparison.Ordinal),
        ObstructionDimChromaTolerance = ProofAlgorithm(proof).Contains("-obstruction-chroma",StringComparison.Ordinal),
    };
    bool audit = proof["boundaryAudit"]!.GetValue<bool>();
    var analyzer = new VisionAnalyzer(settings, includeBoundaryDiagnostics: audit, includeCandidateDiagnostics: audit);

    string desktopIdentity = analyzer.ConfigurationIdentity;
    string deviceIdentity = proof["replayAnalyzerConfigurationIdentity"]!.GetValue<string>();
    bool identityMatches = JsonNode.DeepEquals(JsonNode.Parse(desktopIdentity), JsonNode.Parse(deviceIdentity));
    Console.WriteLine($"configuration identity {(identityMatches ? "matches device replay" : "DIFFERS")}");
    if (!identityMatches) { Console.WriteLine("  desktop: " + desktopIdentity); Console.WriteLine("  device:  " + deviceIdentity); }

    if (o.TryGetValue("threads", out var threads)) Org.Opencv.Core.Core.NumThreads = int.Parse(threads);
    var first = analyzer.Analyze(image, calibration);
    var second = analyzer.Analyze(image, calibration);
    var desktop = Summarize(first, width);
    bool deterministic = JsonSerializer.Serialize(desktop) == JsonSerializer.Serialize(Summarize(second, width));
    Console.WriteLine($"desktop runtime OpenCV {first.Runtime}, threads {Org.Opencv.Core.Core.NumThreads}, {first.ElapsedMs:F0} ms; repeat run identical: {deterministic}");

    var device = sample["width800"]!["analysis"]!;
    var deviceCircles = device["circles"]!.AsArray().Select(c => new Circle(
        c!["ballId"]!.GetValue<string>(), c["kind"]!.GetValue<string>(),
        c["x"]!.GetValue<double>(), c["y"]!.GetValue<double>(), c["radius"]!.GetValue<double>())).ToList();

    // One-to-one matching, nearest pairs first.
    var pairs = (from d in deviceCircles from k in desktop.Circles
                 select (d, k, distance: Math.Sqrt(Math.Pow(d.X - k.X, 2) + Math.Pow(d.Y - k.Y, 2))))
                .OrderBy(p => p.distance).ToList();
    var matched = new List<(Circle Device, Circle Desktop, double Distance)>();
    var usedDevice = new HashSet<Circle>(); var usedDesktop = new HashSet<Circle>();
    foreach (var p in pairs)
    {
        if (usedDevice.Contains(p.d) || usedDesktop.Contains(p.k)) continue;
        usedDevice.Add(p.d); usedDesktop.Add(p.k); matched.Add((p.d, p.k, p.distance));
    }
    var deviceOnly = deviceCircles.Where(c => !usedDevice.Contains(c)).ToList();
    var desktopOnly = desktop.Circles.Where(c => !usedDesktop.Contains(c)).ToList();

    Console.WriteLine();
    Console.WriteLine("device id      desktop id     kind (dev/desk)    dx px   dy px   dist px  dr px");
    foreach (var m in matched.OrderBy(m => m.Device.X))
        Console.WriteLine($"{m.Device.Id,-14} {m.Desktop.Id,-14} {m.Device.Kind + "/" + m.Desktop.Kind,-18} {(m.Desktop.X - m.Device.X) / onePixel,6:F2}  {(m.Desktop.Y - m.Device.Y) / onePixel,6:F2}  {m.Distance / onePixel,7:F2}  {(m.Desktop.Radius - m.Device.Radius) / onePixel,5:F2}");
    foreach (var c in deviceOnly) Console.WriteLine($"device only:  {c.Id} {c.Kind} ({c.X:F4}, {c.Y:F4})");
    foreach (var c in desktopOnly) Console.WriteLine($"desktop only: {c.Id} {c.Kind} ({c.X:F4}, {c.Y:F4})");

    bool deviceUncertain = device["uncertain"]!.GetValue<bool>();
    bool deviceLayout = device["layoutUncertain"]!.GetValue<bool>();
    bool? deviceRecovered = device["packedClusterFallback"]?["recovered"]?.GetValue<bool>();
    bool? desktopRecovered = first.PackedClusterFallback?.Recovered;
    string deviceCounts = string.Join(" ", (device["counts"] as JsonObject ?? []).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
    string desktopCounts = string.Join(" ", first.Balls.GroupBy(b => b.Kind.ToString().ToLowerInvariant()).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}={g.Count()}"));
    int kindMismatches = matched.Count(m => m.Device.Kind != m.Desktop.Kind);
    double maxDistancePx = matched.Count == 0 ? 0 : matched.Max(m => m.Distance) / onePixel;
    double maxRadiusPx = matched.Count == 0 ? 0 : matched.Max(m => Math.Abs(m.Desktop.Radius - m.Device.Radius)) / onePixel;
    var checks = new (string Name, bool Pass, string Detail)[]
    {
        ("circle count", deviceCircles.Count == desktop.Circles.Count, $"device {deviceCircles.Count}, desktop {desktop.Circles.Count}"),
        ("one-to-one", deviceOnly.Count == 0 && desktopOnly.Count == 0, $"device-only {deviceOnly.Count}, desktop-only {desktopOnly.Count}"),
        ("kinds", kindMismatches == 0, $"{kindMismatches} mismatched"),
        ("centres within 1 px", maxDistancePx <= 1.0, $"max {maxDistancePx:F3} px"),
        ("uncertain", deviceUncertain == first.Uncertain, $"device {deviceUncertain}, desktop {first.Uncertain}"),
        ("layoutUncertain", deviceLayout == first.HasUncertainLayout, $"device {deviceLayout}, desktop {first.HasUncertainLayout}"),
        ("packedClusterFallback.recovered", deviceRecovered == desktopRecovered, $"device {Show(deviceRecovered)}, desktop {Show(desktopRecovered)}"),
        ("final ball kinds (after inventory constraint)", deviceCounts == desktopCounts, $"device {deviceCounts}, desktop {desktopCounts}"),
    };
    Console.WriteLine();
    foreach (var c in checks) Console.WriteLine($"{(c.Pass ? "PASS" : "FAIL")}  {c.Name}: {c.Detail}");
    Console.WriteLine($"info  radius delta: max {maxRadiusPx:F3} px (reported separately, not gated)");
    Console.WriteLine($"info  configuration identity matches: {identityMatches}; repeat run identical: {deterministic}");
    bool pass = checks.All(c => c.Pass);
    Console.WriteLine(pass ? "RESULT: PARITY PASS" : "RESULT: PARITY FAIL");

    if (o.TryGetValue("out", out var outPath))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(new
        {
            frameId = sample["frameId"]!.GetValue<long>(), pixelSha256 = pixelSha, runtime = first.Runtime,
            configurationIdentity = desktopIdentity, identityMatches, deterministic, pass,
            checks = checks.Select(c => new { c.Name, c.Pass, c.Detail }),
            maxCentreDeltaPx = maxDistancePx, maxRadiusDeltaPx = maxRadiusPx,
            desktop = new { first.Uncertain, layoutUncertain = first.HasUncertainLayout, first.Warnings, desktop.Circles },
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    return pass ? 0 : 1;
}

static string Show(bool? value) => value?.ToString() ?? "none";

static string ProofAlgorithm(JsonNode proof) =>
    JsonNode.Parse(proof["replayAnalyzerConfigurationIdentity"]!.GetValue<string>())!["Algorithm"]!.GetValue<string>();

// Same projection as VerificationReplayInstrumentation.Summarize: table-2x1 from the portrait warp.
static Summary Summarize(VisionResult vision, int width) => new(vision.Circles.Select(c => new Circle(
    c.BallId, Kind(c.BallId), c.Centre.Y / width, 1 - c.Centre.X / width, c.Radius / width)).ToList());

static string Kind(string id) { int separator = id.IndexOf('-'); return separator > 0 ? id[..separator] : id; }

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>();
    for (int i = 1; i + 1 < args.Length; i += 2)
        if (args[i].StartsWith("--")) result[args[i][2..]] = args[i + 1];
    return result;
}

sealed record Circle(string Id, string Kind, double X, double Y, double Radius);
sealed record Summary(List<Circle> Circles);
