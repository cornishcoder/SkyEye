using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    // Keep the evidence store bounded while leaving room for several lossless live captures.
    const long MaxVerificationEvidenceBytes = 192L * 1024 * 1024;
    // Reserve 2 MiB per pixel file plus 512 KiB overhead for each bounded sample.
    const long VerificationCaptureReservationBytes = (2L * 1024 * 1024 + 512L * 1024)
        * ObservationPolicy.MaximumCapturedVerificationSamples;
    readonly VerificationCaptureRunStatus verificationCaptureStatus = new();

    public bool CanCaptureVerificationFrames => CanAnalyzeTable && HasTableCalibration && !EditingTable && !LiveDetectBusy
        && !CheckingTable && !verificationCaptureStatus.Running && !table.HasPendingWork;
    public bool VerificationCaptureRunning => verificationCaptureStatus.Running;
    public string VerificationCaptureLabel => VerificationCaptureRunning ? "Capturing verifier frames" : "Capture verifier frames";
    public bool VerificationCaptureFeedbackVisible => verificationCaptureStatus.Visible;
    public DateTimeOffset? VerificationCaptureStatusAt => verificationCaptureStatus.StatusAt;
    public string VerificationCaptureStatus => verificationCaptureStatus.Format(UtcTime);

    public Task CaptureVerificationFramesAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (verificationCaptureStatus.Running) return;
        var canStart = CanCaptureVerificationFrames && foreground is not null;
        var operationId = Guid.NewGuid();
        var startedAt = DateTimeOffset.UtcNow;
        verificationCaptureStatus.Begin(operationId, startedAt);
        Publish();
        if (!canStart)
        {
            var summary = $"Capture not started · {VerificationCaptureUnavailableReason()}";
            verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow, summary, 0);
            Message = VerificationCaptureStatus;
            Publish();
            return;
        }

        var root = VerificationEvidenceRoot;
        long retained;
        try
        {
            retained = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length)
                : 0;
        }
        catch (Exception)
        {
            verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow, "Capture not started · evidence storage could not be checked", 0);
            Message = VerificationCaptureStatus;
            Publish();
            return;
        }
        if (retained + VerificationCaptureReservationBytes > MaxVerificationEvidenceBytes)
        {
            var requiredMiB = (long)Math.Ceiling(VerificationCaptureReservationBytes / (1024d * 1024d));
            var freeMiB = Math.Max(0, (MaxVerificationEvidenceBytes - retained) / (1024 * 1024));
            verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow,
                $"Capture not started · {freeMiB} MiB free; {requiredMiB} MiB is required for up to {ObservationPolicy.MaximumCapturedVerificationSamples} frames", 0);
            Message = VerificationCaptureStatus;
            Publish();
            return;
        }
        var token = foreground!.Token;
        var previousCapture = table.VerificationCapture;
        Message = "Capturing the bounded table check for review.";
        Publish();
        try
        {
            table.VerificationCapture = CaptureVerificationPixels;
            await VerifyTableWithDiagnosticsPausedAsync(token, operationId, CommandOrigin.Touch, includeChoices: false);
            var observation = app.State.Observation;
            var verification = observation?.Verification;
            var saved = VerificationCaptureReceipts.CountValid(Path.Combine(root, operationId.ToString("N")), operationId);
            if (verification is null || verification.OperationId != operationId)
            {
                verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow,
                    "Capture incomplete · no matching table result", saved);
                Message = VerificationCaptureStatus;
                return;
            }

            var tableResult = observation!.Outcome == ObservationOutcome.Ready
                ? "Ready"
                : observation.Reason == ObservationReason.None
                    ? observation.Outcome.ToString()
                    : $"{observation.Outcome} ({observation.Reason})";
            verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow,
                $"Table result: {tableResult}.", saved);
            Message = VerificationCaptureStatus;
            Publish();
        }
        catch (OperationCanceledException)
        {
            var saved = VerificationCaptureReceipts.CountValid(Path.Combine(root, operationId.ToString("N")), operationId);
            verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow, "Capture cancelled", saved);
            Message = VerificationCaptureStatus;
        }
        catch (Exception)
        {
            var saved = VerificationCaptureReceipts.CountValid(Path.Combine(root, operationId.ToString("N")), operationId);
            verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow, "Verifier evidence could not be saved", saved);
            Message = VerificationCaptureStatus;
            ShowError("Verifier evidence could not be saved. The readiness result was not accepted.");
        }
        finally
        {
            table.VerificationCapture = previousCapture;
            if (verificationCaptureStatus.Running)
            {
                var saved = VerificationCaptureReceipts.CountValid(Path.Combine(root, operationId.ToString("N")), operationId);
                verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow, "Capture ended without a matching result", saved);
                Message = VerificationCaptureStatus;
            }
            Publish();
        }
    });

    string VerificationCaptureUnavailableReason() => foreground is null ? "an active camera session is required"
        : !CanAnalyzeTable ? "an active live source is required"
        : !HasTableCalibration ? "table calibration is required"
        : EditingTable ? "finish table calibration first"
        : CheckingTable ? "another table check is running"
        : table.HasPendingWork ? "wait for the current table action to finish"
        : "capture prerequisites changed";

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
        if (verificationCaptureStatus.RecordWrittenReceipt(sample.OperationId, decoded.FrameId))
            MainThread.BeginInvokeOnMainThread(Publish);
    }
}
