using System.Net;
using System.Text;
using System.Text.Json;
using SkyEye.Observation;

static class GptTableAdvisoryChecks
{
    public static async Task Run()
    {
        var jpeg = new byte[] { 0xff, 0xd8, 1, 2, 3, 0xff, 0xd9 };
        var handler = new CaptureHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"output":[{"type":"message","content":[{"type":"output_text","text":"{\"summary\":\"Three visible balls; one color uncertain.\",\"balls\":[{\"colour\":\"Red\",\"x\":123.5,\"y\":456,\"confidence\":0.83,\"visibility\":\"Clear\"},{\"colour\":\"Unknown\",\"x\":null,\"y\":null,\"confidence\":0.2,\"visibility\":\"Uncertain\"}]}"}]}]}
                """, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var frame = new TableFrameProvenance("sample-42", "frame-42", 123456, "timeline-7", 9, 11,
            DateTimeOffset.Parse("2026-10-26T12:00:00Z"), "mounted-camera", "cal-9", "analyzer-r3", 1920, 1080,
            [[.1, .2], [.9, .2], [.9, .8], [.1, .8]]);
        var result = await GptTableAdvisory.AskAsync(http, "test-key", jpeg, frame, "{\"balls\":[{\"colour\":\"Red\"}]}");
        var sent = JsonDocument.Parse(handler.Body!);
        var payload = sent.RootElement;
        Check(payload.GetProperty("model").GetString() == GptTableAdvisory.Model, "advisory selects the requested model");
        Check(payload.GetProperty("store").GetBoolean() == false, "advisory response storage is disabled");
        Check(handler.Uri == "https://api.openai.com/v1/responses", "advisory uses the Responses endpoint");
        Check(handler.Authorization == "Bearer test-key", "API key is sent only as bearer authorization");
        var content = payload.GetProperty("input")[0].GetProperty("content");
        var instruction = content[0].GetProperty("text").GetString()!;
        var imageUrl = content[1].GetProperty("image_url").GetString()!;
        Check(imageUrl == "data:image/jpeg;base64," + Convert.ToBase64String(jpeg), "request contains the exact selected JPEG bytes");
        Check(content[1].GetProperty("detail").GetString() == "high", "request retains detailed image input");
        Check(instruction.Contains("frame-42") && instruction.Contains("sample-42") && instruction.Contains("123456")
            && instruction.Contains("timeline-7") && instruction.Contains("media_generation") && instruction.Contains("analysis_generation")
            && instruction.Contains("cal-9") && instruction.Contains("analyzer-r3") && instruction.Contains("source image pixels")
            && instruction.Contains("identity; original selected source JPEG bytes") && instruction.Contains(result.ImageSha256),
            "request binds image hash, source/timeline/generation, calibration, analyzer and identity transform");
        var schema = payload.GetProperty("text").GetProperty("format");
        Check(schema.GetProperty("strict").GetBoolean() && schema.GetProperty("type").GetString() == "json_schema",
            "request uses strict structured output");
        Check(result.Provenance == frame
            && result.Balls.Length == 2 && result.Balls[0].X == 123.5 && result.Balls[1].X is null,
            "response is parsed and remains attached to its frame provenance");

        var refusalHandler = new CaptureHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"output\":[{\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}")
        });
        using var refusalHttp = new HttpClient(refusalHandler);
        await Throws<InvalidDataException>(() => GptTableAdvisory.AskAsync(refusalHttp, "test-key", jpeg, frame,
            "{}"), "refusal is not treated as an empty detection");

        var errorHandler = new CaptureHandler(_ => new(HttpStatusCode.TooManyRequests));
        using var errorHttp = new HttpClient(errorHandler);
        var error = await Throws<HttpRequestException>(() => GptTableAdvisory.AskAsync(errorHttp, "test-key", jpeg, frame,
            "{}"), "HTTP errors fail closed");
        Check(!error.Message.Contains("test-key"), "API error text does not include the key");

        var invalidHandler = new CaptureHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"summary\\\":\\\"x\\\",\\\"balls\\\":[{\\\"colour\\\":\\\"Red\\\",\\\"x\\\":2000,\\\"y\\\":10,\\\"confidence\\\":1,\\\"visibility\\\":\\\"Clear\\\"}]}\"}]}]}")
        });
        using var invalidHttp = new HttpClient(invalidHandler);
        await Throws<InvalidDataException>(() => GptTableAdvisory.AskAsync(invalidHttp, "test-key", jpeg, frame,
            "{}"), "out-of-frame coordinates are rejected");
    }

    static async Task<T> Throws<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T exception) { Console.WriteLine($"PASS {message}"); return exception; }
        throw new InvalidOperationException($"FAIL {message}");
    }

    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException($"FAIL {message}");
        Console.WriteLine($"PASS {message}");
    }

    sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Uri { get; private set; }
        public string? Authorization { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response(request);
        }
    }
}
