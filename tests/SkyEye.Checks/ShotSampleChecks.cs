using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Text.Json;
using System.Text.Json.Nodes;

static class ShotSampleChecks
{
    static readonly ClipSaveScope Scope = new(new string('A', 64), Guid.NewGuid().ToString("N"), 1, 3);
    static readonly ShotSampleContext Context = new("calibration-1", 6, "timeline-1", 123, DateTimeOffset.UtcNow, "config-1");
    static Clip Clip() => new(Guid.NewGuid().ToString("N"), "camera clip", 28.4, 1234, new string('B', 64), DateTimeOffset.UtcNow,
        RecordingOrigin: new(Guid.NewGuid().ToString("N"), 123_000, 1920, 1080, 0));
    static ShotSampleRecord Request(string words = "just potted a red") => new(1, Guid.NewGuid(), "pending", "player-reported",
        new(words, DateTimeOffset.UtcNow, [new("outcome", "potted"), new("ball", "red")], []), Scope, Context,
        DateTimeOffset.UtcNow, 30, 5, 2, "The exact shot time is unknown.");
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-samples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ShotSampleStore(Path.Combine(root, "samples"));
            var legacyReport = JsonNode.Parse(JsonSerializer.Serialize(Request().Report, JsonDefaults.Options))!.AsObject();
            legacyReport.Remove("transcriptStatus");
            Check(JsonSerializer.Deserialize<ShotSampleReport>(legacyReport.ToJsonString(), JsonDefaults.Options)?.TranscriptStatus == "completed",
                "older sample reports remain readable after adding transcript status");
            Check(VoiceClipSavePolicy.TryResolve(5, out var shortWindow) && shortWindow == 30
                && VoiceClipSavePolicy.TryResolve(60, out var longWindow) && longWindow == 60
                && !VoiceClipSavePolicy.TryResolve(6, out _), "five-second voice requests use the existing 30-second camera save and longer durations are unchanged");
            var effect = new FixtureEffect(); var journal = new ClipSaveExecutor(Path.Combine(root, "saves"), effect);
            var app = new AppCoordinator(clipSaves: journal);
            Task<CommandReceipt> Save(Guid id, ClipSaveScope scope, CancellationToken ct) => app.DispatchAsync(new(id, CommandOrigin.Sky, app.State.Version, new SaveClip(30, scope)), ct);
            var collector = new ShotSampleCollector(store, (_, _) => Task.CompletedTask);
            var request = Request(); var result = await collector.CollectAsync(request, TimeSpan.FromSeconds(2), Save, CancellationToken.None);
            var loaded = new ShotSampleStore(Path.Combine(root, "samples")).Read(request.OperationId);
            Check(result.Status == "saved" && loaded?.Clip?.Id == result.Clip?.Id && loaded?.Clip?.Sha256 == result.Clip?.Sha256
                && loaded?.Clip?.RecordingOrigin?.StartUs == 123_000 && loaded?.Report.OriginalWords == request.Report.OriginalWords
                && loaded?.Provenance == "player-reported" && loaded?.RequestedSeconds == 30 && loaded?.DesiredSampleSeconds == 5
                && loaded?.Context.CalibrationId == "calibration-1" && loaded?.ActualClipSeconds == 28.4
                && loaded?.ClipShorterThanRequested == true && loaded?.SourceStartUs == 123_000 && loaded?.ShotWindowCoverage == "unknown",
                "sample retains original report, scope, actual clip identity, origin, shorter duration and unknown shot coverage");
            var savedReceipt = journal.FindAcknowledgedByClip(result.Clip!.Id, result.Clip.Sha256);
            Check(savedReceipt?.OperationId == request.OperationId && savedReceipt.Scope == Scope
                && journal.FindAcknowledgedByClip(result.Clip.Id, new string('C', 64)) is null,
                "retrospective attachment requires the matching durable clip ID and checksum");
            Check(!ShotSampleStore.Valid(request.Report with { Fields = [new("ball", "yellow")] })
                && !ShotSampleStore.Valid(request.Report with { Notes = ["a foul"] }), "invented metadata is rejected");
            var miss = request.Report with { OriginalWords = "missed that one", Fields = [new("outcome", "missed")], Notes = [] };
            Check(ShotSampleStore.Valid(miss), "a sparse miss needs no invented ball or ruling");
            var gate = new ShotSampleTranscriptGate();
            gate.Remember("Just potted a red, off the cushion");
            Check(!gate.TryClaim("potted a red", [new("ball", "yellow")], [], out _, out _, out _), "unspoken extracted text is rejected before claiming the transcript");
            Check(gate.TryClaim("potted a red", [new("ball", "red")], ["off the cushion"], out var spoken, out _, out _)
                && spoken?.OriginalWords == "Just potted a red, off the cushion", "the original completed transcript supplies the report");
            Check(!gate.TryClaim("potted a red", [], [], out _, out _, out _), "the same utterance cannot be claimed twice");
            gate.Clear(); Check(!gate.TryClaim("potted a red", [], [], out _, out _, out _), "speech interruption clears the old utterance");
            gate.Remember("save the last five seconds"); var earlierEpoch = gate.Epoch;
            gate.Clear(); gate.Remember("save the last five seconds");
            Check(!gate.TryClaim("five seconds", [], [], out _, out _, out _, earlierEpoch), "a later repeated utterance cannot attach to an earlier save request");
            gate.Remember("missed that one");
            Check(gate.TryClaim("missed", [new("outcome", "missed")], [], out _, out _, out _), "a later report can be collected without match facts");
            gate.Clear(); gate.Append("I broke "); gate.Append("off");
            Check(gate.TryClaim("broke off", [new("shot", "broke off")], [], out var partial, out _, out _)
                && partial?.OriginalWords == "I broke off" && partial.TranscriptStatus == "partial", "streamed player words can be saved with honest partial-transcript status");
            gate.Remember("I broke off");
            Check(!gate.TryClaim("broke off", [], [], out _, out _, out _), "the later final transcript cannot create a duplicate sample");

            var pending = Request(); store.Write(pending);
            var receipt = await Save(pending.OperationId, pending.Scope, CancellationToken.None);
            Check(receipt.SavedClip is not null && store.Recover(journal) == 1
                && store.Read(pending.OperationId)?.Clip?.Id == receipt.SavedClip.Id, "restart recovers a linked clip only from its acknowledged operation");
            var unknown = Request(); store.Write(unknown);
            Check(store.Recover(journal) == 0 && store.Read(unknown.OperationId)?.Status == "pending", "unacknowledged pending samples are never guessed or retried");
            var unlabelled = Request(); var unlabelledReceipt = await Save(unlabelled.OperationId, unlabelled.Scope, CancellationToken.None);
            var verified = journal.FindAcknowledgedByClip(unlabelledReceipt.SavedClip!.Id, unlabelledReceipt.SavedClip.Sha256)!;
            var retrospective = store.AttachToAcknowledgedClip(verified,
                new("break off with red potted", DateTimeOffset.UtcNow, [new("shot", "break off"), new("outcome", "red potted")], [], "typed"));
            Check(retrospective.Retrospective && retrospective.RequestedAt is null && retrospective.DesiredSampleSeconds is null
                && retrospective.Clip?.Id == unlabelledReceipt.SavedClip.Id && retrospective.Report.OriginalWords == "break off with red potted"
                && retrospective.Report.TranscriptStatus == "typed", "a player can attach their words to a verified existing clip without inventing timing");

            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = new ShotSampleCollector(store, async (_, ct) => { entered.SetResult(); await release.Task.WaitAsync(ct); });
            var firstRequest = Request();
            var first = held.CollectAsync(firstRequest, TimeSpan.FromSeconds(2), Save, CancellationToken.None); await entered.Task;
            var added = new ShotSampleReport("I broke off", DateTimeOffset.UtcNow, [new("shot", "broke off")], []);
            Check(store.AppendReport(firstRequest.OperationId, added).AdditionalReports?.Single().OriginalWords == "I broke off",
                "explicit follow-up detail is journalled against the pending sample");
            var overlap = await held.CollectAsync(Request(), TimeSpan.FromSeconds(2), Save, CancellationToken.None);
            Check(overlap.Status == "busy" && effect.Calls == 3, "overlapping report is explicitly rejected without a second save");
            release.SetResult(); var finished = await first;
            Check(finished.Status == "saved" && finished.AdditionalReports?.Single().OriginalWords == "I broke off"
                && store.Read(firstRequest.OperationId)?.AdditionalReports?.Single().OriginalWords == "I broke off",
                "clip acknowledgement preserves follow-up details added during the pending save");
            try { store.AppendReport(firstRequest.OperationId, added with { Notes = ["invented foul"] }); throw new Exception("unsupported note accepted"); }
            catch (ArgumentException) { }
            var failed = await collector.CollectAsync(Request(), TimeSpan.Zero,
                (id, _, _) => Task.FromResult(new CommandReceipt(id, CommandOutcome.Failed, 0, "Camera refused.")), CancellationToken.None);
            var uncertain = await collector.CollectAsync(Request(), TimeSpan.Zero,
                (id, _, _) => Task.FromResult(new CommandReceipt(id, CommandOutcome.Uncertain, 0, "Save result unknown.")), CancellationToken.None);
            Check(failed.Status == "failed" && failed.Clip is null && uncertain.Status == "unknown" && uncertain.Clip is null, "failed and uncertain saves are not represented as linked clips");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var interrupted = await collector.CollectAsync(Request(), TimeSpan.Zero, Save, cancelled.Token);
            Check(interrupted.Status == "failed" && interrupted.Message!.Contains("before"), "pre-save cancellation is reported accurately");
            Console.WriteLine("PASS shot sample transcript validation, durable clip linkage/recovery, overlap, failure and uncertainty");
        }
        finally { Directory.Delete(root, true); }
    }

    sealed class FixtureEffect : IClipSaveEffect
    {
        public int Calls;
        public Task<Clip> SaveAsync(Guid id, SaveClip command, CancellationToken cancellation)
        { Calls++; return Task.FromResult(Clip()); }
    }
}
