using System.Collections.Immutable;

namespace SkyEye.Observation;

public sealed record CorpusAudit(int RealShotSequences, int HeldOutRealShots, int RealStillRegressions, int SyntheticContracts,
    bool AccuracyGateReady, ImmutableArray<string> Notes)
{
    public int RealVerificationSequences { get; init; }
    public int HeldOutVerificationSequences { get; init; }
}
public static class CorpusValidation
{
    public static CorpusAudit Validate(CorpusManifest manifest, CorpusFiles files)
    {
        if (manifest.SchemaVersion != 1 || !CorpusFiles.Text(manifest.Id) || manifest.Sequences.IsDefault || manifest.Sequences.Length > 1000
            || manifest.Sequences.Any(s => s is null || !CorpusFiles.Text(s.Id) || !CorpusFiles.Text(s.Session) || !Enum.IsDefined(s.Kind)
                || !Enum.IsDefined(s.Split) || !Enum.IsDefined(s.Purpose))
            || manifest.Sequences.Select(s => s.Id).Distinct().Count() != manifest.Sequences.Length) throw new InvalidDataException("Invalid corpus identity/schema.");
        foreach (var session in manifest.Sequences.GroupBy(s => s.Session))
            if (session.Any(s => s.Split == CorpusSplit.Holdout) && session.Any(s => s.Split is CorpusSplit.Development or CorpusSplit.Regression))
                throw new InvalidDataException("Recording session leaks across holdout and development/regression: " + session.Key);
        foreach (var source in manifest.Sequences.GroupBy(s => s.Source?.Sha256))
            if (source.Any(s => s.Split == CorpusSplit.Holdout) && source.Any(s => s.Split is CorpusSplit.Development or CorpusSplit.Regression))
                throw new InvalidDataException("The same source asset leaks across splits.");
        var sampleIds = new HashSet<string>();
        foreach (var sequence in manifest.Sequences)
        {
            ValidateSequence(sequence, files);
            foreach (var frame in files.Index(sequence.IndexFile))
                if (!sampleIds.Add(frame.SampleId)) throw new InvalidDataException("Sample IDs must be unique across the corpus.");
        }
        int real = manifest.Sequences.Count(s => s.Kind == CorpusKind.Real && s.Purpose == SequencePurpose.Shot);
        int held = manifest.Sequences.Count(s => s.Kind == CorpusKind.Real && s.Purpose == SequencePurpose.Shot && s.Split == CorpusSplit.Holdout);
        return new(real, held, manifest.Sequences.Count(s => s.Kind == CorpusKind.Real && s.Purpose == SequencePurpose.StillRegression),
            manifest.Sequences.Count(s => s.Kind == CorpusKind.Synthetic), false,
            [$"Real timed-shot coverage: {real}; held out: {held}. Collection targets remain at least 60/20 with labelled pots/misses and negative challenges.",
             "Manifest/schema success is not tracking accuracy or physical pixel/PTS pairing acceptance."])
        {
            RealVerificationSequences = manifest.Sequences.Count(s => s.Kind == CorpusKind.Real && Verification(s.Purpose)),
            HeldOutVerificationSequences = manifest.Sequences.Count(s => s.Kind == CorpusKind.Real && Verification(s.Purpose) && s.Split == CorpusSplit.Holdout)
        };
    }
    static bool Verification(SequencePurpose purpose) => purpose is SequencePurpose.StillVerification or SequencePurpose.NegativeVerification;
    static void ValidateSequence(CorpusSequence s, CorpusFiles files)
    {
        var source = s.Source;
        if (source is null || !CorpusFiles.Text(source.Id) || !CorpusFiles.Text(source.Origin) || !CorpusFiles.Text(source.CalibrationVersion)
            || source.Width is < 1 or > 8192 || source.Height is < 1 or > 8192 || !Enum.IsDefined(source.Clock)
            || source.NominalFps is { } fps && (!double.IsFinite(fps) || fps <= 0 || fps > 240)
            || s.Kind == CorpusKind.Synthetic && s.Split == CorpusSplit.Holdout
            || (s.Purpose == SequencePurpose.Shot || Verification(s.Purpose)) && source.Clock is SourceClock.Unknown
            || s.Kind == CorpusKind.Real && Verification(s.Purpose) && source.Clock == SourceClock.SyntheticMicroseconds)
            throw new InvalidDataException("Invalid source or split: " + s.Id);
        files.VerifyMedia(source.File, source.Sha256);
        var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(s.LabelsFile));
        if (labels.SchemaVersion is not (1 or 2) || labels.SequenceId != s.Id || labels.CalibrationVersion != source.CalibrationVersion
            || !CorpusFiles.Text(labels.Annotator) || labels.Frames.IsDefault || labels.Intervals.IsDefault
            || labels.Frames.Select(f => f.SampleId).Distinct().Count() != labels.Frames.Length
            || s.Split == CorpusSplit.Holdout && (!labels.Reviewed || s.Kind != CorpusKind.Real || !(s.Purpose == SequencePurpose.Shot || Verification(s.Purpose))))
            throw new InvalidDataException("Invalid or unreviewed labels: " + s.Id);
        var indexed = files.Index(s.IndexFile).ToArray();
        if (indexed.Length == 0 || indexed.Select(f => f.SampleId).Distinct().Count() != indexed.Length) throw new InvalidDataException("Empty/duplicate sample index.");
        foreach (var frame in indexed)
        {
            ValidateFrame(frame, source);
            PixelDimensions.Verify(frame, files.Verified(frame.PixelFile, frame.PixelSha256));
        }
        foreach (var label in labels.Frames)
        {
            var frame = indexed.SingleOrDefault(f => f.SampleId == label.SampleId);
            if (frame is null || frame.PixelSha256 != label.PixelSha256 || label.Balls.IsDefault
                || label.CoordinateSpace is not ("image-normalized" or "table-2x1") || label.Balls.Length > 64
                || labels.SchemaVersion == 1 && !label.Tags.IsDefaultOrEmpty
                || !label.Tags.IsDefault && (label.Tags.Length > 32 || label.Tags.Any(tag => !CorpusFiles.Text(tag))
                    || label.Tags.Distinct(StringComparer.Ordinal).Count() != label.Tags.Length)
                || label.Balls.Select(b => b.Id).Distinct().Count() != label.Balls.Length
                || label.Balls.Any(b => !CorpusFiles.Text(b.Id) || b.Colour is not ("red" or "yellow" or "cue" or "black" or "unknown")
                    || !Enum.IsDefined(b.Visibility) || !double.IsFinite(b.X) || !double.IsFinite(b.Y) || b.X < 0
                    || b.X > (label.CoordinateSpace == "table-2x1" ? 2 : 1) || b.Y is < 0 or > 1
                    || b.RadiusX is null != (b.RadiusY is null)
                    || b.RadiusX is { } rx && (!double.IsFinite(rx) || rx <= 0 || rx > 1)
                    || b.RadiusY is { } ry && (!double.IsFinite(ry) || ry <= 0 || ry > 1)))
                throw new InvalidDataException("Invalid frame labels or payload identity: " + s.Id);
        }
        foreach (var interval in labels.Intervals)
            if (interval.FromUs < 0 || interval.ToUs < interval.FromUs || interval.Certainty is not ("confirmed" or "uncertain")
                || !CorpusFiles.Text(interval.Kind) || interval.BallIds.IsDefault || source.Clock == SourceClock.Unknown)
                throw new InvalidDataException("Invalid source-time label interval.");
        if (Verification(s.Purpose))
        {
            var required = s.Purpose == SequencePurpose.StillVerification ? new[] { "still" } : new[] { "moving", "obstructed", "lighting-changed", "camera-changed", "source-invalid" };
            if (!labels.Reviewed || labels.Frames.Length < 2 || !labels.Intervals.Any(i => required.Contains(i.Kind) && i.Certainty == "confirmed" && i.ToUs > i.FromUs)
                || indexed.Any(f => f.SourceTimeUs is null)
                || (s.Purpose == SequencePurpose.StillVerification || !labels.Intervals.Any(i => i.Kind == "source-invalid" && i.Certainty == "confirmed"))
                    && indexed.Skip(1).Where((f, i) => f.FrameId <= indexed[i].FrameId || f.SourceTimeUs <= indexed[i].SourceTimeUs || f.Scope != indexed[i].Scope).Any())
                throw new InvalidDataException("Verification requires reviewed visible truth, a labelled interval and increasing scoped source receipts.");
        }
    }
    public static void ValidateFrame(IndexedFrame frame, MediaSource source)
    {
        if (frame is null || !CorpusFiles.Text(frame.SampleId) || frame.FrameId < 0 || frame.SourceTimeUs is < 0 || frame.Scope is null
            || !CorpusFiles.Text(frame.Scope.Source) || !CorpusFiles.Text(frame.Scope.Timeline) || frame.Scope.Generation <= 0
            || !CorpusFiles.Text(frame.Scope.Calibration) || frame.Width != source.Width || frame.Height != source.Height
            || !Enum.IsDefined(frame.Encoding) || source.Clock != SourceClock.Unknown && frame.SourceTimeUs is null
            || source.Clock == SourceClock.Unknown && frame.SourceTimeUs is not null
            || frame.EmbeddedFrameId && frame.Encoding != PixelEncoding.Gray8)
            throw new InvalidDataException("Invalid frame receipt: " + frame?.SampleId);
    }
}
