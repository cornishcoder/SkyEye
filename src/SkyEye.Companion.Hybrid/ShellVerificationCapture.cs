using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    public bool CanCaptureVerificationFrames => CanAnalyzeTable && HasTableCalibration && !EditingTable
        && !CheckingTable && !table.HasPendingWork;

    public Task CaptureVerificationFramesAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!CanCaptureVerificationFrames || foreground is null) return;
        var operationId = Guid.NewGuid();
        var token = foreground.Token;
        var previousCapture = table.VerificationCapture;
        Message = "Capturing the bounded table check for review.";
        Publish();
        try
        {
            table.VerificationCapture = CaptureVerificationPixels;
            await VerifyTableWithDiagnosticsPausedAsync(token, operationId, CommandOrigin.Touch, includeChoices: false);
            var verification = app.State.Observation?.Verification;
            if (verification is null || verification.OperationId != operationId)
            {
                Message = "The table check returned no matching frame receipts.";
                Publish();
                return;
            }

            var folder = Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath,
                "o08-corpus", operationId.ToString("N"));
            var saved = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Length : 0;
            Message = $"Saved {saved} of {verification.Samples.Length} verifier frames. Table result: {app.State.Observation?.Reason}.";
            Publish();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            ShowError("Verifier evidence could not be saved. The readiness result was not accepted.");
        }
        finally { table.VerificationCapture = previousCapture; }
    });

    void CaptureVerificationPixels(VerificationCapture sample)
    {
        var root = Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath, "o08-corpus");
        Directory.CreateDirectory(root);
        var retained = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        if (retained > 40 * 1024 * 1024)
            throw new IOException("O08 diagnostic capture budget reached. Export evidence before another capture.");
        var folder = Path.Combine(root, sample.OperationId.ToString("N")); Directory.CreateDirectory(folder);
        var decoded = sample.Frame.DecodedReceipt!;
        byte[] pixels; string extension, encoding, inputSource; int width, height;
        if (sample.Frame.Image is SkyEye.Vision.Android.BitmapTableImage bitmap)
        {
            using var output = new MemoryStream();
            if (!bitmap.Bitmap.Compress(Android.Graphics.Bitmap.CompressFormat.Png!, 0, output))
                throw new IOException("Lossless verifier pixels could not be prepared.");
            pixels = output.ToArray(); extension = ".png"; encoding = "Png"; inputSource = "decoded-bitmap-lossless";
            width = bitmap.Bitmap.Width; height = bitmap.Bitmap.Height;
        }
        else
        {
            pixels = sample.Frame.Jpeg.Length > 0 ? sample.Frame.Jpeg : sample.Frame.ReviewImage();
            extension = ".jpg"; encoding = "Jpeg"; inputSource = "jpeg-analysis-input";
            width = decoded.Width; height = decoded.Height;
        }
        if (pixels.Length > 8 * 1024 * 1024) throw new IOException("O08 diagnostic frame exceeds the per-frame capture budget.");
        if (retained + pixels.Length + 512 * 1024 > 40 * 1024 * 1024) throw new IOException("O08 diagnostic capture budget reached.");
        var name = decoded.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sha = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(folder, name + extension), pixels);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var receipt = new
        {
            indexedFrame = new { sampleId = $"{sample.OperationId:N}-{name}", frameId = decoded.FrameId, sourceTimeUs = sample.Frame.SourceTimeUs,
                scope = new { source = "mounted-camera", timeline = sample.Frame.Timeline, generation = sample.AnalysisGeneration + 1, calibration = sample.CalibrationId },
                pixelFile = name + extension, pixelSha256 = sha, encoding, width, height, analyzerPixelSource = inputSource, embeddedFrameId = false },
            sample.OperationId, sample.CalibrationCorners, sample.Frame.CapturedAt, sample.Frame.ClockSampleAgeMs, sample.Frame.ClockUncertaintyMs,
            sample.Frame.FrameSource, sample.DetectionRequested, sample.AnalyzerConfigurationIdentity,
            decoder = decoded, sample.Measurement, configuration = ObservationPolicy.VerificationConfiguration,
            labelStatus = "Unreviewed development evidence. User declared still table and 16 balls; centres and unsafe intervals need independent labels."
        };
        File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(receipt, options));
    }
}
