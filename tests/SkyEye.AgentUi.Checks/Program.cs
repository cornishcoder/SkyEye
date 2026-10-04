using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using SkyEye.Companion.Hybrid.Probes;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
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

var elapsed = TimeSpan.Zero;
var chrome = new SkyEye.Companion.Hybrid.ControlChrome(() => elapsed);
elapsed = TimeSpan.FromSeconds(30); chrome.Tick();
Check(chrome.Visible, "empty or buffering table keeps connect controls visible");
chrome.CanFade = true; chrome.Reveal(); elapsed += TimeSpan.FromSeconds(4.9); chrome.Tick();
Check(chrome.Visible, "short inactivity threshold keeps controls available");
elapsed += TimeSpan.FromSeconds(.1); chrome.Tick();
Check(!chrome.Visible, "playing table rests after five seconds");
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
