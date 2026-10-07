#if SKYEYE_PERFORMANCE
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    void CaptureVerificationPixels(VerificationCapture sample)
    {
        var root = Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath, "o08-corpus");
        Directory.CreateDirectory(root);
        var retained = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        if (retained > 40 * 1024 * 1024)
            throw new IOException("O08 diagnostic capture budget reached. Export evidence before another capture.");
        var folder = Path.Combine(root, sample.OperationId.ToString("N")); Directory.CreateDirectory(folder);
        var decoded = sample.Frame.DecodedReceipt!;
        var pixels = sample.Frame.ReviewImage();
        if (pixels.Length > 4 * 1024 * 1024) throw new IOException("O08 diagnostic frame exceeds the capture budget.");
        if (retained + pixels.Length + 512 * 1024 > 40 * 1024 * 1024) throw new IOException("O08 diagnostic capture budget reached.");
        var name = decoded.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sha = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(folder, name + ".jpg"), pixels);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var receipt = new
        {
            indexedFrame = new { sampleId = $"{sample.OperationId:N}-{name}", frameId = decoded.FrameId, sourceTimeUs = sample.Frame.SourceTimeUs,
                scope = new { source = "mounted-camera", timeline = sample.Frame.Timeline, generation = sample.AnalysisGeneration + 1, calibration = sample.CalibrationId },
                pixelFile = name + ".jpg", pixelSha256 = sha, encoding = "Jpeg", width = decoded.Width, height = decoded.Height, embeddedFrameId = false },
            sample.OperationId, sample.CalibrationCorners, sample.Frame.CapturedAt, sample.Frame.ClockSampleAgeMs, sample.Frame.ClockUncertaintyMs,
            decoder = decoded, sample.Measurement, configuration = ObservationPolicy.VerificationConfiguration,
            labelStatus = "Unreviewed development evidence. User declared still table and 16 balls; centres and unsafe intervals need independent labels."
        };
        File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(receipt, options));
    }
}
#endif
