using System.Diagnostics;

namespace SkyEye.Core.Analysis;

/// <summary>Opt-in, bounded development measurements. No images, URLs or credentials.</summary>
public static class PipelineDiagnostics
{
    static readonly object gate = new();
    static readonly Dictionary<string, Queue<double>> samples = [];
    static readonly Dictionary<string, long> counts = [];
    public static bool Enabled { get; set; }
    public static void Count(string name)
    {
        if (!Enabled) return;
        lock (gate) counts[name] = counts.GetValueOrDefault(name) + 1;
    }
    public static void Sample(string name, double value)
    {
        if (!Enabled || !double.IsFinite(value)) return;
        lock (gate)
        {
            if (!samples.TryGetValue(name, out var values)) samples[name] = values = new();
            values.Enqueue(value); while (values.Count > 256) values.Dequeue();
        }
    }
    public static IDisposable Measure(string name) => new Timing(name);
    public static object Snapshot()
    {
        lock (gate) return new
        {
            counts = new Dictionary<string, long>(counts),
            stages = samples.ToDictionary(p => p.Key, p =>
            {
                var sorted = p.Value.Order().ToArray();
                return new { n = sorted.Length, mean = sorted.Average(), p50 = sorted.Length >= 20 ? (double?)sorted[(int)Math.Ceiling(sorted.Length * .5) - 1] : null,
                    p95 = sorted.Length >= 20 ? (double?)sorted[(int)Math.Ceiling(sorted.Length * .95) - 1] : null, latest = p.Value.Last() };
            })
        };
    }
    sealed class Timing(string name) : IDisposable
    {
        readonly long start = Stopwatch.GetTimestamp();
        public void Dispose() => Sample(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}
