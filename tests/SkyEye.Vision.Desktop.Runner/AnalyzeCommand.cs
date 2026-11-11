// Analyze one source image with explicit calibration corners (production settings) and print the flags,
// warnings, packed-cluster telemetry and obstruction audit; optionally save the analyser's obstruction mask.
//
//   analyze --image F.png --corners "x,y;x,y;x,y;x,y" [--mask out.png]
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;

static class AnalyzeCommand
{
    public static int Run(Dictionary<string, string> o)
    {
        var corners = o["corners"].Split(';').Select(p => p.Split(',')).Select(p => new Point(double.Parse(p[0]), double.Parse(p[1]))).ToArray();
        var settings = new VisionSettings(Width: 800)
        {
            PackedColourBoundarySearchExtentFraction = .65,
            ObstructionShadowTolerance = !(o.TryGetValue("shadow-tolerance", out var st) && st == "off"),
            PackedClusterLocalUnknowns = !(o.TryGetValue("local-unknowns", out var lu) && lu == "off"),
            ObstructionDimChromaTolerance = !(o.TryGetValue("dim-chroma", out var dc) && dc == "off"),
            PackedBlackAdjacentBallOcclusion = o.TryGetValue("adjacent-black", out var ab) && ab == "on",
            PackedBlackOcclusionDarkBody = !(o.TryGetValue("dark-body", out var db) && db == "off")
        };
        var analyzer = new VisionAnalyzer(settings, includeObstructionMaskDiagnostics: o.ContainsKey("mask"));
        var v = analyzer.Analyze(File.ReadAllBytes(o["image"]), new TableCalibration(corners));
        var kinds = string.Join(" ", v.Balls.GroupBy(b => b.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"));
        Console.WriteLine($"{Path.GetFileName(o["image"])}: balls {v.Balls.Length} ({kinds}) uncertain {v.Uncertain} layoutUncertain {v.HasUncertainLayout} obstructed {v.Obstructed} normalized {v.Normalized} clothHue {v.ClothHue}");
        Console.WriteLine($"  warnings: {string.Join(" | ", v.Warnings)}");
        if (v.PackedCluster is { } p) Console.WriteLine($"  packed cluster: detected {p.Detected} complete {p.Complete} red {p.RedCentres} yellow {p.YellowCentres} black {p.BlackCentre}");
        if (v.ObstructionAudit is { } a)
        {
            Console.WriteLine($"  obstruction: qualifying {a.QualifyingComponents} triggering {a.TriggeringComponents}; min w/h/area {a.MinimumComponentWidth:0}/{a.MinimumComponentHeight:0}/{a.MinimumComponentArea:0}; packed max w/h {a.PackedClusterMaximumWidth:0}/{a.PackedClusterMaximumHeight:0}, min centres {a.PackedClusterMinimumBallCentres}");
            foreach (var c in a.Components)
                Console.WriteLine($"    component {c.Label}: x {c.Left} y {c.Top} w {c.Width} h {c.Height} area {c.Area} ballCentres {c.BallCentres} inBounds {c.BallCentresInBounds} bodyCoverage {c.BallBodyCoverageFraction:0.000} sizeEligible {c.PackedClusterSizeEligible} exempted {c.PackedClusterExempted} triggers {c.TriggersObstruction}");
        }
        if (o.TryGetValue("mask", out var maskPath) && analyzer.TakeObstructionMaskDiagnosticPng() is { } png)
        {
            File.WriteAllBytes(maskPath, png);
            Console.WriteLine("  obstruction mask written to " + maskPath);
        }
        return 0;
    }
}
