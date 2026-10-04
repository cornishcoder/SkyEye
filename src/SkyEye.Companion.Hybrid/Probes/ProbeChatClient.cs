using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace SkyEye.Companion.Hybrid.Probes;

public sealed record ProbeState(long Version, string Group, int CandidateCount)
{
    public ProbeState() : this(0, "open", 0) { }
}
public sealed record ProbeUpdate(ProbeState State, bool Predictive);
public sealed record ProbeProposal(long ExpectedVersion, ProbeState Preview);

[ToolBlock("probe_show_shots")]
public sealed partial class ProbeShotBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "group")] public string? Group { get; set; }
    [ToolResult] public int CandidateCount { get; set; }
}

/// <summary>Deterministic package integration fixture. No camera, credentials or model.</summary>
public sealed class ProbeChatClient(TimeSpan? latency = null) : IChatClient
{
    public ProbeState State { get; private set; } = new(0, "open", 0);
    public ProbeProposal? PendingProposal { get; private set; }
    public bool? ProposalDecision { get; set; }
    public int CompletedOperations { get; private set; }
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("This fixture tests streaming.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string id = Guid.NewGuid().ToString("N");
        if (messages.LastOrDefault()?.Contents.OfType<FunctionResultContent>().Any() == true)
        {
            yield return new(ChatRole.Assistant, "Fixture decision complete.") { MessageId = id, RawRepresentation = State };
            yield break;
        }
        if (messages.LastOrDefault()?.Text == "Propose a fixture group")
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProposalDecision = null;
            PendingProposal = new(State.Version, State with { Group = "yellow", CandidateCount = 0 });
            yield return new(ChatRole.Assistant, "Previewing yellows. The committed fixture is unchanged.")
                { MessageId = id, RawRepresentation = new ProbeUpdate(PendingProposal.Preview, true) };
            yield return new(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), "review_fixture_proposal", new Dictionary<string, object?>())]) { MessageId = id };
            yield break;
        }
        yield return new(ChatRole.Assistant, "Checking the fixture table…") { MessageId = id };
        string call = Guid.NewGuid().ToString("N");
        yield return new(ChatRole.Assistant, [new FunctionCallContent(call, "probe_show_shots", new Dictionary<string, object?> { ["group"] = "red" })]) { MessageId = id };
        await Task.Delay(latency ?? TimeSpan.FromSeconds(1), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        State = new(State.Version + 1, "red", 3);
        ++CompletedOperations;
        yield return new(ChatRole.Tool, [new FunctionResultContent(call, State.CandidateCount)]) { MessageId = id };
        yield return new(ChatRole.Assistant, " Three fixture choices ready.") { MessageId = id, RawRepresentation = State };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
    public bool AcceptProposal()
    {
        var proposal = PendingProposal; PendingProposal = null;
        if (proposal is null || proposal.ExpectedVersion != State.Version) return false;
        State = proposal.Preview with { Version = State.Version + 1 }; return true;
    }
    public void RejectProposal() => PendingProposal = null;
    public void ChangeLayout() => State = State with { Version = State.Version + 1, CandidateCount = 0 };
}

public static class ProbeAgent
{
    public static UIAgent<ProbeState> Create(ProbeChatClient client)
    {
        UIAgent<ProbeState> agent = null!;
        agent = new(client, options =>
        {
        options.AddGeneratedToolBlocks();
        options.RegisterUIAction(AIFunctionFactory.Create(() =>
        {
            if (client.ProposalDecision is not bool accept) throw new InvalidOperationException("A human decision is required.");
            return Resolve(agent, client, accept);
        }, "review_fixture_proposal"));
        options.StateMapper = context =>
        {
            if (context.Update.RawRepresentation is ProbeState state) context.SetState(state);
            else if (context.Update.RawRepresentation is ProbeUpdate update && update.Predictive) context.SetPredictiveState(update.State);
        };
        }, client.State);
        return agent;
    }

    public static bool Resolve(UIAgent<ProbeState> agent, ProbeChatClient client, bool accept)
    {
        bool applied = accept && client.AcceptProposal();
        if (applied) agent.State.AcceptPredictiveState();
        else { client.RejectProposal(); agent.State.RejectPredictiveState(); }
        // RejectPredictiveState restores its old UI snapshot; a concurrent app
        // change is authoritative and must replace that snapshot afterwards.
        agent.State.Value = client.State;
        return applied;
    }
}
