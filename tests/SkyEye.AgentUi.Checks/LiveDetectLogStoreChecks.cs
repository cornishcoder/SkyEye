using SkyEye.Companion.Hybrid;
using SkyEye.Core;
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
                EventTimeline: timeline, WindowStartSourceTimeUs: 1, WindowEndSourceTimeUs: 2,
                LinkedActivityIds: [activityId], LinkedTableEventIds: [tableEvent.EventId],
                LinkedEventTypes: ["possible_pot_settled"]);
            eventStore.AppendClipSave(runId, tableEventAt, eventSave);
            eventStore.AppendClipSave(runId, tableEventAt.AddSeconds(1), eventSave with
                { Stage = "completed", CompletedAt = tableEventAt.AddSeconds(1), Outcome = "Applied" });
            Require(eventStore.Summary.RetainedEventClipOperations == 1
                && new LiveDetectLogStore(eventPath).Summary.RetainedEventClipOperations == 1,
                "only distinct activity-linked event clip operations count; old periodic saves are excluded");
            var groupedClipSave = new LiveDetectLogStore(eventPath).ReadLast(1).Records.Single().ClipSave;
            Require(groupedClipSave?.LinkedActivityIds?.SequenceEqual([activityId]) == true
                && groupedClipSave.LinkedTableEventIds?.SequenceEqual([tableEvent.EventId]) == true
                && groupedClipSave.LinkedEventTypes?.SequenceEqual(["possible_pot_settled"]) == true,
                "clip receipts preserve linked activity, table-event and event-type identities");

            var framePath = Path.Combine(directory, "classified-checks.jsonl");
            var frameStore = new LiveDetectLogStore(framePath);
            var checkAt = start.AddSeconds(2);
            var savedEvidence = frameStore.SaveFrameEvidence(
                new EncodedTableFrameEvidence([10, 20, 30, 40], ".png", "image/png"), "initial");
            Guid savedEvidenceId = savedEvidence.EvidenceId.GetValueOrDefault();
            Require(savedEvidence.Available && savedEvidenceId != Guid.Empty
                && savedEvidence.RelativePath == "evidence/" + savedEvidenceId.ToString("N") + ".png"
                && savedEvidence.Sha256 is { Length: 64 },
                "source frame evidence is saved with a stable relative path and content hash");
            var frame = new LiveDetectLogFrame(checkAt, 12, Guid.NewGuid(), 3, 82, 9_987_654,
                9_987_654, 1_234_567, Guid.NewGuid().ToString("N"), "decoded-frame", "calibration",
                16, 7, 7, 1, 1, 0, false, false, false, 22, 45, [], [], [], "clear_unchanged",
                FrameEvidence: savedEvidence);
            frameStore.AppendFrame(runId, checkAt, frame);
            var reopenedFrameStore = new LiveDetectLogStore(framePath);
            Require(reopenedFrameStore.ReadLast(1).Records.Single().Frame is { CheckOutcome: "clear_unchanged",
                    FrameEvidence.Available: true } && reopenedFrameStore.Summary.RetainedEvidenceFrames == 1
                && reopenedFrameStore.Summary.RetainedEvidenceBytes == 4
                && reopenedFrameStore.TryGetFrameEvidence(savedEvidenceId, out var evidencePath, out var evidenceType)
                && evidenceType == "image/png" && File.ReadAllBytes(evidencePath).SequenceEqual(new byte[] { 10, 20, 30, 40 }),
                "full checks link exact source evidence and expose it after reopening the log store");
            var evidencePolicy = new LiveDetectFrameEvidenceCapture();
            var evidenceBall = new Ball("red-1", "red", new(0.4, 0.5), 1);
            var evidenceVision = new VisionResult([evidenceBall], false, [], 1, 1, true, "fixture", [1, 1, 1], 100, false, []);
            TableFrame EvidenceFrame(DateTimeOffset capturedAt) => new([1, 2], capturedAt, "timeline");
            Require(evidencePolicy.Observe(EvidenceFrame(checkAt), evidenceVision)?.Trigger == "initial"
                && evidencePolicy.Observe(EvidenceFrame(checkAt.AddSeconds(1)), evidenceVision) is null
                && evidencePolicy.Observe(EvidenceFrame(checkAt.AddSeconds(3)), evidenceVision)?.Trigger == "interval:3s"
                && evidencePolicy.Observe(EvidenceFrame(checkAt.AddSeconds(4)), evidenceVision with { Balls = [evidenceBall, new("yellow-1", "yellow", new(.6, .5), 1)] })?.Trigger.StartsWith("inventory:") == true,
                "source evidence samples initial, interval and inventory-change frames without copying every pass");
            // The budget is not spent before the shot: interval samples stop after two, quality flicker stops
            // before the reserved slots, and every new settled-change comparison still gets a frame.
            var budget = new LiveDetectFrameEvidenceCapture();
            var clearVision = evidenceVision; var uncertainVision = evidenceVision with { LayoutUncertain = true };
            var triggers = new List<string?>();
            for (int second = 0; second < 30; second++)
                triggers.Add(budget.Observe(EvidenceFrame(checkAt.AddSeconds(second * 3)), second % 2 == 0 ? clearVision : uncertainVision)?.Trigger);
            for (int k = 0; k < 4; k++)
                triggers.Add(budget.Observe(EvidenceFrame(checkAt.AddSeconds(100 + k)), clearVision, settledComparison: true)?.Trigger);
            Require(triggers.Count(t => t is not null && t.StartsWith("quality:")) == LiveDetectFrameEvidenceCapture.MaximumFramesPerRun - LiveDetectFrameEvidenceCapture.ReservedForSettledChange - 1
                && triggers.Count(t => t == "settled-change") == LiveDetectFrameEvidenceCapture.ReservedForSettledChange
                && triggers.Count(t => t is not null) == LiveDetectFrameEvidenceCapture.MaximumFramesPerRun,
                $"evidence keeps slots for settled-change comparisons after quality flicker ({string.Join(",", triggers.Where(t => t is not null))})");
            var quiet = new LiveDetectFrameEvidenceCapture();
            var quietTriggers = Enumerable.Range(0, 20).Select(s => quiet.Observe(EvidenceFrame(checkAt.AddSeconds(s * 3)), clearVision)?.Trigger).ToList();
            Require(quietTriggers.Count(t => t == "interval:3s") == LiveDetectFrameEvidenceCapture.MaximumIntervalFrames,
                "a quiet table takes only the initial frame and two interval samples");

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
            Require(audited.SchemaVersion == LiveDetectLogStore.CurrentSchemaVersion
                && audited.Frame?.ObstructionAudit is { } retainedAudit
                && retainedAudit.AnalysisWidth == 800 && retainedAudit.Components.Length == 3
                && retainedAudit.Components[0].BallBodyCoverageFraction == .125
                && retainedAudit.Components[0].BallCentresInBounds == 3,
                "obstruction bounds and body evidence survive JSONL serialization and reopening");
            var pocketAudit = new PocketColourAuditTelemetry(800, 1600, 38.4, 64,
                [new(new(0, 800), 718, 640, 78, 2, true,
                    [new(3, "red", new(41, 1036), 56.7, "pocket-facing-arc-unsupported",
                        .75, 1.2, .1, 6, 5)])]);
            frameStore.AppendFrame(runId, checkAt.AddSeconds(2), frame with { PocketColourAudit = pocketAudit });
            var pocketLogged = new LiveDetectLogStore(framePath).ReadLast(1).Records.Single();
            Require(pocketLogged.SchemaVersion == LiveDetectLogStore.CurrentSchemaVersion
                && pocketLogged.Frame?.PocketColourAudit?.Pockets.Single().BallSupportAudit is
                    [{ Decision: "pocket-facing-arc-unsupported", CircleIndex: 3, PocketFacingSectors: 6,
                       PocketFacingSupportedSectors: 5 }],
                "per-ball pocket support rejection details survive JSONL serialization and reopening");
            var oldOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var oldRecord = JsonSerializer.Serialize(new LiveDetectLogRecord(6, "frame", runId, checkAt, Frame: frame), oldOptions);
            Require(!oldRecord.Contains("obstructionAudit")
                && JsonSerializer.Deserialize<LiveDetectLogRecord>(oldRecord, oldOptions)?.Frame?.ObstructionAudit is null,
                "schema-six frames without obstruction audits stay readable");
            var oldPocketFrame = frame with { PocketColourAudit = new PocketColourAuditTelemetry(800, 1600, 38.4, 64,
                [new(new(0, 800), 718, 640, 78, 2, true)]) };
            var oldPocketRecord = JsonSerializer.Serialize(new LiveDetectLogRecord(13, "frame", runId, checkAt,
                Frame: oldPocketFrame), oldOptions);
            Require(!oldPocketRecord.Contains("ballSupportAudit")
                && JsonSerializer.Deserialize<LiveDetectLogRecord>(oldPocketRecord, oldOptions)
                    ?.Frame?.PocketColourAudit?.Pockets.Single().BallSupportAudit is null,
                "older pocket audits without per-ball support details remain readable");

            // Schema 15: D3 settled-change pass, evidence, shadow proposals and raster sidecars.
            var settledPath = Path.Combine(directory, "settled", "live-detect.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(settledPath)!);
            var settledStore = new LiveDetectLogStore(settledPath);
            var reference = settledStore.SaveSettledChangeRaster([0xFF, 0xD8, 1, 2, 3], "reference");
            var current = settledStore.SaveSettledChangeRaster([0xFF, 0xD8, 4, 5], "current");
            Require(reference.Available && reference.RelativePath == "settled-change/" + reference.EvidenceId!.Value.ToString("N") + ".jpg"
                && reference.ContentType == "image/jpeg" && current.Available,
                "settled-change rasters are saved as JPEG sidecars in their own folder");
            var grid = new byte[100]; grid[3] = 0b1010;
            var region = new SettledChangeRegion(1.48, .067, 380, 612, "cushion", 1, 1, 0, 1.9, true, null, ChangeDirection.Arrival,
                .9, .1, 0, .7, 0, 0);
            var evidence = new SettledChangeEvidence(Guid.NewGuid(), 4, SettledChangeStatus.Compared, [], 1, 2, 3, 4, 5, 6,
                new([], .01, 0, .05, 1, [region], grid, 20, 40, 51));
            var pass = new SettledChangePass(SettledChangeState.Settled, .001, 5, 17.5, evidence, true);
            var shadow = new LiveDetectSettledChangeShadow(LiveDetectSettledChangeMode.Shadow, true, 1, 4, true,
                [new("infer_black", "black", 1.48, .067, 4, false, "recheck_pending")], [new("black", 1.48, .067, "inferred", 5, 4)]);
            settledStore.AppendFrame(runId, checkAt, frame with
            {
                SettledChange = LiveDetectLogSettledChange.From("shadow", pass, shadow, [reference, current])
            });
            var settledRecord = new LiveDetectLogStore(settledPath).ReadLast(1).Records.Single();
            Require(settledRecord.SchemaVersion == 15 && settledRecord.Frame?.SettledChange is { Mode: "shadow", State: "Settled", EvidenceIsNew: true,
                    EvidenceSequence: 4, ChainIntact: true } logged
                && logged.Evidence?.Comparison?.ChangeGrid.SequenceEqual(grid) == true
                && logged.Evidence.Comparison.Regions.Single().TableX == 1.48
                && logged.Proposals is [{ Kind: "infer_black", Applied: false }]
                && logged.BallStates is [{ State: "inferred" }] && logged.Rasters?.Length == 2,
                "schema-15 frames carry the settled-change pass, evidence, change grid, shadow proposals and raster references");
            var settledFolder = Path.Combine(Path.GetDirectoryName(settledPath)!, "settled-change");
            Require(Directory.GetFiles(settledFolder).Length == 2, "referenced rasters survive reopening the store");
            var orphan = settledStore.SaveSettledChangeRaster([0xFF, 0xD8, 9], "reference");
            Require(orphan.Available && Directory.GetFiles(settledFolder).Length == 3, "an unreferenced raster is written");
            _ = new LiveDetectLogStore(settledPath);
            Require(Directory.GetFiles(settledFolder).Length == 2 && !File.Exists(Path.Combine(settledFolder, orphan.EvidenceId!.Value.ToString("N") + ".jpg")),
                "an unreferenced raster is pruned when the store reopens");
            string later = JsonSerializer.Serialize(new LiveDetectLogRecord(14, "frame", runId, checkAt, Frame: frame), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Require(!later.Contains("settledChange") && JsonSerializer.Deserialize<LiveDetectLogRecord>(later, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Frame?.SettledChange is null,
                "frames without settled-change evidence omit the field and older records stay readable");
            var capStore = new LiveDetectLogStore(Path.Combine(directory, "cap", "live-detect.jsonl"));
            int saved = 0;
            for (int i = 0; i < LiveDetectLogStore.MaximumSettledChangeFiles + 5; i++)
                if (capStore.SaveSettledChangeRaster([0xFF, 0xD8, (byte)i], "current").Available) saved++;
            Require(saved == LiveDetectLogStore.MaximumSettledChangeFiles
                && capStore.SaveSettledChangeRaster([0xFF, 0xD8], "current").SkipReason == "settled-change storage limit reached",
                "settled-change rasters have their own file cap");
            Require(capStore.SaveFrameEvidence(new EncodedTableFrameEvidence([1, 2, 3], ".png", "image/png"), "initial").Available,
                "full settled-change storage does not block frame evidence");

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
