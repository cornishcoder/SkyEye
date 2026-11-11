// D3 criteria A: run the settled-change comparer on the staged still pairs
// (tools/evidence/d3_stills_pairs.py) and score each pair as work/evidence-audit/d3/scale_check.py does.
//
//   settled-change --pairs work/d3/stills-pairs.json --out work/d3/stills-csharp.csv [--regions work/d3/stills-regions.jsonl]
//
// Hit: a ball-sized region within 1.6 ball radii of the reviewed black (B) or cue (C) position, mapped
// through the "before" frame's homography. Unexpected: a ball-sized region not within 2.5 radii of B or C.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyEye.Core;
using SkyEye.Vision.Android;

static class SettledChangeCommand
{
    public static int Run(Dictionary<string, string> o)
    {
        var comparer = new SettledChangeComparer();
        var s = comparer.Settings; double r = s.BallRadius;
        var doc = JsonNode.Parse(File.ReadAllText(o["pairs"]))!;
        var csv = new StringBuilder("pair,kind,expect,B_hit,C_hit,unexpected,drift,bed_drift,gated,regions,ball_sized,B_direction,B_dark_before,B_dark_after,C_direction,compute_ms\n");
        using var regionLog = o.TryGetValue("regions", out var rp) ? new StreamWriter(rp) : null;
        var inv = CultureInfo.InvariantCulture;
        foreach (var p in doc["pairs"]!.AsArray())
        {
            string name = p!["pair"]!.GetValue<string>();
            Point[] Corners(string key) => p[key]!.AsArray().Select(c => new Point(c![0]!.GetValue<double>(), c[1]!.GetValue<double>())).ToArray();
            byte[] beforeBytes = File.ReadAllBytes(p["before"]!.GetValue<string>()), afterBytes = File.ReadAllBytes(p["after"]!.GetValue<string>());
            var before = comparer.Rectify(beforeBytes, Corners("cornersBefore"));
            var after = comparer.Rectify(afterBytes, Corners("cornersAfter"));
            var result = comparer.Compare(before, after);
            var hits = new Dictionary<string, SkyEye.Core.Analysis.SettledChangeRegion?>();
            var matched = new HashSet<SkyEye.Core.Analysis.SettledChangeRegion>();
            foreach (var (key, label) in new[] { ("black", "B"), ("cue", "C") })
            {
                var px = p[key]!.AsArray();
                var (x, y) = comparer.MapToRaster(new(px[0]!.GetValue<double>(), px[1]!.GetValue<double>()), Corners("cornersBefore"), 1920, 1080);
                double D(SkyEye.Core.Analysis.SettledChangeRegion g) => Math.Sqrt((g.RasterX - x) * (g.RasterX - x) + (g.RasterY - y) * (g.RasterY - y));
                hits[label] = result.Regions.Where(g => g.BallSized && D(g) < 1.6 * r).OrderBy(D).FirstOrDefault();
                foreach (var g in result.Regions.Where(g => D(g) < 2.5 * r)) matched.Add(g);
            }
            int unexpected = result.Regions.Count(g => g.BallSized && !matched.Contains(g));
            var b = hits["B"]; var c = hits["C"];
            csv.Append(string.Join(",", name, p["kind"]!.GetValue<string>(), "\"" + p["expect"]!.ToJsonString().Replace("\"", "'") + "\"",
                b is not null, c is not null, unexpected, result.CushionBandDrift.ToString(inv), result.BedDrift.ToString(inv),
                result.Gated ? string.Join("|", result.GateReasons) : "", result.Regions.Count, result.Regions.Count(g => g.BallSized),
                b?.Direction.ToString() ?? "", b?.DarkBefore.ToString(inv) ?? "", b?.DarkAfter.ToString(inv) ?? "",
                c?.Direction.ToString() ?? "", result.ComputeMs.ToString("F1", inv))).Append('\n');
            regionLog?.WriteLine(JsonSerializer.Serialize(new { pair = name, result.GateReasons, result.CushionBandDrift, result.BedDrift, result.BedP99, result.RawComponentCount, result.Regions }));
            Console.WriteLine($"{name,-38} B {(b is not null ? "hit " : "-   ")} C {(c is not null ? "hit " : "-   ")} unexpected {unexpected} drift {result.CushionBandDrift:F4}{(result.Gated ? " GATED " + string.Join("|", result.GateReasons) : "")}  {result.ComputeMs:F0} ms");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(o["out"]))!);
        File.WriteAllText(o["out"], csv.ToString());
        Console.WriteLine("wrote " + o["out"]);
        return 0;
    }
}
