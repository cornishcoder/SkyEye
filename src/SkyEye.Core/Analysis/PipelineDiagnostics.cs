using System.Diagnostics;

namespace SkyEye.Core.Analysis;

/// <summary>Opt-in, bounded development measurements. No images, URLs or credentials.</summary>
public static class PipelineDiagnostics
{
    static readonly object gate = new();
    static readonly Dictionary<string, Queue<double>> samples = [];
    static readonly Dictionary<string, long> counts = [];
    static readonly Dictionary<string, StageTotals> stageTotals = [];
    public static bool Enabled { get; set; }

    /// <summary>A non-resetting cursor into one stage's cumulative measurements.</summary>
    public readonly struct StageCheckpoint
    {
        internal StageCheckpoint(string stage, long sampleCount, double totalMilliseconds)
            => (Stage, SampleCount, TotalMilliseconds) = (stage, sampleCount, totalMilliseconds);

        internal string Stage { get; }
        internal long SampleCount { get; }
        internal double TotalMilliseconds { get; }
    }

    /// <summary>Measurements added to one stage since a checkpoint.</summary>
    public readonly record struct StageDelta(long N, double TotalMs, double? MeanMs, double? LatestMs);

    readonly record struct StageTotals(long SampleCount, double TotalMilliseconds, double LatestMilliseconds);

    /// <summary>Captures one stage's cumulative cursor without clearing run-wide diagnostics.</summary>
    public static StageCheckpoint Checkpoint(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        lock (gate)
        {
            var totals = stageTotals.GetValueOrDefault(stage);
            return new(stage, totals.SampleCount, totals.TotalMilliseconds);
        }
    }

    /// <summary>Returns bounded-size summary values recorded after a checkpoint.</summary>
    public static StageDelta DeltaSince(string stage, StageCheckpoint checkpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        if (!string.Equals(stage, checkpoint.Stage, StringComparison.Ordinal))
            throw new ArgumentException("The checkpoint belongs to a different pipeline stage.", nameof(checkpoint));

        lock (gate)
        {
            var current = stageTotals.GetValueOrDefault(stage);
            long n = current.SampleCount - checkpoint.SampleCount;
            if (n < 0) throw new InvalidOperationException("Pipeline stage measurements cannot move behind a checkpoint.");
            double totalMs = current.TotalMilliseconds - checkpoint.TotalMilliseconds;
            if (n == 0) return new(0, 0, null, null);
            return new(n, Math.Max(0, totalMs), Math.Max(0, totalMs) / n, current.LatestMilliseconds);
        }
    }

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
            var totals = stageTotals.GetValueOrDefault(name);
            stageTotals[name] = new(totals.SampleCount + 1, totals.TotalMilliseconds + value, value);
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
