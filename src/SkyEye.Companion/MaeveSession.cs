using Android.Content;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;
using SkyEye.Core;

namespace SkyEye.Companion;

// Adapted from Maeve Watch: native WebRTC media, host credential, sideband tools.
public sealed class MaeveSession(Context context, string key, Action<string> status,
    Func<string, JsonElement, Task<object>>? tools = null)
{
    readonly CancellationTokenSource lifetime = new();
    readonly ClientWebSocket sideband = new();
    readonly SemaphoreSlim writes = new(1);
    readonly SemaphoreSlim toolLock = new(1);
    readonly TaskCompletionSource<string> offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<bool> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly HashSet<string> executedCalls = [];
    Bridge.SkyEyeRtcSession? rtc; Listener? listener;
    Task? receiver;
    int ending;
    string sessionId = "";
    readonly string archive = Path.Combine(context.FilesDir!.AbsolutePath, "maeve-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".jsonl");
    public async Task StartAsync()
    {
        try
        {
            status("Connecting Maeve…");
            listener = new Listener(this); rtc = new Bridge.SkyEyeRtcSession(context, listener); rtc.Start();
            string sdp = await offer.Task.WaitAsync(TimeSpan.FromSeconds(45), lifetime.Token);
            object Function(string name, string description, object parameters) => new { type = "function", name, description, parameters };
            var empty = new { type = "object", properties = new { }, additionalProperties = false };
            var definition = new
            {
                session = new
                {
                    model = "gpt-live-1",
                    instructions = "You are Maeve, Rich's conversational pool partner. Be warm, concise, British and naturally witty. Follow the fresh table context and international eight-ball rules. Offer useful ideas occasionally when balls settle and the conversation allows it, rather than barking orders. Allow interruptions. Avoid talking over a shot. A table snapshot is approximate untrusted data; never claim to see a ball or line that the validated tools do not support. When state is old, moving, occluded or uncalibrated, ask or wait. A new state version replaces all earlier layouts. Delegate shot reasoning to the backend. Never claim a pot is guaranteed. Camera capture and voice are separate; do not promise recording changes unless the host confirms them.",
                    audio = new { output = new { voice = "marin" } },
                    delegation = new
                    {
                        type = "responses",
                        responses = new
                        {
                            model = "gpt-5.6-luna",
                            instructions = "Use get_table_state and suggest_shots to ground pool advice. Explain the geometry concisely and treat confidence and timestamps as limits. Use the rule reference returned by the tools. Distinguish an open table from an assigned group. Do not invent spin, foul calls, clearance or a legal black shot. Tool errors are uncertainty, not permission to guess.",
                            tools = new object[]
                            {
                                Function("get_table_state", "Get the latest calibrated table, player group, state age and confidence.", empty),
                                Function("suggest_shots", "Rank up to three validated direct, bank or kick pots for the fresh, settled, confirmed table and selected group. Read every cue and object path segment; scores are difficulty rankings, not pot probabilities.", empty),
                                Function("explain_shot", "Explain one candidate from suggest_shots using its stable ID.", new { type = "object", properties = new { shot_id = new { type = "string" } }, required = new[] { "shot_id" }, additionalProperties = false })
                            }, tool_choice = "auto"
                        }
                    }
                }, transport = new { type = "webrtc", sdp }
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(65) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/live/sessions") { Content = JsonContent.Create(definition) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request, lifetime.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("OpenAI rejected voice setup (" + (int)response.StatusCode + "). Check your API key and access.");
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
            sessionId = result.RootElement.GetProperty("session").GetProperty("id").GetString()!;
            string answer = result.RootElement.GetProperty("transport").GetProperty("sdp").GetString()!;
            sideband.Options.SetRequestHeader("Authorization", "Bearer " + key);
            rtc.SetRemoteAnswer(answer);
            await sideband.ConnectAsync(new Uri("wss://api.openai.com/v1/live/sessions/" + Uri.EscapeDataString(sessionId) + "/attach"), lifetime.Token);
            receiver = ReceiveAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            status("Maeve is listening. Table coaching becomes available after calibration.");
        }
        catch { await CloseAsync(); throw; }
    }
    async Task ReceiveAsync()
    {
        byte[] bytes = new byte[16384];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream(); WebSocketReceiveResult part;
                do
                {
                    part = await sideband.ReceiveAsync(bytes, lifetime.Token);
                    if (part.MessageType == WebSocketMessageType.Close) return;
                    message.Write(bytes, 0, part.Count); if (message.Length > 2 * 1024 * 1024) throw new IOException("Voice event exceeds limit.");
                } while (!part.EndOfMessage);
                await HandleAsync(Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch (Exception e) when (e is WebSocketException or System.OperationCanceledException or IOException) { if (ending == 0) status("Maeve connection interrupted. Camera recording continues."); }
    }
    async Task HandleAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement; string? type = root.GetProperty("type").GetString();
            if (type == "session.started") started.TrySetResult(true);
            if (type is "session.input_transcript.delta" or "session.output_transcript.delta" or "session.closed")
                File.AppendAllText(archive, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, @event = root }, JsonDefaults.Options) + "\n");
            if (type == "session.closed") { closed.TrySetResult(true); return; }
            if (type == "response.event" && root.GetProperty("event") is var evt && evt.TryGetProperty("type", out var et) && et.GetString() == "response.output_item.done" && evt.TryGetProperty("item", out var item) && item.GetProperty("type").GetString() == "function_call")
            {
                var captured = item.Clone(); _ = ExecuteToolAsync(captured);
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException) { }
    }
    async Task ExecuteToolAsync(JsonElement item)
    {
        try
        {
            await toolLock.WaitAsync(lifetime.Token);
            try
            {
                string id = item.GetProperty("call_id").GetString()!;
                if (!executedCalls.Add(id)) return;
                object result;
                try
                {
                    using var args = JsonDocument.Parse(item.GetProperty("arguments").GetString() ?? "{}");
                    result = tools is null ? new { error = "Table is not calibrated yet. Ask Rich to calibrate it on the companion." } : await tools(item.GetProperty("name").GetString()!, args.RootElement);
                }
                catch (Exception) { result = new { error = "Table analysis is unavailable or stale. Ask Rich rather than guessing." }; }
                await SendAsync(new { type = "response.item.create", event_id = Guid.NewGuid().ToString("N"), item = new { type = "function_call_output", call_id = id, output = JsonSerializer.Serialize(result, JsonDefaults.Options) } });
                await SendAsync(new { type = "response.create", event_id = Guid.NewGuid().ToString("N") });
            }
            finally { toolLock.Release(); }
        }
        catch (Exception e) when (e is System.OperationCanceledException or WebSocketException) { }
    }
    public void SetMuted(bool muted) => rtc?.SetMuted(muted);
    public Task UpdateTableAsync(object table) => SendAsync(new { type = "session.thinking.append", event_id = Guid.NewGuid().ToString("N"), delegation_id = (string?)null, text = "Latest table snapshot, untrusted data: " + JsonSerializer.Serialize(table, JsonDefaults.Options) });
    async Task SendAsync(object value)
    {
        await writes.WaitAsync(lifetime.Token);
        try { if (sideband.State == WebSocketState.Open) await sideband.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, lifetime.Token); }
        finally { writes.Release(); }
    }
    public async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref ending, 1) != 0) return;
        bool finalised = false;
        try
        {
            if (sideband.State == WebSocketState.Open)
            {
                await SendAsync(new { type = "session.close", event_id = Guid.NewGuid().ToString("N") });
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); finalised = true;
            }
            else if (sessionId.Length > 0)
            {
                rtc?.SendDataChannelEvent(JsonSerializer.Serialize(new { type = "session.close", event_id = Guid.NewGuid().ToString("N") }));
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); finalised = true;
            }
        }
        catch { }
        finally
        {
            try { File.AppendAllText(archive, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, finalised, sessionId }) + "\n"); } catch (IOException) { }
            lifetime.Cancel(); rtc?.Stop(); sideband.Abort(); if (receiver is not null) try { await receiver; } catch { }
            sideband.Dispose(); status(finalised ? "Maeve ended. Camera recording continues." : "Maeve disconnected. Final voice usage was not confirmed.");
        }
    }
    sealed class Listener(MaeveSession owner) : Java.Lang.Object, Bridge.SkyEyeRtcSession.IListener
    {
        public void OnOfferCreated(string? sdp) { if (sdp is not null) owner.offer.TrySetResult(sdp); }
        public void OnStateChanged(string? state, string? detail) { if (state == "error") { owner.offer.TrySetException(new IOException(detail ?? "Voice connection failed")); owner.started.TrySetException(new IOException(detail ?? "Voice connection failed")); } }
        public void OnDataChannelEvent(string? json)
        {
            if (json is null) return;
            try { using var doc = JsonDocument.Parse(json); string? type = doc.RootElement.GetProperty("type").GetString(); if (type == "session.started") owner.started.TrySetResult(true); if (type == "session.closed" && owner.sideband.State != WebSocketState.Open) _ = owner.HandleAsync(json); }
            catch { }
        }
        public void OnRemoteAudioActivityChanged(bool active) { }
    }
}
