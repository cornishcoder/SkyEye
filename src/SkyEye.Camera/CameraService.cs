using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net.Nsd;
using Android.OS;
using SkyEye.Core;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Engine = UK.CO.Skyeye.Camera.Nativebridge.CaptureEngine;

namespace SkyEye.Camera;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeCamera | ForegroundService.TypeMicrophone)]
public sealed class CameraService : Service
{
    public const int Port = 8765;
    public static CameraService? Instance { get; private set; }
    public readonly SessionAuthority Authority = new();
    readonly object historyGate = new();
    readonly List<Segment> history = [];
    readonly Dictionary<string, int> pins = [];
    readonly List<Clip> clips = [];
    readonly SemaphoreSlim mutations = new(1);
    readonly Dictionary<string, HttpResponse> completed = [];
    readonly Queue<string> completedOrder = [];
    readonly string readToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    readonly CancellationTokenSource lifetime = new();
    TaskCompletionSource<bool> nextSegment = Signal();
    Engine? engine;
    Listener? captureListener;
    LocalHttpServer? server;
    RecentReplayStore recentReplays = null!;
    PowerManager.WakeLock? wake;
    NsdManager? discovery;
    Registration? registration;
    string root = "", buffer = "", clipDir = "", error = "", timeline = "";
    bool ready;
    DateTimeOffset armedAt = DateTimeOffset.UtcNow;
    long completedFrames;
    static TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnCreate()
    {
        base.OnCreate(); Instance = this;
        root = FilesDir!.AbsolutePath; buffer = Path.Combine(root, "buffer"); clipDir = Path.Combine(root, "clips");
        Directory.CreateDirectory(buffer); Directory.CreateDirectory(clipDir);
        recentReplays = new RecentReplayStore(Path.Combine(root, "recent-replays"));
        // Rolling history is ephemeral across process death; durable completed clips survive.
        foreach (var file in Directory.GetFiles(buffer)) File.Delete(file);
        foreach (var file in Directory.GetFiles(clipDir, "*.part")) File.Delete(file);
        foreach (var metadata in Directory.GetFiles(clipDir, "*.json"))
        {
            try { var clip = JsonSerializer.Deserialize<Clip>(File.ReadAllText(metadata), JsonDefaults.Options); if (clip is not null && Guid.TryParseExact(clip.Id, "N", out _) && File.Exists(Path.Combine(clipDir, clip.Id + ".mp4"))) clips.Add(clip); }
            catch { error = "One clip's metadata could not be read."; }
        }
        var notifications = (NotificationManager)GetSystemService(NotificationService)!;
        notifications.CreateNotificationChannel(new NotificationChannel("camera", "SkyEye mounted camera", NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 1, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var notification = new Notification.Builder(this, "camera").SetContentTitle("SkyEye Camera is armed").SetContentText("Session control is on your companion. Display may switch off.").SetSmallIcon(Android.Resource.Drawable.IcMenuCamera).SetContentIntent(open).SetOngoing(true).Build();
        StartForeground(1001, notification, ForegroundService.TypeCamera | ForegroundService.TypeMicrophone);
        try
        {
            captureListener = new Listener(this); engine = new Engine(this, buffer, captureListener); engine.Arm();
            server = new LocalHttpServer(Port, HandleAsync); server.Start();
            discovery = (NsdManager)GetSystemService(NsdService)!;
            registration = new Registration();
            discovery.RegisterService(new NsdServiceInfo { ServiceName = "SkyEye Camera", ServiceType = "_skyeye._tcp.", Port = Port }, NsdProtocol.DnsSd, registration);
            _ = MonitorAsync();
        }
        catch (Exception e) { Fault(e.Message); }
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId) => StartCommandResult.NotSticky;
    public string DisplayStatus => !ready ? "Arming camera… " + error : $"Armed · {Authority.State}\n" + string.Join("\n", Addresses().Select(x => $"http://{x}:{Port}")) + (error.Length == 0 ? "" : "\n" + error);
    static string[] Addresses() => System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().SelectMany(x => x.GetIPProperties().UnicastAddresses).Select(x => x.Address).Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(x) && !x.ToString().StartsWith("169.254.")).Select(x => x.ToString()).ToArray();
    object Status()
    {
        var battery = RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
        var owner = Authority.Current;
        lock (historyGate) return new
        {
            protocol = 1, armed = ready, state = Authority.State.ToString(), Authority.Revision, Authority.SessionId,
            host = owner is null ? null : new { owner.Owner, owner.Generation, owner.ExpiresAt },
            battery = battery?.GetIntExtra(BatteryManager.ExtraLevel, -1) ?? -1,
            temperatureC = (battery?.GetIntExtra(BatteryManager.ExtraTemperature, 0) ?? 0) / 10.0,
            availableBytes = new StatFs(root).AvailableBytes, uptimeSeconds = (DateTimeOffset.UtcNow - armedAt).TotalSeconds,
            historySeconds = history.Where(x => x.Timeline == timeline).Sum(x => x.Duration),
            error, readToken, sourceTimeUs = Engine.TimeUs(), timeline, recentReplayVersion = 1,
            streamPath = $"/live/index.m3u8?access={readToken}", clipCount = clips.Count, addresses = Addresses(),
            width = 1920, height = 1080, fps = 30, bitrate = 6000000, completedFrames
        };
    }
    async Task<HttpResponse> HandleAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.Method == "OPTIONS") return HttpResponse.Text("");
        if (request.Path == "/status" && request.Method == "GET") return HttpResponse.Json(Status());
        if (request.Method == "POST" && request.Path == "/replays/recent")
        {
            RequireViewing(request);
            var replay = request.Json<RecentReplayRequest>(); RecentReplay.Validate(replay);
            await mutations.WaitAsync(ct);
            try
            {
                if (Authority.State != CaptureState.Active || replay.Timeline != timeline)
                    throw new InvalidOperationException("An active camera recording is needed for recent replay.");
                if (new StatFs(root).AvailableBytes < 256L * 1024 * 1024)
                    throw new InvalidOperationException("Camera storage is low. Recent replay is unavailable.");
                var asset = await recentReplays.CreateAsync(replay, (path, token) => AssembleRecentAsync(replay, path, token), ct);
                return HttpResponse.Json(asset);
            }
            finally { mutations.Release(); }
        }
        if (request.Path == "/table/snapshot" && request.Method == "GET")
        {
            Authority.Require(request.HostToken);
            Segment segment;
            lock (historyGate) { segment = history.LastOrDefault(x => x.Timeline == timeline) ?? throw new InvalidOperationException("Start recording and wait for a video frame."); pins[segment.Name] = pins.GetValueOrDefault(segment.Name) + 1; }
            try
            {
                long sample = (long)Math.Max(0, (segment.Duration - 1.0 / 30) * 1_000_000);
                byte[] jpeg = await Task.Run(() => Engine.Snapshot(Path.Combine(buffer, segment.Name + ".mp4"), sample), ct) ?? throw new IOException("Snapshot unavailable");
                return new(200, "image/jpeg", jpeg, Headers: new() { ["X-SkyEye-Captured-At"] = segment.CapturedAt.AddSeconds(sample / 1_000_000.0).ToString("O"), ["X-SkyEye-Timeline"] = segment.Timeline });
            }
            finally { lock (historyGate) { if (--pins[segment.Name] == 0) pins.Remove(segment.Name); } }
        }
        if (request.Method is "GET" or "HEAD")
        {
            RequireViewing(request);
            if (request.Path.StartsWith("/replays/"))
            {
                var id = request.Path[9..]; var asset = recentReplays.Find(id); var path = recentReplays.MediaPath(id);
                if (asset is null || path is null) return HttpResponse.Json(new { error = "Temporary replay expired. Request recent footage again." }, 410);
                return new(200, "video/mp4", FilePath: path, Headers: new() { ["ETag"] = '"' + asset.Clip.Sha256 + '"' });
            }
            if (request.Path == "/clips") { lock (historyGate) return HttpResponse.Json(clips.ToArray()); }
            if (request.Path == "/live/index.m3u8") return Playlist();
            if (request.Path.StartsWith("/live/"))
            {
                var name = request.Path[6..]; if (Path.GetFileName(name) != name || !name.EndsWith(".ts")) return HttpResponse.Json(new { error = "Media not found" }, 404);
                var path = Path.Combine(buffer, name); return File.Exists(path) ? new(200, "video/mp2t", FilePath: path) : HttpResponse.Json(new { error = "Segment expired" }, 404);
            }
            if (request.Path.StartsWith("/clips/"))
            {
                var id = request.Path[7..]; Clip? clip; lock (historyGate) clip = clips.FirstOrDefault(x => x.Id == id);
                if (clip is null) return HttpResponse.Json(new { error = "Clip not found" }, 404);
                return new(200, "video/mp4", FilePath: Path.Combine(clipDir, clip.Id + ".mp4"), Headers: new() { ["ETag"] = '"' + clip.Sha256 + '"' });
            }
            return HttpResponse.Json(new { error = "Not found" }, 404);
        }
        if (request.Method != "POST") return HttpResponse.Json(new { error = "Method not supported" }, 405);
        // Lease renewal must remain available while a long clip is being assembled.
        if (request.Path == "/host/heartbeat") return HttpResponse.Json(Authority.Heartbeat(request.HostToken ?? ""));
        await mutations.WaitAsync(ct);
        try
        {
            if (request.Path == "/host/claim") { using var body = JsonDocument.Parse(request.Body); return HttpResponse.Json(Authority.Claim(body.RootElement.GetProperty("owner").GetString() ?? "")); }
            Authority.Require(request.HostToken);
            if (request.Path == "/host/release") { Authority.Release(request.HostToken!); return HttpResponse.Json(new { released = true }); }
            using var command = JsonDocument.Parse(request.Body);
            var id = command.RootElement.GetProperty("commandId").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 80) throw new ArgumentException("Missing command ID.");
            // Bind deduplication to both authority and route; stale hosts are rejected before cache lookup.
            string key = request.HostToken + ":" + request.Path + ":" + id;
            if (completed.TryGetValue(key, out var cached)) return cached;
            HttpResponse result;
            switch (request.Path)
            {
                case "/session/start": case "/session/resume":
                    if (!ready || engine is null || !engine.IsReady) throw new InvalidOperationException("Camera is not ready. Arm it on the mounted phone.");
                    if (request.Path.EndsWith("resume") && Authority.State != CaptureState.Paused) throw new InvalidOperationException("Session is not paused.");
                    if (Authority.State == CaptureState.Active) { result = HttpResponse.Json(Status()); break; }
                    if (Authority.State == CaptureState.Faulted) throw new InvalidOperationException("Re-arm the camera after the error.");
                    timeline = Guid.NewGuid().ToString("N");
                    wake ??= ((PowerManager)GetSystemService(PowerService)!).NewWakeLock(WakeLockFlags.Partial, "SkyEye:Capture");
                    wake!.Acquire();
                    try { engine.Begin(timeline); Authority.Transition(CaptureState.Active); error = ""; }
                    catch { if (wake.IsHeld) wake.Release(); throw; }
                    result = HttpResponse.Json(Status()); break;
                case "/session/pause":
                    if (Authority.State != CaptureState.Active) throw new InvalidOperationException("Only an active session can pause.");
                    engine!.Pause(); Authority.Transition(CaptureState.Paused); if (wake?.IsHeld == true) wake.Release(); result = HttpResponse.Json(Status()); break;
                case "/session/stop":
                    if (Authority.State == CaptureState.Ready) { result = HttpResponse.Json(Status()); break; }
                    if (Authority.State != CaptureState.Faulted) engine?.Pause(); Authority.Transition(CaptureState.Stopped); if (wake?.IsHeld == true) wake.Release(); result = HttpResponse.Json(Status()); break;
                case "/clips/save": result = await SaveAsync(command.RootElement.GetProperty("seconds").GetInt32(), ct); break;
                default: return HttpResponse.Json(new { error = "Not found" }, 404);
            }
            completed[key] = result; completedOrder.Enqueue(key);
            while (completedOrder.Count > 256) completed.Remove(completedOrder.Dequeue());
            return result;
        }
        finally { mutations.Release(); }
    }
    HttpResponse Playlist()
    {
        lock (historyGate)
        {
            // Paused/stopped playback must not masquerade as a current live stream.
            if (Authority.State != CaptureState.Active) return HttpResponse.Json(new { error = "Capture is " + Authority.State }, 409);
            var segments = history.Where(x => x.Timeline == timeline).TakeLast(6).ToArray();
            if (segments.Length == 0) return HttpResponse.Json(new { error = "Waiting for the first video segment" }, 503);
            var text = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-INDEPENDENT-SEGMENTS\n");
            text.Append("#EXT-X-TARGETDURATION:").Append((int)Math.Ceiling(segments.Max(x => x.Duration))).Append('\n');
            text.Append("#EXT-X-MEDIA-SEQUENCE:").Append(segments[0].Sequence).Append('\n');
            foreach (var segment in segments)
            {
                text.Append("#EXT-X-PROGRAM-DATE-TIME:").Append(segment.CapturedAt.ToString("O")).Append('\n');
                text.Append("#EXTINF:").Append(segment.Duration.ToString("F6", CultureInfo.InvariantCulture)).Append(",\n").Append(segment.Name).Append(".ts?access=").Append(readToken).Append('\n');
            }
            return HttpResponse.Text(text.ToString(), "application/vnd.apple.mpegurl");
        }
    }
    void RequireViewing(HttpRequest request)
    {
        if (!request.Query.TryGetValue("access", out var access) || access != readToken)
            throw new UnauthorizedAccessException("Reconnect to the camera to refresh viewing access.");
    }
    async Task<(double Duration, long StartUs, long EndUs)> AssembleRecentAsync(RecentReplayRequest request, string output, CancellationToken ct)
    {
        // Capture end is fixed by the camera status sampled before this request.
        // Closing an encoder segment does not stop or pause recording.
        long now = Engine.TimeUs();
        if (request.EndUs > now || now - request.EndUs > 30_000_000)
            throw new InvalidOperationException("This replay request is too old. Request recent footage again.");
        Task wait; lock (historyGate) wait = nextSegment.Task;
        engine!.RequestBoundary();
        try { await wait.WaitAsync(TimeSpan.FromSeconds(15), ct); }
        catch (TimeoutException) { throw new InvalidOperationException("Camera is still finalising recent footage. Try again shortly."); }
        RecentReplayWindow window;
        lock (historyGate)
        {
            window = RecentReplay.Select(history, request, timeline, Engine.TimeUs());
            foreach (var segment in window.Segments) pins[segment.Name] = pins.GetValueOrDefault(segment.Name) + 1;
        }
        try
        {
            await Task.Run(() => Engine.JoinClips(window.Segments.Select(s => Path.Combine(buffer, s.Name + ".mp4")).ToArray(),
                window.Segments.Select(s => s.StartUs).ToArray(), window.EndUs, output));
            ct.ThrowIfCancellationRequested();
            return (Engine.Duration(output), window.StartUs, window.EndUs);
        }
        finally { lock (historyGate) foreach (var segment in window.Segments) { if (--pins[segment.Name] == 0) pins.Remove(segment.Name); } }
    }
    async Task<HttpResponse> SaveAsync(int seconds, CancellationToken ct)
    {
        if (!ReplaySelection.Durations.Contains(seconds)) throw new ArgumentException("Unsupported replay duration.");
        if (new StatFs(root).AvailableBytes < 256L * 1024 * 1024) throw new InvalidOperationException("Camera storage is low. Free device space before saving more.");
        long end = Engine.TimeUs();
        if (Authority.State == CaptureState.Active)
        {
            Task wait; lock (historyGate) wait = nextSegment.Task;
            engine!.RequestBoundary();
            try { await wait.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            catch (TimeoutException) { throw new InvalidOperationException("Camera is still finalising replay footage. Try saving again shortly."); }
        }
        Segment[] selected;
        lock (historyGate)
        {
            selected = ReplaySelection.Select(history, seconds, end);
            if (selected.Length == 0) throw new InvalidOperationException("No recording history is available yet.");
            foreach (var segment in selected) pins[segment.Name] = pins.GetValueOrDefault(segment.Name) + 1;
        }
        var id = Guid.NewGuid().ToString("N"); string temporary = Path.Combine(clipDir, id + ".part"), final = Path.Combine(clipDir, id + ".mp4");
        try
        {
            await Task.Run(() => Engine.JoinClips(selected.Select(x => Path.Combine(buffer, x.Name + ".mp4")).ToArray(), selected.Select(x => x.StartUs).ToArray(), end, temporary), ct);
            await using var source = File.OpenRead(temporary); string hash = Convert.ToHexString(await SHA256.HashDataAsync(source, ct));
            var clip = new Clip(id, "SkyEye " + DateTime.Now.ToString("dd MMM HH:mm:ss"), Engine.Duration(temporary), source.Length, hash, DateTimeOffset.UtcNow,
                RecordingOrigin: new SkyEye.Core.Analysis.ClipRecordingOrigin(selected[0].Timeline, selected[0].StartUs, 1920, 1080, 0));
            source.Close();
            // The independent metadata journal survives a crash without rewriting a global catalogue.
            string metadata = Path.Combine(clipDir, id + ".json");
            File.WriteAllText(metadata + ".part", JsonSerializer.Serialize(clip, JsonDefaults.Options)); File.Move(metadata + ".part", metadata);
            File.Move(temporary, final);
            lock (historyGate)
            {
                clips.Add(clip);
            }
            return HttpResponse.Json(clip);
        }
        catch (Exception e) { Android.Util.Log.Warn("SkyEye", "Replay save failed: " + e); throw; }
        finally { lock (historyGate) foreach (var s in selected) { if (--pins[s.Name] == 0) pins.Remove(s.Name); } if (File.Exists(temporary)) File.Delete(temporary); }
    }
    void AddSegment(string name, long sequence, string timeline, long start, double duration, long wall)
    {
        lock (historyGate)
        {
            // Date the frame from its capture timestamp, including encoder delay, rather than receipt time.
            history.Add(new(name, sequence, timeline, start, duration, DateTimeOffset.UtcNow.AddSeconds((start - Engine.TimeUs()) / 1_000_000.0)));
            completedFrames += (long)Math.Round(duration * 30);
            // Retain ten minutes plus live-playback grace. Pinning protects concurrent clip assembly.
            while (history.Count > 1 && start - history[0].StartUs > 620_000_000 && !pins.ContainsKey(history[0].Name))
            {
                var oldest = history[0]; history.RemoveAt(0);
                foreach (var extension in new[] { ".mp4", ".ts" }) try { File.Delete(Path.Combine(buffer, oldest.Name + extension)); } catch { }
            }
            var old = nextSegment; nextSegment = Signal(); old.TrySetResult(true);
        }
    }
    void Fault(string message) { error = message; ready = false; Authority.Transition(CaptureState.Faulted); if (wake?.IsHeld == true) wake.Release(); Android.Util.Log.Error("SkyEye", message); _ = Task.Run(() => engine?.Close()); }
    async Task MonitorAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token);
                recentReplays.Cleanup();
                var status = JsonSerializer.Serialize(Status(), JsonDefaults.Options);
                // Credentials and viewing/control tokens are deliberately omitted from metrics.
                using var document = JsonDocument.Parse(status); var s = document.RootElement;
                File.AppendAllText(Path.Combine(root, "benchmarks.jsonl"), JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, state = Authority.State.ToString(), battery = s.GetProperty("battery").GetInt32(), temperatureC = s.GetProperty("temperatureC").GetDouble(), availableBytes = s.GetProperty("availableBytes").GetInt64(), completedFrames }) + "\n");
                if (Authority.State == CaptureState.Active && (s.GetProperty("battery").GetInt32() is >= 0 and <= 10 || s.GetProperty("availableBytes").GetInt64() < 128L * 1024 * 1024))
                {
                    await mutations.WaitAsync(lifetime.Token);
                    try { engine?.Pause(); Authority.Transition(CaptureState.Stopped); if (wake?.IsHeld == true) wake.Release(); error = "Stopped to preserve battery or storage. Saved clips remain available."; }
                    finally { mutations.Release(); }
                }
            }
        }
        catch (System.OperationCanceledException) { }
        catch (Exception e) { Android.Util.Log.Warn("SkyEye", "Metrics failed: " + e.GetType().Name); }
    }
    public override void OnDestroy()
    {
        lifetime.Cancel(); ready = false; engine?.Close(); if (wake?.IsHeld == true) wake.Release();
        if (server is not null) _ = server.DisposeAsync();
        try { if (registration is not null) discovery?.UnregisterService(registration); } catch { }
        Instance = null; base.OnDestroy();
    }
    sealed class Listener(CameraService owner) : Java.Lang.Object, Engine.IListener
    {
        public void OnReady() { owner.ready = true; Android.Util.Log.Info("SkyEye", "Camera armed and ready for screen-off commands"); }
        public void OnError(string? message) => owner.Fault(message ?? "Capture error");
        public void OnSegment(string? name, long sequence, string? timeline, long startUs, double duration, long wallMs) => owner.AddSegment(name!, sequence, timeline!, startUs, duration, wallMs);
    }
    sealed class Registration : Java.Lang.Object, NsdManager.IRegistrationListener
    {
        public void OnRegistrationFailed(NsdServiceInfo? info, NsdFailure error) => Android.Util.Log.Warn("SkyEye", "Discovery registration failed: " + error);
        public void OnUnregistrationFailed(NsdServiceInfo? info, NsdFailure error) { }
        public void OnServiceRegistered(NsdServiceInfo? info) { }
        public void OnServiceUnregistered(NsdServiceInfo? info) { }
    }
}
