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
