// T2 `audit`: diagnostic. Runs one manifest frame with candidate diagnostics and lists every
// candidate (Hough, packed-colour, dark-body, cue recovery, ...) whose proposal or fitted
// centre lies near a table point, with its stage and decision. Use it to find which stage
// drops a labelled ball.
//
//   audit --manifest M.json --names 382 --at 0.062,0.868 [--at 0.03,0.928] [--within 0.06] [--width 800]
//
// Table points are table-2x1 (x 0..2, y 0..1). Analysis pixels map to table as in the
// device harness: x = Y / width, y = 1 - X / width.
using System.Text.Json.Nodes;
using SkyEye.Core;
using SkyEye.Vision.Android;

static class AuditCommand
{
    public static int Run(Dictionary<string, string> o, List<string> points)
    {
        var names = o["names"].Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        double within = o.TryGetValue("within", out var w) ? double.Parse(w) : .06;
        int width = o.TryGetValue("width", out var wd) ? int.Parse(wd) : 800;
        var targets = points.Select(p => p.Split(',')).Select(p => new Point(double.Parse(p[0]), double.Parse(p[1]))).ToArray();
        var settings = new VisionSettings(Width: width) { PackedColourBoundarySearchExtentFraction = .65 };
        var manifest = JsonNode.Parse(File.ReadAllText(o["manifest"]))!;
        foreach (var row in manifest["frames"]!.AsArray())
        {
            var rowNames = (row!["frameIds"] as JsonArray ?? []).Select(x => x!.ToString())
                .Concat((row["names"] as JsonArray ?? []).Select(x => x!.ToString())).Distinct().ToArray();
            if (!rowNames.Any(names.Contains)) continue;
            if (row["role"]?.ToString() == "holdout-excluded" || string.Equals(row["split"]?.ToString(), "Holdout", StringComparison.OrdinalIgnoreCase)) continue;
            if (row["pixelPresent"]?.GetValue<bool>() != true || row["calibration"]?["corners"] is not JsonArray { Count: 4 } cornerArray) continue;
            var corners = cornerArray.Select(c => new Point(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToArray();
            var analyzer = new VisionAnalyzer(settings, includeBoundaryDiagnostics: true, includeCandidateDiagnostics: true);
            var vision = analyzer.Analyze(File.ReadAllBytes(Path.Combine(o.GetValueOrDefault("root", "."), row["path"]!.ToString())), new TableCalibration(corners));
            Console.WriteLine($"== {string.Join(",", rowNames)}  {row["path"]}  light {row["light"]?["stratum"]} V {row["light"]?["tableV"]}");
            Console.WriteLine($"   balls {vision.Balls.Length}, uncertain {vision.Uncertain}, layoutUncertain {vision.HasUncertainLayout}, normalized {vision.Normalized}, fallback {vision.PackedClusterFallback?.Recovered.ToString() ?? "none"}");
            Console.WriteLine($"   warnings: {string.Join(" | ", vision.Warnings)}");
            foreach (var target in targets)
            {
                Point ToTable(Point p, int wpx) => new(p.Y / wpx, 1 - p.X / wpx);
                var near = vision.CandidateAudits
                    .Select(a =>
                    {
                        int wpx = a.Pass.Contains("1000", StringComparison.Ordinal) ? 1000 : width;
                        var proposal = ToTable(a.ProposalCentre, wpx);
                        Point? fitted = a.FittedCentre is { } f ? ToTable(f, wpx) : null;
                        double d = Math.Min((proposal - target).Length, fitted is { } ft ? (ft - target).Length : double.MaxValue);
                        return (a, proposal, fitted, d, wpx);
                    })
                    .Where(x => x.d <= within).OrderBy(x => x.d).ToList();
                var published = vision.Balls.Where(b => (b.Position - target).Length <= within).ToList();
                Console.WriteLine($"   target ({target.X:F3}, {target.Y:F3}): {near.Count} candidate audits within {within}; published nearby: {(published.Count == 0 ? "none" : string.Join(", ", published.Select(b => $"{b.Kind} ({b.Position.X:F3}, {b.Position.Y:F3})")))}");
                foreach (var (a, proposal, fitted, d, wpx) in near)
                    Console.WriteLine($"     {a.Pass,-10} {a.Stage,-22} kind {a.Kind,-7} classified {a.ClassifiedKind ?? "-",-7} r {a.ProposalRadius / wpx:F4}" +
                        $" support {(a.BoundarySupport is { } s ? s.ToString("F3") : "-"),-6} d {d:F3}  prop ({proposal.X:F3}, {proposal.Y:F3})" +
                        $"{(fitted is { } f2 ? $" fit ({f2.X:F3}, {f2.Y:F3})" : "")}  -> {a.Decision}{(a.Evidence is null ? "" : "  [" + a.Evidence + "]")}");
            }
        }
        return 0;
    }
}
