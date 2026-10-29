using Microsoft.AspNetCore.Components.AI;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkyEye.Core.Application;
using SkyEye.GameState;
using Microsoft.Extensions.AI;
using SkyEye.Companion.Hybrid;
using SkyEye.Companion.Hybrid.Probes;

if (args.Length == 2 && args[0] == "--replay-live-detect-summary")
{
    LiveDetectCountCandidateChecks.ReplaySummary(args[1]);
    return;
}
if (args.Length == 3 && args[0] == "--replay-live-detect-events" && Guid.TryParse(args[2], out var replayRunId))
{
    LiveDetectTableEventTrackerChecks.Replay(args[1], replayRunId);
    return;
}

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
static Guid OperationId(string callId, string action) => new(SHA256.HashData(Encoding.UTF8.GetBytes(callId + "\n" + action)).AsSpan(0, 16));

LiveDetectCountCandidateChecks.Run();
LiveDetectTableEventTrackerChecks.Run();
LiveDetectShotClipWindowChecks.Run();
LiveDetectLogStoreChecks.Run();

var transcript = new SkyVoiceTranscript();
transcript.BeginSession();
transcript.AddUserTranscript("Show ", final: false);
transcript.AddUserTranscript("Show my shots", final: true);
transcript.BeginResponse("response-voice");
transcript.AddAssistantTranscript("response-voice", "response.output_text.delta", "I found two shots.");
transcript.AddAssistantTranscript("response-voice", "response.output_audio_transcript.delta", "I found two shots.");
transcript.FinishResponse("response-voice", completesTurn: true);
Check(transcript.Text == "You: Show my shots" + Environment.NewLine + "Sky: I found two shots.", "transcript keeps both speakers and prefers the spoken transcript channel");
transcript.BeginSession();
transcript.AddAssistantTranscript("early-response", "response.output_audio_transcript.delta", "A response arrived early.");
transcript.FinishResponse("early-response", completesTurn: true);
transcript.AddUserTranscript("What is on the table?", final: true);
Check(transcript.Text.EndsWith("You: What is on the table?" + Environment.NewLine + "Sky: A response arrived early.", StringComparison.Ordinal), "early response text is ordered after its late user transcript");
for (int i = 0; i < 14; i++)
{
    transcript.AddUserTranscript("Turn " + i, final: true);
    transcript.BeginResponse("bounded-" + i);
    transcript.AddAssistantTranscript("bounded-" + i, "response.output_audio_transcript.delta", "Reply " + i);
    transcript.FinishResponse("bounded-" + i, completesTurn: true);
}
Check(transcript.Text.Split(Environment.NewLine).Length <= 12 && !transcript.Text.Contains("Turn 0", StringComparison.Ordinal), "transcript history remains bounded to recent lines");
transcript.AddUserTranscript("Explain the shot", final: true);
transcript.BeginResponse("interrupted-response");
transcript.AddAssistantTranscript("interrupted-response", "response.output_audio_transcript.delta", "It looks like ");
transcript.InterruptResponse("interrupted-response");
transcript.AddUserTranscript("Actually, stop", final: true);
Check(!transcript.Text.Contains("It looks like", StringComparison.Ordinal) && transcript.Text.EndsWith("You: Actually, stop", StringComparison.Ordinal), "interruption drops partial speech and starts the new user turn cleanly");
transcript.BeginSession();
Check(transcript.Text.Contains("New Sky conversation", StringComparison.Ordinal), "transcript remains visible in memory across reconnects with a separator");
Console.WriteLine("PASS bounded Sky transcript preserves user/assistant turns, event ordering, and spoken text");

var gameApp = new AppCoordinator();
var gameTools = new SkyVoiceGameTools(() => gameApp.State,
    (envelope, token) => gameApp.DispatchAsync(envelope, token), OperationId);
var aggregateTable = TableState.Empty with
{
    Motion = TableMotion.Settled, Uncertain = false,
    GroupCounts = ImmutableDictionary<BallColour, Fact<int>>.Empty
        .Add(BallColour.Red, new(7, Provenance.Observed, null, Guid.NewGuid()))
        .Add(BallColour.Yellow, new(6, Provenance.Observed, null, Guid.NewGuid()))
        .Add(BallColour.Cue, new(1, Provenance.Observed, null, Guid.NewGuid()))
        .Add(BallColour.Black, new(1, Provenance.Observed, null, Guid.NewGuid()))
};
var inventoryRead = new SkyVoiceGameTools(() => gameApp.State with { Game = gameApp.State.Game with { Table = aggregateTable } },
    (envelope, token) => gameApp.DispatchAsync(envelope, token), OperationId);
var voiceInventory = JsonSerializer.SerializeToElement(inventoryRead.Read(CancellationToken.None)).GetProperty("table").GetProperty("inventory");
Check(voiceInventory.GetProperty("reds").GetInt32() == 7 && voiceInventory.GetProperty("yellows").GetInt32() == 6
    && voiceInventory.GetProperty("cueBalls").GetInt32() == 1 && voiceInventory.GetProperty("blackBalls").GetInt32() == 1
    && voiceInventory.GetProperty("identityFree").GetBoolean(), "Sky reads aggregate inventory even when no individual ball IDs are available");
var newMatch = await gameTools.StartMatchAsync("match-start", 0, "Richard", "Alex", 3, CancellationToken.None);
Check(JsonSerializer.SerializeToElement(newMatch).GetProperty("succeeded").GetBoolean() && gameApp.State.Game.Version == 1,
    "voice starts a match through the canonical application writer");
var staleFrame = await gameTools.StartFrameAsync("frame-stale", 0, "Richard", CancellationToken.None);
Check(!JsonSerializer.SerializeToElement(staleFrame).GetProperty("succeeded").GetBoolean() && gameApp.State.Game.Version == 1,
    "stale voice game versions are refused without changing state");
var newFrame = await gameTools.StartFrameAsync("frame-start", 1, "Richard", CancellationToken.None);
Check(JsonSerializer.SerializeToElement(newFrame).GetProperty("succeeded").GetBoolean() && gameApp.State.Game.Version == 2,
    "voice starts a frame by the exact current player name");
var colours = await gameTools.DeclareColoursAsync("colours", 2, "red", "Richard", CancellationToken.None);
Check(JsonSerializer.SerializeToElement(colours).GetProperty("succeeded").GetBoolean()
    && gameApp.State.Match.Players.Single(player => player.Name == "Richard").Group == "reds"
    && gameApp.State.Match.Players.Single(player => player.Name == "Alex").Group == "yellows",
    "voice group declaration uses the rules reducer and complementary colours");
var turnChange = await gameTools.CorrectAsync("turn", 3, "turn", "Alex", null, null, null, null, CancellationToken.None);
Check(JsonSerializer.SerializeToElement(turnChange).GetProperty("succeeded").GetBoolean()
    && gameApp.State.Match.Name(gameApp.State.Game.Frame!.CurrentPlayer.Value) == "Alex",
    "voice turn correction updates canonical GameState");
var undone = await gameTools.UndoAsync("undo", 4, CancellationToken.None);
Check(JsonSerializer.SerializeToElement(undone).GetProperty("succeeded").GetBoolean()
    && gameApp.State.Match.Name(gameApp.State.Game.Frame!.CurrentPlayer.Value) == "Richard",
    "voice undo reverses the latest eligible declared decision");
var declaredShot = await gameTools.RecordShotAsync("shot", 5,
    new DeclaredShot(false, DeclaredLegality.Unknown), replaceLatest: false, CancellationToken.None);
Check(JsonSerializer.SerializeToElement(declaredShot).GetProperty("succeeded").GetBoolean()
    && gameApp.State.Match.Rules is { Pending: true } && gameApp.State.Match.CorrectableShotInputId is not null,
    "voice records only declared shot facts and exposes unresolved rules as pending");
var correctedShot = await gameTools.RecordShotAsync("shot-correction", 6,
    new DeclaredShot(false, DeclaredLegality.Legal), replaceLatest: true, CancellationToken.None);
Check(JsonSerializer.SerializeToElement(correctedShot).GetProperty("succeeded").GetBoolean()
    && gameApp.State.Match.CorrectableShotInputId == OperationId("shot-correction", "replace-shot"),
    "voice replaces only the latest current-frame shot decision");
var darkAwareTools = new SkyVoiceGameTools(() => gameApp.State,
    (envelope, token) => gameApp.DispatchAsync(envelope, token), OperationId,
    () => (false, "Unavailable during a dark session."));
var darkAwareMatch = JsonSerializer.SerializeToElement(darkAwareTools.Read(CancellationToken.None));
Check(!darkAwareMatch.GetProperty("physicsAvailable").GetBoolean()
    && darkAwareMatch.GetProperty("physicsStatus").GetString() == "Unavailable during a dark session.",
    "Sky match-state reads carry current physics availability and its limitation");
Console.WriteLine("PASS Sky match tools read and update canonical GameState with version fences, declared facts and reversible corrections");

using var client = new ProbeChatClient(TimeSpan.Zero);
using var agent = ProbeAgent.Create(client);
var blocks = new List<ContentBlock>(); bool pending = false;
await foreach (var block in agent.SendMessageAsync(new(ChatRole.User, "fixture")))
{
    blocks.Add(block);
    if (block is ProbeShotBlock shot && !shot.HasResult) pending = true;
}
var tool = blocks.OfType<ProbeShotBlock>().Single();
Check(pending && tool.HasResult && tool.Group == "red" && tool.CandidateCount == 3, "typed tool pending/result mapping");
Check(agent.State.Value == new ProbeState(1, "red", 3) && client.CompletedOperations == 1, "application snapshot mapping");
Check(blocks.OfType<RichContentBlock>().Any(b => b.RawText.Contains("Three fixture choices")), "streamed text update");
Console.WriteLine("PASS real Blazor AI streaming, source-generated typed tool block and observable state mapping");

using var voiceClient = new SkyVoiceProjectionClient();
UIAgent<ProbeState> voiceAgent = new(voiceClient, options => options.AddGeneratedToolBlocks(), new ProbeState());
using var voiceContext = new AgentContext(voiceAgent);
var voiceBlocks = new System.Collections.Concurrent.ConcurrentQueue<ContentBlock>();
using var voiceRegistration = voiceContext.RegisterOnBlockAdded((_, block) => voiceBlocks.Enqueue(block));
Check(voiceClient.BeginTurn("Show my shots") is not null, "native transcript opens an Agent turn");
var voiceTurn = Task.Run(() => voiceContext.SendMessageAsync(new ChatMessage(ChatRole.User, "Show my shots")));
Check(voiceClient.StartResponse("response-1"), "native response correlates with the active Agent turn");
Check(voiceClient.PublishAssistantText("response-1", "Checking the table."), "assistant transcript streams to Agent");
Check(voiceClient.PublishToolCall("response-1", "call-1", "suggest_shots", "{}"), "native function call becomes an Agent tool block");
Check(!voiceClient.PublishToolCall("response-1", "call-1", "suggest_shots", "{}"), "duplicate native function call is suppressed");
Check(voiceClient.PublishToolResult("call-1", "{\"shown\":true}"), "native function result pairs with its call");
Check(!voiceClient.PublishToolResult("call-1", "{\"shown\":true}"), "duplicate native result is suppressed");
Check(voiceClient.PublishToolCall("response-1", "call-2", "select_shot_by_number", "{\"number\":2}"), "typed native arguments project to the selection block");
Check(voiceClient.PublishToolResult("call-2", "{\"selected\":true}"), "selection result pairs with its correlated call");
Check(voiceClient.PublishToolCall("response-1", "call-camera", "connect_camera", "{\"camera_index\":1}"), "camera connection request becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-camera", "{\"connected\":true}"), "camera connection result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-live-session", "host_live_session", "{}"), "hosted live session request becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-live-session", "{\"ready\":true}"), "hosted live session result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-host", "claim_camera_host", "{}"), "host claim becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-host", "{\"host\":true}"), "host claim result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-capture", "start_capture", "{}"), "capture start becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-capture", "{\"succeeded\":true}"), "capture result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-picker", "choose_chromecast", "{}"), "native Chromecast picker request becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-picker", "{\"pickerOpened\":true}"), "Cast picker result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-cast", "cast_live_table", "{}"), "live Cast request becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-cast", "{\"playing\":true}"), "live Cast result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-match-state", "get_match_state", "{}"), "match state read becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-match-state", "{\"gameVersion\":3}"), "match state result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-match-correct", "correct_match_state", "{\"kind\":\"turn\",\"player_name\":\"Alex\",\"expected_game_version\":3}"), "match correction becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-match-correct", "{\"succeeded\":true}"), "match correction result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-shot", "record_declared_shot", "{\"legality\":\"Unknown\",\"expected_game_version\":3}"), "declared shot becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-shot", "{\"succeeded\":true}"), "declared shot result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-save", "save_recent_clip", "{\"seconds\":30}"), "saved clip request becomes a typed voice block");
Check(voiceClient.PublishToolCall("response-1", "call-save-five", "save_recent_clip", "{\"seconds\":5}"), "five-second voice request remains representable");
Check(voiceClient.PublishToolResult("call-save-five", "{\"pending\":true,\"saved\":false}"), "short save pending result remains unconfirmed");
Check(voiceClient.PublishToolCall("response-1", "call-sample-mode", "set_sample_collection", "{\"enabled\":true}"), "sample mode becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-sample-mode", "{\"enabled\":true}"), "sample mode result is correlated");
Check(voiceClient.PublishToolCall("response-1", "call-sample", "save_shot_sample", "{\"reported_words\":\"potted a red\",\"fields\":[{\"kind\":\"ball\",\"text\":\"red\"}]}"), "shot sample becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-sample", "{\"pending\":true,\"saved\":false}"), "sample pending result remains distinct from saved");
Check(voiceClient.PublishToolCall("response-1", "call-sample-details", "add_sample_details", "{\"operation_id\":\"operation-1\",\"reported_words\":\"I broke off\"}"), "sample follow-up becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-sample-details", "{\"added\":true}"), "sample detail result is correlated");
Check(voiceClient.PublishToolCall("response-1", "call-attach", "attach_report_to_saved_clip", "{\"clip_id\":\"clip-1\",\"reported_words\":\"red potted\"}"), "saved clip attachment becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-attach", "{\"attached\":true}"), "saved clip attachment result is correlated");
Check(voiceClient.PublishToolResult("call-save", "{\"saved\":true}"), "saved clip result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-replay", "replay_recent", "{\"seconds\":30}"), "recent replay request becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-replay", "{\"playing\":true}"), "recent replay result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-clips", "get_saved_clips", "{}"), "saved clip list becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-clips", "{\"available\":true}"), "saved clip list result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-play-clip", "play_saved_clip", "{\"clip_number\":2,\"target\":\"tv\"}"), "saved clip playback becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-play-clip", "{\"playing\":true}"), "saved clip playback result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-tv-control", "control_tv_replay", "{\"action\":\"seek\",\"seconds\":42}"), "TV replay control becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-tv-control", "{\"confirmed\":true}"), "TV replay control result pairs with its call");
Check(voiceClient.PublishToolCall("response-1", "call-return-live", "return_to_live", "{}"), "return-live action becomes a typed voice block");
Check(voiceClient.PublishToolResult("call-return-live", "{\"confirmed\":true}"), "return-live result pairs with its call");
Check(voiceClient.FinishResponse("response-1", completesTurn: false), "tool-call response ends without closing the user turn");
Check(voiceClient.StartResponse("response-2"), "tool follow-up response stays in the same Agent turn");
Check(voiceClient.PublishAssistantText("response-2", "Three choices are ready."), "follow-up speech streams to Agent");
Check(voiceClient.CompleteResponse("response-2"), "final native response closes the Agent stream");
await voiceTurn.WaitAsync(TimeSpan.FromSeconds(5));
var projectedTool = voiceBlocks.OfType<SkySuggestShotsBlock>().Single();
Check(projectedTool.HasResult && projectedTool.ResultText == "{\"shown\":true}", "generated Sky tool block shows the native result");
Check(voiceBlocks.OfType<SkySelectShotNumberBlock>().Single() is { HasResult: true, Number: 2 }, "generated Sky selection block retains its typed number");
Check(voiceBlocks.OfType<SkyConnectCameraBlock>().Single() is { HasResult: true, CameraIndex: 1 }, "generated camera block retains the discovered camera index and operation result");
Check(voiceBlocks.OfType<SkyHostLiveSessionBlock>().Single() is { HasResult: true, ResultText: "{\"ready\":true}" }, "generated hosted-session block exposes its operation result");
Check(voiceBlocks.OfType<SkyClaimCameraHostBlock>().Single().HasResult && voiceBlocks.OfType<SkyStartCaptureBlock>().Single().HasResult, "generated host and capture blocks expose their operation results");
Check(voiceBlocks.OfType<SkyChooseChromecastBlock>().Single().HasResult && voiceBlocks.OfType<SkyCastLiveTableBlock>().Single().HasResult, "generated Chromecast and live playback blocks expose correlated results");
Check(voiceBlocks.OfType<SkyMatchStateBlock>().Single() is { HasResult: true, ResultText: "{\"gameVersion\":3}" }, "generated match state block shows the canonical snapshot result");
Check(voiceBlocks.OfType<SkyCorrectMatchStateBlock>().Single() is { HasResult: true, Kind: "turn", ExpectedGameVersion: 3 }, "generated match correction block retains its versioned arguments");
Check(voiceBlocks.OfType<SkyRecordDeclaredShotBlock>().Single() is { HasResult: true, Legality: "Unknown", ExpectedGameVersion: 3 }, "generated shot block retains explicit declaration fields");
Check(voiceBlocks.OfType<SkySaveRecentClipBlock>().Any(block => block is { HasResult: true, Seconds: 30 })
    && voiceBlocks.OfType<SkyReplayRecentBlock>().Single() is { HasResult: true, Seconds: 30 }, "generated media blocks show their bounded actions and results");
Check(voiceBlocks.OfType<SkySaveRecentClipBlock>().Any(block => block is { HasResult: true, Seconds: 5, ResultText: "{\"pending\":true,\"saved\":false}" })
    && voiceBlocks.OfType<SkyAddSampleDetailsBlock>().Single() is { HasResult: true, OperationId: "operation-1", ReportedWords: "I broke off" }, "short request and later player details retain their identity and pending status");
Check(voiceBlocks.OfType<SkyAttachReportToSavedClipBlock>().Single() is { HasResult: true, ClipId: "clip-1", ReportedWords: "red potted" }, "retrospective report retains the selected clip and player words");
Check(voiceBlocks.OfType<SkySetSampleCollectionBlock>().Single() is { HasResult: true, Enabled: true }
    && voiceBlocks.OfType<SkySaveShotSampleBlock>().Single() is { HasResult: true, ReportedWords: "potted a red", ResultText: "{\"pending\":true,\"saved\":false}" }, "sample tool blocks retain their mode, quotation and pending result");
Check(voiceBlocks.OfType<SkySavedClipsBlock>().Single().HasResult
    && voiceBlocks.OfType<SkyPlaySavedClipBlock>().Single() is { HasResult: true, ClipNumber: 2, Target: "tv" }, "generated saved-clip blocks retain current target selection");
Check(voiceBlocks.OfType<SkyControlTvReplayBlock>().Single() is { HasResult: true, Action: "seek", Seconds: 42 }
    && voiceBlocks.OfType<SkyReturnToLiveBlock>().Single().HasResult, "generated TV replay controls and return-live expose receiver results");
Check(voiceBlocks.OfType<RichContentBlock>().Any(block => block.RawText.Contains("Three choices are ready")), "native assistant transcript becomes rich content");
Check(!voiceClient.PublishAssistantText("response-2", "stale"), "completed response cannot append stale text");
voiceAgent.Dispose();
Console.WriteLine("PASS native Sky transcript, correlated tool/result blocks, follow-up response and duplicate suppression");

using var bufferedClient = new SkyVoiceProjectionClient();
UIAgent<ProbeState> bufferedAgent = new(bufferedClient, options => options.AddGeneratedToolBlocks(), new ProbeState());
using var bufferedContext = new AgentContext(bufferedAgent);
var bufferedBlocks = new System.Collections.Concurrent.ConcurrentQueue<ContentBlock>();
using var bufferedRegistration = bufferedContext.RegisterOnBlockAdded((_, block) => bufferedBlocks.Enqueue(block));
Check(bufferedClient.StartResponse("early-response"), "native response can arrive before transcript completion");
Check(bufferedClient.PublishAssistantText("early-response", "I have your table."), "early response text is buffered");
Check(bufferedClient.PublishToolCall("early-response", "early-call", "get_table_state", "{}"), "early native tool call is buffered");
Check(bufferedClient.PublishToolResult("early-call", "{\"fresh\":true}"), "early native tool result is buffered");
Check(bufferedClient.FinishResponse("early-response", completesTurn: false), "early tool response is marked finished");
Check(bufferedClient.StartResponse("early-followup"), "early follow-up response is buffered in order");
Check(bufferedClient.PublishAssistantText("early-followup", "The layout is fresh."), "early follow-up text is buffered");
Check(bufferedClient.FinishResponse("early-followup", completesTurn: true), "early final response is marked complete");
Check(bufferedClient.BeginTurn("Check this layout") is not null, "late transcript attaches all earlier response events");
await bufferedContext.SendMessageAsync(new ChatMessage(ChatRole.User, "Check this layout")).WaitAsync(TimeSpan.FromSeconds(5));
Check(bufferedBlocks.OfType<SkyTableStateBlock>().Single() is { HasResult: true, ResultText: "{\"fresh\":true}" }, "late transcript retains the buffered typed tool result");
Check(bufferedBlocks.OfType<RichContentBlock>().Any(block => block.RawText.Contains("The layout is fresh")), "late transcript retains buffered assistant text");
bufferedAgent.Dispose();
Console.WriteLine("PASS response-before-transcript ordering buffers text, tool results and the final response");

var interruptedClient = new SkyVoiceProjectionClient();
UIAgent<ProbeState> interruptedVoiceAgent = new(interruptedClient, options => options.AddGeneratedToolBlocks(), new ProbeState());
Check(interruptedClient.BeginTurn("Stop that answer") is not null, "interrupted voice turn is queued");
var interruptedUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var interruptedTurn = Task.Run(async () =>
{
    await foreach (var _ in interruptedVoiceAgent.SendMessageAsync(new(ChatRole.User, "Stop that answer")))
        interruptedUpdate.TrySetResult();
});
Check(interruptedClient.StartResponse("response-old"), "interrupted response is correlated");
Check(interruptedClient.PublishAssistantText("response-old", "The answer is starting."), "interrupted stream receives an initial update");
await interruptedUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));
interruptedClient.InterruptCurrent();
bool streamStopped = false;
try { await interruptedTurn.WaitAsync(TimeSpan.FromSeconds(5)); streamStopped = true; }
catch (OperationCanceledException) { streamStopped = true; }
Check(streamStopped && !interruptedClient.PublishAssistantText("response-old", "stale response"), "interruption ends UI stream and rejects late response events");
Check(interruptedClient.BeginTurn("Fresh reply") is not null, "new user transcript opens a new turn after interruption");
var recoveredTurn = Task.Run(async () =>
{
    await foreach (var _ in interruptedVoiceAgent.SendMessageAsync(new(ChatRole.User, "Fresh reply"))) { }
});
Check(interruptedClient.StartResponse("response-new"), "new response maps to the replacement turn");
interruptedClient.InterruptResponse("response-old");
Check(interruptedClient.PublishAssistantText("response-new", "The fresh response continues."), "late cancellation for an old response cannot cancel the new turn");
Check(interruptedClient.CompleteResponse("response-new"), "replacement turn completes normally");
await recoveredTurn.WaitAsync(TimeSpan.FromSeconds(5));
interruptedVoiceAgent.Dispose();
Console.WriteLine("PASS native voice interruption cancels the Agent stream, drops late events and preserves the next turn");

using var slowClient = new ProbeChatClient(TimeSpan.FromSeconds(2));
using var slowAgent = ProbeAgent.Create(slowClient);
using var cancellation = new CancellationTokenSource();
bool cancelled = false;
try
{
    await foreach (var block in slowAgent.SendMessageAsync(new(ChatRole.User, "cancel fixture"), cancellation.Token))
        if (block is ProbeShotBlock) cancellation.Cancel();
}
catch (OperationCanceledException) { cancelled = true; }
Check(cancelled && slowClient.CompletedOperations == 0 && slowAgent.State.Value.Version == 0, "cancel before fixture effect");
Console.WriteLine("PASS cancellation leaves canonical fixture state unchanged");

using var previewClient = new ProbeChatClient(TimeSpan.Zero);
using var previewAgent = ProbeAgent.Create(previewClient);
using var context = new AgentContext(previewAgent);
TaskCompletionSource<UIActionBlock> actionReady = new();
using var registration = context.RegisterOnBlockAdded((_, block) =>
{
    if (block is UIActionBlock action) actionReady.TrySetResult(action);
});
async Task<(Task Turn, UIActionBlock Action)> Propose()
{
    actionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var turn = context.SendMessageAsync("Propose a fixture group");
    var action = await actionReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!turn.IsCompleted && previewAgent.State.HasPendingPredictiveState, "boundary retains preview until human decision");
    return (turn, action);
}
async Task Decide((Task Turn, UIActionBlock Action) proposal, bool accept)
{
    previewClient.ProposalDecision = accept;
    await proposal.Action.InvokeAsync();
    await proposal.Turn.WaitAsync(TimeSpan.FromSeconds(5));
    Check(previewAgent.State.Value == previewClient.State && !previewAgent.State.HasPendingPredictiveState, "completed boundary reflects canonical state");
}
var first = await Propose();
Check(previewAgent.State.HasPendingPredictiveState && previewAgent.State.Value.Group == "yellow" && previewClient.State.Group == "open", "preview is separate from canonical fixture");
await Decide(first, false);
Check(!previewAgent.State.HasPendingPredictiveState && previewAgent.State.Value.Group == "open" && previewClient.State.Version == 0, "reject preserves original state");
var second = await Propose();
await Decide(second, true);
Check(previewClient.State.Version == 1 && previewClient.State.Group == "yellow", "accept commits once");
await second.Action.InvokeAsync();
Check(previewClient.State.Version == 1, "duplicate UI action does not commit twice");
var stale = await Propose(); previewClient.ChangeLayout();
await Decide(stale, true);
Check(previewAgent.State.Value == previewClient.State && previewClient.State.Version == 2, "obsolete proposal cannot apply or roll back a newer layout");
var rejectChanged = await Propose(); previewClient.ChangeLayout(); await Decide(rejectChanged, false);
Check(previewAgent.State.Value.Version == 3, "reject after concurrent change preserves newest canonical state");
var cancelledPreview = await Propose();
await context.CancelAsync();
await cancelledPreview.Turn;
ProbeAgent.Resolve(previewAgent, previewClient, false);
Check(previewClient.State.Version == 3 && !previewAgent.State.HasPendingPredictiveState, "cancel preview preserves canonical state");
Console.WriteLine("PASS real AgentContext predictive preview, registered UI action, accept/reject, duplicate and stale version handling");

// Drive native media effect selection through interruption and competing requests.
var media = new SkyEye.Companion.Hybrid.PlaybackSelection();
var liveGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.Live);
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, null), "inactive camera waits without cancelling live selection");
Check(media.Kind == SkyEye.Companion.Hybrid.ViewingSource.Live && media.Source is null, "waiting camera retains viewing intent");
Check(media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "active camera starts after waiting");
media.Started();
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "unchanged observation or sheet render never reloads media");
media.Suspend();
Check(media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "foreground or handler replacement restarts validated source");
media.Started();
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "resume restarts once");
media.Stop();
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "obsolete-source") && media.Source is null, "late refresh cannot undo stop");
var previewGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.Preview);
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "obsolete-source"), "live reply cannot replace selected preview");
Check(media.Observe(previewGeneration, SkyEye.Companion.Hybrid.ViewingSource.Preview, "private-local-file"), "current preview publishes");
media.Started();
var nextGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.Live);
Check(!media.Observe(previewGeneration, SkyEye.Companion.Hybrid.ViewingSource.Preview, "late-export-file"), "late preview cannot replace reconnect");
Check(!media.Observe(nextGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, null) && media.NeedsStart, "loss of reachability removes media but permits validated recovery");
Check(media.Observe(nextGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "new-live-source"), "validated recovery can start after reachability loss");
Console.WriteLine("PASS portrait media selection: inactive-to-active, stable refresh, resume/detach, stop, obsolete live/preview and recovery");

var tvReplay = new SkyEye.Companion.Hybrid.TvReplaySelection();
var tvCamera = Guid.NewGuid(); var tvClip = Guid.NewGuid().ToString("N");
var firstTvRequest = tvReplay.Begin(tvCamera, tvClip, "Saved shot", 30);
Check(!tvReplay.Confirmed && tvReplay.Selected && tvReplay.PositionSeconds == 0, "TV load remains pending until receiver proof");
Check(!tvReplay.Observe(firstTvRequest, Guid.NewGuid().ToString("N"), 3000, 30000, 2, 0), "unrelated receiver clip rejected");
Check(tvReplay.Observe(firstTvRequest, tvClip, 3100, 30000, 2, 0) && tvReplay.PositionSeconds == 3, "receiver position accepted for current request");
Check(!tvReplay.Observe(firstTvRequest, tvClip, 3200, 30000, 2, 0), "subsecond progress does not churn UI");
var nextTvRequest = tvReplay.Begin(tvCamera, tvClip, "Replay again", 30);
Check(!tvReplay.Observe(firstTvRequest, tvClip, 8000, 30000, 2, 0) && !tvReplay.Confirmed, "late completion from same clip previous load rejected");
Check(tvReplay.Observe(nextTvRequest, tvClip, 30000, 30000, 1, 1) && tvReplay.IdleReason == 1, "receiver completion preserved without restarting clip");
Check(!tvReplay.Observe(nextTvRequest, tvClip, 0, 30000, 1, 1) && tvReplay.PositionSeconds == 30, "receiver idle reset cannot erase finished position");
tvReplay.Clear();
Check(!tvReplay.Observe(nextTvRequest, tvClip, 4000, 30000, 2, 0) && !tvReplay.Selected, "return-live and disconnect reject late replay progress");
Console.WriteLine("PASS buffered TV replay: pending load, scoped receiver progress, duplicate, same-clip replacement, finish and return-live");

var commandTime = DateTimeOffset.UnixEpoch;
var commandRequest = tvReplay.Begin(tvCamera, tvClip, "Control fixture", 30.4);
var pauseId = Guid.NewGuid().ToString("N");
Check(tvReplay.BeginCommand(commandRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime) is null, "unconfirmed load cannot receive replay commands");
tvReplay.Observe(commandRequest, tvClip, 6000, 30400, 2, 0);
Check(tvReplay.BeginCommand(firstTvRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime) is null, "old rendered request cannot control replacement clip");
var pauseCommand = tvReplay.BeginCommand(commandRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime);
Check(pauseCommand is not null && tvReplay.CommandPending, "current playing clip admits pause");
Check(tvReplay.BeginCommand(commandRequest, Guid.NewGuid().ToString("N"), SkyEye.Companion.Hybrid.TvReplayAction.Seek, 10, commandTime) is null, "competing command waits for receiver proof");
Check(!tvReplay.ObserveCommand(commandRequest, pauseId, "accepted") && tvReplay.CommandPending, "accepted pause does not invent paused state");
Check(tvReplay.Observe(commandRequest, tvClip, 6000, 30400, 3, 0) && tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "pause confirms only with receiver paused state");
Check(tvReplay.BeginCommand(commandRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Resume, 0, commandTime) is null, "duplicate acknowledged command ID never resends");
var seekId = Guid.NewGuid().ToString("N");
var seekCommand = tvReplay.BeginCommand(commandRequest, seekId, SkyEye.Companion.Hybrid.TvReplayAction.Seek, 31, commandTime);
Check(seekCommand?.PositionMs == 30150, "end seek bounded inside fractional clip duration");
Check(!tvReplay.ObserveCommand(commandRequest, seekId, "accepted"), "accepted seek still waits for position");
Check(!tvReplay.Observe(commandRequest, tvClip, 6000, 30400, 3, 0) && tvReplay.CommandPending, "unchanged receiver position cannot confirm seek");
Check(tvReplay.Observe(commandRequest, tvClip, 30150, 30400, 3, 0) && tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "seek preserves pause and confirms target position");
var resumeId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, resumeId, SkyEye.Companion.Hybrid.TvReplayAction.Resume, 0, commandTime);
tvReplay.Observe(commandRequest, tvClip, 30150, 30400, 2, 0);
Check(tvReplay.CommandPending && tvReplay.ObserveCommand(commandRequest, resumeId, "accepted"), "status before callback remains pending then confirms resume");
var playingSeekId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, playingSeekId, SkyEye.Companion.Hybrid.TvReplayAction.Seek, 5, commandTime);
Check(!tvReplay.ObserveCommand(commandRequest, playingSeekId, "accepted") && tvReplay.CommandPending, "playing seek requires target proof");
Check(tvReplay.Observe(commandRequest, tvClip, 5500, 30400, 2, 0) && tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "playing seek confirms position while preserving play state");
var rejectedId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, rejectedId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime);
Check(!tvReplay.ObserveCommand(firstTvRequest, rejectedId, "rejected") && tvReplay.CommandPending, "wrong-load rejection ignored");
Check(tvReplay.ObserveCommand(commandRequest, rejectedId, "rejected") && tvReplay.PlayerState == 2, "rejection preserves actual receiver state");
Check(tvReplay.BeginCommand(commandRequest, Guid.NewGuid().ToString("N"), SkyEye.Companion.Hybrid.TvReplayAction.Seek, -1, commandTime) is null, "negative seek rejected");
Check(tvReplay.BeginCommand(commandRequest, Guid.NewGuid().ToString("N"), SkyEye.Companion.Hybrid.TvReplayAction.Seek, long.MaxValue, commandTime) is null, "overflowing seek rejected");
var timeoutId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, timeoutId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime);
Check(!tvReplay.ExpireCommand(commandTime.AddSeconds(9)) && tvReplay.ExpireCommand(commandTime.AddSeconds(10)), "unconfirmed command expires as unknown");
Check(tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Unknown && !tvReplay.ObserveCommand(commandRequest, timeoutId, "accepted"), "late acknowledgement cannot rewrite timeout outcome");
tvReplay.Clear();
Check(!tvReplay.ObserveCommand(commandRequest, timeoutId, "accepted") && !tvReplay.CanControl, "return-live/disconnect cancels pending clip commands");
var liveProof = new SkyEye.Companion.Hybrid.TvLiveSelection();
var liveRequest = liveProof.Begin(tvCamera, commandTime);
Check(!liveProof.Observe(liveRequest, "clip", 2) && liveProof.Observe(liveRequest, "scene", 4)
    && liveProof.Outcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Pending && liveProof.PlayerState == 4, "return-live records buffering but waits for matching PLAYING proof");
Check(!liveProof.Observe(Guid.NewGuid().ToString("N"), "camera", 2), "obsolete live media cannot confirm current request");
Check(liveProof.Expire(commandTime.AddSeconds(20)) && liveProof.Outcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Unknown, "buffered live acknowledgement times out honestly");
Check(liveProof.Observe(liveRequest, "scene", 2) && liveProof.Outcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "later current live proof can establish playback");
Check(liveProof.Observe(liveRequest, "scene", 4) && liveProof.PlayerState == 4 && liveProof.Outcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed,
    "later live buffering remains visible without erasing fulfilled command proof");
liveProof.Clear();
Check(!liveProof.Observe(liveRequest, "camera", 2), "camera switch or clip replacement clears live proof");
Console.WriteLine("PASS TV controls: scoped admission, single flight, duplicates, receiver pause/resume/seek proof, fractional end bounds, rejection, timeout and return-live confirmation");

var clipGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.LocalClip);
Check(media.Observe(clipGeneration, SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "private-portrait-file"), "offline clip selection starts independently of camera reachability");
media.Started();
Check(!media.Observe(nextGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "late-camera-source") && media.Kind == SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "camera refresh cannot replace offline playback");
media.Suspend();
Check(media.Observe(clipGeneration, SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "private-portrait-file"), "offline clip survives foreground or handler replacement");
media.Started(); media.Stop();
Check(!media.Observe(clipGeneration, SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "private-portrait-file") && media.Source is null, "late local selection cannot undo stop");
Console.WriteLine("PASS offline clip selection: camera-independent playback, obsolete camera response, foreground recovery and stop");

var elapsed = TimeSpan.Zero;
var chrome = new SkyEye.Companion.Hybrid.ControlChrome(() => elapsed);
Check(chrome.Pinned, "menu starts pinned by default");
elapsed = TimeSpan.FromSeconds(30); chrome.Tick();
Check(chrome.Visible, "empty or buffering table keeps connect controls visible");
chrome.SetPinned(false); chrome.CanFade = true; chrome.Reveal(); elapsed += TimeSpan.FromSeconds(4.9); chrome.Tick();
Check(chrome.Visible, "short inactivity threshold keeps controls available");
elapsed += TimeSpan.FromSeconds(.1); chrome.Tick();
Check(!chrome.Visible, "playing table rests after five seconds");
chrome.SetPinned(true); elapsed += TimeSpan.FromSeconds(30); chrome.Tick();
Check(chrome.Visible, "pinned menu remains visible during playback");
chrome.SetPinned(false); elapsed += TimeSpan.FromSeconds(4.9); chrome.Tick();
Check(chrome.Visible, "unpinning gives a fresh interaction window");
elapsed += TimeSpan.FromSeconds(.1); chrome.Tick();
Check(!chrome.Visible, "deliberately unpinned menu rests after five seconds");
chrome.SetEditing(true); elapsed += TimeSpan.FromMinutes(2); chrome.Tick();
Check(chrome.Visible, "sheet editing pins controls beyond inactivity timeout");
chrome.SetEditing(false); elapsed += TimeSpan.FromSeconds(4); chrome.Tick();
Check(chrome.Visible, "closing sheet gives a fresh interaction window");
elapsed += TimeSpan.FromSeconds(2); chrome.Tick(); Check(!chrome.Visible, "controls can rest again after editing");
chrome.SetAttention(true); elapsed += TimeSpan.FromMinutes(2); chrome.Tick();
Check(chrome.Visible, "actionable error pins controls");
chrome.SetAttention(false); elapsed += TimeSpan.FromSeconds(6); chrome.Tick(); Check(!chrome.Visible, "acknowledged message releases pin");
chrome.SetAccessibility(true); elapsed += TimeSpan.FromMinutes(2); chrome.Tick();
Check(chrome.Visible, "touch exploration never fades available controls");
chrome.SetAccessibility(false); elapsed += TimeSpan.FromSeconds(6); chrome.Tick();
Check(!chrome.Visible, "leaving accessibility interaction returns to normal fade behavior");
chrome.Reveal(); Check(chrome.Visible, "tap or swipe restores controls");
Console.WriteLine("PASS transient controls: idle/buffering, inactivity, editing, error acknowledgment, touch exploration and reveal");

var fit = SkyEye.Companion.Hybrid.SceneProjection.Portrait(400, 600, 1920, 1080);
var sourcePoint = new SkyEye.Core.Point(.2, .7);
var mappedPoint = fit.Screen(sourcePoint);
Check((fit.Image(mappedPoint)!.Value - sourcePoint).Length < 1e-10, "portrait frozen-frame mapping round trips touch and drawing");
Check(fit.Image(new(0, 300)) is null, "letterbox space cannot become a corner or ball");
Check(fit.Screen(new(0, 0)).X > fit.Screen(new(0, 1)).X && fit.Screen(new(1, 0)).Y > fit.Screen(new(0, 0)).Y, "camera corners rotate clockwise into portrait review order");
var corners = new SkyEye.Core.Point[] { new(.1, .1), new(.9, .1), new(.9, .9), new(.1, .9) };
var draft = new SkyEye.Companion.Hybrid.TableEditDraft();
draft.BeginCalibration(4, corners); draft.MoveCorner(0, new(.12, .11));
Check(corners[0] == new SkyEye.Core.Point(.1, .1) && draft.Corners[0] != corners[0], "adjusting proposed corners does not alter saved calibration or proposal");
Check(draft.Matches(4, null) && !draft.Matches(5, null), "obsolete calibration draft cannot match a newer service generation");
draft.ResetCorners(); draft.AddCorner(new(-.1, .5)); Check(draft.Corners.Count == 0, "manual corners reject points outside frozen image");
foreach (var corner in corners) draft.AddCorner(corner);
draft.AddCorner(new(.5, .5)); Check(draft.Corners.Count == 4, "calibration review retains exactly four corners until explicit acceptance");
draft.UndoCorner(); Check(draft.Corners.Count == 3, "manual corner review supports undo");
var calibration = new SkyEye.Core.TableCalibration(corners);
var originalLayout = new SkyEye.Core.TableState(12, DateTimeOffset.UtcNow,
    [new("cue", "cue", new(.4, .4), 1), new("red", "red", new(1.2, .5), 1)], "open", true, false, true, "mount");
draft.BeginCorrection(8, originalLayout, calibration); draft.Select(1); draft.SetKind("yellow");
draft.Place(calibration.Unmap(new(1.4, .6)));
Check(originalLayout.Balls[1].Kind == "red" && originalLayout.Balls[1].Position == new SkyEye.Core.Point(1.2, .5), "ball preview cannot mutate confirmed layout before apply");
Check(draft.Balls[1].Kind == "yellow" && (draft.Balls[1].Position - new SkyEye.Core.Point(1.4, .6)).Length < 1e-9, "drag and colour changes affect reviewed draft");
Check(draft.Matches(8, 12) && !draft.Matches(8, 13) && !draft.Matches(9, 12), "layout or source changes invalidate reviewed correction");
draft.AddBallMode(); draft.SetKind("black"); draft.Place(calibration.Unmap(new(1.6, .4)));
Check(draft.Balls.Count == 3 && draft.Selected == 2 && draft.Balls[2].Kind == "black", "adding a ball needs a cloth location and chooses it for review");
draft.DeleteSelected(); Check(draft.Balls.Count == 2 && draft.Selected == -1, "remove changes preview only");
Check(draft.LayoutText().Contains("yellow"), "reviewed draft serializes to existing validated correction parser");
draft.Clear(); Check(draft.Mode == SkyEye.Companion.Hybrid.TableEditMode.None && draft.Balls.Count == 0 && originalLayout.Balls.Length == 2, "reject clears preview while preserving canonical layout");
var unknownLayout = originalLayout with { Balls = [new("uncertain", "unknown", new(.7, .5), .2)] };
draft.BeginCorrection(8, unknownLayout, calibration); draft.Select(0); draft.AddBallMode();
Check(draft.Kind == "red" && draft.Selected == -1 && unknownLayout.Balls[0].Kind == "unknown", "adding after an uncertain detection starts with a valid colour without changing that detection");
draft.Place(calibration.Unmap(new(1.4, .5)));
Check(draft.Balls[1].Kind == "red", "new ball cannot inherit an unsupported detection colour");
draft.Clear();
Console.WriteLine("PASS frozen review: portrait inverse/letterbox mapping, manual corner adjustment/undo, isolated ball edits, apply scope and cancel");

// Perspective camera measurements share the exact matrix sent to Android.
var skewed = new SkyEye.Core.TableCalibration([new(.2, .2), new(.8, .25), new(.78, .75), new(.18, .8)]);
var framed = SkyEye.Companion.Hybrid.SceneProjection.Table(1080, 1920, 1920, 1080, skewed);
Check(framed.Framed && framed.Fallback is null, "valid calibrated view rectifies with a pocket border");
foreach (var pocket in SkyEye.Core.PoolGeometry.Pockets)
{
    var imagePoint = skewed.Unmap(pocket); var screenPoint = framed.Screen(imagePoint);
    Check(screenPoint.X > framed.Left && screenPoint.X < framed.Left + framed.Width && screenPoint.Y > framed.Top && screenPoint.Y < framed.Top + framed.Height, "all six pockets stay inside the framed border");
    Check((framed.Image(screenPoint)!.Value - imagePoint).Length < 1e-9, "perspective pocket hit test matches drawing and video");
}
var topLeft = framed.Screen(skewed.Unmap(new(0, 0))); var bottomLeft = framed.Screen(skewed.Unmap(new(2, 0)));
var topRight = framed.Screen(skewed.Unmap(new(0, 1))); var bottomRight = framed.Screen(skewed.Unmap(new(2, 1)));
Check(Math.Abs(topLeft.X - bottomLeft.X) < 1e-8 && Math.Abs(topRight.X - bottomRight.X) < 1e-8 && Math.Abs(topLeft.Y - topRight.Y) < 1e-8 && Math.Abs(bottomLeft.Y - bottomRight.Y) < 1e-8, "skewed cloth becomes a straight portrait rectangle");
var pathA = skewed.Unmap(new(.35, .27)); var pathB = skewed.Unmap(new(1.7, .75));
var middle = framed.Screen(skewed.Unmap(new(1.025, .51)));
Check(SkyEye.Core.PoolGeometry.DistanceToSegment(middle, framed.Screen(pathA), framed.Screen(pathB)) < 1e-8, "calibrated paths remain straight and agree with table geometry");
var tight = new SkyEye.Core.TableCalibration([new(.01, .01), new(.99, .01), new(.99, .99), new(.01, .99)]);
var fallback = SkyEye.Companion.Hybrid.SceneProjection.Table(1080, 1920, 1920, 1080, tight);
Check(!fallback.Framed && fallback.Fallback is not null && (fallback.Screen(sourcePoint) - SkyEye.Companion.Hybrid.SceneProjection.Portrait(1080, 1920, 1920, 1080).Screen(sourcePoint)).Length < 1e-9, "missing pocket border keeps whole-camera fallback");
Check(framed.Image(new(-1, 500)) is null, "framed picking ignores letterbox space");
Console.WriteLine("PASS scene framing: perspective rectification, six retained pockets, inverse hits, path alignment and whole-camera fallback");
foreach (bool portrait in new[] { true, false })
{
    var pockets = SkyEye.Companion.Hybrid.SceneProjection.Pockets(portrait ? 1080 : 1920, portrait ? 1920 : 1080, 1920, 1080, skewed, portrait);
    var points = skewed.Corners.Select(pockets.Screen).ToArray();
    var renderHash=new string('A',64);
    var renderSource=new SkyEye.Core.Analysis.RecordedGeometrySource(renderHash,SkyEye.Core.Analysis.RecordedGeometryClock.ClipRelative,renderHash,0,4_000_000,1920,1080,0);
    var renderPockets=new SkyEye.Core.Analysis.PocketLayout(skewed.Corners.ToImmutableArray(),[new(.5,.1)],.3);
    var renderJournal=SkyEye.Core.Analysis.RecordedGeometry.Build(renderSource,[new(0,renderPockets),new(2_000_000,renderPockets)]);
    var recorded=SkyEye.Core.Analysis.RecordedRenderPlan.Create(renderJournal,renderSource,portrait?1080:1920,portrait?1920:1080,portrait);
    foreach(var point in skewed.Corners)Check((recorded.Pixel(1_000_000,point)!.Value-pockets.Screen(point)).Length<1e-8,"recorded GPU matrix and live video/overlay mapping agree");
    Check(Math.Abs(points[0].X - points[portrait ? 1 : 3].X) < 1e-8 && Math.Abs(points[0].Y - points[portrait ? 3 : 1].Y) < 1e-8, "detected pocket centres define exact portrait/landscape rectangle corners");
    foreach (var p in skewed.Corners) Check((pockets.Image(pockets.Screen(p))!.Value - p).Length < 1e-9, "pocket rectangle inverse agrees with video and overlays");
}
Console.WriteLine("PASS exact pocket-centre rectangle for phone and TV with shared inverse mapping");
var geometryFence = new SkyEye.Companion.Hybrid.FramingGeometryFence();
Check(!geometryFence.Observe(1920, 1080, "fixed"), "initial known geometry binds without invalidating stored calibration");
Check(!geometryFence.Observe(0, 0, "fixed") && !geometryFence.Observe(1920, 1080, "fixed"), "reconnect's transient unknown size retains calibration");
Check(geometryFence.Observe(1280, 720, "fixed"), "real resolution change invalidates calibration");
Check(geometryFence.Observe(1280, 720, "zoom-crop-2"), "real crop/profile change invalidates calibration");
Console.WriteLine("PASS stored correction geometry: reconnect unknown sizes retained, real resolution/crop changes invalidated");
