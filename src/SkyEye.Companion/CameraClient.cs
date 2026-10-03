using System.Net.Http.Json;
using System.Text.Json;
using SkyEye.Core;

namespace SkyEye.Companion;

public sealed class CameraRequestException(System.Net.HttpStatusCode code, string message) : InvalidOperationException(message) { public System.Net.HttpStatusCode StatusCode { get; } = code; }

public sealed class CameraClient : IDisposable
{
    readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(45) };
    public string Address { get; private set; } = "";
    public string? HostToken { get; set; }
    public string ReadToken { get; private set; } = "";
    public void Connect(string input)
    {
        var text = input.Trim(); if (!text.Contains("://")) text = "http://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "http" || !string.IsNullOrEmpty(uri.UserInfo) || uri.Query.Length > 0 || uri.AbsolutePath != "/") throw new ArgumentException("Enter the camera's local address, for example 192.168.1.100:8765.");
        Address = uri.GetLeftPart(UriPartial.Authority); ReadToken = "";
    }
    public string MediaUrl(string path) => Address + path;
    public async Task<JsonElement> StatusAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Address + "/status", ct); using var document = await ParseAsync(response, ct);
        var status = document.RootElement.Clone(); ReadToken = status.GetProperty("readToken").GetString()!; return status;
    }
    public async Task<JsonElement> PostAsync(string path, object payload, CancellationToken ct = default)
    {
        // Android's streaming JsonContent uses chunked transfer; this bounded protocol requires a length.
        using var request = new HttpRequestMessage(HttpMethod.Post, Address + "/" + path.TrimStart('/')) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.Options)) };
        request.Content.Headers.ContentType = new("application/json");
        if (HostToken is not null) request.Headers.Add("X-SkyEye-Host", HostToken);
        using var response = await http.SendAsync(request, ct); using var document = await ParseAsync(response, ct); return document.RootElement.Clone();
    }
    static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            string message = "Camera request failed (" + (int)response.StatusCode + ").";
            try { using var doc = JsonDocument.Parse(body); message = doc.RootElement.GetProperty("error").GetString() ?? message; } catch { }
            throw new CameraRequestException(response.StatusCode, message);
        }
        return JsonDocument.Parse(body);
    }
    public async Task<Clip[]> ClipsAsync(CancellationToken ct = default) => await http.GetFromJsonAsync<Clip[]>(Address + "/clips?access=" + ReadToken, JsonDefaults.Options, ct) ?? [];
    public async Task<(byte[] Jpeg, DateTimeOffset CapturedAt, string Timeline)> SnapshotAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Address + "/table/snapshot"); if (HostToken is not null) request.Headers.Add("X-SkyEye-Host", HostToken);
        using var response = await http.SendAsync(request, ct); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadAsByteArrayAsync(ct), DateTimeOffset.Parse(response.Headers.GetValues("X-SkyEye-Captured-At").Single()), response.Headers.GetValues("X-SkyEye-Timeline").Single());
    }
    public async Task DownloadAsync(Clip clip, string destination, Action<long> progress, CancellationToken ct)
    {
        if (!Guid.TryParseExact(clip.Id, "N", out _) || clip.Bytes <= 0) throw new ArgumentException("Invalid clip metadata.");
        long have = File.Exists(destination) ? new FileInfo(destination).Length : 0;
        if (have > clip.Bytes) { File.Delete(destination); have = 0; }
        if (have < clip.Bytes)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Address + "/clips/" + clip.Id + "?access=" + ReadToken);
            if (have > 0) request.Headers.Range = new(have, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (have > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0;
            await using var output = new FileStream(destination, have == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write);
            await using var input = await response.Content.ReadAsStreamAsync(ct); byte[] buffer = new byte[65536]; int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0) { await output.WriteAsync(buffer.AsMemory(0, n), ct); have += n; progress(have); }
        }
        await using var file = File.OpenRead(destination);
        if (file.Length != clip.Bytes || !string.Equals(Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(file, ct)), clip.Sha256, StringComparison.OrdinalIgnoreCase)) { file.Close(); File.Delete(destination); throw new IOException("Download verification failed. Please retry."); }
    }
    public void Dispose() => http.Dispose();
}
