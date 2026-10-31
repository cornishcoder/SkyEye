using System.Text.Json;
using SkyEye.Core.Analysis;

static class PipelineDiagnosticsChecks
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Run()
    {
        bool previousEnabled = PipelineDiagnostics.Enabled;
        string stage = "pipeline-delta-check-" + Guid.NewGuid().ToString("N");
        PipelineDiagnostics.Enabled = true;
        try
        {
            PipelineDiagnostics.Sample(stage, 10);
            var checkpoint = PipelineDiagnostics.Checkpoint(stage);
            PipelineDiagnostics.Sample(stage, 7);
            PipelineDiagnostics.Sample(stage, 13);

            var delta = PipelineDiagnostics.DeltaSince(stage, checkpoint);
            Require(delta.N == 2 && delta.TotalMs == 20 && delta.MeanMs == 10 && delta.LatestMs == 13,
                "Stage delta should summarize only measurements added after its checkpoint.");

            var emptyCheckpoint = PipelineDiagnostics.Checkpoint(stage);
            var emptyDelta = PipelineDiagnostics.DeltaSince(stage, emptyCheckpoint);
            Require(emptyDelta.N == 0 && emptyDelta.TotalMs == 0 && emptyDelta.MeanMs is null && emptyDelta.LatestMs is null,
                "An empty stage delta should be explicit and must not reuse the previous latest sample.");

            PipelineDiagnostics.Enabled = false;
            PipelineDiagnostics.Sample(stage, 99);
            var disabledDelta = PipelineDiagnostics.DeltaSince(stage, emptyCheckpoint);
            Require(disabledDelta.N == 0 && disabledDelta.LatestMs is null,
                "Disabled diagnostics should not advance a stage checkpoint.");

            PipelineDiagnostics.Enabled = true;
            PipelineDiagnostics.Sample(stage, 4);
            var nextDelta = PipelineDiagnostics.DeltaSince(stage, emptyCheckpoint);
            Require(nextDelta.N == 1 && nextDelta.TotalMs == 4 && nextDelta.MeanMs == 4 && nextDelta.LatestMs == 4,
                "A later sample should produce a fresh delta after an empty window.");

            using var aggregate = JsonDocument.Parse(JsonSerializer.Serialize(PipelineDiagnostics.Snapshot()));
            var stageSummary = aggregate.RootElement.GetProperty("stages").GetProperty(stage);
            Require(stageSummary.GetProperty("n").GetInt32() == 4
                    && stageSummary.GetProperty("mean").GetDouble() == 8.5
                    && stageSummary.GetProperty("latest").GetDouble() == 4,
                "Delta reads must preserve the existing run-wide aggregate.");

            var rollingCheckpoint = PipelineDiagnostics.Checkpoint(stage);
            for (int value = 1; value <= 300; value++) PipelineDiagnostics.Sample(stage, value);
            var rollingDelta = PipelineDiagnostics.DeltaSince(stage, rollingCheckpoint);
            Require(rollingDelta.N == 300 && rollingDelta.TotalMs == 45150
                    && rollingDelta.MeanMs == 150.5 && rollingDelta.LatestMs == 300,
                "Stage deltas should retain complete window totals after the bounded display queue rolls over.");
            using var boundedAggregate = JsonDocument.Parse(JsonSerializer.Serialize(PipelineDiagnostics.Snapshot()));
            var boundedStageSummary = boundedAggregate.RootElement.GetProperty("stages").GetProperty(stage);
            Require(boundedStageSummary.GetProperty("n").GetInt32() == 256
                    && boundedStageSummary.GetProperty("latest").GetDouble() == 300,
                "Run-wide display samples should remain bounded while deltas stay complete.");

            try
            {
                PipelineDiagnostics.DeltaSince(stage + "-different", checkpoint);
                throw new Exception("A stage checkpoint must not be accepted for a different stage.");
            }
            catch (ArgumentException) { }
        }
        finally
        {
            PipelineDiagnostics.Enabled = previousEnabled;
        }
        Console.WriteLine("PASS non-resetting bounded PipelineDiagnostics stage deltas and empty-window freshness");
    }
}
