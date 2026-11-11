// D3 slice 2 (design 5.3 C): live-cadence simulation. Decodes each T4 clip, takes one frame per simulated
// Live Detect pass (cadence plus uniform jitter, fixed seed), rectifies it and feeds SettledChangeProcessor.
//
//   settled-change-clips --clips work/d3/clips.json --out work/d3/clips-live.jsonl [--cadence 0.35] [--jitter 0.1] [--seed 1]
//
// One JSON line per clip: pass and state counts, compute times, and every evidence record with its windows
// (seconds into the clip) and regions. Score with tools/evidence/d3_clips.py score.
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using Ocv = OpenCvSharp;

static class SettledChangeClipsCommand
{
    public static int Run(Dictionary<string, string> o)
    {
        double cadence = o.TryGetValue("cadence", out var c) ? double.Parse(c) : .35;
        double jitter = o.TryGetValue("jitter", out var j) ? double.Parse(j) : .1;
        var rng = new Random(o.TryGetValue("seed", out var sd) ? int.Parse(sd) : 1);
        var doc = JsonNode.Parse(File.ReadAllText(o["clips"]))!;
        using var output = new StreamWriter(o["out"]);
        foreach (var clip in doc["clips"]!.AsArray())
        {
            string path = clip!["path"]!.GetValue<string>();
            var corners = clip["corners"]!.AsArray().Select(p => new Point(p![0]!.GetValue<double>(), p[1]!.GetValue<double>())).ToArray();
            var comparer = new SettledChangeComparer(); var processor = new SettledChangeProcessor(comparer);
            using var capture = new Ocv.VideoCapture(path);
            if (!capture.IsOpened()) { Console.WriteLine("cannot open " + path); continue; }
            double fps = capture.Fps > 0 ? capture.Fps : 30;
            var evidence = new List<object>(); var stateCounts = new Dictionary<string, int>(); var passMs = new List<double>();
            double nextPass = 0; int frameIndex = 0, passes = 0;
            using var frame = new Ocv.Mat();
            byte[]? buffer = null;
            while (capture.Read(frame) && !frame.Empty())
            {
                double t = frameIndex++ / fps;
                if (t + 1e-9 < nextPass) continue;
                nextPass = t + cadence + (rng.NextDouble() * 2 - 1) * jitter;
                int bytes = frame.Rows * frame.Cols * 3;
                buffer ??= new byte[bytes];
                if (!frame.IsContinuous() || frame.Type() != Ocv.MatType.CV_8UC3) throw new InvalidDataException("Unexpected frame format.");
                Marshal.Copy(frame.Data, buffer, 0, bytes);
                var raster = comparer.RectifyBgr(buffer, frame.Cols, frame.Rows, corners);
                var pass = processor.Observe(raster, (long)Math.Round(t * 1e6));
                passes++; passMs.Add(pass.ComputeMs);
                stateCounts[pass.State.ToString()] = stateCounts.GetValueOrDefault(pass.State.ToString()) + 1;
                if (pass.EvidenceIsNew && pass.Evidence is { } e)
                    evidence.Add(new
                    {
                        sequence = e.Sequence, status = e.Status.ToString(), gates = e.GateReasons,
                        refSegmentStartS = S(e.ReferenceSegmentStartUs), refSegmentEndS = S(e.ReferenceSegmentEndUs),
                        refWindowStartS = S(e.ReferenceWindowStartUs), refWindowEndS = S(e.ReferenceWindowEndUs),
                        curWindowStartS = S(e.CurrentWindowStartUs), curWindowEndS = S(e.CurrentWindowEndUs),
                        cushionBandDrift = e.Comparison?.CushionBandDrift, bedDrift = e.Comparison?.BedDrift,
                        compareMs = e.Comparison?.ComputeMs, passMs = Math.Round(pass.ComputeMs, 1),
                        regions = e.Comparison?.Regions.Select(g => new
                        {
                            g.RasterX, g.RasterY, g.TableX, g.TableY, g.Zone, g.AreaBalls, g.InscribedRadiusBalls, g.OutsideFraction,
                            g.Peak, g.BallSized, g.RejectReason, direction = g.Direction.ToString(), g.DarkBefore, g.DarkAfter, g.WhiteBefore, g.WhiteAfter
                        }) ?? []
                    });
            }
            passMs.Sort();
            var line = new
            {
                clip = path, fps, frames = frameIndex, passes, settledPasses = stateCounts.GetValueOrDefault("Settled"), states = stateCounts,
                passMsMedian = passMs.Count == 0 ? 0 : Math.Round(passMs[passMs.Count / 2], 1),
                passMsP95 = passMs.Count == 0 ? 0 : Math.Round(passMs[(int)(.95 * (passMs.Count - 1))], 1),
                evidence
            };
            output.WriteLine(JsonSerializer.Serialize(line));
            output.Flush();
            Console.WriteLine($"{Path.GetFileName(path),-48} {frameIndex / fps,6:F1} s  passes {passes,4}  settled {line.settledPasses,4}  evidence {evidence.Count,2}  pass ms p50 {line.passMsMedian}");
        }
        return 0;
    }

    static double? S(long? us) => us is null ? null : Math.Round(us.Value / 1e6, 2);
}
