using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Analysis;

namespace SkyEye.Companion.Hybrid;

public sealed record VisionDiagnosticBallMark(string Id, string Colour, string Visibility,
    double X, double Y, double RadiusX, double RadiusY);
sealed record VisionDiagnosticStoredBall(string Id, string Colour, double X, double Y, string Visibility,
    double RadiusX, double RadiusY);
sealed record VisionDiagnosticStoredScope(string Source, string Timeline, long Generation, string Calibration);
sealed record VisionDiagnosticStoredFrame(string SampleId, long FrameId, long? SourceTimeUs,
    VisionDiagnosticStoredScope Scope, string PixelFile, string PixelSha256, string Encoding,
    int Width, int Height, bool EmbeddedFrameId);
sealed record VisionDiagnosticLabelReceipt(int SchemaVersion, string DiagnosticSessionId, string RecordingSessionId, bool Reviewed,
    DateTimeOffset CapturedAt, double FrameAgeMs, long AnalysisGeneration, string CalibrationId,
    string AnalyzerConfiguration, SkyEye.Core.Point[] CalibrationCorners, string FrameSource, VisionDiagnosticStoredFrame IndexedFrame,
    string[] Tags, VisionDiagnosticStoredBall[] Balls, string Notes);
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
    Guid visionDiagnosticLabelSessionId;
    string? visionDiagnosticLabelPreviewSampleId;
    string? visionDiagnosticLabelImageUri;
    string visionDiagnosticLabelColour = "unknown";
    string visionDiagnosticLabelVisibility = "Visible";
    string visionDiagnosticLabelLighting = "ordinary";
    string visionDiagnosticLabelMotion = "still";
    string visionDiagnosticLabelNotes = "";
    bool visionDiagnosticEmptyFrameConfirmed;
    bool visionDiagnosticLabelSavePending;
    int visionDiagnosticSavedLabels;
    bool visionDiagnosticLabelSessionFinalized;
    string visionDiagnosticLabelStatus = "No labelled diagnostic frames saved.";

    public VisionDiagnosticLabelFrame? VisionDiagnosticLabelFrame => table.VisionDiagnostics.LabelFrame;
    public string? VisionDiagnosticLabelImageUri => visionDiagnosticLabelImageUri;
    public IReadOnlyList<VisionDiagnosticBallMark> VisionDiagnosticMarks => visionDiagnosticMarks;
    public string VisionDiagnosticLabelColour => visionDiagnosticLabelColour;
    public string VisionDiagnosticLabelVisibility => visionDiagnosticLabelVisibility;
    public string VisionDiagnosticLabelLighting => visionDiagnosticLabelLighting;
    public string VisionDiagnosticLabelMotion => visionDiagnosticLabelMotion;
    public string VisionDiagnosticLabelNotes => visionDiagnosticLabelNotes;
    public bool VisionDiagnosticEmptyFrameConfirmed => visionDiagnosticEmptyFrameConfirmed;
    public int VisionDiagnosticSavedLabels => visionDiagnosticSavedLabels;
    public bool VisionDiagnosticLabelSessionFinalized => visionDiagnosticLabelSessionFinalized;
    public bool VisionDiagnosticLabelSavePending => visionDiagnosticLabelSavePending;
    public string VisionDiagnosticLabelStatus => visionDiagnosticLabelStatus;
    public bool CanFinishVisionDiagnosticLabelSession => !VisionDiagnosticsActive && visionDiagnosticSavedLabels > 0
        && !visionDiagnosticLabelSessionFinalized && visionDiagnosticLabelSessionId != Guid.Empty;

    void SyncVisionDiagnosticLabelState(VisionDiagnosticsSnapshot snapshot)
    {
        if (snapshot.SessionId != visionDiagnosticLabelSessionId)
        {
            visionDiagnosticLabelSessionId = snapshot.SessionId;
            visionDiagnosticLabelPreviewSampleId = null;
            visionDiagnosticLabelImageUri = null;
            visionDiagnosticMarks.Clear();
            visionDiagnosticEmptyFrameConfirmed = false;
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
            visionDiagnosticLabelNotes = "";
            visionDiagnosticLabelImageUri = frame is null ? null : "data:image/jpeg;base64," + Convert.ToBase64String(frame.Jpeg);
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
        Changed?.Invoke();
    }

    public void RemoveLastVisionDiagnosticBox()
    {
        if (visionDiagnosticLabelSavePending) return;
        if (visionDiagnosticMarks.Count > 0) visionDiagnosticMarks.RemoveAt(visionDiagnosticMarks.Count - 1);
        Changed?.Invoke();
    }

    public void ClearVisionDiagnosticBoxes()
    {
        if (visionDiagnosticLabelSavePending) return;
        visionDiagnosticMarks.Clear();
        visionDiagnosticEmptyFrameConfirmed = false;
        Changed?.Invoke();
    }

    public async Task SaveVisionDiagnosticLabelsAsync()
    {
        var frame = VisionDiagnosticLabelFrame;
        if (visionDiagnosticLabelSavePending) return;
        if (frame is null || visionDiagnosticMarks.Count == 0 && !visionDiagnosticEmptyFrameConfirmed)
        {
            visionDiagnosticLabelStatus = "Draw each visible ball, or explicitly confirm that no balls are visible.";
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
            await Task.Run(() => SaveVisionDiagnosticLabelFrame(frame, marks, tags, notes)).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                visionDiagnosticSavedLabels++;
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
            "Every included frame was labelled by a person without showing detector results in the label view.");
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
        VisionDiagnosticBallMark[] marks, string[] tags, string notes)
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
        var receipt = new VisionDiagnosticLabelReceipt(1, frame.SessionId.ToString("N"), frame.RecordingSessionId, true,
            frame.CapturedAt, frame.FrameAgeMs, frame.AnalysisGeneration, frame.CalibrationId, frame.AnalyzerConfiguration,
            frame.CalibrationCorners, frame.FrameSource, storedFrame, tags, balls,
            notes);
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
