using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    const long MaxVerificationEvidenceBytes = 96L * 1024 * 1024;
    // Reserve 2 MiB per pixel file plus 512 KiB overhead for each of six observed frames.
    const long VerificationCaptureReservationBytes = 15L * 1024 * 1024;
    Guid? verificationCaptureOperationId;
    DateTimeOffset? verificationCaptureStartedAt, verificationCaptureCompletedAt;
    string? verificationCaptureResult;
    bool verificationCaptureRunning;

    public bool CanCaptureVerificationFrames => CanAnalyzeTable && HasTableCalibration && !EditingTable
        && !CheckingTable && !verificationCaptureRunning && !table.HasPendingWork;
    public bool VerificationCaptureRunning => verificationCaptureRunning;
    public string VerificationCaptureLabel => verificationCaptureRunning ? "Capturing verifier frames" : "Capture verifier frames";
    public bool VerificationCaptureFeedbackVisible => verificationCaptureOperationId is not null;
    public DateTimeOffset? VerificationCaptureStatusAt => verificationCaptureCompletedAt ?? verificationCaptureStartedAt;
    public string VerificationCaptureStatus => verificationCaptureRunning && verificationCaptureStartedAt is { } startedAt
        ? $"Capturing verifier frames… {UtcTime(startedAt)} · run {ShortId(verificationCaptureOperationId!.Value)}"
        : verificationCaptureOperationId is { } operationId && verificationCaptureCompletedAt is { } completedAt
            ? $"{verificationCaptureResult ?? "Capture completed."} · {UtcTime(completedAt)} · run {ShortId(operationId)}"
            : "";

    public Task CaptureVerificationFramesAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!CanCaptureVerificationFrames || foreground is null) return;
        var operationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        var root = VerificationEvidenceRoot;
        var retained = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
            : 0;
        if (retained + VerificationCaptureReservationBytes > MaxVerificationEvidenceBytes)
        {
            verificationCaptureOperationId = operationId;
            verificationCaptureStartedAt = verificationCaptureCompletedAt = startedAt;
            var requiredMiB = VerificationCaptureReservationBytes / (1024 * 1024);
            verificationCaptureResult = $"Capture not started · {Math.Max(0, (MaxVerificationEvidenceBytes - retained) / (1024 * 1024))} MiB free; {requiredMiB} MiB is required for six frames.";
            Publish();
            return;
        }
        verificationCaptureOperationId = operationId;
        verificationCaptureStartedAt = startedAt;
        verificationCaptureCompletedAt = null;
        verificationCaptureResult = null;
        verificationCaptureRunning = true;
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
                verificationCaptureResult = "Capture incomplete · no matching frame receipts";
                Message = "The table check returned no matching frame receipts.";
                Publish();
                return;
            }

            var folder = Path.Combine(root, operationId.ToString("N"));
            var saved = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Length : 0;
            verificationCaptureResult = $"Saved {saved} of {verification.Samples.Length} verifier frames. Table result: {app.State.Observation?.Reason}.";
            Message = verificationCaptureResult;
            Publish();
        }
        catch (OperationCanceledException)
        {
            verificationCaptureResult = "Capture cancelled";
        }
        catch (Exception)
        {
            verificationCaptureResult = "Verifier evidence could not be saved";
            ShowError("Verifier evidence could not be saved. The readiness result was not accepted.");
        }
        finally
        {
            table.VerificationCapture = previousCapture;
            verificationCaptureRunning = false;
            verificationCaptureCompletedAt = DateTimeOffset.UtcNow;
            Publish();
        }
    });

    string VerificationEvidenceRoot => Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath, "o08-corpus");

    void CaptureVerificationPixels(VerificationCapture sample)
    {
        var root = VerificationEvidenceRoot;
        Directory.CreateDirectory(root);
        var retained = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
        if (retained > MaxVerificationEvidenceBytes)
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
        if (retained + pixels.Length + 512 * 1024 > MaxVerificationEvidenceBytes) throw new IOException("O08 diagnostic capture budget reached.");
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
