using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Companion.Hybrid;

static class VerificationCaptureChecks
{
    static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
    static string FormatTime(DateTimeOffset value) => value.ToUniversalTime().ToString("HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    static void CheckEvidenceBudget()
    {
        const long MiB = 1024 * 1024;
        Check(VerificationEvidenceBudget.MaximumBytes == 256 * MiB,
            "The bounded verifier store cap is 256 MiB");
        Check(VerificationEvidenceBudget.CaptureReservationBytes == 17 * MiB + MiB / 2,
            "The preflight still reserves 2.5 MiB for each of the seven possible samples");
        Check(VerificationEvidenceBudget.CanReserveCapture(VerificationEvidenceBudget.MaximumBytes - VerificationEvidenceBudget.CaptureReservationBytes)
            && !VerificationEvidenceBudget.CanReserveCapture(VerificationEvidenceBudget.MaximumBytes - VerificationEvidenceBudget.CaptureReservationBytes + 1),
            "The capture preflight accepts its exact reserve boundary and rejects one byte over it");
        Check(VerificationEvidenceBudget.CanWriteFrame(VerificationEvidenceBudget.MaximumBytes - VerificationEvidenceBudget.MaximumFrameBytes
                - VerificationEvidenceBudget.FrameWriteOverheadBytes, VerificationEvidenceBudget.MaximumFrameBytes)
            && !VerificationEvidenceBudget.CanWriteFrame(VerificationEvidenceBudget.MaximumBytes - VerificationEvidenceBudget.MaximumFrameBytes
                - VerificationEvidenceBudget.FrameWriteOverheadBytes + 1, VerificationEvidenceBudget.MaximumFrameBytes)
            && !VerificationEvidenceBudget.CanWriteFrame(0, VerificationEvidenceBudget.MaximumFrameBytes + 1),
            "A frame write respects both exact remaining-space and per-image boundaries");
    }

    public static Task Run()
    {
        CheckEvidenceBudget();
        var root = Path.Combine(Path.GetTempPath(), "skyeye-verification-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Parse("2026-10-26T08:00:00Z", CultureInfo.InvariantCulture);
            var first = Guid.NewGuid();
            var firstFolder = Path.Combine(root, first.ToString("N"));
            Directory.CreateDirectory(firstFolder);
            for (var frame = 1; frame <= 7; frame++) WriteReceipt(firstFolder, first, frame, [1, 2, 3, (byte)frame]);
            Check(VerificationCaptureReceipts.CountValid(firstFolder, first) == 7,
                "A complete run counts seven matching image and metadata receipts");
            Check(VerificationCaptureReceipts.CountValid(Path.Combine(root, Guid.NewGuid().ToString("N")), first) == 0,
                "A receipt folder with a different operation ID cannot supply this run's count");

            var status = new VerificationCaptureRunStatus();
            status.Begin(first, now);
            for (var frame = 1; frame <= 7; frame++) status.RecordWrittenReceipt(first, frame);
            status.RecordWrittenReceipt(first, 7);
            status.Complete(first, now.AddSeconds(3), "Table result: DetectionAmbiguous.", 7);
            var completedClear = status.Format(FormatTime);
            Check(completedClear.Contains("Saved 7 verifier frames (minimum met; maximum 7).", StringComparison.Ordinal)
                && completedClear.Contains("DetectionAmbiguous", StringComparison.Ordinal),
                "Seven valid receipts remain tied to their own table result");

            var second = Guid.NewGuid();
            var secondFolder = Path.Combine(root, second.ToString("N"));
            Directory.CreateDirectory(secondFolder);
            WriteReceipt(secondFolder, second, 1, [4, 5, 6]);
            var unrelatedOperation = Guid.NewGuid();
            WriteReceipt(secondFolder, unrelatedOperation, 2, [7, 8, 9]);
            WriteReceipt(secondFolder, second, 3, [10, 11, 12], hashOverride: new string('0', 64));
            WriteReceipt(secondFolder, second, 4, [13, 14, 15], writePixels: false);
            WriteReceipt(secondFolder, second, 5, [16, 17, 18], pixelFileOverride: "../5.png");
            File.WriteAllText(Path.Combine(secondFolder, "6.json"), "{not json");

            status.Begin(second, now.AddSeconds(4));
            var running = status.Format(FormatTime);
            Check(running.Contains("0 of 7 valid receipt pairs", StringComparison.Ordinal)
                && running.Contains(second.ToString("N")[..8], StringComparison.Ordinal)
                && !running.Contains("DetectionAmbiguous", StringComparison.Ordinal),
                "A new run immediately replaces the previous success while showing its own running count");
            Check(VerificationCaptureReceipts.CountValid(secondFolder, second) == 1,
                "Only a matching operation, valid image file, and correct pixel hash count as saved");
            Check(status.RecordWrittenReceipt(second, 1), "The active run accepts progress from its matching receipt");
            Check(!status.Complete(first, now.AddSeconds(5), "stale result", 7), "A late result from the prior run cannot overwrite current progress");
            Check(status.Format(FormatTime).Contains("1 of 7 valid receipt pairs", StringComparison.Ordinal),
                "Running progress reports the verified partial count against the seven-frame target");
            Check(status.Complete(second, now.AddSeconds(6), "Capture failed.", 1), "The current run records its partial failure");
            var failed = status.Format(FormatTime);
            Check(failed.Contains("Capture failed.", StringComparison.Ordinal)
                && failed.Contains("Saved 1 of 5 required verifier frames (maximum 7).", StringComparison.Ordinal)
                && !failed.Contains("DetectionAmbiguous", StringComparison.Ordinal),
                "A one-frame failure cannot be presented as a complete one-of-one run or retain an old success");

            var cancelledRun = Guid.NewGuid();
            status.Begin(cancelledRun, now.AddSeconds(7));
            status.RecordWrittenReceipt(cancelledRun, 7);
            status.Complete(cancelledRun, now.AddSeconds(8), "Capture cancelled", 1);
            var cancelled = status.Format(FormatTime);
            Check(cancelled.Contains("Capture cancelled", StringComparison.Ordinal)
                && cancelled.Contains("Saved 1 of 5 required verifier frames (maximum 7).", StringComparison.Ordinal)
                && !cancelled.Contains("Capture failed", StringComparison.Ordinal),
                "Cancellation retains the current partial count and clears the prior run result");

            var notStartedRun = Guid.NewGuid();
            status.Begin(notStartedRun, now.AddSeconds(9));
            status.Complete(notStartedRun, now.AddSeconds(9), "Capture not started · prerequisites changed", 0);
            var notStarted = status.Format(FormatTime);
            Check(notStarted.Contains("Capture not started", StringComparison.Ordinal)
                && notStarted.Contains("Saved 0 of 5 required verifier frames (maximum 7).", StringComparison.Ordinal)
                && !notStarted.Contains("Capture cancelled", StringComparison.Ordinal),
                "An early no-start result replaces the prior capture status rather than leaving it visible");

            var timedOutRun = Guid.NewGuid();
            status.Begin(timedOutRun, now.AddSeconds(10));
            for (var frame = 1; frame <= 6; frame++) status.RecordWrittenReceipt(timedOutRun, frame);
            Check(status.Complete(timedOutRun, now.AddSeconds(14), "Table result: Unsettled (TimedOut).", 6),
                "A timed-out run publishes its accepted receipt count");
            Check(status.Format(FormatTime).Contains("Saved 6 verifier frames", StringComparison.Ordinal),
                "The timeout result initially shows six saved frames");
            Check(status.RecordWrittenReceipt(timedOutRun, 7)
                && status.Format(FormatTime).Contains("Saved 7 verifier frames", StringComparison.Ordinal),
                "A same-operation receipt that finishes after timeout updates the saved count");

            var replacementRun = Guid.NewGuid();
            status.Begin(replacementRun, now.AddSeconds(15));
            Check(!status.RecordWrittenReceipt(timedOutRun, 8)
                && status.Format(FormatTime).Contains(replacementRun.ToString("N")[..8], StringComparison.Ordinal),
                "A late receipt from a completed run cannot overwrite the next run's status");

            Console.WriteLine("PASS verifier capture counts are operation-scoped, hash-validated, truthful for partial runs, and quota-bounded");
        }
        finally
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
            foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
        return Task.CompletedTask;
    }

    static void WriteReceipt(string folder, Guid operationId, int frameId, byte[] pixels,
        string? hashOverride = null, bool writePixels = true, string? pixelFileOverride = null)
    {
        var name = frameId.ToString(CultureInfo.InvariantCulture) + ".png";
        if (writePixels) File.WriteAllBytes(Path.Combine(folder, name), pixels);
        var actualHash = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        var receipt = new
        {
            operationId,
            indexedFrame = new
            {
                sampleId = $"{operationId:N}-{frameId.ToString(CultureInfo.InvariantCulture)}",
                frameId,
                pixelFile = pixelFileOverride ?? name,
                pixelSha256 = hashOverride ?? actualHash
            }
        };
        File.WriteAllText(Path.Combine(folder, frameId.ToString(CultureInfo.InvariantCulture) + ".json"), JsonSerializer.Serialize(receipt));
    }
}
