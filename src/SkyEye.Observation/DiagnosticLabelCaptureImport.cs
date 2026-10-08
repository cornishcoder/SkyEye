using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core;

namespace SkyEye.Observation;

public sealed record DiagnosticBallLabel(string Id, string Colour, double X, double Y, string Visibility,
    double? RadiusX, double? RadiusY);
public sealed record DiagnosticLabelCapture(int SchemaVersion, string DiagnosticSessionId, string RecordingSessionId, bool Reviewed,
    DateTimeOffset CapturedAt, double FrameAgeMs, long AnalysisGeneration, string CalibrationId, string AnalyzerConfiguration,
    Point[] CalibrationCorners, string FrameSource, IndexedFrame IndexedFrame,
    ImmutableArray<string> Tags, ImmutableArray<DiagnosticBallLabel> Balls, string Notes)
{
    public DiagnosticLabelDetection? DetectionOutput { get; init; }
}
public sealed record DiagnosticLabelDetection(string Model, string Provenance, bool Uncertain,
    ImmutableArray<DetectedBall> Balls);
public sealed record DiagnosticLabelSession(int SchemaVersion, string DiagnosticSessionId, string RecordingSessionId,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, bool Reviewed, int FrameCount, string Notes);
public sealed record DiagnosticLabelSessionAssignment(string DiagnosticSessionId, CorpusSplit Split, string? HoldoutCondition,
    SequencePurpose? Purpose = null);
public sealed record DiagnosticLabelAssignmentManifest(int SchemaVersion,
    ImmutableArray<DiagnosticLabelSessionAssignment> Sessions);

/// <summary>Imports explicitly reviewed diagnostic images and hand-drawn ball labels into O01.</summary>
public static class DiagnosticLabelCaptureImport
{
    const int MaximumCaptures = 120;
    const long MaximumPixels = 40L * 1024 * 1024;
    static readonly string[] HoldoutConditions = ["still", "moving", "obstructed", "lighting-changed", "camera-changed"];

    sealed record PreparedSession(DiagnosticLabelSession Session, CorpusSplit Split, string? HoldoutCondition,
        ImmutableArray<(DiagnosticLabelCapture Capture, string ReceiptPath, byte[] Pixels)> Frames);

    public static string Import(string captureRoot, string outputRoot, string assignmentsFile)
        => ImportCore(captureRoot, outputRoot, assignmentsFile, associationReady: false);

    public static string ImportAssociationSequences(string captureRoot, string outputRoot, string assignmentsFile)
        => ImportCore(captureRoot, outputRoot, assignmentsFile, associationReady: true);

    static string ImportCore(string captureRoot, string outputRoot, string assignmentsFile, bool associationReady)
    {
        if (new FileInfo(assignmentsFile).Length > 64 * 1024) throw new InvalidDataException("Diagnostic split assignment file exceeds bounds.");
        var assignmentManifest = CorpusJson.Read<DiagnosticLabelAssignmentManifest>(assignmentsFile);
        if (assignmentManifest.SchemaVersion != 1 || assignmentManifest.Sessions.IsDefaultOrEmpty
            || assignmentManifest.Sessions.Length > MaximumCaptures
            || assignmentManifest.Sessions.Any(a => a is null || !Guid.TryParseExact(a.DiagnosticSessionId, "N", out _)
                || a.Split is not (CorpusSplit.Development or CorpusSplit.Holdout)
                || a.Purpose is { } purpose && purpose != SequencePurpose.Shot
                || associationReady && a.Purpose != SequencePurpose.Shot
                || !associationReady && a.Purpose == SequencePurpose.Shot
                || a.Split == CorpusSplit.Development && a.HoldoutCondition is not null
                || a.Purpose == SequencePurpose.Shot && a.HoldoutCondition is not null
                || a.Split == CorpusSplit.Holdout && a.Purpose != SequencePurpose.Shot
                    && !HoldoutConditions.Contains(a.HoldoutCondition, StringComparer.Ordinal))
            || assignmentManifest.Sessions.Select(a => a.DiagnosticSessionId).Distinct(StringComparer.Ordinal).Count() != assignmentManifest.Sessions.Length)
            throw new InvalidDataException(associationReady
                ? "O04 shot assignments need each diagnostic session ID once, Purpose Shot, Development or Holdout, and no Holdout condition."
                : "Assignments need each diagnostic session ID once, with Development or Holdout and a Holdout condition where applicable; use the O04 shot import for Purpose Shot.");
        var assignments = assignmentManifest.Sessions.ToDictionary(a => a.DiagnosticSessionId, StringComparer.Ordinal);

        var sourceRoot = Path.GetFullPath(captureRoot);
        var destination = Path.GetFullPath(outputRoot);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Choose a new corpus output directory to preserve earlier evidence.");
        var sourceFiles = new CorpusFiles(sourceRoot);
        var sessions = new List<PreparedSession>();
        int captureCount = 0;
        long capturedBytes = 0;

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot).Order(StringComparer.Ordinal))
        {
            var sessionPath = Path.Combine(directory, "session.json");
            var receiptPaths = Directory.EnumerateFiles(directory, "*.label.json", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal).ToArray();
            if (receiptPaths.Length == 0) continue;
            string diagnosticSessionId = Path.GetFileName(directory);
            if (!Guid.TryParseExact(diagnosticSessionId, "N", out _))
                throw new InvalidDataException("Diagnostic label directory name is not a session ID: " + directory);
            DiagnosticLabelSession? session = null;
            if (File.Exists(sessionPath))
            {
                if (new FileInfo(sessionPath).Length > 64 * 1024) throw new InvalidDataException("Diagnostic label session receipt exceeds bounds.");
                session = CorpusJson.Read<DiagnosticLabelSession>(sessionPath);
                if (!session.Reviewed || session.SchemaVersion != 1 || session.DiagnosticSessionId != diagnosticSessionId
                    || !Guid.TryParseExact(session.RecordingSessionId, "N", out _)
                    || session.FrameCount is < 1 or > MaximumCaptures || session.CompletedAt < session.StartedAt
                    || !CorpusFiles.Text(session.Notes))
                    throw new InvalidDataException("Invalid reviewed diagnostic label session: " + directory);
                if (receiptPaths.Length != session.FrameCount)
                    throw new InvalidDataException("Reviewed session frame count does not match its label receipts: " + diagnosticSessionId);
            }
            if (!assignments.TryGetValue(diagnosticSessionId, out var assignment))
                throw new InvalidDataException("Reviewed diagnostic batch has no frozen split assignment: " + diagnosticSessionId);
            if (receiptPaths.Length > MaximumCaptures)
                throw new InvalidDataException("Diagnostic batch exceeds 120 reviewed frame receipts: " + diagnosticSessionId);
            captureCount = checked(captureCount + receiptPaths.Length);
            if (captureCount > MaximumCaptures) throw new InvalidDataException("Diagnostic import exceeds 120 reviewed frames.");
            var frames = ImmutableArray.CreateBuilder<(DiagnosticLabelCapture, string, byte[])>(receiptPaths.Length);
            foreach (var receiptPath in receiptPaths)
            {
                if (new FileInfo(receiptPath).Length > 512 * 1024) throw new InvalidDataException("Diagnostic label receipt exceeds bounds.");
                var capture = CorpusJson.Read<DiagnosticLabelCapture>(receiptPath);
                var frame = capture.IndexedFrame;
                string imageName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(receiptPath)) + ".jpg";
                if (capture.SchemaVersion != 1 || !capture.Reviewed || capture.DiagnosticSessionId != diagnosticSessionId
                    || !Guid.TryParseExact(capture.RecordingSessionId, "N", out _)
                    || session is not null && capture.RecordingSessionId != session.RecordingSessionId
                    || frame is null || frame.SampleId != capture.DiagnosticSessionId + "-" + frame.FrameId.ToString("D12", System.Globalization.CultureInfo.InvariantCulture)
                    || !Path.GetFileName(receiptPath).Equals(frame.SampleId + ".label.json", StringComparison.OrdinalIgnoreCase)
                    || !frame.PixelFile.Equals(imageName, StringComparison.OrdinalIgnoreCase)
                    || frame.Encoding != PixelEncoding.Jpeg || frame.SourceTimeUs is null || frame.FrameId <= 0
                    || frame.Scope is null || frame.Scope.Generation <= 0
                    || frame.Scope.Calibration != capture.CalibrationId || !CorpusFiles.Text(capture.CalibrationId)
                    || !CorpusFiles.Text(frame.Scope.Timeline) || !CorpusFiles.Text(frame.Scope.Source)
                    || !CorpusFiles.Text(capture.AnalyzerConfiguration)
                    || capture.AnalysisGeneration <= 0 || !CorpusFiles.Text(capture.FrameSource)
                    || !double.IsFinite(capture.FrameAgeMs) || capture.FrameAgeMs is < -200 or > 60_000
                    || capture.CalibrationCorners is not { Length: 4 }
                    || capture.Balls.IsDefault || capture.Balls.Length > 64
                    || capture.Tags.IsDefault || capture.Tags.Length > 32
                    || capture.Tags.Any(t => !CorpusFiles.Text(t))
                    || capture.Tags.Distinct(StringComparer.Ordinal).Count() != capture.Tags.Length
                    || !CorpusFiles.Text(capture.Notes))
                    throw new InvalidDataException("Invalid reviewed diagnostic label receipt: " + receiptPath);
                if (associationReady)
                {
                    var detector = capture.DetectionOutput;
                    if (detector is null || !CorpusFiles.Text(detector.Model) || !CorpusFiles.Text(detector.Provenance)
                        || detector.Balls.IsDefault || detector.Balls.Length > 64
                        || detector.Balls.Select(ball => ball.Id).Distinct(StringComparer.Ordinal).Count() != detector.Balls.Length
                        || detector.Balls.Any(ball => ball is null || !CorpusFiles.Text(ball.Id)
                            || ball.Colour is not ("cue" or "black" or "red" or "yellow" or "unknown")
                            || !double.IsFinite(ball.X) || ball.X is < 0 or > 2
                            || !double.IsFinite(ball.Y) || ball.Y is < 0 or > 1
                            || ball.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1)))
                        throw new InvalidDataException("O04 shot import needs the paired, valid detector result for every frame: " + frame.SampleId);
                }
                _ = new TableCalibration(capture.CalibrationCorners);
                var relativeImage = Path.GetRelativePath(sourceRoot, Path.Combine(directory, frame.PixelFile));
                var pixels = sourceFiles.Verified(relativeImage, frame.PixelSha256);
                PixelDimensions.Verify(frame, pixels);
                capturedBytes = checked(capturedBytes + pixels.Length + new FileInfo(receiptPath).Length);
                if (capturedBytes > MaximumPixels) throw new InvalidDataException("Diagnostic import exceeds the 40 MiB capture bound.");
                if (capture.Balls.Any(ball => ball is null || !CorpusFiles.Text(ball.Id)
                    || ball.Colour is not ("cue" or "black" or "red" or "yellow" or "unknown")
                    || ball.Visibility is not ("Visible" or "Occluded" or "Uncertain" or "Absent")
                    || !double.IsFinite(ball.X) || !double.IsFinite(ball.Y)
                    || ball.X is < 0 or > 1 || ball.Y is < 0 or > 1
                    || (ball.RadiusX is null) != (ball.RadiusY is null)
                    || ball.RadiusX is { } rx && (!double.IsFinite(rx) || rx <= 0 || rx > 1)
                    || ball.RadiusY is { } ry && (!double.IsFinite(ry) || ry <= 0 || ry > 1))
                    || capture.Balls.Select(ball => ball.Id).Distinct(StringComparer.Ordinal).Count() != capture.Balls.Length)
                    throw new InvalidDataException("Diagnostic ball labels have invalid classes, boxes or identities: " + frame.SampleId);
                frames.Add((capture, receiptPath, pixels));
            }
            if (frames.Count == 0) throw new InvalidDataException("Reviewed diagnostic label session is empty.");
            string recordingSessionId = frames[0].Item1.RecordingSessionId;
            if (frames.Any(f => f.Item1.RecordingSessionId != recordingSessionId))
                throw new InvalidDataException("Diagnostic batches cannot mix camera recording sessions.");
            var orderedFrames = frames.OrderBy(f => f.Item1.IndexedFrame.SourceTimeUs).ToArray();
            var firstFrame = orderedFrames[0].Item1;
            for (int i = 0; i < orderedFrames.Length; i++)
            {
                var current = orderedFrames[i].Item1;
                if (current.CalibrationId != firstFrame.CalibrationId
                    || !current.CalibrationCorners.SequenceEqual(firstFrame.CalibrationCorners)
                    || current.IndexedFrame.Width != firstFrame.IndexedFrame.Width
                    || current.IndexedFrame.Height != firstFrame.IndexedFrame.Height
                    || current.IndexedFrame.Scope != firstFrame.IndexedFrame.Scope
                    || i > 0 && (current.IndexedFrame.FrameId <= orderedFrames[i - 1].Item1.IndexedFrame.FrameId
                        || current.IndexedFrame.SourceTimeUs!.Value <= orderedFrames[i - 1].Item1.IndexedFrame.SourceTimeUs!.Value))
                    throw new InvalidDataException("A reviewed diagnostic label session changed camera scope, resolution, calibration or source ordering.");
            }
            if ((assignment.Split == CorpusSplit.Holdout || associationReady) && orderedFrames.Length < 2)
                throw new InvalidDataException("A Holdout or O04 shot diagnostic session needs at least two fresh frames.");
            if (assignment.Split == CorpusSplit.Holdout && orderedFrames.Select(f => f.Item1.IndexedFrame.SourceTimeUs).Distinct().Count() < 2)
                throw new InvalidDataException("Holdout diagnostic frames need distinct source timestamps.");
            session ??= new(1, diagnosticSessionId, recordingSessionId,
                frames.Min(f => f.Item1.CapturedAt), frames.Max(f => f.Item1.CapturedAt), true,
                frames.Count, "Recovered from individually reviewed frame receipts; finish marker was not written.");
            sessions.Add(new(session, assignment.Split, assignment.HoldoutCondition, frames.ToImmutable()));
        }

        if (sessions.Count == 0) throw new InvalidDataException("No individually reviewed diagnostic label frames were found.");
        var importedSessionIds = sessions.Select(s => s.Session.DiagnosticSessionId).ToHashSet(StringComparer.Ordinal);
        var unusedAssignments = assignments.Keys.Where(id => !importedSessionIds.Contains(id)).ToArray();
        if (unusedAssignments.Length > 0)
            throw new InvalidDataException("Split assignment references a session with no reviewed frame receipts: " + unusedAssignments[0]);
        if (sessions.GroupBy(s => s.Session.RecordingSessionId, StringComparer.Ordinal)
            .Any(group => group.Select(s => s.Split).Distinct().Count() > 1))
            throw new InvalidDataException("Diagnostic batches from one camera recording session must share the same frozen split.");

        Directory.CreateDirectory(destination);
        var sequences = ImmutableArray.CreateBuilder<CorpusSequence>(sessions.Count);
        var compact = new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false };
        var associationDetections = new Dictionary<CorpusSplit, List<StoredDetection>>();
        foreach (var prepared in sessions)
        {
            string id = prepared.Session.DiagnosticSessionId;
            string folder = id;
            var indexFrames = ImmutableArray.CreateBuilder<IndexedFrame>(prepared.Frames.Length);
            var labelFrames = ImmutableArray.CreateBuilder<FrameLabels>(prepared.Frames.Length);
            var sessionDetections = new List<StoredDetection>(prepared.Frames.Length);
            var tableCalibration = new TableCalibration(prepared.Frames[0].Capture.CalibrationCorners);
            Directory.CreateDirectory(Path.Combine(destination, folder, "images"));
            Directory.CreateDirectory(Path.Combine(destination, folder, "receipts"));
            foreach (var (capture, receiptPath, pixels) in prepared.Frames.OrderBy(f => f.Capture.IndexedFrame.SourceTimeUs))
            {
                var sourceFrame = capture.IndexedFrame;
                string pixelFile = $"{folder}/images/{sourceFrame.SampleId}.jpg";
                var frame = sourceFrame with { PixelFile = pixelFile };
                File.WriteAllBytes(Path.Combine(destination, pixelFile.Replace('/', Path.DirectorySeparatorChar)), pixels);
                File.Copy(receiptPath, Path.Combine(destination, folder, "receipts", Path.GetFileName(receiptPath)));
                indexFrames.Add(frame);
                if (associationReady)
                {
                    var balls = capture.Balls.Select(ball =>
                    {
                        var tablePoint = tableCalibration.Map(new(ball.X, ball.Y));
                        return new BallLabel(ball.Id, ball.Colour, tablePoint.X, tablePoint.Y,
                            Enum.Parse<LabelVisibility>(ball.Visibility, ignoreCase: false));
                    }).ToImmutableArray();
                    labelFrames.Add(new(frame.SampleId, frame.PixelSha256, "table-2x1", balls,
                        capture.Notes) { Tags = capture.Tags });
                    var detector = capture.DetectionOutput!;
                    sessionDetections.Add(new(frame.SampleId, frame.PixelSha256, "table-2x1", detector.Model,
                        detector.Provenance, detector.Uncertain, detector.Balls));
                }
                else
                {
                    var balls = capture.Balls.Select(ball => new BallLabel(ball.Id, ball.Colour, ball.X, ball.Y,
                        Enum.Parse<LabelVisibility>(ball.Visibility, ignoreCase: false))
                        { RadiusX = ball.RadiusX, RadiusY = ball.RadiusY }).ToImmutableArray();
                    labelFrames.Add(new(frame.SampleId, frame.PixelSha256, "image-normalized", balls,
                        capture.Notes) { Tags = capture.Tags });
                }
            }
            string indexFile = $"{folder}/frames.jsonl", labelsFile = $"{folder}/labels.json";
            File.WriteAllLines(Path.Combine(destination, indexFile.Replace('/', Path.DirectorySeparatorChar)),
                indexFrames.Select(frame => JsonSerializer.Serialize(frame, compact)));
            string calibration = prepared.Frames[0].Capture.CalibrationId;
            var intervals = ImmutableArray<LabelInterval>.Empty;
            var purpose = associationReady ? SequencePurpose.Shot : SequencePurpose.StillRegression;
            if (!associationReady && prepared.Split == CorpusSplit.Holdout)
            {
                purpose = prepared.HoldoutCondition == "still" ? SequencePurpose.StillVerification : SequencePurpose.NegativeVerification;
                long from = prepared.Frames.Min(f => f.Capture.IndexedFrame.SourceTimeUs!.Value);
                long to = prepared.Frames.Max(f => f.Capture.IndexedFrame.SourceTimeUs!.Value);
                if (to <= from) throw new InvalidDataException("Holdout diagnostic condition interval must have increasing source time.");
                intervals = [new(prepared.HoldoutCondition!, from, to, "confirmed", [], "Condition reviewed by the user in the O09 session assignment.")];
            }
            File.WriteAllText(Path.Combine(destination, labelsFile.Replace('/', Path.DirectorySeparatorChar)),
                CorpusJson.Write(new SequenceLabels(2, id, "user-reviewed diagnostic labels", calibration, true,
                    labelFrames.ToImmutable(), intervals)) + "\n");
            var first = indexFrames[0];
            sequences.Add(new(id, prepared.Session.RecordingSessionId, CorpusKind.Real, prepared.Split, purpose,
                new("diagnostic-" + id, "Opt-in, hand-labelled live camera frames", first.PixelFile, first.PixelSha256,
                    first.Width, first.Height, SourceClock.RecordingMicroseconds, calibration), indexFile, labelsFile,
                "User-labelled diagnostic frame subset. Detector output is not included in ground truth.", []));
            if (associationReady)
            {
                if (sessionDetections.Count != prepared.Frames.Length)
                    throw new InvalidDataException("O04 shot import did not pair one detector result with every labelled frame: " + id);
                if (!associationDetections.TryGetValue(prepared.Split, out var splitDetections))
                    associationDetections.Add(prepared.Split, splitDetections = []);
                splitDetections.AddRange(sessionDetections);
            }
        }
        var manifest = new CorpusManifest(1, "o09-diagnostics-" + Guid.NewGuid().ToString("N"), sequences.ToImmutable());
        string manifestPath = Path.Combine(destination, "manifest.json");
        File.WriteAllText(manifestPath, CorpusJson.Write(manifest) + "\n");
        CorpusValidation.Validate(manifest, new CorpusFiles(destination));
        if (associationReady)
            foreach (var (split, detections) in associationDetections.OrderBy(p => p.Key))
            {
                string file = "detections-" + split.ToString().ToLowerInvariant() + ".json";
                File.WriteAllText(Path.Combine(destination, file), CorpusJson.Write(detections) + "\n");
            }
        return manifestPath;
    }
}
