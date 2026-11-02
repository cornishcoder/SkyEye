using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    // Keep the evidence store bounded while leaving room for several lossless live captures.
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
        if (!VerificationEvidenceBudget.CanReserveCapture(retained))
        {
            var requiredMiB = (long)Math.Ceiling(VerificationEvidenceBudget.CaptureReservationBytes / (1024d * 1024d));
            var freeMiB = Math.Max(0, (VerificationEvidenceBudget.MaximumBytes - retained) / (1024 * 1024));
            verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow,
                $"Capture not started · {freeMiB} MiB free; {requiredMiB} MiB is required for up to {ObservationPolicy.MaximumCapturedVerificationSamples} frames", 0);
            Message = VerificationCaptureStatus;
            Publish();
            return;
        }
        var token = foreground!.Token;
        var previousCapture = table.VerificationCapture;
        var pendingWrites = new VerificationCaptureWriteQueue();
        var completionSummary = "Capture ended without a matching result";
        Message = "Capturing the bounded table check for review.";
        Publish();
        try
        {
            table.VerificationCapture = sample => CaptureVerificationPixels(sample, pendingWrites);
            await VerifyTableWithDiagnosticsPausedAsync(token, operationId, CommandOrigin.Touch, includeChoices: false);
            var observation = app.State.Observation;
            var verification = observation?.Verification;
            if (verification is null || verification.OperationId != operationId)
            {
                completionSummary = "Capture incomplete · no matching table result";
                return;
            }

            var tableResult = observation!.Outcome == ObservationOutcome.Ready
                ? "Ready"
                : observation.Reason == ObservationReason.None
                    ? observation.Outcome.ToString()
                    : $"{observation.Outcome} ({observation.Reason})";
            completionSummary = $"Table result: {tableResult}.";
        }
        catch (OperationCanceledException)
        {
            completionSummary = "Capture cancelled";
        }
        catch (Exception)
        {
            completionSummary = "Verifier evidence could not be saved";
            ShowError("Verifier evidence could not be saved. The readiness result was not accepted.");
        }
        finally
        {
            table.VerificationCapture = previousCapture;
            var writeFailures = await pendingWrites.DrainAsync();
            var saved = VerificationCaptureReceipts.CountValid(Path.Combine(root, operationId.ToString("N")), operationId);
            if (writeFailures > 0) completionSummary += $" · {writeFailures} evidence write(s) failed";
            if (verificationCaptureStatus.Running)
            {
                verificationCaptureStatus.Complete(operationId, DateTimeOffset.UtcNow, completionSummary, saved);
            }
            Message = VerificationCaptureStatus;
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

    void CaptureVerificationPixels(VerificationCapture sample, VerificationCaptureWriteQueue pendingWrites)
    {
        var root = VerificationEvidenceRoot;
        var decoded = sample.Frame.DecodedReceipt ?? throw new IOException("Verifier frame receipt is unavailable.");
        global::Android.Graphics.Bitmap? bitmapCopy = null;
        byte[]? encodedPixels = null;
        string extension, encoding, inputSource; int width, height;
        try
        {
            if (sample.Frame.Image is SkyEye.Vision.Android.BitmapTableImage bitmap)
            {
                // Copy the exact decoded ARGB pixels while the TableFrame is borrowed.
                // PNG compression and storage happen on the ordered background writer.
                var config = bitmap.Bitmap.GetConfig() ?? global::Android.Graphics.Bitmap.Config.Argb8888;
                if (config is null) throw new IOException("Decoded verifier pixel format is unavailable.");
                bitmapCopy = bitmap.Bitmap.Copy(config, false)
                    ?? throw new IOException("Lossless verifier pixel snapshot could not be copied.");
                extension = ".png"; encoding = "Png"; inputSource = "decoded-bitmap-lossless";
                width = bitmapCopy.Width; height = bitmapCopy.Height;
            }
            else
            {
                encodedPixels = sample.Frame.Jpeg.Length > 0 ? sample.Frame.Jpeg.ToArray() : sample.Frame.ReviewImage();
                extension = ".jpg"; encoding = "Jpeg"; inputSource = "jpeg-analysis-input";
                width = decoded.Width; height = decoded.Height;
            }

            var pending = new PendingVerificationEvidence(root, sample.OperationId, sample.AnalysisGeneration + 1,
                sample.CalibrationId, sample.CalibrationCorners.ToArray(), sample.Frame.CapturedAt,
                sample.Frame.ClockSampleAgeMs, sample.Frame.ClockUncertaintyMs, sample.Frame.FrameSource,
                sample.DetectionRequested, sample.AnalyzerConfigurationIdentity, decoded, sample.Frame.SourceTimeUs,
                sample.Frame.Timeline, sample.Measurement, inputSource, extension, encoding, width, height,
                bitmapCopy, encodedPixels);
            pendingWrites.Enqueue(() => WriteVerificationPixels(pending));
            bitmapCopy = null; // ownership transferred to the queue
        }
        catch
        {
            bitmapCopy?.Recycle();
            bitmapCopy?.Dispose();
            throw;
        }
    }

    void WriteVerificationPixels(PendingVerificationEvidence sample)
    {
        try
        {
            byte[] pixels;
            if (sample.Bitmap is { } bitmap)
            {
                using var output = new MemoryStream();
                if (!bitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Png!, 0, output))
                    throw new IOException("Lossless verifier pixels could not be prepared.");
                pixels = output.ToArray();
            }
            else pixels = sample.EncodedPixels ?? throw new IOException("Verifier pixel snapshot is unavailable.");

            Directory.CreateDirectory(sample.Root);
            var retained = Directory.EnumerateFiles(sample.Root, "*", SearchOption.AllDirectories).Sum(p => new FileInfo(p).Length);
            if (retained > VerificationEvidenceBudget.MaximumBytes)
                throw new IOException("O08 diagnostic capture budget reached. Export evidence before another capture.");
            if (pixels.Length > VerificationEvidenceBudget.MaximumFrameBytes)
                throw new IOException("O08 diagnostic frame exceeds the per-frame capture budget.");
            if (!VerificationEvidenceBudget.CanWriteFrame(retained, pixels.Length))
                throw new IOException("O08 diagnostic capture budget reached.");

            var folder = Path.Combine(sample.Root, sample.OperationId.ToString("N")); Directory.CreateDirectory(folder);
            var name = sample.Decoded.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sha = Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(folder, name + sample.Extension), pixels);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
            var receipt = new
            {
                indexedFrame = new { sampleId = $"{sample.OperationId:N}-{name}", frameId = sample.Decoded.FrameId, sourceTimeUs = sample.SourceTimeUs,
                    scope = new { source = "mounted-camera", timeline = sample.Timeline, generation = sample.Generation, calibration = sample.CalibrationId },
                    pixelFile = name + sample.Extension, pixelSha256 = sha, encoding = sample.Encoding, width = sample.Width, height = sample.Height,
                    analyzerPixelSource = sample.InputSource, embeddedFrameId = false },
                operationId = sample.OperationId, calibrationCorners = sample.CalibrationCorners, capturedAt = sample.CapturedAt,
                clockSampleAgeMs = sample.ClockSampleAgeMs, clockUncertaintyMs = sample.ClockUncertaintyMs,
                frameSource = sample.FrameSource, detectionRequested = sample.DetectionRequested,
                analyzerConfigurationIdentity = sample.AnalyzerConfigurationIdentity,
                decoder = sample.Decoded, measurement = sample.Measurement, configuration = ObservationPolicy.VerificationConfiguration,
                labelStatus = "Unreviewed development evidence. User declared still table and 16 balls; centres and unsafe intervals need independent labels."
            };
            File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(receipt, options));
            if (verificationCaptureStatus.RecordWrittenReceipt(sample.OperationId, sample.Decoded.FrameId))
                MainThread.BeginInvokeOnMainThread(Publish);
        }
        finally
        {
            sample.Bitmap?.Recycle();
            sample.Bitmap?.Dispose();
        }
    }

    sealed record PendingVerificationEvidence(string Root, Guid OperationId, long Generation, string CalibrationId,
        SkyEye.Core.Point[] CalibrationCorners, DateTimeOffset CapturedAt, double? ClockSampleAgeMs, double? ClockUncertaintyMs,
        string FrameSource, bool DetectionRequested, string AnalyzerConfigurationIdentity, DecodedFrameReceipt Decoded,
        long? SourceTimeUs, string Timeline, VerificationMeasurement Measurement, string InputSource, string Extension,
        string Encoding, int Width, int Height, global::Android.Graphics.Bitmap? Bitmap, byte[]? EncodedPixels);
}
