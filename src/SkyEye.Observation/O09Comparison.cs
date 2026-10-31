using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SkyEye.Observation;

public sealed record O09CandidateInput(string Name, string DetectionSha256, ImmutableArray<StoredDetection> Detections);
public sealed record O09RateEstimate(double? Value, double? Lower95, double? Upper95);
public sealed record O09SessionMetrics(string Session, int Sequences, int Frames, int VisibleLabels, int Detections,
    int SpatialMatches, int FalsePositives, int MissedVisible, int ColourCorrect, int UnknownColourMatches,
    int UncertainFrames, int MatchesWithReviewedRadii, int MatchesWithPredictedAndReviewedRadii,
    double? Precision, double? Recall, double? ColourAccuracy)
{
    public int DuplicateFalsePositives { get; init; }
    public int OtherFalsePositives { get; init; }
    public int TimedFrames { get; init; }
    public double? AnalysisWallP50Ms { get; init; }
    public double? AnalysisWallP95Ms { get; init; }
}
public sealed record O09MetricSummary(int Sessions, int Sequences, int Frames, int VisibleLabels, int Detections,
    int SpatialMatches, int FalsePositives, int MissedVisible, int ColourCorrect, int UnknownColourMatches,
    int UncertainFrames, O09RateEstimate Precision, O09RateEstimate Recall, O09RateEstimate ColourAccuracy,
    double? FalsePositivesPerFrame, double? UncertainFrameRate, double? CentreErrorP95InBallRadii,
    int MatchesWithReviewedRadii, double? RadiusErrorP95InBallRadii, int MatchesWithPredictedAndReviewedRadii)
{
    public int DuplicateFalsePositives { get; init; }
    public int OtherFalsePositives { get; init; }
    public int TimedFrames { get; init; }
    public double? AnalysisWallP50Ms { get; init; }
    public double? AnalysisWallP95Ms { get; init; }
}
public sealed record O09SliceResult(string Kind, string Value, O09MetricSummary Metrics, ImmutableArray<O09SessionMetrics> BySession);
public sealed record O09FailureSample(string SampleId, string SequenceId, string Session, ImmutableArray<string> Tags,
    int VisibleLabels, int SpatialMatches, int FalsePositives, int MissedVisible, int WrongColours,
    int UnknownColourMatches, bool Uncertain, ImmutableArray<string> MissedIds, ImmutableArray<string> ExtraIds)
{
    public ImmutableArray<string> DuplicateExtraIds { get; init; } = [];
    public ImmutableArray<string> OtherExtraIds { get; init; } = [];
}
public sealed record O09FrameScore(string SequenceId, string Session, ImmutableArray<string> Tags,
    long FrameId, long? SourceTimeUs, FrameScope Scope, string PixelSha256, BaselineScore Score)
{
    public ImmutableArray<string> DuplicateExtraIds { get; init; } = [];
    public ImmutableArray<string> OtherExtraIds { get; init; } = [];
    public O08ReplayProvenance? O08Replay { get; init; }
}
public sealed record O09ReplayConfigurationSummary(ImmutableArray<string> RecordedConfigurations,
    ImmutableArray<string> ReplayConfigurations, ImmutableArray<string> ReplayReportSha256s,
    ImmutableArray<string> ApkSha256s, int CaptureConfigurationExactFrames, int CaptureConfigurationMismatchFrames);
public sealed record O09CandidateResult(string Name, string DetectionSha256, ImmutableArray<string> Models,
    ImmutableArray<string> Provenances, O09MetricSummary Overall, ImmutableArray<O09SliceResult> ByLightingOrSceneTag,
    ImmutableArray<O09FailureSample> FailureSamples, ImmutableArray<O09FrameScore> Frames)
{
    public O09ReplayConfigurationSummary? ReplayConfiguration { get; init; }
}
public sealed record O09ComparisonReport(int SchemaVersion, string CorpusId, string Split, string ManifestSha256,
    double Tolerance, int BootstrapReplicates, string UncertaintyMethod, string PublicationLimit,
    ImmutableArray<O09CandidateResult> Candidates)
{
    public string? SplitLockSha256 { get; init; }
    public string DuplicateClassification { get; init; } = "An unmatched detection is a duplicate only when it lies within the centre tolerance of a reviewed visible ball that already has a spatial match; every other unmatched detection is a non-duplicate extra.";
    public string TimingScope { get; init; } = "O08 runtime fields are offline width-800 analyzer-call wall time. Source age and live end-to-end latency are not measured by this replay comparison.";
}

/// <summary>Scores frozen O09 candidate outputs on exactly the same reviewed real samples.</summary>
public static class O09Comparison
{
    const int BootstrapCount = 2000;
    const int MaximumSamples = 100_000;

    sealed record SampleContext(string SequenceId, string Session, ImmutableArray<string> Tags, FrameLabels Labels, IndexedFrame Frame);
    sealed record ScoredSample(SampleContext Context, StoredDetection Detection, BaselineScore Score,
        ImmutableArray<string> DuplicateExtraIds, ImmutableArray<string> OtherExtraIds);

    public static O09ComparisonReport Compare(CorpusManifest manifest, FrozenCorpus frozen, CorpusFiles files,
        CorpusSplit split, IReadOnlyList<O09CandidateInput> candidates, double tolerance, string? splitLockSha256 = null)
    {
        if (split is not (CorpusSplit.Development or CorpusSplit.Holdout))
            throw new InvalidDataException("O09 comparison requires an explicit Development or Holdout split.");
        if (!double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 1)
            throw new InvalidDataException("The O09 centre tolerance is invalid.");
        if (splitLockSha256 is not null && !CorpusFiles.IsHash(splitLockSha256))
            throw new InvalidDataException("The O09 split-lock fingerprint is invalid.");
        if (candidates is null || candidates.Count != 3 || candidates.Any(c => c is null || !CorpusFiles.Text(c.Name)
            || !CorpusFiles.IsHash(c.DetectionSha256) || c.Detections.IsDefault)
            || candidates.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != candidates.Count)
            throw new InvalidDataException("Supply three uniquely named, fingerprinted candidate outputs.");

        var sequences = manifest.Sequences.Where(s => s.Kind == CorpusKind.Real && s.Split == split)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (sequences.Length == 0) throw new InvalidDataException("The selected split has no real O09 sequences.");
        var expected = new Dictionary<string, SampleContext>(StringComparer.Ordinal);
        foreach (var sequence in sequences)
        {
            var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(sequence.LabelsFile));
            if (!labels.Reviewed || labels.Frames.IsDefaultOrEmpty)
                throw new InvalidDataException("Every selected O09 sequence must have reviewed frame labels: " + sequence.Id);
            var index = files.Index(sequence.IndexFile).ToDictionary(f => f.SampleId, StringComparer.Ordinal);
            foreach (var frame in labels.Frames)
            {
                if (expected.Count >= MaximumSamples) throw new InvalidDataException("O09 comparison exceeds the 100,000-sample report bound.");
                if (!index.TryGetValue(frame.SampleId, out var receipt) || receipt.PixelSha256 != frame.PixelSha256)
                    throw new InvalidDataException("O09 labelled frame is missing its matching indexed receipt: " + frame.SampleId);
                var tags = (sequence.Tags.IsDefault ? ImmutableArray<string>.Empty : sequence.Tags)
                    .AddRange(frame.Tags.IsDefault ? ImmutableArray<string>.Empty : frame.Tags)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
                if (!expected.TryAdd(frame.SampleId, new(sequence.Id, sequence.Session,
                    tags, frame, receipt)))
                    throw new InvalidDataException("O09 sample IDs must be unique across selected sequences.");
            }
        }

        var results = ImmutableArray.CreateBuilder<O09CandidateResult>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var detections = new Dictionary<string, StoredDetection>(StringComparer.Ordinal);
            foreach (var detection in candidate.Detections)
            {
                if (detection is null || !CorpusFiles.Text(detection.SampleId) || !detections.TryAdd(detection.SampleId, detection))
                    throw new InvalidDataException("Candidate output contains an empty or duplicate sample ID: " + candidate.Name);
                if (!expected.ContainsKey(detection.SampleId))
                    throw new InvalidDataException("Candidate output contains a sample outside the selected frozen split: " + detection.SampleId);
            }
            bool hasO08Metadata = candidate.Detections.Any(d => d.O08Replay is not null);
            if (hasO08Metadata && (candidate.Detections.Any(d => d.O08Replay is null) || splitLockSha256 is null))
                throw new InvalidDataException("O08 candidate outputs require complete per-frame provenance and the exact frozen split-lock hash.");
            if (hasO08Metadata)
            {
                foreach (var detection in candidate.Detections)
                {
                    var replay = detection.O08Replay!;
                    var context = expected[detection.SampleId];
                    bool ValidIdentity(string? value) => value is { Length: > 0 and <= 65536 };
                    bool ExactHash(string value, string identity) => CorpusFiles.IsHash(value)
                        && CorpusFiles.Hash(Encoding.UTF8.GetBytes(identity)).Equals(value, StringComparison.OrdinalIgnoreCase);
                    if (replay.CorpusId != manifest.Id || !CorpusFiles.IsHash(replay.ManifestSha256)
                        || !replay.ManifestSha256.Equals(frozen.ManifestSha256, StringComparison.OrdinalIgnoreCase)
                        || !CorpusFiles.IsHash(replay.SplitLockSha256) || !replay.SplitLockSha256.Equals(splitLockSha256, StringComparison.OrdinalIgnoreCase)
                        || replay.SequenceId != context.SequenceId || replay.Split != split.ToString()
                        || replay.FrameId != context.Frame.FrameId || replay.SourceTimeUs != context.Frame.SourceTimeUs
                        || !CorpusFiles.IsHash(replay.PixelSha256) || !replay.PixelSha256.Equals(context.Frame.PixelSha256, StringComparison.OrdinalIgnoreCase)
                        || replay.Scope != context.Frame.Scope
                        || !CorpusFiles.IsHash(replay.ReceiptSha256) || !CorpusFiles.IsHash(replay.ReplayManifestSha256)
                        || !CorpusFiles.IsHash(replay.ReplayReportSha256) || !CorpusFiles.IsHash(replay.ApkSha256)
                        || !ValidIdentity(replay.RecordedConfiguration) || !ValidIdentity(replay.ReplayConfiguration)
                        || !ExactHash(replay.RecordedConfigurationSha256, replay.RecordedConfiguration)
                        || !ExactHash(replay.ReplayConfigurationSha256, replay.ReplayConfiguration)
                        || replay.CaptureConfigurationExact != (replay.RecordedConfiguration == replay.ReplayConfiguration)
                        || !double.IsFinite(replay.AnalysisWallMs) || replay.AnalysisWallMs < 0
                        || detection.Uncertain != (replay.AnalysisUncertain || replay.LayoutUncertain || replay.Obstructed))
                        throw new InvalidDataException("O08 candidate provenance does not match its reviewed source frame or frozen split: " + detection.SampleId);
                }
            }
            var missing = expected.Keys.Where(id => !detections.ContainsKey(id)).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"Candidate {candidate.Name} is missing {missing.Length} reviewed samples; emit an explicit empty/uncertain detection for abstentions.");

            var scored = expected.Values.OrderBy(s => s.SequenceId, StringComparer.Ordinal).ThenBy(s => s.Labels.SampleId, StringComparer.Ordinal)
                .Select(context => Score(context, detections[context.Labels.SampleId], tolerance)).ToArray();
            var allMetrics = Metrics(scored, candidate.Name + ":all");
            var bySession = scored.GroupBy(s => s.Context.Session, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => SessionMetrics(g.Key, g.ToArray())).ToImmutableArray();
            var tags = scored.SelectMany(s => s.Context.Tags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var slices = ImmutableArray.CreateBuilder<O09SliceResult>(tags.Length);
            foreach (var tag in tags)
            {
                var inTag = scored.Where(s => s.Context.Tags.Contains(tag, StringComparer.Ordinal)).ToArray();
                slices.Add(new("tag", tag, Metrics(inTag, candidate.Name + ":tag:" + tag),
                    inTag.GroupBy(s => s.Context.Session, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                        .Select(g => SessionMetrics(g.Key, g.ToArray())).ToImmutableArray()));
            }
            var failures = scored.Where(s => s.Score.FalsePositives > 0 || s.Score.MissedVisible > 0
                    || s.Score.ColourCorrect < s.Score.SpatialTruePositives)
                .Select(Failure)
                .ToImmutableArray();
            var allSlices = ImmutableArray.Create(new O09SliceResult("all", "all", allMetrics, bySession)).AddRange(slices);
            var frames = scored.Select(s => new O09FrameScore(s.Context.SequenceId, s.Context.Session, s.Context.Tags,
                s.Context.Frame.FrameId, s.Context.Frame.SourceTimeUs, s.Context.Frame.Scope, s.Context.Frame.PixelSha256, s.Score)
                { DuplicateExtraIds = s.DuplicateExtraIds, OtherExtraIds = s.OtherExtraIds, O08Replay = s.Detection.O08Replay })
                .ToImmutableArray();
            var replayRows = candidate.Detections.Select(d => d.O08Replay).Where(p => p is not null).Cast<O08ReplayProvenance>().ToArray();
            var replayConfiguration = replayRows.Length == 0 ? null : new O09ReplayConfigurationSummary(
                replayRows.Select(p => p.RecordedConfiguration).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
                replayRows.Select(p => p.ReplayConfiguration).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
                replayRows.Select(p => p.ReplayReportSha256).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
                replayRows.Select(p => p.ApkSha256).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
                replayRows.Count(p => p.CaptureConfigurationExact), replayRows.Count(p => !p.CaptureConfigurationExact));
            results.Add(new O09CandidateResult(candidate.Name, candidate.DetectionSha256,
                candidate.Detections.Select(d => d.Model).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
                candidate.Detections.Select(d => d.Provenance).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
                allMetrics, allSlices, failures, frames) { ReplayConfiguration = replayConfiguration });
        }

        return new O09ComparisonReport(3, manifest.Id, split.ToString(), frozen.ManifestSha256, tolerance, BootstrapCount,
            "Deterministic cluster bootstrap resampling recording sessions; per-session counts are retained. Intervals are null when fewer than two sessions contribute or the rate denominator is empty.",
            "Per-frame ball metrics do not establish safe table readiness. Use the O08 verification scorer for false-ready and current-layout gates. Offline replay runtime is not live source age or end-to-end latency.",
            results.ToImmutable()) { SplitLockSha256 = splitLockSha256 };
    }

    static ScoredSample Score(SampleContext context, StoredDetection detection, double tolerance)
    {
        var score = DetectorBaseline.Score(context.Labels, detection, tolerance);
        var matchedTruth = score.Matches.Select(m => m.TruthId).ToHashSet(StringComparer.Ordinal);
        var truthById = context.Labels.Balls.Where(b => b.Visibility == LabelVisibility.Visible)
            .ToDictionary(b => b.Id, StringComparer.Ordinal);
        var foundById = detection.Balls.ToDictionary(b => b.Id, StringComparer.Ordinal);
        double Distance(BallLabel truth, DetectedBall found)
            => Math.Sqrt(Math.Pow(truth.X - found.X, 2) + Math.Pow(truth.Y - found.Y, 2));
        var duplicate = ImmutableArray.CreateBuilder<string>();
        var other = ImmutableArray.CreateBuilder<string>();
        foreach (var id in score.ExtraIds)
        {
            var found = foundById[id];
            bool duplicatesMatchedTruth = truthById.Values.Any(truth => matchedTruth.Contains(truth.Id)
                && Distance(truth, found) <= tolerance);
            (duplicatesMatchedTruth ? duplicate : other).Add(id);
        }
        return new(context, detection, score, duplicate.ToImmutable(), other.ToImmutable());
    }

    static O09FailureSample Failure(ScoredSample sample)
    {
        var detectedById = sample.Detection.Balls.ToDictionary(b => b.Id, StringComparer.Ordinal);
        int unknown = sample.Score.Matches.Count(m => detectedById[m.DetectionId].Colour == "unknown");
        return new O09FailureSample(sample.Context.Labels.SampleId, sample.Context.SequenceId, sample.Context.Session, sample.Context.Tags,
            sample.Score.VisibleLabels, sample.Score.SpatialTruePositives, sample.Score.FalsePositives, sample.Score.MissedVisible,
            sample.Score.SpatialTruePositives - sample.Score.ColourCorrect, unknown, sample.Score.Uncertain,
            sample.Score.MissedIds, sample.Score.ExtraIds)
            { DuplicateExtraIds = sample.DuplicateExtraIds, OtherExtraIds = sample.OtherExtraIds };
    }

    static O09MetricSummary Metrics(ScoredSample[] samples, string seedKey)
    {
        var perSession = samples.GroupBy(s => s.Context.Session, StringComparer.Ordinal)
            .Select(g => Counts(g.Key, g.ToArray())).OrderBy(s => s.Session, StringComparer.Ordinal).ToArray();
        var total = Sum(perSession);
        var precision = Estimate(perSession, s => s.SpatialMatches, s => s.Detections, seedKey + ":precision");
        var recall = Estimate(perSession, s => s.SpatialMatches, s => s.VisibleLabels, seedKey + ":recall");
        var colour = Estimate(perSession, s => s.ColourCorrect, s => s.SpatialMatches, seedKey + ":colour");
        var centreErrors = samples.SelectMany(s => s.Score.Matches).Where(m => m.CentreErrorInRadii.HasValue)
            .Select(m => m.CentreErrorInRadii!.Value).Order().ToArray();
        var radiusErrors = samples.SelectMany(s => s.Score.Matches).Where(m => m.RadiusErrorInBallRadii.HasValue)
            .Select(m => m.RadiusErrorInBallRadii!.Value).Order().ToArray();
        return new O09MetricSummary(perSession.Length, total.Sequences, total.Frames, total.VisibleLabels, total.Detections,
            total.SpatialMatches, total.FalsePositives, total.MissedVisible, total.ColourCorrect, total.UnknownColourMatches,
            total.UncertainFrames, precision, recall, colour,
            total.Frames == 0 ? null : (double)total.FalsePositives / total.Frames,
            total.Frames == 0 ? null : (double)total.UncertainFrames / total.Frames,
            Percentile95(centreErrors), total.MatchesWithReviewedRadii, Percentile95(radiusErrors),
            total.MatchesWithPredictedAndReviewedRadii)
        {
            DuplicateFalsePositives = samples.Sum(s => s.DuplicateExtraIds.Length),
            OtherFalsePositives = samples.Sum(s => s.OtherExtraIds.Length),
            TimedFrames = samples.Count(s => s.Detection.O08Replay is not null),
            AnalysisWallP50Ms = Percentile50(samples.Select(s => s.Detection.O08Replay?.AnalysisWallMs).Where(v => v.HasValue).Select(v => v!.Value).Order().ToArray()),
            AnalysisWallP95Ms = Percentile95(samples.Select(s => s.Detection.O08Replay?.AnalysisWallMs).Where(v => v.HasValue).Select(v => v!.Value).Order().ToArray())
        };
    }

    static O09SessionMetrics SessionMetrics(string session, ScoredSample[] samples)
    {
        var c = Counts(session, samples);
        return new(c.Session, c.Sequences, c.Frames, c.VisibleLabels, c.Detections, c.SpatialMatches, c.FalsePositives,
            c.MissedVisible, c.ColourCorrect, c.UnknownColourMatches, c.UncertainFrames,
            c.MatchesWithReviewedRadii, c.MatchesWithPredictedAndReviewedRadii,
            Rate(c.SpatialMatches, c.Detections), Rate(c.SpatialMatches, c.VisibleLabels), Rate(c.ColourCorrect, c.SpatialMatches));
    }

    static O09SessionMetrics Counts(string session, ScoredSample[] samples)
    {
        int matches = samples.Sum(s => s.Score.SpatialTruePositives);
        int unknown = samples.Sum(s =>
        {
            var byId = s.Detection.Balls.ToDictionary(b => b.Id, StringComparer.Ordinal);
            return s.Score.Matches.Count(m => byId[m.DetectionId].Colour == "unknown");
        });
        int visible = samples.Sum(s => s.Score.VisibleLabels), detections = samples.Sum(s => s.Score.Detections);
        int correct = samples.Sum(s => s.Score.ColourCorrect);
        int withTruthRadii = samples.Sum(s => s.Score.MatchesWithReviewedRadii);
        int withPredictedRadii = samples.Sum(s => s.Score.MatchesWithPredictedAndReviewedRadii);
        return new O09SessionMetrics(session, samples.Select(s => s.Context.SequenceId).Distinct(StringComparer.Ordinal).Count(), samples.Length,
            visible, detections, matches, samples.Sum(s => s.Score.FalsePositives), samples.Sum(s => s.Score.MissedVisible),
            correct, unknown, samples.Count(s => s.Score.Uncertain), withTruthRadii, withPredictedRadii,
            Rate(matches, detections), Rate(matches, visible), Rate(correct, matches))
        {
            DuplicateFalsePositives = samples.Sum(s => s.DuplicateExtraIds.Length),
            OtherFalsePositives = samples.Sum(s => s.OtherExtraIds.Length),
            TimedFrames = samples.Count(s => s.Detection.O08Replay is not null),
            AnalysisWallP50Ms = Percentile50(samples.Select(s => s.Detection.O08Replay?.AnalysisWallMs).Where(v => v.HasValue).Select(v => v!.Value).Order().ToArray()),
            AnalysisWallP95Ms = Percentile95(samples.Select(s => s.Detection.O08Replay?.AnalysisWallMs).Where(v => v.HasValue).Select(v => v!.Value).Order().ToArray())
        };
    }

    static O09SessionMetrics Sum(O09SessionMetrics[] values)
    {
        int spatial = values.Sum(v => v.SpatialMatches), detections = values.Sum(v => v.Detections), visible = values.Sum(v => v.VisibleLabels);
        int correct = values.Sum(v => v.ColourCorrect);
        return new O09SessionMetrics("all", values.Sum(v => v.Sequences), values.Sum(v => v.Frames), visible, detections,
            spatial, values.Sum(v => v.FalsePositives), values.Sum(v => v.MissedVisible), correct,
            values.Sum(v => v.UnknownColourMatches), values.Sum(v => v.UncertainFrames),
            values.Sum(v => v.MatchesWithReviewedRadii), values.Sum(v => v.MatchesWithPredictedAndReviewedRadii),
            Rate(spatial, detections), Rate(spatial, visible), Rate(correct, spatial))
        {
            DuplicateFalsePositives = values.Sum(v => v.DuplicateFalsePositives),
            OtherFalsePositives = values.Sum(v => v.OtherFalsePositives),
            TimedFrames = values.Sum(v => v.TimedFrames)
        };
    }

    static O09RateEstimate Estimate(O09SessionMetrics[] sessions, Func<O09SessionMetrics, int> numerator,
        Func<O09SessionMetrics, int> denominator, string seedKey)
    {
        int totalNumerator = sessions.Sum(numerator), totalDenominator = sessions.Sum(denominator);
        double? point = Rate(totalNumerator, totalDenominator);
        if (point is null || sessions.Length < 2) return new(point, null, null);
        ulong state = BinaryPrimitives.ReadUInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(seedKey)));
        var samples = new List<double>(BootstrapCount);
        for (int iteration = 0; iteration < BootstrapCount; iteration++)
        {
            int n = 0, d = 0;
            for (int draw = 0; draw < sessions.Length; draw++)
            {
                var chosen = sessions[NextIndex(ref state, sessions.Length)];
                n += numerator(chosen); d += denominator(chosen);
            }
            if (d > 0) samples.Add((double)n / d);
        }
        if (samples.Count == 0) return new(point, null, null);
        samples.Sort();
        int low = (int)Math.Floor(.025 * (samples.Count - 1));
        int high = (int)Math.Ceiling(.975 * (samples.Count - 1));
        return new(point, samples[low], samples[high]);
    }

    static int NextIndex(ref ulong state, int length)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong value = state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        value ^= value >> 31;
        return (int)(value % (uint)length);
    }

    static double? Rate(int numerator, int denominator) => denominator <= 0 ? null : (double)numerator / denominator;
    static double? Percentile50(double[] sorted) => sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(.50 * sorted.Length) - 1];
    static double? Percentile95(double[] sorted) => sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1];
}
