using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkyEye.Observation;

public sealed record TableAdvisoryBall(string Colour, double? X, double? Y, double Confidence, string Visibility);
public sealed record TableFrameProvenance(string SampleId, string FrameId, long SourceTimeUs, string Timeline,
    long MediaGeneration, long AnalysisGeneration, DateTimeOffset CapturedAt, string FrameSource,
    string CalibrationId, string AnalyzerConfiguration, int Width, int Height, double[][] CalibrationCorners);
public sealed record TableAdvisoryResult(string Summary, TableAdvisoryBall[] Balls, string ImageSha256,
    TableFrameProvenance Provenance, double ElapsedMilliseconds);

/// <summary>Explicit, advisory-only image review. The caller supplies the exact selected source image and provenance.</summary>
public static class GptTableAdvisory
{
    public const string Model = "gpt-6-luna";
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<TableAdvisoryResult> AskAsync(HttpClient http, string apiKey, byte[] jpeg,
        TableFrameProvenance frame, string localDetections, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("An API key is required.", nameof(apiKey));
        if (jpeg is not { Length: > 0 and <= 4 * 1024 * 1024 }) throw new ArgumentException("Image must be between 1 byte and 4 MiB.", nameof(jpeg));
        if (frame is null || frame.Width <= 0 || frame.Height <= 0 || string.IsNullOrWhiteSpace(frame.SampleId)
            || string.IsNullOrWhiteSpace(frame.FrameId) || frame.SourceTimeUs < 0 || string.IsNullOrWhiteSpace(frame.CalibrationId)
            || frame.CalibrationCorners is not { Length: 4 })
            throw new ArgumentException("Frame provenance is incomplete.");

        var sha = Convert.ToHexStringLower(SHA256.HashData(jpeg));
        var provenance = JsonSerializer.Serialize(new
        {
            frame_id = frame.FrameId, sample_id = frame.SampleId, source_time_us = frame.SourceTimeUs,
            timeline = frame.Timeline, media_generation = frame.MediaGeneration, analysis_generation = frame.AnalysisGeneration,
            captured_at = frame.CapturedAt, frame_source = frame.FrameSource, width = frame.Width, height = frame.Height,
            pixel_origin = "top-left", coordinates = "source image pixels",
            image_sha256 = sha, calibration_id = frame.CalibrationId, calibration_corners = frame.CalibrationCorners,
            analyzer_configuration = frame.AnalyzerConfiguration,
            image_transform = "identity; original selected source JPEG bytes; coordinates map directly to this image",
            local_detector = localDetections
        }, JsonOptions);
        var schema = new
        {
            type = "object", additionalProperties = false,
            properties = new
            {
                summary = new { type = "string" },
                balls = new
                {
                    type = "array", items = new
                    {
                        type = "object", additionalProperties = false,
                        properties = new
                        {
                            colour = new { type = "string", @enum = new[] { "Red", "Yellow", "Black", "Cue", "Unknown" } },
                            x = new { type = new[] { "number", "null" } },
                            y = new { type = new[] { "number", "null" } },
                            confidence = new { type = "number" },
                            visibility = new { type = "string", @enum = new[] { "Clear", "Partial", "Uncertain" } }
                        }, required = new[] { "colour", "x", "y", "confidence", "visibility" }
                    }
                }
            }, required = new[] { "summary", "balls" }
        };
        var payload = new
        {
            model = Model,
            reasoning = new { effort = "none" },
            store = false,
            input = new object[]
            {
                new { role = "user", content = new object[]
                {
                    new { type = "input_text", text = "Give a cautious second opinion on only the visible balls in this exact table frame. Do not infer hidden balls or force a standard ball count. Report image pixel centre coordinates from the top-left; use null coordinates if not localizable. Use Unknown and low confidence when color is unclear. This is advisory and must not change detector output or game/readiness state. Frame and local detector provenance follows:\n" + provenance },
                    new { type = "input_image", detail = "high", image_url = "data:image/jpeg;base64," + Convert.ToBase64String(jpeg) }
                } }
            },
            text = new { format = new { type = "json_schema", name = "table_frame_advisory", strict = true, schema } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"OpenAI returned HTTP {(int)response.StatusCode}.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var outputText = ExtractOutputText(document.RootElement);
        using var resultDoc = JsonDocument.Parse(outputText);
        var root = resultDoc.RootElement;
        var summary = root.GetProperty("summary").GetString() ?? "";
        var items = root.GetProperty("balls");
        if (summary.Length > 1000 || items.GetArrayLength() > 64) throw new InvalidDataException("Structured output exceeded advisory bounds.");
        var balls = items.EnumerateArray().Select(item =>
        {
            var colour = item.GetProperty("colour").GetString()!;
            var visibility = item.GetProperty("visibility").GetString()!;
            var x = NullableNumber(item.GetProperty("x")); var y = NullableNumber(item.GetProperty("y"));
            var confidence = item.GetProperty("confidence").GetDouble();
            if (colour is not ("Red" or "Yellow" or "Black" or "Cue" or "Unknown")
                || visibility is not ("Clear" or "Partial" or "Uncertain")
                || !double.IsFinite(confidence) || confidence is < 0 or > 1
                || x.HasValue != y.HasValue || x is { } px && (px < 0 || px >= frame.Width) || y is { } py && (py < 0 || py >= frame.Height))
                throw new InvalidDataException("Structured output contained invalid coordinates or labels.");
            return new TableAdvisoryBall(colour, x, y, confidence, visibility);
        }).ToArray();
        watch.Stop();
        return new(summary, balls, sha, frame, watch.Elapsed.TotalMilliseconds);
    }

    static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Response did not contain output items.");
        foreach (var item in output.EnumerateArray())
        {
            if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) && type.GetString() == "refusal")
                        throw new InvalidDataException("The model declined this image review.");
                    if (block.TryGetProperty("type", out type) && type.GetString() == "output_text" && block.TryGetProperty("text", out var text))
                        return text.GetString() ?? throw new InvalidDataException("Structured response text was empty.");
                }
        }
        throw new InvalidDataException("Response did not contain structured output text.");
    }

    static double? NullableNumber(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
