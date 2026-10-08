using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SkyEye.Core;

public sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Query, Dictionary<string, string> Headers, byte[] Body)
{
    public string? HostToken => Headers.GetValueOrDefault("X-SkyEye-Host");
    public T Json<T>() => JsonSerializer.Deserialize<T>(Body, JsonDefaults.Options) ?? throw new ArgumentException("Missing request body.");
}
public static class JsonDefaults { public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web); }
public sealed record HttpResponse(int Status, string ContentType, byte[]? Body = null, string? FilePath = null, Dictionary<string, string>? Headers = null,
    Func<Stream, CancellationToken, Task>? StreamingBody = null)
{
    public static HttpResponse Json(object value, int status = 200) => new(status, "application/json", JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options));
    public static HttpResponse Text(string value, string contentType = "text/plain") => new(200, contentType, Encoding.UTF8.GetBytes(value));
}

// Small bounded HTTP/1.1 server used on Android; one request per connection.
public sealed class LocalHttpServer(int port, Func<HttpRequest, CancellationToken, Task<HttpResponse>> handler, IPAddress? bindAddress = null) : IAsyncDisposable
{
    readonly TcpListener listener = new(bindAddress ?? IPAddress.Any, port);
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim capacity = new(16);
    Task? run;
    readonly System.Collections.Concurrent.ConcurrentDictionary<long, Task> clients = new();
    long nextClient;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public void Start() { listener.Start(32); run = AcceptAsync(); }
    async Task AcceptAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(lifetime.Token); }
            catch (Exception e) when (e is OperationCanceledException or SocketException) { break; }
            if (!capacity.Wait(0)) { client.Dispose(); continue; }
            long id = Interlocked.Increment(ref nextClient);
            var job = ServeAsync(client); clients[id] = job;
            _ = job.ContinueWith(_ => { clients.TryRemove(id, out var ignored); capacity.Release(); }, TaskScheduler.Default);
        }
    }
    async Task ServeAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, timeout.Token);
                HttpResponse response;
                try { response = await handler(request, timeout.Token); }
                catch (UnauthorizedAccessException e) { response = HttpResponse.Json(new { error = e.Message }, 403); }
                catch (ArgumentException e) { response = HttpResponse.Json(new { error = e.Message }, 400); }
                catch (InvalidOperationException e) { response = HttpResponse.Json(new { error = e.Message }, 409); }
                catch (Exception) { response = HttpResponse.Json(new { error = "Camera operation failed. Check camera status." }, 500); }
                await WriteResponseAsync(stream, request, response, timeout.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ArgumentException) { }
        }
    }
    public static async Task<HttpRequest> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        using var header = new MemoryStream(); int state = 0;
        var one = new byte[1];
        while (state != 4)
        {
            if (header.Length >= 16384) throw new ArgumentException("Request headers too large.");
            await stream.ReadExactlyAsync(one, ct); header.WriteByte(one[0]);
            state = (state, one[0]) switch { (0, 13) => 1, (1, 10) => 2, (2, 13) => 3, (3, 10) => 4, (_, 13) => 1, _ => 0 };
        }
        var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || !first[1].StartsWith('/')) throw new ArgumentException("Invalid HTTP request.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(x => x.Contains(':'))) { var p = line.IndexOf(':'); if (!headers.TryAdd(line[..p].Trim(), line[(p + 1)..].Trim())) throw new ArgumentException("Duplicate header."); }
        if (headers.ContainsKey("Transfer-Encoding")) throw new ArgumentException("Chunked request bodies are not supported.");
        int length = 0;
        if (headers.TryGetValue("Content-Length", out var raw) && (!int.TryParse(raw, out length) || length < 0 || length > 65536)) throw new ArgumentException("Invalid request length.");
        var body = new byte[length]; await stream.ReadExactlyAsync(body, ct);
        var uri = new Uri("http://camera" + first[1]);
        var query = new Dictionary<string, string>();
        foreach (var entry in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)) { var p = entry.Split('=', 2); query[Uri.UnescapeDataString(p[0])] = p.Length == 2 ? Uri.UnescapeDataString(p[1]) : ""; }
        return new(first[0], Uri.UnescapeDataString(uri.AbsolutePath), query, headers, body);
    }
    static async Task WriteResponseAsync(Stream stream, HttpRequest request, HttpResponse response, CancellationToken ct)
    {
        if (response.StreamingBody is not null)
        {
            // HTTP/1.1 close-delimited body; send every encoded sample immediately.
            // No file assembly, range or content-length is implied for a live stream.
            var header = $"HTTP/1.1 {response.Status} OK\r\nContent-Type: {response.ContentType}\r\nConnection: close\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
            if (request.Method != "HEAD") await response.StreamingBody(stream, ct);
            return;
        }
        using var file = response.FilePath is null ? null : File.Open(response.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        long full = file?.Length ?? response.Body?.LongLength ?? 0, start = 0, end = full - 1; int status = response.Status;
        var extra = response.Headers ?? new();
        if (file is not null)
        {
            extra["Accept-Ranges"] = "bytes";
            if (request.Headers.TryGetValue("Range", out var range))
            {
                var parts = range.StartsWith("bytes=") ? range[6..].Split('-') : [];
                if (parts.Length != 2 || !long.TryParse(parts[0], out start) || start < 0 || start >= full ||
                    (parts[1].Length > 0 && (!long.TryParse(parts[1], out end) || end < start)))
                { status = 416; extra["Content-Range"] = $"bytes */{full}"; start = 0; end = -1; }
                else { end = Math.Min(end, full - 1); status = 206; extra["Content-Range"] = $"bytes {start}-{end}/{full}"; }
            }
        }
        long remaining = Math.Max(0, end - start + 1);
        var h = new StringBuilder($"HTTP/1.1 {status} {(status < 400 ? "OK" : "Error")}\r\nContent-Type: {response.ContentType}\r\nContent-Length: {remaining}\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: Range, Content-Type, X-SkyEye-Host\r\nAccess-Control-Expose-Headers: Content-Range, Accept-Ranges\r\nCache-Control: no-store\r\n");
        foreach (var entry in extra) h.Append(entry.Key).Append(": ").Append(entry.Value).Append("\r\n"); h.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(h.ToString()), ct);
        if (request.Method == "HEAD") return;
        if (file is null) { if (response.Body is not null) await stream.WriteAsync(response.Body, ct); return; }
        file.Position = start; var buffer = new byte[65536];
        while (remaining > 0) { int n = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct); if (n == 0) throw new EndOfStreamException(); await stream.WriteAsync(buffer.AsMemory(0, n), ct); remaining -= n; }
    }
    public async ValueTask DisposeAsync() { lifetime.Cancel(); listener.Stop(); if (run is not null) await run; await Task.WhenAll(clients.Values); lifetime.Dispose(); capacity.Dispose(); }
}
