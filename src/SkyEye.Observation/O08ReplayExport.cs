using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SkyEye.Observation;

public sealed record O08ExportFrame(string SampleId, long FrameId, long SourceTimeUs, FrameScope Scope, string PixelSha256,
    string ReceiptSha256, bool DetectionRequested, double? AnalysisWallMs, bool? AnalysisUncertain,
    bool? LayoutUncertain, bool? Obstructed)
{
    public string SequenceId { get; init; } = "";
}
public sealed record O08ReplayExportResult(string CorpusId, string ManifestSha256, string SequenceId, string Split,
    string SplitLockSha256, bool LabelsReviewed, string ReplayManifestSha256, string ReplayReportSha256, string ApkSha256, string Mode,
    string DeviceSerial, int BatchSize, bool IndependentFrames, string WarmupTreatment,
    string RecordedConfiguration, string ReplayConfiguration, bool CaptureConfigurationExact,
    ImmutableArray<O08ExportFrame> Frames, ImmutableArray<StoredDetection> Detections);
public sealed record O08ExportArtifactManifest(int SchemaVersion, string CorpusId, string ManifestSha256,
    string SplitLockSha256, string SequenceId, string Split, bool LabelsReviewed, string ReplayManifestSha256,
    string ReplayReportSha256, string ApkSha256, string Mode, string DeviceSerial, int BatchSize,
    bool IndependentFrames, string WarmupTreatment, string RecordedConfiguration,
    string ReplayConfiguration, bool CaptureConfigurationExact, string DetectionFile, string DetectionSha256,
    ImmutableArray<O08ExportFrame> Frames, string Scope);
public sealed record O08MergedInput(string SequenceId, string ArtifactManifestSha256, string DetectionSha256,
    string Mode, string DeviceSerial, int BatchSize, bool IndependentFrames, string WarmupTreatment);
public sealed record O08MergedExportManifest(int SchemaVersion, string CorpusId, string ManifestSha256,
    string SplitLockSha256, string Split, bool LabelsReviewed, string DetectionFile, string DetectionSha256,
    int SequenceCount, int FrameCount, int RequestedCount, int SkippedCount,
    ImmutableArray<O08MergedInput> Inputs, ImmutableArray<O08ExportFrame> Frames, string Scope);
public sealed record O08ReplayMergeResult(O08MergedExportManifest Manifest, ImmutableArray<StoredDetection> Detections);
public sealed record O09CandidateBinding(int SchemaVersion, string ArtifactManifestSha256, string CorpusId,
    string ManifestSha256, string SplitLockSha256, string Split, bool LabelsReviewed, string DetectionFile,
    string DetectionSha256);

/// <summary>Offline conversion only: never creates labels, readiness, events or a live timing claim.</summary>
public static class O08ReplayExport
{
    public const string WarmupTreatment = "No explicit warm-up call is excluded; the first measured analyzer call is retained.";
    static JsonElement Required(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value : throw new InvalidDataException("Missing replay field: " + property);
    static string Text(JsonElement element, string property)
        => Required(element, property).GetString() is { Length: > 0 } value
            ? value : throw new InvalidDataException("Empty replay field: " + property);
    static bool SameHash(string a, string b) => CorpusFiles.IsHash(a) && CorpusFiles.IsHash(b)
        && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    static string HashText(string value) => CorpusFiles.Hash(Encoding.UTF8.GetBytes(value));

    public static O08ReplayExportResult Convert(CorpusManifest manifest, string manifestPath, FrozenCorpus frozen,
        CorpusFiles corpus, string sequenceId, string replayDirectory, byte[] splitLockBytes)
    {
        if (splitLockBytes is null) throw new InvalidDataException("Split lock is missing.");
        if (splitLockBytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Split lock exceeds bounds.");
        var suppliedLock = JsonSerializer.Deserialize<FrozenCorpus>(splitLockBytes, CorpusJson.Options)
            ?? throw new InvalidDataException("Split lock is empty.");
        var splitLockSha256 = CorpusFiles.Hash(splitLockBytes);
        if (!CorpusFiles.IsHash(splitLockSha256)) throw new InvalidDataException("Split lock fingerprint is invalid.");
        SplitLock.Verify(suppliedLock, frozen);
        SplitLock.Verify(frozen, SplitLock.Freeze(manifest, corpus, manifestPath));
        var sequence = manifest.Sequences.SingleOrDefault(s => s.Id == sequenceId)
            ?? throw new InvalidDataException("Replay sequence is not in the frozen corpus.");
        var labels = CorpusJson.Read<SequenceLabels>(corpus.Resolve(sequence.LabelsFile));
        var indexed = corpus.Index(sequence.IndexFile).ToDictionary(f => f.SampleId, StringComparer.Ordinal);
        var run = new CorpusFiles(replayDirectory);
        var manifestFile = run.Resolve("manifest.json");
        if (new FileInfo(manifestFile).Length > 2 * 1024 * 1024) throw new InvalidDataException("Replay manifest exceeds bounds.");
        var manifestBytes = File.ReadAllBytes(manifestFile);
        if (manifestBytes.Length > 2 * 1024 * 1024) throw new InvalidDataException("Replay manifest exceeds bounds.");
        using var replayManifest = JsonDocument.Parse(manifestBytes);
        var m = replayManifest.RootElement;
        var reportSha = Text(m, "reportSha256");
        var reportBytes = run.Verified(Text(m, "report"), reportSha);
        using var report = JsonDocument.Parse(reportBytes);
        var r = report.RootElement;
        if (!Required(r, "completed").GetBoolean()) throw new InvalidDataException("Replay report is incomplete.");
        var mode = Text(r, "mode");
        if (mode is not ("screening" or "probe-only") || mode != Text(m, "mode"))
            throw new InvalidDataException("Only matching width-800 screening or probe-only reports are supported.");
        var deviceSerial = Text(m, "deviceSerial");
        var independentFrames = Required(r, "independentFrames").GetBoolean();
        if (Required(m, "independentFrames").GetBoolean() != independentFrames)
            throw new InvalidDataException("Replay manifest and report disagree about independent-frame mode.");
        var recordedConfig = Text(r, "recordedAnalyzerConfigurationIdentity");
        var replayConfig = Text(r, "replayAnalyzerConfigurationIdentity");
        using var config = JsonDocument.Parse(replayConfig);
        if (Required(Required(config.RootElement, "Settings"), "Width").GetInt32() != 800)
            throw new InvalidDataException("Replay configuration is not width 800.");
        var apkSha = Text(m, "apkSha256");
        if (!CorpusFiles.IsHash(apkSha)) throw new InvalidDataException("Replay APK fingerprint is invalid.");
        var sourceFrames = Required(m, "sourceFrames").EnumerateArray().ToArray();
        var samples = Required(r, "samples").EnumerateArray().ToArray();
        var observations = Required(Required(r, "fullAnalysis800"), "observations").EnumerateArray().ToArray();
        if (samples.Length is < 1 or > 24 || samples.Length != sourceFrames.Length)
            throw new InvalidDataException("Replay source/sample coverage is invalid.");
        if (samples.Count(s => Required(s, "detectionRequested").GetBoolean()) != observations.Length)
            throw new InvalidDataException("Replay observation count does not match requested detections.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var frames = ImmutableArray.CreateBuilder<O08ExportFrame>();
        var detections = ImmutableArray.CreateBuilder<StoredDetection>();
        var configSha = HashText(replayConfig);
        var recordedConfigSha = HashText(recordedConfig);
        string? cornerSignature = null;
        int observationIndex = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i]; var source = sourceFrames[i];
            var receiptSha = Text(source, "receiptSha256");
            if (!SameHash(receiptSha, Text(sample, "receiptSha256"))) throw new InvalidDataException("Replay receipt hash mismatch.");
            var receiptBytes = run.Verified(Text(source, "receiptArtifact"), receiptSha);
            using var receiptDocument = JsonDocument.Parse(receiptBytes);
            var receipt = receiptDocument.RootElement; var item = Required(receipt, "indexedFrame");
            var sampleId = Text(item, "sampleId");
            if (!seen.Add(sampleId) || !indexed.TryGetValue(sampleId, out var frame))
                throw new InvalidDataException("Replay sample is duplicated or outside the selected sequence: " + sampleId);
            var scope = JsonSerializer.Deserialize<FrameScope>(Required(item, "scope"), CorpusJson.Options)
                ?? throw new InvalidDataException("Missing receipt scope.");
            var frameId = Required(item, "frameId").GetInt64(); var pts = Required(item, "sourceTimeUs").GetInt64();
            var pixelSha = Text(item, "pixelSha256");
            if (frame.FrameId != frameId || frame.SourceTimeUs != pts || frame.Scope != scope
                || frame.Width != Required(item, "width").GetInt32() || frame.Height != Required(item, "height").GetInt32()
                || frame.EmbeddedFrameId != Required(item, "embeddedFrameId").GetBoolean()
                || frame.Encoding.ToString() != Text(item, "encoding") || !SameHash(frame.PixelSha256, pixelSha)
                || frameId != Required(sample, "frameId").GetInt64() || pts != Required(sample, "sourceTimeUs").GetInt64()
                || frameId != Required(source, "frameId").GetInt64() || !SameHash(pixelSha, Text(sample, "pixelSha256"))
                || !SameHash(pixelSha, Text(source, "sha256")) || Text(item, "pixelFile") != Text(source, "pixelFile"))
                throw new InvalidDataException("Replay source identity, scope, dimensions or pixels mismatch: " + sampleId);
            PixelDimensions.Verify(frame, corpus.Verified(frame.PixelFile, frame.PixelSha256));
            // VerificationCaptureImport preserves this sibling, but the corpus
            // lock does not hash it. Bind it independently to the device-read
            // receipt and retain the hash in this export's per-frame manifest.
            corpus.Verified(Path.ChangeExtension(frame.PixelFile, ".receipt.json"), receiptSha);
            if (Text(receipt, "analyzerConfigurationIdentity") != recordedConfig)
                throw new InvalidDataException("Recorded analyzer identity does not match receipt: " + sampleId);
            var corners = Required(receipt, "calibrationCorners").EnumerateArray().ToArray();
            if (corners.Length != 4) throw new InvalidDataException("Receipt must contain four calibration corners.");
            var coordinates = corners.Select(c => (X: Required(c, "x").GetDouble(), Y: Required(c, "y").GetDouble())).ToArray();
            if (coordinates.Any(c => !double.IsFinite(c.X) || !double.IsFinite(c.Y))) throw new InvalidDataException("Invalid calibration corners.");
            var signature = string.Join(";", coordinates.Select(c =>
                c.X.ToString("R", CultureInfo.InvariantCulture) + "," + c.Y.ToString("R", CultureInfo.InvariantCulture)));
            if (cornerSignature is not null && cornerSignature != signature) throw new InvalidDataException("Replay calibration corners differ across frames.");
            cornerSignature = signature;
            var requested = Required(sample, "detectionRequested").GetBoolean();
            if (Required(receipt, "detectionRequested").GetBoolean() != requested)
                throw new InvalidDataException("Detection request differs from receipt: " + sampleId);
            if (!requested)
            {
                frames.Add(new O08ExportFrame(sampleId, frameId, pts, scope, frame.PixelSha256, receiptSha,
                    false, null, null, null, null) { SequenceId = sequenceId });
                continue;
            }
            var analysis = observations[observationIndex++];
            if (Required(analysis, "configuredWidth").GetInt32() != 800) throw new InvalidDataException("Unexpected analysis width.");
            var circles = Required(analysis, "circles").EnumerateArray().ToArray();
            if (circles.Length > 64 || Required(analysis, "circleCount").GetInt32() != circles.Length)
                throw new InvalidDataException("Replay circle count mismatch.");
            var balls = circles.Select(c => new DetectedBall(Text(c, "ballId"), Text(c, "kind"),
                Required(c, "x").GetDouble(), Required(c, "y").GetDouble(), null)).ToImmutableArray();
            if (balls.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != balls.Length
                || balls.Any(b => !CorpusFiles.Text(b.Id) || b.Colour is not ("cue" or "red" or "yellow" or "black" or "unknown")
                    || !double.IsFinite(b.X) || b.X is < 0 or > 2 || !double.IsFinite(b.Y) || b.Y is < 0 or > 1))
                throw new InvalidDataException("Replay contains invalid detection geometry or labels.");
            var wall = Required(Required(sample, "analysis800Metrics"), "wallMs").GetDouble();
            if (!double.IsFinite(wall) || wall < 0) throw new InvalidDataException("Replay timing is invalid.");
            var analysisUncertain = Required(analysis, "uncertain").GetBoolean();
            var layoutUncertain = Required(analysis, "layoutUncertain").GetBoolean();
            var obstructed = Required(analysis, "obstructed").GetBoolean();
            var replayProvenance = new O08ReplayProvenance(manifest.Id, frozen.ManifestSha256, splitLockSha256,
                sequenceId, sequence.Split.ToString(), frameId, pts, scope, frame.PixelSha256, receiptSha,
                CorpusFiles.Hash(manifestBytes), reportSha, apkSha, mode, deviceSerial, samples.Length, independentFrames,
                WarmupTreatment, recordedConfig, replayConfig,
                recordedConfigSha, configSha, recordedConfig == replayConfig, wall,
                analysisUncertain, layoutUncertain, obstructed);
            detections.Add(new StoredDetection(sampleId, frame.PixelSha256, "table-2x1", "o08-" + apkSha[..12],
                $"offline report={reportSha}; receipt={receiptSha}; config={configSha}; width=800",
                analysisUncertain || layoutUncertain || obstructed, balls) { O08Replay = replayProvenance });
            frames.Add(new O08ExportFrame(sampleId, frameId, pts, scope, frame.PixelSha256, receiptSha,
                true, wall, analysisUncertain, layoutUncertain, obstructed) { SequenceId = sequenceId });
        }
        return new(manifest.Id, frozen.ManifestSha256, sequenceId, sequence.Split.ToString(), splitLockSha256, labels.Reviewed,
            CorpusFiles.Hash(manifestBytes), reportSha, apkSha, mode, deviceSerial, samples.Length, independentFrames,
            WarmupTreatment, recordedConfig, replayConfig,
            recordedConfig == replayConfig, frames.ToImmutable(), detections.ToImmutable());
    }

    public static O08ReplayMergeResult Merge(CorpusManifest manifest, string manifestPath, FrozenCorpus frozen,
        CorpusFiles corpus, IEnumerable<string> exportDirectories, byte[] splitLockBytes)
    {
        if (splitLockBytes is null || splitLockBytes.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("Split lock is missing or exceeds bounds.");
        var suppliedLock = JsonSerializer.Deserialize<FrozenCorpus>(splitLockBytes, CorpusJson.Options)
            ?? throw new InvalidDataException("Split lock is empty.");
        var splitLockSha256 = CorpusFiles.Hash(splitLockBytes);
        SplitLock.Verify(suppliedLock, frozen);
        SplitLock.Verify(frozen, SplitLock.Freeze(manifest, corpus, manifestPath));

        if (exportDirectories is null) throw new InvalidDataException("O08 export directories are missing.");
        var rawDirectories = exportDirectories.ToArray();
        if (rawDirectories.Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("O08 export directory is empty.");
        var directories = rawDirectories.Select(Path.GetFullPath).ToArray();
        if (directories.Length is < 1 or > 256 || directories.Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != directories.Length)
            throw new InvalidDataException("Merge 1 to 256 unique O08 export directories.");

        var inputRows = ImmutableArray.CreateBuilder<O08MergedInput>(directories.Length);
        var allFrames = ImmutableArray.CreateBuilder<O08ExportFrame>();
        var allDetections = ImmutableArray.CreateBuilder<StoredDetection>();
        var seenSequences = new HashSet<string>(StringComparer.Ordinal);
        var seenSamples = new HashSet<string>(StringComparer.Ordinal);
        string? split = null;
        bool? labelsReviewed = null;
        foreach (var directory in directories)
        {
            var run = new CorpusFiles(directory);
            var artifactPath = run.Resolve("manifest.json");
            if (new FileInfo(artifactPath).Length > 16 * 1024 * 1024) throw new InvalidDataException("O08 export manifest exceeds bounds.");
            var artifactBytes = File.ReadAllBytes(artifactPath);
            if (artifactBytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("O08 export manifest exceeds bounds.");
            var artifact = JsonSerializer.Deserialize<O08ExportArtifactManifest>(artifactBytes, CorpusJson.Options)
                ?? throw new InvalidDataException("O08 export manifest is empty.");
            if (artifact.SchemaVersion != 2 || artifact.CorpusId != manifest.Id
                || !SameHash(artifact.ManifestSha256, frozen.ManifestSha256)
                || !SameHash(artifact.SplitLockSha256, splitLockSha256)
                || artifact.Split is not ("Development" or "Holdout")
                || artifact.Mode is not ("screening" or "probe-only") || artifact.DetectionFile != "detections.json"
                || !CorpusFiles.IsHash(artifact.ReplayManifestSha256)
                || !CorpusFiles.IsHash(artifact.ReplayReportSha256) || !CorpusFiles.IsHash(artifact.ApkSha256)
                || string.IsNullOrWhiteSpace(artifact.SequenceId) || artifact.SequenceId.Length > 500
                || string.IsNullOrWhiteSpace(artifact.DeviceSerial) || artifact.DeviceSerial.Length > 500
                || artifact.BatchSize != artifact.Frames.Length || artifact.WarmupTreatment != WarmupTreatment
                || artifact.RecordedConfiguration is not { Length: > 0 and <= 65536 }
                || artifact.ReplayConfiguration is not { Length: > 0 and <= 65536 }
                || artifact.Frames.IsDefaultOrEmpty || artifact.Frames.Length > 24)
                throw new InvalidDataException("O08 export manifest identity, mode or frame coverage is invalid.");
            if (split is not null && split != artifact.Split) throw new InvalidDataException("O08 exports cross corpus splits.");
            split = artifact.Split;
            if (labelsReviewed is not null && labelsReviewed != artifact.LabelsReviewed)
                throw new InvalidDataException("O08 exports mix reviewed and unreviewed sequence labels.");
            labelsReviewed = artifact.LabelsReviewed;
            seenSequences.Add(artifact.SequenceId);
            var sequence = manifest.Sequences.SingleOrDefault(s => s.Id == artifact.SequenceId)
                ?? throw new InvalidDataException("O08 sequence is outside the current corpus.");
            if (sequence.Split.ToString() != artifact.Split) throw new InvalidDataException("O08 sequence split changed.");
            var sequenceLabels = CorpusJson.Read<SequenceLabels>(corpus.Resolve(sequence.LabelsFile));
            if (sequenceLabels.Reviewed != artifact.LabelsReviewed) throw new InvalidDataException("O08 label review status changed.");
            using var recordedConfig = JsonDocument.Parse(artifact.RecordedConfiguration);
            using var replayConfig = JsonDocument.Parse(artifact.ReplayConfiguration);
            if (Required(Required(recordedConfig.RootElement, "Settings"), "Width").GetInt32() != 800
                || Required(Required(replayConfig.RootElement, "Settings"), "Width").GetInt32() != 800
                || artifact.CaptureConfigurationExact != (artifact.RecordedConfiguration == artifact.ReplayConfiguration))
                throw new InvalidDataException("O08 export configuration identity is inconsistent or not width 800.");

            var indexed = corpus.Index(sequence.IndexFile).ToDictionary(f => f.SampleId, StringComparer.Ordinal);
            var detectionBytes = run.Verified(artifact.DetectionFile, artifact.DetectionSha256);
            var detections = JsonSerializer.Deserialize<StoredDetection[]>(detectionBytes, CorpusJson.Options)
                ?? throw new InvalidDataException("O08 detections are empty.");
            if (detections.Length > 24 || artifact.Frames.Length > 24)
                throw new InvalidDataException("O08 export exceeds the per-run 24-frame bound.");
            var detectionsBySample = new Dictionary<string, StoredDetection>(StringComparer.Ordinal);
            foreach (var detection in detections)
                if (detection is null || !CorpusFiles.Text(detection.SampleId) || !detectionsBySample.TryAdd(detection.SampleId, detection))
                    throw new InvalidDataException("O08 export has a missing or duplicate detection sample.");

            int requested = 0;
            foreach (var exportedFrame in artifact.Frames)
            {
                if (exportedFrame is null) throw new InvalidDataException("O08 export contains an empty frame row.");
                if (exportedFrame.SequenceId != artifact.SequenceId || !CorpusFiles.Text(exportedFrame.SampleId) || !seenSamples.Add(exportedFrame.SampleId)
                    || !indexed.TryGetValue(exportedFrame.SampleId, out var frame)
                    || frame.FrameId != exportedFrame.FrameId || frame.SourceTimeUs != exportedFrame.SourceTimeUs
                    || frame.Scope != exportedFrame.Scope || !SameHash(frame.PixelSha256, exportedFrame.PixelSha256)
                    || !CorpusFiles.IsHash(exportedFrame.ReceiptSha256))
                    throw new InvalidDataException("O08 export frame does not match the frozen corpus.");
                PixelDimensions.Verify(frame, corpus.Verified(frame.PixelFile, frame.PixelSha256));
                corpus.Verified(Path.ChangeExtension(frame.PixelFile, ".receipt.json"), exportedFrame.ReceiptSha256);
                allFrames.Add(exportedFrame);
                if (!exportedFrame.DetectionRequested)
                {
                    if (exportedFrame.AnalysisWallMs is not null || exportedFrame.AnalysisUncertain is not null
                        || exportedFrame.LayoutUncertain is not null || exportedFrame.Obstructed is not null
                        || detectionsBySample.ContainsKey(exportedFrame.SampleId))
                        throw new InvalidDataException("Skipped O08 frame must remain unscored: " + exportedFrame.SampleId);
                    continue;
                }
                requested++;
                if (exportedFrame.AnalysisWallMs is not { } wall || !double.IsFinite(wall) || wall < 0
                    || exportedFrame.AnalysisUncertain is null || exportedFrame.LayoutUncertain is null || exportedFrame.Obstructed is null
                    || !detectionsBySample.TryGetValue(exportedFrame.SampleId, out var detection) || detection.O08Replay is not { } replay)
                    throw new InvalidDataException("Requested O08 frame lacks a complete analysis/provenance record: " + exportedFrame.SampleId);
                if (replay.CorpusId != artifact.CorpusId || !SameHash(replay.ManifestSha256, artifact.ManifestSha256)
                    || !SameHash(replay.SplitLockSha256, artifact.SplitLockSha256) || replay.SequenceId != artifact.SequenceId
                    || replay.Split != artifact.Split || replay.FrameId != exportedFrame.FrameId
                    || replay.SourceTimeUs != exportedFrame.SourceTimeUs || replay.Scope != exportedFrame.Scope
                    || replay.Mode != artifact.Mode || replay.DeviceSerial != artifact.DeviceSerial || replay.BatchSize != artifact.BatchSize
                    || replay.IndependentFrames != artifact.IndependentFrames || replay.WarmupTreatment != artifact.WarmupTreatment
                    || !SameHash(replay.PixelSha256, exportedFrame.PixelSha256) || !SameHash(replay.ReceiptSha256, exportedFrame.ReceiptSha256)
                    || !SameHash(replay.ReplayManifestSha256, artifact.ReplayManifestSha256)
                    || !SameHash(replay.ReplayReportSha256, artifact.ReplayReportSha256) || !SameHash(replay.ApkSha256, artifact.ApkSha256)
                    || replay.RecordedConfiguration != artifact.RecordedConfiguration || replay.ReplayConfiguration != artifact.ReplayConfiguration
                    || !SameHash(replay.RecordedConfigurationSha256, HashText(artifact.RecordedConfiguration))
                    || !SameHash(replay.ReplayConfigurationSha256, HashText(artifact.ReplayConfiguration))
                    || replay.CaptureConfigurationExact != artifact.CaptureConfigurationExact || replay.AnalysisWallMs != wall
                    || replay.AnalysisUncertain != exportedFrame.AnalysisUncertain || replay.LayoutUncertain != exportedFrame.LayoutUncertain
                    || replay.Obstructed != exportedFrame.Obstructed
                    || detection.Uncertain != (replay.AnalysisUncertain || replay.LayoutUncertain || replay.Obstructed))
                    throw new InvalidDataException("O08 detection metadata disagrees with the export manifest: " + exportedFrame.SampleId);
                allDetections.Add(detection);
            }
            if (requested != detections.Length) throw new InvalidDataException("O08 requested frames and detections differ.");
            inputRows.Add(new(artifact.SequenceId, CorpusFiles.Hash(artifactBytes), artifact.DetectionSha256,
                artifact.Mode, artifact.DeviceSerial, artifact.BatchSize, artifact.IndependentFrames, artifact.WarmupTreatment));
        }
        if (allDetections.Count > 100_000 || allFrames.Count > 100_000)
            throw new InvalidDataException("Merged O08 export exceeds the 100,000-frame limit.");
        var detectionOutput = allDetections.OrderBy(d => d.O08Replay!.SequenceId, StringComparer.Ordinal)
            .ThenBy(d => d.O08Replay!.FrameId).ToImmutableArray();
        var frameOutput = allFrames.OrderBy(f => f.Scope.Timeline, StringComparer.Ordinal)
            .ThenBy(f => f.Scope.Generation).ThenBy(f => f.SourceTimeUs).ToImmutableArray();
        if (detectionOutput.IsDefaultOrEmpty) throw new InvalidDataException("Merged O08 exports contain no requested detections.");
        var mergedDetectionBytes = Encoding.UTF8.GetBytes(CorpusJson.Write(detectionOutput) + "\n");
        var mergedManifest = new O08MergedExportManifest(2, manifest.Id, frozen.ManifestSha256, splitLockSha256,
            split ?? throw new InvalidDataException("No O08 exports supplied."), labelsReviewed!.Value,
            "detections.json", CorpusFiles.Hash(mergedDetectionBytes), seenSequences.Count, frameOutput.Length,
            frameOutput.Count(f => f.DetectionRequested), frameOutput.Count(f => !f.DetectionRequested),
            inputRows.ToImmutable(), frameOutput,
            "Merged offline width-800 replay exports. Per-frame timing is analyzer-call wall time. Skipped frames remain unscored; source age, live latency, labels, readiness and events are not created.");
        return new(mergedManifest, detectionOutput);
    }
}
