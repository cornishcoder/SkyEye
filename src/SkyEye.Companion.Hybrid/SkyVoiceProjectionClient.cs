using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace SkyEye.Companion.Hybrid;

/// <summary>Projects one native Sky conversation at a time into the Agent UI stream.</summary>
public sealed class SkyVoiceProjectionClient : IChatClient
{
    const int MaxBufferedResponses = 4;
    readonly Channel<Turn> pending = Channel.CreateUnbounded<Turn>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    readonly object gate = new();
    readonly Dictionary<string, Turn> responses = new(StringComparer.Ordinal);
    readonly Dictionary<string, PendingResponse> awaitingTranscript = new(StringComparer.Ordinal);
    readonly List<string> responseOrder = [];
    Turn? active;
    bool disposed;

    public string? BeginTurn(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return null;
        var turn = new Turn(transcript);
        lock (gate)
        {
            if (disposed) return null;
            if (active is { } previous) CancelTurnLocked(previous);
            active = turn;
            if (!pending.Writer.TryWrite(turn)) { active = null; turn.Cancel(); turn.Release(); return null; }
            bool hadBufferedResponses = false, allBufferedResponsesFinished = true, lastBufferedResponseComplete = false;
            foreach (string responseId in responseOrder)
            {
                if (!awaitingTranscript.Remove(responseId, out var buffered)) continue;
                hadBufferedResponses = true;
                lastBufferedResponseComplete = buffered.Completed;
                allBufferedResponsesFinished &= buffered.Finished;
                responses[responseId] = turn;
                foreach (var call in buffered.Calls) turn.RegisterCall(call.Key, responseId, call.Value.Completed != 0);
                foreach (var update in buffered.Updates) turn.Write(update);
            }
            responseOrder.Clear();
            if (hadBufferedResponses && lastBufferedResponseComplete && allBufferedResponsesFinished && !turn.HasPendingCalls)
                CompleteTurnLocked(turn);
        }
        return turn.Id;
    }

    public bool StartResponse(string? responseId)
    {
        if (string.IsNullOrWhiteSpace(responseId)) return false;
        lock (gate)
        {
            if (disposed) return false;
            if (active is { IsClosed: false } turn)
            {
                responses[responseId] = turn;
                return true;
            }
            if (!awaitingTranscript.ContainsKey(responseId))
            {
                if (awaitingTranscript.Count >= MaxBufferedResponses) return false;
                awaitingTranscript.Add(responseId, new());
                responseOrder.Add(responseId);
            }
            return true;
        }
    }

    public bool PublishAssistantText(string? responseId, string text)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(responseId)) return false;
        lock (gate)
        {
            var update = new ChatResponseUpdate(ChatRole.Assistant, text) { MessageId = responseId };
            if (responses.TryGetValue(responseId, out var turn)) return turn.Write(update);
            if (awaitingTranscript.TryGetValue(responseId, out var buffered)) return buffered.TryAppendText(update);
            return false;
        }
    }

    public bool PublishToolCall(string? responseId, string callId, string name, string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(responseId) || string.IsNullOrWhiteSpace(callId) || string.IsNullOrWhiteSpace(name)) return false;
        Dictionary<string, object?> arguments;
        try { arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson) ?? []; }
        catch (JsonException) { arguments = []; }
        lock (gate)
        {
            var update = new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent(callId, name, arguments)]) { MessageId = responseId };
            if (responses.TryGetValue(responseId, out var turn))
            {
                if (!turn.BeginCall(callId, responseId)) return false;
                if (turn.Write(update)) return true;
                turn.FinishCall(callId);
                return false;
            }
            return awaitingTranscript.TryGetValue(responseId, out var buffered) && buffered.TryAddCall(callId, update);
        }
    }

    public bool PublishToolResult(string callId, string resultJson)
    {
        lock (gate)
        {
            if (active is { } turn && turn.CompleteCall(callId, out string? activeResponseId))
                return turn.Write(new(ChatRole.Tool, [new FunctionResultContent(callId, resultJson)]) { MessageId = activeResponseId });
            foreach (var pair in awaitingTranscript)
            {
                if (pair.Value.TryCompleteCall(callId, new(ChatRole.Tool, [new FunctionResultContent(callId, resultJson)]) { MessageId = pair.Key })) return true;
            }
            return false;
        }
    }

    public bool CompleteResponse(string? responseId)
        => FinishResponse(responseId, completesTurn: true);

    public bool FinishResponse(string? responseId, bool completesTurn)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(responseId)) return false;
            if (responses.TryGetValue(responseId, out var turn))
            {
                if (!ReferenceEquals(active, turn) || turn.IsClosed) return false;
                if (!completesTurn) return true;
                if (turn.HasPendingCalls) return false;
                CompleteTurnLocked(turn);
                return true;
            }
            if (!awaitingTranscript.TryGetValue(responseId, out var buffered)) return false;
            buffered.Finished = true;
            if (completesTurn)
            {
                if (buffered.HasPendingCalls) return false;
                buffered.Completed = true;
            }
            return true;
        }
    }

    public void InterruptCurrent()
    {
        lock (gate)
        {
            if (active is { } turn) InterruptLocked(turn);
            ClearAwaitingTranscriptLocked();
        }
    }

    public void InterruptResponse(string? responseId)
    {
        if (string.IsNullOrWhiteSpace(responseId)) return;
        lock (gate)
        {
            if (responses.TryGetValue(responseId, out var turn) && ReferenceEquals(active, turn)) InterruptLocked(turn);
            else ClearAwaitingTranscriptLocked(responseId);
        }
    }

    void InterruptLocked(Turn turn)
    {
        CancelTurnLocked(turn);
    }

    void CancelTurnLocked(Turn turn)
    {
        if (ReferenceEquals(active, turn)) active = null;
        foreach (var key in responses.Where(pair => ReferenceEquals(pair.Value, turn)).Select(pair => pair.Key).ToArray())
            responses.Remove(key);
        turn.Cancel();
    }

    void CompleteTurnLocked(Turn turn)
    {
        if (ReferenceEquals(active, turn)) active = null;
        foreach (var key in responses.Where(pair => ReferenceEquals(pair.Value, turn)).Select(pair => pair.Key).ToArray())
            responses.Remove(key);
        turn.Complete();
    }

    void ClearAwaitingTranscriptLocked(string? responseId = null)
    {
        if (responseId is null) { awaitingTranscript.Clear(); responseOrder.Clear(); return; }
        awaitingTranscript.Remove(responseId);
        responseOrder.Remove(responseId);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromException<ChatResponse>(new NotSupportedException("Sky conversations require the native streaming session."));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var userMessage = messages.LastOrDefault(message => message.Role == ChatRole.User)?.Text;
        Turn turn = await pending.Reader.ReadAsync(cancellationToken);
        if (!string.Equals(userMessage, turn.Transcript, StringComparison.Ordinal))
        {
            turn.Cancel();
            turn.Release();
            throw new InvalidOperationException("The native transcript did not match the queued Agent turn.");
        }
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turn.CancellationToken);
            await foreach (var update in turn.ReadAllAsync(linked.Token)) yield return update;
        }
        finally { turn.Release(); }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            active?.Cancel();
            active = null;
            responses.Clear();
            ClearAwaitingTranscriptLocked();
            pending.Writer.TryComplete();
            while (pending.Reader.TryRead(out var queued)) { queued.Cancel(); queued.Release(); }
        }
    }

    sealed class Turn
    {
        readonly Channel<ChatResponseUpdate> updates = Channel.CreateUnbounded<ChatResponseUpdate>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        readonly Dictionary<string, Call> calls = new(StringComparer.Ordinal);
        readonly CancellationTokenSource cancellation = new();
        int closed;
        int released;
        public Turn(string transcript)
        {
            Transcript = transcript;
            CancellationToken = cancellation.Token;
        }
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string Transcript { get; }
        public CancellationToken CancellationToken { get; }
        public bool IsClosed => Volatile.Read(ref closed) != 0;
        public bool HasPendingCalls => calls.Values.Any(call => Volatile.Read(ref call.Completed) == 0);
        public bool Write(ChatResponseUpdate update) => !IsClosed && updates.Writer.TryWrite(update);
        public bool BeginCall(string id, string responseId) => !IsClosed && calls.TryAdd(id, new(responseId));
        public void RegisterCall(string id, string responseId, bool completed) => calls.TryAdd(id, new(responseId, completed));
        public void FinishCall(string id) => calls.Remove(id);
        public bool CompleteCall(string id, out string? responseId)
        {
            responseId = null;
            if (IsClosed || !calls.TryGetValue(id, out var call) || Interlocked.Exchange(ref call.Completed, 1) != 0) return false;
            responseId = call.ResponseId;
            return true;
        }
        public void Complete()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0) updates.Writer.TryComplete();
        }
        public void Cancel()
        {
            if (Interlocked.Exchange(ref closed, 1) == 0) updates.Writer.TryComplete();
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        }
        public void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) cancellation.Dispose();
        }
        public IAsyncEnumerable<ChatResponseUpdate> ReadAllAsync(CancellationToken cancellationToken) => updates.Reader.ReadAllAsync(cancellationToken);
        sealed class Call(string responseId, bool completed = false) { public string ResponseId { get; } = responseId; public int Completed = completed ? 1 : 0; }
    }

    sealed class PendingResponse
    {
        const int MaxBufferedTextCharacters = 65536;
        const int MaxBufferedCalls = 32;
        int bufferedTextCharacters;
        public List<ChatResponseUpdate> Updates { get; } = [];
        public Dictionary<string, PendingCall> Calls { get; } = new(StringComparer.Ordinal);
        public bool Finished { get; set; }
        public bool Completed { get; set; }
        public bool HasPendingCalls => Calls.Values.Any(call => Volatile.Read(ref call.Completed) == 0);
        public sealed class PendingCall { public int Completed; }
        public bool TryAppendText(ChatResponseUpdate update)
        {
            int length = update.Text?.Length ?? 0;
            if (bufferedTextCharacters + length > MaxBufferedTextCharacters) return false;
            bufferedTextCharacters += length;
            Updates.Add(update);
            return true;
        }
        public bool TryAddCall(string id, ChatResponseUpdate update)
        {
            if (Calls.Count >= MaxBufferedCalls || !Calls.TryAdd(id, new())) return false;
            Updates.Add(update);
            return true;
        }
        public bool TryCompleteCall(string id, ChatResponseUpdate update)
        {
            if (!Calls.TryGetValue(id, out var call) || Interlocked.Exchange(ref call.Completed, 1) != 0) return false;
            Updates.Add(update);
            return true;
        }
    }
}
