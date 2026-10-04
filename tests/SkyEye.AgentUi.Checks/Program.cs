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
