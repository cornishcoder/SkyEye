// T2 `classify`: diagnostic. For manifest rows selected by --names, runs the detector, then
// re-classifies every published circle from the first-pass classifier pixels and prints the
// raw kind and quality next to the published kind. Uses the detector's internal diagnostic
// hooks (ObserveNextClassifierPixels, ClassifyDiagnosticCandidate). The re-classification
// reads the pixels before colour normalisation, so it can differ from the published label
// when gains were applied; the "published" column is the authority.
using System.Text.Json.Nodes;
using SkyEye.Core;
using SkyEye.Vision.Android;

static class ClassifyCommand
{
    public static int Run(Dictionary<string, string> o)
    {
        var names = o["names"].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        string root = o.GetValueOrDefault("root", ".");
        bool surplusOnly = !o.TryGetValue("surplus-only", out var s) || bool.Parse(s);
        var settings = new VisionSettings(Width: 800) { PackedColourBoundarySearchExtentFraction = .65, SurplusOnlyInventoryDemotion = surplusOnly };
        var manifest = JsonNode.Parse(File.ReadAllText(o["manifest"]))!;
        foreach (var row in manifest["frames"]!.AsArray())
        {
            var rowNames = (row!["frameIds"] as JsonArray ?? []).Select(x => x!.ToString())
                .Concat((row["names"] as JsonArray ?? []).Select(x => x!.ToString()));
            if (!rowNames.Any(names.Contains)) continue;
            if (row["role"]?.ToString() == "holdout-excluded" || string.Equals(row["split"]?.ToString(), "Holdout", StringComparison.OrdinalIgnoreCase)) continue;
            if (row["pixelPresent"]?.GetValue<bool>() != true || row["calibration"]?["corners"] is not JsonArray { Count: 4 }) continue;
            var corners = row["calibration"]!["corners"]!.AsArray().Select(c => new Point(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToArray();
            var analyzer = new VisionAnalyzer(settings);
            byte[]? hsv = null;
            analyzer.ObserveNextClassifierPixels(p => hsv = (byte[])p.Clone());
            var vision = analyzer.Analyze(File.ReadAllBytes(Path.Combine(root, row["path"]!.ToString())), new TableCalibration(corners));
            Console.WriteLine($"== {string.Join(",", rowNames.Distinct())}  {row["path"]}  normalized={vision.Normalized}");
            Console.WriteLine("published          raw kind   quality  pattern  table (x, y)       clusters H/S/V:count, most saturated first");
            foreach (var (circle, ball) in vision.Circles.Zip(vision.Balls))
            {
                var raw = analyzer.ClassifyDiagnosticCandidate(circle.Centre, circle.Radius, hsv!).Classification;
                string clusters = string.Join("  ", raw.Clusters.OrderByDescending(c => c.Centre.S)
                    .Select(c => $"{c.Centre.H:F0}/{c.Centre.S:F0}/{c.Centre.V:F0}:{c.Count}"));
                Console.WriteLine($"{ball.Id,-18} {raw.Kind,-10} {raw.Quality,7:F4}  {raw.Pattern,-7}  ({ball.Position.X:F3}, {ball.Position.Y:F3})  {clusters}");
            }
        }
        return 0;
    }
}
