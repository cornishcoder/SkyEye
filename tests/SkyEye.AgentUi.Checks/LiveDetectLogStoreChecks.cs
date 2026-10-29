using SkyEye.Companion.Hybrid;
using SkyEye.Core.Analysis;
using System.Text.Json;

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

            var eventPath = Path.Combine(directory, "table-events.jsonl");
            var eventStore = new LiveDetectLogStore(eventPath);
            var tableEventAt = start.AddSeconds(1);
            var tableEvent = new LiveDetectLogTableEvent(Guid.NewGuid(), Guid.NewGuid(), "no_pot_observed",
                tableEventAt, Guid.NewGuid(), 4, 81, 9_876_543, Guid.NewGuid().ToString("N"), "calibration",
                "Stable same-inventory layout change.", new(7, 4, 1, 1, 0), new(7, 4, 1, 1, 0),
                9_000_000, 2, .12);
            eventStore.AppendTableEvent(runId, tableEventAt, tableEvent);
            var eventReadout = eventStore.ReadLast(1);
            Require(eventReadout.Summary.RetainedTableEvents == 1
                && eventReadout.Records.Single().TableEvent?.SourceTimeUs == 9_876_543
                && eventReadout.Records.Single().TableEvent?.ActivityId == tableEvent.ActivityId,
                "table events retain typed kinds, activity identity and source timestamps");
            Require(new LiveDetectLogStore(eventPath).Summary.RetainedTableEvents == 1,
                "table event counts and records survive log-store reopening");

            var clipOperation = Guid.NewGuid();
            var activityId = Guid.NewGuid();
            var timeline = Guid.NewGuid().ToString("N");
            var legacyPeriodicSave = new LiveDetectLogClipSave(Guid.NewGuid(), "completed", tableEventAt,
                tableEventAt.AddSeconds(1), 30, "Applied", "Legacy periodic clip.", null, null, null,
                null, null, null, "legacy", "legacy clip", 30, tableEventAt.AddSeconds(1), 100, "hash");
            eventStore.AppendClipSave(runId, tableEventAt, legacyPeriodicSave);
            var eventSave = new LiveDetectLogClipSave(clipOperation, "requested", tableEventAt, null,
                60, "Pending", "Event-linked clip.", null, null, null, null, null, null,
                null, null, null, null, null, null, ActivityId: activityId, EventType: "possible_pot_settled",
                EventTimeline: timeline, WindowStartSourceTimeUs: 1, WindowEndSourceTimeUs: 2);
            eventStore.AppendClipSave(runId, tableEventAt, eventSave);
            eventStore.AppendClipSave(runId, tableEventAt.AddSeconds(1), eventSave with
                { Stage = "completed", CompletedAt = tableEventAt.AddSeconds(1), Outcome = "Applied" });
            Require(eventStore.Summary.RetainedEventClipOperations == 1
                && new LiveDetectLogStore(eventPath).Summary.RetainedEventClipOperations == 1,
                "only distinct activity-linked event clip operations count; old periodic saves are excluded");

            var framePath = Path.Combine(directory, "classified-checks.jsonl");
            var frameStore = new LiveDetectLogStore(framePath);
            var checkAt = start.AddSeconds(2);
            var frame = new LiveDetectLogFrame(checkAt, 12, Guid.NewGuid(), 3, 82, 9_987_654,
                9_987_654, 1_234_567, Guid.NewGuid().ToString("N"), "decoded-frame", "calibration",
                16, 7, 7, 1, 1, 0, false, false, false, 22, 45, [], [], [], "clear_unchanged");
            frameStore.AppendFrame(runId, checkAt, frame);
            Require(new LiveDetectLogStore(framePath).ReadLast(1).Records.Single().Frame?.CheckOutcome == "clear_unchanged",
                "every full analysis check records its typed outcome, including unchanged layouts");

            // A large exempted region must not crowd the actual triggering
            // components out of the bounded audit. Include unreported triggers.
            ObstructionComponentAudit Component(int label, int area, bool triggers) =>
                new(label, 10, 20, 140, 160, area, 2, !triggers, true, !triggers, triggers)
                { BallCentresInBounds = 3, BallBodyPixels = 120, BallBodyCoverageFraction = .125 };
            var fullAudit = new ObstructionAuditTelemetry(800, 1600, 52, 96, 96, 3200, 320, 320, 8,
                90, 7, 5, 2, 5, 1,
                [Component(1, 90_000, false), Component(2, 7000, true), Component(3, 10_000, true),
                 Component(4, 8000, true), Component(5, 6000, true)]);
            var compact = LiveDetectLogObstructionAudit.From(fullAudit)!;
            Require(compact.Components.Select(component => component.Label).SequenceEqual([3, 4, 2])
                && compact.OmittedComponents == 4 && compact.OmittedTriggeringComponents == 2,
                "bounded audit keeps largest actual triggers and preserves omitted counts");
            frameStore.AppendFrame(runId, checkAt.AddSeconds(1), frame with { Obstructed = true, ObstructionAudit = compact });
            var audited = new LiveDetectLogStore(framePath).ReadLast(1).Records.Single();
            Require(audited.SchemaVersion == 7 && audited.Frame?.ObstructionAudit is { } retainedAudit
                && retainedAudit.AnalysisWidth == 800 && retainedAudit.Components.Length == 3
                && retainedAudit.Components[0].BallBodyCoverageFraction == .125
                && retainedAudit.Components[0].BallCentresInBounds == 3,
                "obstruction bounds and body evidence survive JSONL serialization and reopening");
            var oldOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var oldRecord = JsonSerializer.Serialize(new LiveDetectLogRecord(6, "frame", runId, checkAt, Frame: frame), oldOptions);
            Require(!oldRecord.Contains("obstructionAudit")
                && JsonSerializer.Deserialize<LiveDetectLogRecord>(oldRecord, oldOptions)?.Frame?.ObstructionAudit is null,
                "schema-six frames without obstruction audits stay readable");

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
