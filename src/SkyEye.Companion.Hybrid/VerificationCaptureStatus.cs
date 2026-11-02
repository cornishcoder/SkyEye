using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;

namespace SkyEye.Companion.Hybrid;

public static class VerificationCaptureReceipts
{
    public static int CountValid(string operationFolder, Guid operationId)
    {
        if (operationId == Guid.Empty
            || !string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(operationFolder)), operationId.ToString("N"), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(operationFolder)) return 0;
        var frames = new HashSet<long>();
        IEnumerable<string> receipts;
        try { receipts = Directory.EnumerateFiles(operationFolder, "*.json", SearchOption.TopDirectoryOnly).ToArray(); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }

        foreach (var receiptPath in receipts)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !TryProperty(root, "operationId", out var operationValue)
                    || operationValue.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(operationValue.GetString(), out var receiptOperationId)
                    || receiptOperationId != operationId
                    || !TryProperty(root, "indexedFrame", out var indexed)
                    || indexed.ValueKind != JsonValueKind.Object
                    || !TryProperty(indexed, "frameId", out var frameValue)
                    || !frameValue.TryGetInt64(out var frameId) || frameId <= 0
                    || !TryProperty(indexed, "sampleId", out var sampleValue)
                    || sampleValue.ValueKind != JsonValueKind.String
                    || sampleValue.GetString() != $"{operationId:N}-{frameId.ToString(CultureInfo.InvariantCulture)}"
                    || Path.GetFileNameWithoutExtension(receiptPath) != frameId.ToString(CultureInfo.InvariantCulture)
                    || !TryProperty(indexed, "pixelFile", out var pixelFileValue)
                    || pixelFileValue.ValueKind != JsonValueKind.String)
                    continue;

                var pixelFile = pixelFileValue.GetString();
                if (string.IsNullOrWhiteSpace(pixelFile) || pixelFile.IndexOfAny(['/', '\\']) >= 0
                    || Path.GetFileNameWithoutExtension(pixelFile) != frameId.ToString(CultureInfo.InvariantCulture)
                    || Path.GetExtension(pixelFile).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")
                    || !TryProperty(indexed, "pixelSha256", out var hashValue)
                    || hashValue.ValueKind != JsonValueKind.String)
                    continue;

                var expectedHash = hashValue.GetString();
                if (expectedHash is null || expectedHash.Length != 64) continue;
                var pixelPath = Path.Combine(operationFolder, pixelFile);
                if (!File.Exists(pixelPath)) continue;
                using var pixels = File.OpenRead(pixelPath);
                var actualHash = Convert.ToHexString(SHA256.HashData(pixels));
                if (string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase)) frames.Add(frameId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or ArgumentException)
            {
                // A damaged, incomplete, or unreadable receipt is not a saved frame.
            }
        }

        return frames.Count;
    }

    static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }
}

/// <summary>Runs bounded evidence writes in order without blocking verifier sample acquisition.</summary>
public sealed class VerificationCaptureWriteQueue
{
    readonly object gate = new();
    readonly List<Task> writes = [];
    Task tail = Task.CompletedTask;

    public void Enqueue(Action write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (gate)
        {
            tail = tail.ContinueWith(_ => write(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default);
            writes.Add(tail);
        }
    }

    public async Task<int> DrainAsync()
    {
        while (true)
        {
            Task observed;
            lock (gate) observed = tail;
            try { await observed.ConfigureAwait(false); }
            catch { /* Count failed writes below; keep draining later queued frames. */ }
            lock (gate)
            {
                if (ReferenceEquals(observed, tail))
                    return writes.Count(task => task.IsFaulted || task.IsCanceled);
            }
        }
    }
}

public sealed class VerificationCaptureRunStatus
{
    readonly object gate = new();
    readonly HashSet<long> writtenFrameIds = [];
    Guid? operationId;
    DateTimeOffset? startedAt, completedAt;
    string? result;
    int savedFrames;
    bool running;

    public bool Running { get { lock (gate) return running; } }
    public bool Visible { get { lock (gate) return operationId is not null; } }
    public DateTimeOffset? StatusAt { get { lock (gate) return completedAt ?? startedAt; } }

    public void Begin(Guid id, DateTimeOffset at)
    {
        if (id == Guid.Empty) throw new ArgumentException("A capture run needs an operation ID.", nameof(id));
        lock (gate)
        {
            if (running) throw new InvalidOperationException("A verifier capture is already running.");
            operationId = id;
            startedAt = at;
            completedAt = null;
            result = null;
            savedFrames = 0;
            writtenFrameIds.Clear();
            running = true;
        }
    }

    public bool RecordWrittenReceipt(Guid id, long frameId)
    {
        lock (gate)
        {
            // A native analysis worker can finish writing a frame just after the
            // verifier publishes TimedOut. Keep that matching late receipt visible
            // until a newer run replaces this status; never let an older run update
            // the progress for a newer operation.
            if (operationId != id || frameId <= 0) return false;
            writtenFrameIds.Add(frameId);
            savedFrames = Math.Min(writtenFrameIds.Count, ObservationPolicy.MaximumCapturedVerificationSamples);
            return true;
        }
    }

    public bool Complete(Guid id, DateTimeOffset at, string summary, int count)
    {
        lock (gate)
        {
            if (!running || operationId != id) return false;
            savedFrames = Math.Clamp(count, 0, ObservationPolicy.MaximumCapturedVerificationSamples);
            result = summary;
            completedAt = at;
            running = false;
            return true;
        }
    }

    public string Format(Func<DateTimeOffset, string> formatTime)
    {
        lock (gate)
        {
            if (operationId is not { } id || startedAt is not { } start) return "";
            var run = id.ToString("N")[..8];
            var minimum = ObservationPolicy.MinimumVerificationSamples;
            var maximum = ObservationPolicy.MaximumCapturedVerificationSamples;
            var count = savedFrames >= minimum
                ? $"Saved {savedFrames} verifier frames (minimum met; maximum {maximum})."
                : $"Saved {savedFrames} of {minimum} required verifier frames (maximum {maximum}).";
            if (running) return $"Capturing verifier frames… {savedFrames} of {maximum} valid receipt pairs (minimum {minimum}) · {formatTime(start)} · run {run}";
            if (completedAt is not { } completed) return "";
            var message = result?.StartsWith("Table result:", StringComparison.Ordinal) == true
                ? $"{count} {result}"
                : $"{result ?? "Capture finished."} · {count}";
            return $"{message} · {formatTime(completed)} · run {run}";
        }
    }
}
