using SkyEye.Companion.Hybrid;

internal static class LiveDetectLogStoreChecks
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "skyeye-live-detect-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "live-detect.jsonl");
            var store = new LiveDetectLogStore(path);
            var runId = Guid.NewGuid();
            var start = DateTimeOffset.UtcNow;
            string largeMessage = new('x', 9_000);
            for (int index = 0; index < 600; index++)
            {
                var recordedAt = start.AddMilliseconds(index);
                store.AppendRunEvent(runId, recordedAt, new("fixture", recordedAt, largeMessage, null, index, index, recordedAt));
            }

            var summary = store.Summary;
            long fileBytes = new FileInfo(path).Length;
            Require(fileBytes <= LiveDetectLogStore.MaximumBytes, "live log stays under the hard byte cap after large records");
            Require(summary.RetainedRecords < 600 && summary.RetainedRecords <= LiveDetectLogStore.MaximumRetainedRecords,
                "byte trimming removes older records and stays within the record cap");
            Require(store.ReadLast(1).Records.Single().RunEvent?.FrameId == 599,
                "byte trimming preserves the newest source-stamped record");

            var reopened = new LiveDetectLogStore(path);
            Require(reopened.Summary.RetainedRecords == summary.RetainedRecords
                && reopened.ReadLast(1).Records.Single().RunEvent?.FrameId == 599,
                "retained byte-bounded records remain readable after reopening the store");

            var countPath = Path.Combine(directory, "record-cap.jsonl");
            var countStore = new LiveDetectLogStore(countPath);
            for (int index = 0; index <= LiveDetectLogStore.MaximumRetainedRecords; index++)
            {
                var recordedAt = start.AddMilliseconds(index);
                countStore.AppendRunEvent(runId, recordedAt, new("fixture", recordedAt, "small", null, index, index, recordedAt));
            }
            Require(countStore.Summary.RetainedRecords < LiveDetectLogStore.MaximumRetainedRecords
                && countStore.ReadLast(1).Records.Single().RunEvent?.FrameId == LiveDetectLogStore.MaximumRetainedRecords,
                "record-count trimming also keeps the newest record under the cap");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine("PASS Live Detect log retention enforces byte and record caps while keeping recent records");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
