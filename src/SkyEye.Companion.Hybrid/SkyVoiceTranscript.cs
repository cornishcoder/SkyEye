namespace SkyEye.Companion.Hybrid;

/// <summary>Keeps a small in-memory view of recent Sky turns without persisting transcript text.</summary>
public sealed class SkyVoiceTranscript
{
    const int MaxLines = 12;
    const int MaxLineLength = 480;
    const int MaxResponses = 4;
    readonly List<string> lines = [];
    readonly Dictionary<string, ResponseText> responses = new(StringComparer.Ordinal);
    readonly List<string> responseOrder = [];
    string pendingUser = "";
    bool hasOpenUserTurn;

    public string Text
    {
        get
        {
            var visible = new List<string>(lines);
            if (pendingUser.Length > 0) visible.Add("You: " + pendingUser);
            if (hasOpenUserTurn)
            {
                foreach (string id in responseOrder)
                    if (responses.TryGetValue(id, out var response) && !response.Completed && response.VisibleText.Length > 0)
                        visible.Add("Sky: " + response.VisibleText);
            }
            return string.Join(Environment.NewLine, visible);
        }
    }

    public void BeginSession()
    {
        ClearDrafts();
        if (lines.Count > 0) AddLine("— New Sky conversation —");
    }

    public void AddUserTranscript(string text, bool final)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!final)
        {
            pendingUser = AppendCapped(pendingUser, text);
            return;
        }

        string complete = text.Trim();
        if (complete.Length == 0) complete = pendingUser;
        pendingUser = "";
        if (complete.Length > 0) AddLine("You: " + Limit(complete));
        hasOpenUserTurn = true;
        FlushCompletedResponses();
    }

    public void BeginResponse(string? responseId)
    {
        if (string.IsNullOrWhiteSpace(responseId)) return;
        EnsureResponse(responseId);
    }

    public void AddAssistantTranscript(string? responseId, string? eventType, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string id = string.IsNullOrWhiteSpace(responseId) ? "unidentified" : responseId;
        var response = EnsureResponse(id);
        if (eventType?.Contains("output_audio_transcript", StringComparison.OrdinalIgnoreCase) == true)
            response.AudioText = AppendCapped(response.AudioText, text);
        else
            response.OtherText = AppendCapped(response.OtherText, text);
    }

    public void FinishResponse(string? responseId, bool completesTurn)
    {
        if (!string.IsNullOrWhiteSpace(responseId) && responses.TryGetValue(responseId, out var response))
        {
            response.Completed = true;
            if (hasOpenUserTurn) CommitResponse(responseId, response);
        }
        if (completesTurn) hasOpenUserTurn = false;
    }

    public void DiscardResponse(string? responseId)
    {
        if (string.IsNullOrWhiteSpace(responseId))
        {
            responses.Clear(); responseOrder.Clear();
            return;
        }
        responses.Remove(responseId);
        responseOrder.Remove(responseId);
    }

    public void InterruptResponse(string? responseId)
    {
        DiscardResponse(responseId);
        pendingUser = "";
        hasOpenUserTurn = false;
    }

    public void EndSession() => ClearDrafts();

    void FlushCompletedResponses()
    {
        foreach (string id in responseOrder.ToArray())
            if (responses.TryGetValue(id, out var response) && response.Completed)
                CommitResponse(id, response);
    }

    void CommitResponse(string id, ResponseText response)
    {
        if (response.VisibleText.Length > 0) AddLine("Sky: " + response.VisibleText);
        responses.Remove(id);
        responseOrder.Remove(id);
    }

    ResponseText EnsureResponse(string id)
    {
        if (responses.TryGetValue(id, out var response)) return response;
        if (responses.Count >= MaxResponses)
        {
            string oldest = responseOrder[0];
            responses.Remove(oldest); responseOrder.RemoveAt(0);
        }
        response = new();
        responses.Add(id, response);
        responseOrder.Add(id);
        return response;
    }

    void AddLine(string line)
    {
        lines.Add(Limit(line));
        while (lines.Count > MaxLines) lines.RemoveAt(0);
    }

    void ClearDrafts()
    {
        pendingUser = "";
        hasOpenUserTurn = false;
        responses.Clear(); responseOrder.Clear();
    }

    static string AppendCapped(string current, string addition)
    {
        if (addition.Length == 0 || current.Length >= MaxLineLength) return current;
        int length = Math.Min(addition.Length, MaxLineLength - current.Length);
        return current + addition[..length];
    }

    static string Limit(string text) => text.Length <= MaxLineLength ? text : text[..MaxLineLength];

    sealed class ResponseText
    {
        public string AudioText { get; set; } = "";
        public string OtherText { get; set; } = "";
        public bool Completed { get; set; }
        public string VisibleText => AudioText.Length > 0 ? AudioText : OtherText;
    }
}
