using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Observation;

namespace SkyEye.Companion.Hybrid;

public sealed record VisionDiagnosticBallMark(string Id, string Colour, string Visibility,
    double X, double Y, double RadiusX, double RadiusY);
sealed record VisionDiagnosticStoredBall(string Id, string Colour, double X, double Y, string Visibility,
    double RadiusX, double RadiusY);
sealed record VisionDiagnosticStoredDetectionBall(string Id, string Colour, double X, double Y, double Confidence);
sealed record VisionDiagnosticStoredDetection(string Model, string Provenance, bool Uncertain,
    VisionDiagnosticStoredDetectionBall[] Balls);
sealed record VisionDiagnosticStoredScope(string Source, string Timeline, long Generation, string Calibration);
sealed record VisionDiagnosticStoredFrame(string SampleId, long FrameId, long? SourceTimeUs,
    VisionDiagnosticStoredScope Scope, string PixelFile, string PixelSha256, string Encoding,
    int Width, int Height, bool EmbeddedFrameId);
sealed record VisionDiagnosticLabelReceipt(int SchemaVersion, string DiagnosticSessionId, string RecordingSessionId, bool Reviewed,
    bool AllVisibleBallsLabelled, bool EmptyFrameConfirmed,
    DateTimeOffset CapturedAt, double FrameAgeMs, long AnalysisGeneration, string CalibrationId,
    string AnalyzerConfiguration, SkyEye.Core.Point[] CalibrationCorners, string FrameSource, VisionDiagnosticStoredFrame IndexedFrame,
    string[] Tags, VisionDiagnosticStoredBall[] Balls, string Notes)
{
    public VisionDiagnosticStoredDetection? DetectionOutput { get; init; }
}
sealed record VisionDiagnosticLabelSessionReceipt(int SchemaVersion, string DiagnosticSessionId, string RecordingSessionId,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, bool Reviewed, int FrameCount, string Notes);

public sealed partial class ShellSession
{
    const int VisionLabelMaximumFrames = 120;
    const long VisionLabelMaximumBytes = 40L * 1024 * 1024;
    const int VisionLabelMaximumImageBytes = 4 * 1024 * 1024;
    static readonly JsonSerializerOptions VisionLabelJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly List<VisionDiagnosticBallMark> visionDiagnosticMarks = [];
    readonly HashSet<string> visionDiagnosticSceneTags = new(StringComparer.Ordinal);
    readonly HashSet<string> visionDiagnosticKnownBallIds = new(StringComparer.Ordinal);
    Guid visionDiagnosticLabelSessionId;
    string? visionDiagnosticLabelPreviewSampleId;
    string? visionDiagnosticLabelImageUri;
    string visionDiagnosticLabelColour = "unknown";
    string visionDiagnosticLabelVisibility = "Visible";
    string visionDiagnosticLabelLighting = "ordinary";
    string visionDiagnosticLabelMotion = "still";
    string visionDiagnosticLabelNotes = "";
    bool visionDiagnosticEmptyFrameConfirmed;
    bool visionDiagnosticAllBallsLabelledConfirmed;
    bool visionDiagnosticLabelSavePending;
    int visionDiagnosticSavedLabels;
    bool visionDiagnosticLabelSessionFinalized;
    string visionDiagnosticLabelStatus = "No labelled diagnostic frames saved.";
    string visionAdvisoryStatus = "No second opinion requested.";
    TableAdvisoryResult? visionAdvisoryResult;
    bool visionAdvisoryPending;
    bool visionAdvisoryHomeFeedbackVisible;

    public VisionDiagnosticLabelFrame? VisionDiagnosticLabelFrame => table.VisionDiagnostics.LabelFrame;
    public string? VisionDiagnosticLabelImageUri => visionDiagnosticLabelImageUri;
    public IReadOnlyList<VisionDiagnosticBallMark> VisionDiagnosticMarks => visionDiagnosticMarks;
    public IReadOnlyList<string> VisionDiagnosticKnownBallIds => visionDiagnosticKnownBallIds.Order(StringComparer.Ordinal).ToArray();
    public string VisionDiagnosticLabelColour => visionDiagnosticLabelColour;
    public string VisionDiagnosticLabelVisibility => visionDiagnosticLabelVisibility;
    public string VisionDiagnosticLabelLighting => visionDiagnosticLabelLighting;
    public string VisionDiagnosticLabelMotion => visionDiagnosticLabelMotion;
    public string VisionDiagnosticLabelNotes => visionDiagnosticLabelNotes;
    public bool VisionDiagnosticEmptyFrameConfirmed => visionDiagnosticEmptyFrameConfirmed;
    public bool VisionDiagnosticAllBallsLabelledConfirmed => visionDiagnosticAllBallsLabelledConfirmed;
    public int VisionDiagnosticSavedLabels => visionDiagnosticSavedLabels;
    public bool VisionDiagnosticLabelSessionFinalized => visionDiagnosticLabelSessionFinalized;
    public bool VisionDiagnosticLabelSavePending => visionDiagnosticLabelSavePending;
    public string VisionDiagnosticLabelStatus => visionDiagnosticLabelStatus;
    public string VisionAdvisoryStatus => visionAdvisoryStatus;
    public TableAdvisoryResult? VisionAdvisoryResult => visionAdvisoryResult;
    public bool VisionAdvisoryPending => visionAdvisoryPending;
    public bool CanCaptureAdvisoryFrame => PersonalKeyConfigured && CanAnalyzeTable && HasTableCalibration && !EditingTable
        && !table.HasPendingWork && VisionDiagnosticLabelFrame is null && !VisionDiagnosticLabelCapturePending
        && (VisionDiagnosticsActive || CanStartVisionDiagnostics);
    public bool VisionAdvisoryHomeFeedbackVisible => visionAdvisoryHomeFeedbackVisible;
    public string AdvisoryFrameUnavailableReason => !PersonalKeyConfigured ? "Save a personal OpenAI key in Settings."
        : !CanAnalyzeTable ? "Start a live session and watch the live camera as host."
        : !HasTableCalibration ? "Calibrate the table before capturing a frame."
        : EditingTable ? "Finish editing the table before capturing a frame."
        : VisionDiagnosticLabelFrame is not null ? "Finish or close the current diagnostic frame first."
        : VisionDiagnosticLabelCapturePending ? "Wait for the current diagnostic frame request."
        : table.HasPendingWork ? "Wait for the current table action to finish."
        : visionDiagnosticSavedLabels > 0 && !visionDiagnosticLabelSessionFinalized ? "Finish the current reviewed label session first."
        : "";
    public bool CanFinishVisionDiagnosticLabelSession => !VisionDiagnosticsActive && visionDiagnosticSavedLabels > 0
        && !visionDiagnosticLabelSessionFinalized && visionDiagnosticLabelSessionId != Guid.Empty;

    public async Task<bool> CaptureAdvisoryFrameAsync()
    {
        Guid sessionId = Guid.Empty;
        bool startedSampling = false;
        bool requested = false;
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            visionAdvisoryHomeFeedbackVisible = true;
            if (!CanCaptureAdvisoryFrame)
            {
                visionDiagnosticLabelStatus = AdvisoryFrameUnavailableReason;
                Changed?.Invoke();
                return;
            }
            string message;
            startedSampling = !VisionDiagnosticsActive;
            if (startedSampling && !table.StartVisionDiagnostics(out message))
            {
                visionDiagnosticLabelStatus = message;
                Changed?.Invoke();
                return;
            }
            sessionId = table.VisionDiagnostics.SessionId;
            if (!table.RequestVisionDiagnosticLabelFrame(out message))
            {
                sessionId = Guid.Empty;
                visionDiagnosticLabelStatus = message;
                if (startedSampling) _ = table.StopVisionDiagnosticsAsync();
                Changed?.Invoke();
                return;
            }
            requested = true;
            visionDiagnosticLabelStatus = "Capturing one fresh frame for an optional advisory…";
            Changed?.Invoke();
        });
        if (startedSampling && sessionId != Guid.Empty) _ = StopAdvisorySamplingAfterFrameAsync(sessionId);
        return requested;
    }

    async Task StopAdvisorySamplingAfterFrameAsync(Guid sessionId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            while (!timeout.IsCancellationRequested && !disposed)
            {
                var snapshot = table.VisionDiagnostics;
                if (snapshot.SessionId != sessionId || !snapshot.Active) return;
                if (snapshot.LabelFrame is not null)
                {
                    await table.StopVisionDiagnosticsAsync().ConfigureAwait(false);
                    return;
                }
                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            var current = table.VisionDiagnostics;
            if (!disposed && current.Active && current.SessionId == sessionId)
                await table.StopVisionDiagnosticsAsync().ConfigureAwait(false);
        }
    }

    void SyncVisionDiagnosticLabelState(VisionDiagnosticsSnapshot snapshot)
    {
        if (snapshot.SessionId != visionDiagnosticLabelSessionId)
        {
            visionDiagnosticLabelSessionId = snapshot.SessionId;
            visionDiagnosticLabelPreviewSampleId = null;
            visionDiagnosticLabelImageUri = null;
            visionDiagnosticMarks.Clear();
            visionDiagnosticKnownBallIds.Clear();
            visionDiagnosticEmptyFrameConfirmed = false;
            visionDiagnosticAllBallsLabelledConfirmed = false;
            visionDiagnosticSavedLabels = 0;
            visionDiagnosticLabelSessionFinalized = false;
            visionDiagnosticLabelLighting = "ordinary";
            visionDiagnosticLabelMotion = "still";
            visionDiagnosticLabelNotes = "";
            visionDiagnosticSceneTags.Clear();
            if (snapshot.SessionId != Guid.Empty)
            {
                var directory = VisionDiagnosticLabelDirectory(snapshot.SessionId);
                if (Directory.Exists(directory))
                {
                    visionDiagnosticSavedLabels = Directory.EnumerateFiles(directory, "*.label.json", SearchOption.TopDirectoryOnly).Take(VisionLabelMaximumFrames + 1).Count();
                    visionDiagnosticLabelSessionFinalized = File.Exists(Path.Combine(directory, "session.json"));
                }
            }
            visionDiagnosticLabelStatus = visionDiagnosticSavedLabels == 0 ? "No labelled diagnostic frames saved."
                : visionDiagnosticLabelSessionFinalized ? $"{visionDiagnosticSavedLabels} frames saved; this label session is finished."
                : $"{visionDiagnosticSavedLabels} labelled frames saved. Stop sampling and finish the session when ready.";
        }

        var frame = snapshot.LabelFrame;
        if (frame is not null || snapshot.LabelCapturePending
            || visionDiagnosticLabelStatus.StartsWith("Waiting for one fresh frame", StringComparison.Ordinal))
            visionDiagnosticLabelStatus = snapshot.Status;
        if (frame?.SampleId != visionDiagnosticLabelPreviewSampleId)
        {
            visionDiagnosticLabelPreviewSampleId = frame?.SampleId;
            visionDiagnosticMarks.Clear();
            visionDiagnosticEmptyFrameConfirmed = false;
            visionDiagnosticAllBallsLabelledConfirmed = false;
            visionDiagnosticLabelNotes = "";
            visionDiagnosticLabelImageUri = frame is null ? null : "data:image/jpeg;base64," + Convert.ToBase64String(frame.Jpeg);
            visionAdvisoryResult = null;
            visionAdvisoryStatus = "No second opinion requested.";
        }
    }

    public async Task AskVisionAdvisoryAsync()
    {
        var frame = VisionDiagnosticLabelFrame;
        if (!active || disposed || frame is null || visionAdvisoryPending) return;
        string key;
        try { key = SecretStore.Get(Android.App.Application.Context, "openai") ?? ""; }
        catch { key = ""; }
        if (string.IsNullOrWhiteSpace(key)) { visionAdvisoryStatus = "Save a personal OpenAI key in Settings before requesting an advisory."; Changed?.Invoke(); return; }

        visionAdvisoryPending = true;
        visionAdvisoryResult = null;
        visionAdvisoryStatus = "Sending this selected image to OpenAI for a billable GPT-6 Luna advisory…";
        Changed?.Invoke();
        try
        {
            var provenance = new TableFrameProvenance(frame.SampleId,
                frame.FrameId.ToString(System.Globalization.CultureInfo.InvariantCulture), frame.SourceTimeUs, frame.Timeline,
                frame.MediaGeneration, frame.AnalysisGeneration, frame.CapturedAt, frame.FrameSource, frame.CalibrationId,
                frame.AnalyzerConfiguration, frame.Width, frame.Height,
                frame.CalibrationCorners.Select(point => new[] { point.X, point.Y }).ToArray());
            var local = JsonSerializer.Serialize(new
            {
                coordinates = "normalized calibrated table coordinates; not source image pixels",
                uncertain = frame.DetectionUncertain,
                balls = frame.DetectedBalls.Select(ball => new { colour = ball.Kind, x = ball.Position.X, y = ball.Position.Y, confidence = ball.Confidence })
            }, VisionLabelJson);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var result = await GptTableAdvisory.AskAsync(http, key, frame.Jpeg, provenance, local, foreground?.Token ?? CancellationToken.None);
            if (VisionDiagnosticLabelFrame?.SampleId == frame.SampleId)
            {
                visionAdvisoryResult = result;
                visionAdvisoryStatus = $"Advisory complete in {result.ElapsedMilliseconds:0} ms · one image sent · not saved · not used by game state.";
            }
        }
        catch (OperationCanceledException) { visionAdvisoryStatus = "Advisory cancelled; no result applied."; }
        catch (HttpRequestException ex) { visionAdvisoryStatus = ex.Message; }
        catch (Exception) { visionAdvisoryStatus = "Advisory failed or returned invalid structured output. No result applied."; }
        finally
        {
            visionAdvisoryPending = false;
            Changed?.Invoke();
        }
    }

    public void RequestVisionDiagnosticLabelFrame()
    {
        if (table.RequestVisionDiagnosticLabelFrame(out var message))
            visionDiagnosticLabelStatus = "Waiting for one fresh frame. Detector output will be hidden while you label it.";
        else visionDiagnosticLabelStatus = message;
        Changed?.Invoke();
    }

    public void SetVisionDiagnosticLabelColour(string? colour)
    {
        if (visionDiagnosticLabelSavePending) return;
        if (colour is "cue" or "black" or "red" or "yellow" or "unknown") visionDiagnosticLabelColour = colour;
        Changed?.Invoke();
    }

    public void SetVisionDiagnosticLabelVisibility(string? visibility)
    {
        if (visionDiagnosticLabelSavePending) return;
        if (visibility is "Visible" or "Occluded" or "Uncertain") visionDiagnosticLabelVisibility = visibility;
        Changed?.Invoke();
    }

    public void SetVisionDiagnosticLabelLighting(string? lighting)
    {
        if (visionDiagnosticLabelSavePending) return;
        if (lighting is "ordinary" or "dim" or "bright" or "blind-sunlight") visionDiagnosticLabelLighting = lighting;
        Changed?.Invoke();
    }

    public void SetVisionDiagnosticLabelMotion(string? motion)
    {
        if (visionDiagnosticLabelSavePending) return;
        if (motion is "still" or "moving") visionDiagnosticLabelMotion = motion;
        Changed?.Invoke();
    }

    public void SetVisionDiagnosticLabelNotes(string? notes)
    {
        if (visionDiagnosticLabelSavePending) return;
        visionDiagnosticLabelNotes = new string((notes ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (visionDiagnosticLabelNotes.Length > 400) visionDiagnosticLabelNotes = visionDiagnosticLabelNotes[..400];
        Changed?.Invoke();
    }

    public void ToggleVisionDiagnosticSceneTag(string tag, bool selected)
    {
        if (visionDiagnosticLabelSavePending) return;
        if (tag is not ("touching" or "near-cushion" or "occlusion" or "empty-region")) return;
        if (selected) visionDiagnosticSceneTags.Add(tag); else visionDiagnosticSceneTags.Remove(tag);
        Changed?.Invoke();
    }

    public bool HasVisionDiagnosticSceneTag(string tag) => visionDiagnosticSceneTags.Contains(tag);

    public void SetVisionDiagnosticEmptyFrameConfirmed(bool confirmed)
    {
        if (visionDiagnosticLabelSavePending) return;
        visionDiagnosticEmptyFrameConfirmed = confirmed && visionDiagnosticMarks.Count == 0;
        Changed?.Invoke();
    }

    public void SetVisionDiagnosticAllBallsLabelledConfirmed(bool confirmed)
    {
        if (visionDiagnosticLabelSavePending) return;
        visionDiagnosticAllBallsLabelledConfirmed = confirmed && visionDiagnosticMarks.Count > 0;
        if (visionDiagnosticAllBallsLabelledConfirmed) visionDiagnosticEmptyFrameConfirmed = false;
        Changed?.Invoke();
    }

    public void AddVisionDiagnosticBox(double x1, double y1, double x2, double y2)
    {
        if (visionDiagnosticLabelSavePending || VisionDiagnosticLabelFrame is null || visionDiagnosticMarks.Count >= 64) return;
        double left = Math.Clamp(Math.Min(x1, x2), 0, 1), right = Math.Clamp(Math.Max(x1, x2), 0, 1);
        double top = Math.Clamp(Math.Min(y1, y2), 0, 1), bottom = Math.Clamp(Math.Max(y1, y2), 0, 1);
        if (right - left < 0.002 || bottom - top < 0.002) return;
        visionDiagnosticMarks.Add(new($"manual-{visionDiagnosticMarks.Count + 1:D2}", visionDiagnosticLabelColour,
            visionDiagnosticLabelVisibility, (left + right) / 2, (top + bottom) / 2,
            (right - left) / 2, (bottom - top) / 2));
        visionDiagnosticEmptyFrameConfirmed = false;
        visionDiagnosticAllBallsLabelledConfirmed = false;
        Changed?.Invoke();
    }

    public void RenameVisionDiagnosticBall(string currentId, string? requestedId)
    {
        if (visionDiagnosticLabelSavePending) return;
        string id = (requestedId ?? "").Trim();
        int index = visionDiagnosticMarks.FindIndex(mark => mark.Id == currentId);
        if (index < 0) return;
        if (id.Length is < 1 or > 64 || id.Any(char.IsControl)
            || visionDiagnosticMarks.Where((_, i) => i != index).Any(mark => mark.Id == id))
        {
            visionDiagnosticLabelStatus = "Use a unique ball ID of 1–64 characters in this frame.";
            Changed?.Invoke();
            return;
        }
        visionDiagnosticMarks[index] = visionDiagnosticMarks[index] with { Id = id };
        Changed?.Invoke();
    }

    public void RemoveLastVisionDiagnosticBox()
    {
        if (visionDiagnosticLabelSavePending) return;
        if (visionDiagnosticMarks.Count > 0)
        {
            visionDiagnosticMarks.RemoveAt(visionDiagnosticMarks.Count - 1);
            visionDiagnosticAllBallsLabelledConfirmed = false;
        }
        if (visionDiagnosticMarks.Count == 0) visionDiagnosticEmptyFrameConfirmed = false;
        Changed?.Invoke();
    }

    public void ClearVisionDiagnosticBoxes()
    {
        if (visionDiagnosticLabelSavePending) return;
        visionDiagnosticMarks.Clear();
        visionDiagnosticEmptyFrameConfirmed = false;
        visionDiagnosticAllBallsLabelledConfirmed = false;
        Changed?.Invoke();
    }

    public async Task SaveVisionDiagnosticLabelsAsync()
    {
        var frame = VisionDiagnosticLabelFrame;
        if (visionDiagnosticLabelSavePending) return;
        if (frame is null || visionDiagnosticMarks.Count == 0 && !visionDiagnosticEmptyFrameConfirmed
            || visionDiagnosticMarks.Count > 0 && !visionDiagnosticAllBallsLabelledConfirmed)
        {
            visionDiagnosticLabelStatus = visionDiagnosticMarks.Count == 0
                ? "Box every visible ball, or confirm that you checked the whole frame and no balls are visible."
                : "Before saving, confirm that every visible ball has a box. Include unclear or partly hidden balls and label them honestly.";
            Changed?.Invoke();
            return;
        }
        if (visionDiagnosticMarks.Select(mark => mark.Id).Distinct(StringComparer.Ordinal).Count() != visionDiagnosticMarks.Count)
        {
            visionDiagnosticLabelStatus = "Each ball needs a unique ID in this frame. Reuse the same ID for that ball in later frames.";
            Changed?.Invoke();
            return;
        }
        var marks = visionDiagnosticMarks.ToArray();
        var tags = new[] { visionDiagnosticLabelLighting, visionDiagnosticLabelMotion }
            .Concat(visionDiagnosticSceneTags.Order(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
        string notes = string.IsNullOrWhiteSpace(visionDiagnosticLabelNotes)
            ? "Independently drawn from the captured image; detector output is not ground truth."
            : "Independently drawn from the captured image; " + visionDiagnosticLabelNotes;
        visionDiagnosticLabelSavePending = true;
        visionDiagnosticLabelStatus = "Saving reviewed image and labels…";
        Changed?.Invoke();
        try
        {
            bool allVisibleBallsLabelled = visionDiagnosticAllBallsLabelledConfirmed;
            bool emptyFrameConfirmed = visionDiagnosticEmptyFrameConfirmed;
            await Task.Run(() => SaveVisionDiagnosticLabelFrame(frame, marks, tags, notes,
                allVisibleBallsLabelled, emptyFrameConfirmed)).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                visionDiagnosticSavedLabels++;
                foreach (var mark in marks) visionDiagnosticKnownBallIds.Add(mark.Id);
                visionDiagnosticLabelSavePending = false;
                visionDiagnosticLabelStatus = $"Saved {visionDiagnosticSavedLabels} hand-labelled frames for this recording session.";
                table.CompleteVisionDiagnosticLabelFrame("Labels saved. Capture another fresh frame or stop diagnostics to finish this session.");
                Changed?.Invoke();
            });
        }
        catch (Exception)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                visionDiagnosticLabelSavePending = false;
                visionDiagnosticLabelStatus = "Could not save these labels. Check available diagnostic storage and try again.";
                Changed?.Invoke();
            });
        }
    }

    public void DiscardVisionDiagnosticLabelFrame()
    {
        if (visionDiagnosticLabelSavePending) return;
        visionDiagnosticMarks.Clear();
        visionDiagnosticEmptyFrameConfirmed = false;
        visionDiagnosticAllBallsLabelledConfirmed = false;
        table.CompleteVisionDiagnosticLabelFrame("Frame discarded. The detector sample remains only in the bounded diagnostics view.");
        visionDiagnosticLabelStatus = "Frame discarded without saving image or labels.";
    }

    public void FinishVisionDiagnosticLabelSession()
    {
        if (!CanFinishVisionDiagnosticLabelSession) return;
        var snapshot = table.VisionDiagnostics;
        var directory = VisionDiagnosticLabelDirectory(snapshot.SessionId);
        var receiptPaths = Directory.EnumerateFiles(directory, "*.label.json", SearchOption.TopDirectoryOnly).Take(VisionLabelMaximumFrames + 1).ToArray();
        if (receiptPaths.Length is < 1 or > VisionLabelMaximumFrames)
        { visionDiagnosticLabelStatus = "This session has no valid saved frames or exceeds the 120-frame limit."; Changed?.Invoke(); return; }
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = snapshot.StartedAt ?? completedAt;
        var session = new VisionDiagnosticLabelSessionReceipt(1, snapshot.SessionId.ToString("N"), snapshot.RecordingSessionId, startedAt,
            completedAt, true, receiptPaths.Length,
            "Every visible ball in each included frame was labelled by a person without showing detector results in the label view; empty frames were explicitly confirmed.");
        try
        {
            if (File.Exists(Path.Combine(directory, "session.json")))
                throw new IOException("This diagnostic label session is already finished.");
            AtomicWrite(Path.Combine(directory, "session.json"),
                System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session, VisionLabelJson)));
            visionDiagnosticLabelSessionFinalized = true;
            visionDiagnosticLabelStatus = $"Finished reviewed label session · {receiptPaths.Length} frames. Copy it from Android data/o09-labels for O01 import.";
        }
        catch (Exception)
        { visionDiagnosticLabelStatus = "Could not finish the label session. Check available diagnostic storage and try again."; }
        Changed?.Invoke();
    }

    static string VisionDiagnosticLabelDirectory(Guid sessionId) => Path.Combine(VisionDiagnosticLabelRoot(), sessionId.ToString("N"));
    static string VisionDiagnosticLabelRoot() => Path.Combine(
        Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath, "o09-labels");

    static void SaveVisionDiagnosticLabelFrame(VisionDiagnosticLabelFrame frame,
        VisionDiagnosticBallMark[] marks, string[] tags, string notes,
        bool allVisibleBallsLabelled, bool emptyFrameConfirmed)
    {
        if (frame.Jpeg.Length is < 1 or > VisionLabelMaximumImageBytes || frame.Width <= 0 || frame.Height <= 0
            || frame.FrameId <= 0 || frame.SourceTimeUs < 0 || frame.MediaGeneration <= 0
            || frame.AnalysisGeneration <= 0 || frame.CalibrationCorners.Length != 4)
            throw new IOException("Diagnostic image receipt is outside its bounds.");
        var root = VisionDiagnosticLabelRoot();
        Directory.CreateDirectory(root);
        var retainedFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        int retainedFrames = Directory.EnumerateFiles(root, "*.label.json", SearchOption.AllDirectories)
            .Take(VisionLabelMaximumFrames + 1).Count();
        var directory = VisionDiagnosticLabelDirectory(frame.SessionId);
        Directory.CreateDirectory(directory);
        string imageName = frame.SampleId + ".jpg", receiptName = frame.SampleId + ".label.json";
        string imagePath = Path.Combine(directory, imageName), receiptPath = Path.Combine(directory, receiptName);
        bool imageAlreadyWritten = File.Exists(imagePath);
        if (File.Exists(receiptPath) || File.Exists(Path.Combine(directory, "session.json")))
            throw new IOException("This label session already contains that frame or is finished.");
        string sha = Convert.ToHexString(SHA256.HashData(frame.Jpeg)).ToLowerInvariant();
        if (imageAlreadyWritten && !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))).Equals(sha, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A partial label capture already has different image bytes for this frame.");
        if (retainedFrames >= VisionLabelMaximumFrames
            || retainedFiles.Sum(path => new FileInfo(path).Length) + (imageAlreadyWritten ? 0 : frame.Jpeg.Length) + 128 * 1024 > VisionLabelMaximumBytes)
            throw new IOException("O09 label capture reached its 40 MiB or 120-frame storage limit. Export the existing labels before starting another session.");
        var storedFrame = new VisionDiagnosticStoredFrame(frame.SampleId, frame.FrameId, frame.SourceTimeUs,
            new("mounted-camera", frame.Timeline, frame.MediaGeneration, frame.CalibrationId), imageName, sha,
            "Jpeg", frame.Width, frame.Height, false);
        var balls = marks.Select(mark => new VisionDiagnosticStoredBall(mark.Id, mark.Colour, mark.X, mark.Y,
            mark.Visibility, mark.RadiusX, mark.RadiusY)).ToArray();
        var receipt = new VisionDiagnosticLabelReceipt(2, frame.SessionId.ToString("N"), frame.RecordingSessionId, true,
            allVisibleBallsLabelled, emptyFrameConfirmed,
            frame.CapturedAt, frame.FrameAgeMs, frame.AnalysisGeneration, frame.CalibrationId, frame.AnalyzerConfiguration,
            frame.CalibrationCorners, frame.FrameSource, storedFrame, tags, balls, notes)
        {
            DetectionOutput = new(frame.AnalyzerConfiguration,
                "Paired on-device VisionAnalyzer result from this exact indexed source frame; not shown in the hand-label view.",
                frame.DetectionUncertain,
                frame.DetectedBalls.Select(ball => new VisionDiagnosticStoredDetectionBall(
                    ball.Id, ball.Kind, ball.Position.X, ball.Position.Y, ball.Confidence)).ToArray())
        };
        if (!imageAlreadyWritten) AtomicWrite(imagePath, frame.Jpeg);
        AtomicWrite(receiptPath, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(receipt, VisionLabelJson)));
    }

    static void AtomicWrite(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
