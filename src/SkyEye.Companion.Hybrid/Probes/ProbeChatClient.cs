using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace SkyEye.Companion.Hybrid.Probes;

public sealed record ProbeState(long Version, string Group, int CandidateCount)
{
    public ProbeState() : this(0, "open", 0) { }
}

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
    public int CompletedOperations { get; private set; }
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("This fixture tests streaming.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string id = Guid.NewGuid().ToString("N");
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
}

public static class ProbeAgent
{
    public static UIAgent<ProbeState> Create(ProbeChatClient client) => new(client, options =>
    {
        options.AddGeneratedToolBlocks();
        options.StateMapper = context =>
        {
            if (context.Update.RawRepresentation is ProbeState state) context.SetState(state);
        };
    }, client.State);
}
