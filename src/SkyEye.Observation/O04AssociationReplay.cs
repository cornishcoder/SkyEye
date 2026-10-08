using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SkyEye.Observation;

public sealed record O04AssociationOptions(double CentreTolerance, double MaximumSpeedTableWidthsPerSecond,
    double AssociationSlackTableWidths, double MovementStartTableWidthsPerSecond,
    double MovementStopTableWidthsPerSecond, double TruthMovementStartTableWidthsPerSecond,
    double TruthMovementStopTableWidthsPerSecond, int MaximumBallInventory, long MaximumTrackGapUs,
    double AmbiguityMargin, double ColourMismatchPenalty);

public enum O04MotionState { Unknown, Stationary, Moving }

public sealed record O04AssociationMetrics(int Sessions, int Sequences, int Frames, int VisibleLabels, int Detections,
    int SpatialMatches, int FalsePositives, int MissedVisible, int NewTracks, int ContinuedTracks,
    int ExpiredTracks, int ScopeOrGapResets, int AmbiguousTracks, int AmbiguousDetections, int UntrackedDetections,
    int InventoryConflictFrames, int InventoryConstrainedCandidates, int IdentitySwitches,
    int GroundTruthMotionPairs, int ClassifiedMotionPairs, int MotionAbstentions,
    int MovingTruePositive, int MovingFalsePositive, int MovingFalseNegative, int StationaryTrueNegative,
    int UnobservedMoving, int UnobservedStationary, double? MotionCoverage,
    double? MovementPrecision, double? MovementRecall, double? StationarySpecificity);

public sealed record O04AssociationSlice(string Kind, string Value, O04AssociationMetrics Metrics);
public sealed record O04AssociationFailure(string SequenceId, string Session, string SampleId, long FrameId,
    long SourceTimeUs, ImmutableArray<string> Tags, int Detections, int NewTracks, int ContinuedTracks,
    int AmbiguousTracks, int AmbiguousDetections, int UntrackedDetections, int IdentitySwitches,
    int InventoryConstrainedCandidates, int MovingFalsePositive, int MovingFalseNegative, int UnobservedMoving,
    int MotionAbstentions, int FalsePositives, int MissedVisible);

public sealed record O04AssociationTraceLabel(string Id, string Colour, double X, double Y, string Visibility);
public sealed record O04AssociationTraceDetection(string Id, string Colour, double X, double Y, double? Confidence,
    string Status, long? TrackId, double? Residual, double? LastSeenX, double? LastSeenY,
    long? LastSeenSourceTimeUs, double? PredictedX, double? PredictedY, string Motion,
    ImmutableArray<long> AlternativeTrackIds, string? EvaluationTruthId);
public sealed record O04AssociationTraceTrack(long TrackId, double LastSeenX, double LastSeenY,
    long LastSeenSourceTimeUs, long AgeUs, double PredictedX, double PredictedY,
    string Motion, string Status, string? DetectionId);
public sealed record O04AssociationTraceFrame(string SequenceId, string Session, string SampleId,
    long FrameId, long SourceTimeUs, ImmutableArray<string> Tags, string CoordinateSpace,
    string PixelFile, string PixelSha256, PixelEncoding Encoding, int Width, int Height,
    ImmutableArray<O04AssociationTraceLabel> Labels, ImmutableArray<O04AssociationTraceDetection> Detections,
    ImmutableArray<O04AssociationTraceTrack> Tracks);
public sealed record O04AssociationReport(int SchemaVersion, string CorpusId, string Split, string ManifestSha256,
    string CandidateName, string DetectionSha256, ImmutableArray<string> Models, ImmutableArray<string> Provenances,
    O04AssociationOptions Configuration, O04AssociationMetrics Overall,
    ImmutableArray<O04AssociationSlice> BySession, ImmutableArray<O04AssociationSlice> ByLightingOrSceneTag,
    int FailureFrameCount, bool FailureFramesTruncated, ImmutableArray<O04AssociationFailure> FailureFrames,
    string PublicationLimit)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<O04AssociationTraceFrame>? TraceFrames { get; init; }
}

/// <summary>Offline, source-time ball association and movement-threshold evaluation for O04.
/// This is a tuning/reporting tool; it does not publish tracks to the runtime or GameState.</summary>
public static class O04AssociationReplay
{
    const int MaximumSamples = 100_000;
    const int MaximumFailureFrames = 10_000;

    sealed class Track(long id, DetectedBall ball, long sourceTimeUs)
    {
        public long Id { get; } = id;
        public double X { get; set; } = ball.X;
        public double Y { get; set; } = ball.Y;
        public double? PreviousX { get; set; }
        public double? PreviousY { get; set; }
        public long? PreviousTimeUs { get; set; }
        public long LastSeenUs { get; set; } = sourceTimeUs;
        public string Colour { get; set; } = ball.Colour;
        // Truth identity is evaluation-only; it is never used to make an association.
        public string? LastTruthId { get; set; }
        public O04MotionState Motion { get; set; }
    }

    sealed record Edge(int TrackIndex, int DetectionIndex, double Cost, double Residual);
    sealed record TruthHistory(double X, double Y, long SourceTimeUs, O04MotionState Motion);
    sealed record TruthTrack(long TrackId, long SourceTimeUs);
    sealed record FrameResult(string SequenceId, string Session, long FrameId, long SourceTimeUs,
        ImmutableArray<string> Tags, int VisibleLabels, int Detections, int SpatialMatches, int FalsePositives,
        int MissedVisible, int NewTracks, int ContinuedTracks, int ExpiredTracks, int ScopeOrGapResets,
        int AmbiguousTracks, int AmbiguousDetections, int UntrackedDetections, int InventoryConflictFrames,
        int InventoryConstrainedCandidates, int IdentitySwitches,
        int GroundTruthMotionPairs, int ClassifiedMotionPairs, int MotionAbstentions,
        int MovingTruePositive, int MovingFalsePositive, int MovingFalseNegative, int StationaryTrueNegative,
        int UnobservedMoving, int UnobservedStationary);

    public static O04AssociationReport Replay(CorpusManifest manifest, FrozenCorpus frozen, CorpusFiles files,
        CorpusSplit split, IReadOnlyList<StoredDetection> detections, string detectionSha256,
        string candidateName, O04AssociationOptions options, string? traceSequenceId = null)
    {
        ValidateOptions(options);
        if (split is not (CorpusSplit.Development or CorpusSplit.Holdout))
            throw new InvalidDataException("O04 replay requires an explicit Development or Holdout split.");
        if (!CorpusFiles.IsHash(detectionSha256) || !CorpusFiles.Text(candidateName)
            || detections is null || detections.Count == 0)
            throw new InvalidDataException("O04 needs a named, fingerprinted candidate detection file.");

        var sequences = manifest.Sequences.Where(s => s.Kind == CorpusKind.Real && s.Purpose == SequencePurpose.Shot && s.Split == split)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (sequences.Length == 0) throw new InvalidDataException("The selected split has no real, labelled shot sequences for O04.");
        var traceSequence = traceSequenceId is null ? null : sequences.SingleOrDefault(s => s.Id == traceSequenceId)
            ?? throw new InvalidDataException("The selected viewer sequence is not a real, labelled shot in the selected frozen split.");
        if (traceSequence is not null)
        {
            var traceLabels = CorpusJson.Read<SequenceLabels>(files.Resolve(traceSequence.LabelsFile));
            if (traceLabels.Frames.Length > O04AssociationHtml.MaximumFrames)
                throw new InvalidDataException($"The O04 viewer is limited to {O04AssociationHtml.MaximumFrames} frames per sequence.");
            var traceSampleIds = traceLabels.Frames.Select(l => l.SampleId).ToHashSet(StringComparer.Ordinal);
            var traceIndex = files.Index(traceSequence.IndexFile).Where(f => traceSampleIds.Contains(f.SampleId)).ToArray();
            if (traceIndex.Length != traceLabels.Frames.Length || traceIndex.Any(f => f.Encoding != PixelEncoding.Jpeg))
                throw new InvalidDataException("The O04 HTML viewer requires one JPEG source frame for each reviewed sample.");
        }

        var expected = new Dictionary<string, (CorpusSequence Sequence, FrameLabels Labels)>(StringComparer.Ordinal);
        foreach (var sequence in sequences)
        {
            var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(sequence.LabelsFile));
            if (!labels.Reviewed || labels.Frames.IsDefaultOrEmpty)
                throw new InvalidDataException("Every O04 sequence must have reviewed frame labels: " + sequence.Id);
            foreach (var frame in labels.Frames)
            {
                if (frame.CoordinateSpace != "table-2x1")
                    throw new InvalidDataException("O04 movement scoring requires calibrated table-2x1 labels: " + frame.SampleId);
                if (expected.Count >= MaximumSamples) throw new InvalidDataException("O04 replay exceeds the 100,000-sample bound.");
                if (!expected.TryAdd(frame.SampleId, (sequence, frame)))
                    throw new InvalidDataException("O04 sample IDs must be unique across the selected split.");
            }
        }

        var detectionBySample = new Dictionary<string, StoredDetection>(StringComparer.Ordinal);
        foreach (var detection in detections)
        {
            if (detection is null || !CorpusFiles.Text(detection.SampleId) || !detectionBySample.TryAdd(detection.SampleId, detection))
                throw new InvalidDataException("O04 candidate contains an empty or duplicate sample ID.");
            if (!expected.ContainsKey(detection.SampleId))
                throw new InvalidDataException("O04 candidate contains a sample outside the selected frozen split: " + detection.SampleId);
        }
        var missing = expected.Keys.Where(id => !detectionBySample.ContainsKey(id)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"O04 candidate is missing {missing.Length} reviewed samples; emit empty detections for abstentions.");

        var results = new List<FrameResult>();
        var failureFrames = ImmutableArray.CreateBuilder<O04AssociationFailure>();
        var traceFrames = traceSequence is null ? null : ImmutableArray.CreateBuilder<O04AssociationTraceFrame>();
        int totalFailureFrames = 0;
        foreach (var sequence in sequences)
        {
            var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(sequence.LabelsFile));
            var bySample = labels.Frames.ToDictionary(f => f.SampleId, StringComparer.Ordinal);
            var indexed = files.Index(sequence.IndexFile).ToArray();
            var indexedLabels = indexed.Where(f => bySample.ContainsKey(f.SampleId)).ToArray();
            if (indexedLabels.Length != labels.Frames.Length)
                throw new InvalidDataException("Every O04 label must match one indexed source frame: " + sequence.Id);

            var activeTracks = new Dictionary<long, Track>();
            var lastTruthById = new Dictionary<string, TruthHistory>(StringComparer.Ordinal);
            var lastTrackByTruth = new Dictionary<string, TruthTrack>(StringComparer.Ordinal);
            FrameScope? lastScope = null;
            long? lastFrameId = null, lastSourceTimeUs = null;
            long nextTrackId = 1;
            foreach (var indexedFrame in indexedLabels)
            {
                CorpusValidation.ValidateFrame(indexedFrame, sequence.Source);
                if (indexedFrame.SourceTimeUs is not { } sourceTimeUs)
                    throw new InvalidDataException("O04 requires source PTS on every labelled frame: " + indexedFrame.SampleId);
                var labelsForFrame = bySample[indexedFrame.SampleId];
                var detection = detectionBySample[indexedFrame.SampleId];
                var spatial = DetectorBaseline.Score(labelsForFrame, detection, options.CentreTolerance);
                var tags = (sequence.Tags.IsDefault ? ImmutableArray<string>.Empty : sequence.Tags)
                    .AddRange(labelsForFrame.Tags.IsDefault ? ImmutableArray<string>.Empty : labelsForFrame.Tags)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
                int resets = 0, expired = 0;
                bool sameScope = lastScope == indexedFrame.Scope;
                if (lastScope is not null && !sameScope)
                {
                    activeTracks.Clear(); lastTruthById.Clear(); lastTrackByTruth.Clear(); resets++;
                }
                else if (sameScope && lastSourceTimeUs is { } oldTime)
                {
                    if (indexedFrame.FrameId <= lastFrameId || sourceTimeUs <= oldTime)
                        throw new InvalidDataException("O04 source frame IDs and PTS must increase within one source scope: " + sequence.Id);
                    if (sourceTimeUs - oldTime > options.MaximumTrackGapUs)
                    {
                        activeTracks.Clear(); lastTruthById.Clear(); lastTrackByTruth.Clear(); resets++;
                    }
                }
                foreach (var stale in activeTracks.Values.Where(t => sourceTimeUs - t.LastSeenUs > options.MaximumTrackGapUs).Select(t => t.Id).ToArray())
                { activeTracks.Remove(stale); expired++; }

                var truthMotion = new Dictionary<string, O04MotionState>(StringComparer.Ordinal);
                var visibleTruth = labelsForFrame.Balls.Where(b => b.Visibility == LabelVisibility.Visible)
                    .OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
                foreach (var truth in labelsForFrame.Balls.Where(b => b.Visibility != LabelVisibility.Visible))
                { lastTruthById.Remove(truth.Id); lastTrackByTruth.Remove(truth.Id); }
                foreach (var truth in visibleTruth)
                {
                    var motion = O04MotionState.Unknown;
                    if (lastTruthById.TryGetValue(truth.Id, out var prior)
                        && sourceTimeUs > prior.SourceTimeUs && sourceTimeUs - prior.SourceTimeUs <= options.MaximumTrackGapUs)
                    {
                        double speed = Distance(prior.X, prior.Y, truth.X, truth.Y)
                            / ((sourceTimeUs - prior.SourceTimeUs) / 1_000_000d);
                        motion = NextTruthMotion(prior.Motion, speed, options);
                    }
                    truthMotion[truth.Id] = motion;
                }

                var truthForDetection = spatial.Matches.ToDictionary(m => m.DetectionId, m => m.TruthId, StringComparer.Ordinal);
                var detectionValues = detection.Balls.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
                var tracks = activeTracks.Values.OrderBy(t => t.Id).ToArray();
                var edgesByTrack = new List<Edge>[tracks.Length];
                var allEdges = new List<Edge>();
                for (int ti = 0; ti < tracks.Length; ti++)
                {
                    edgesByTrack[ti] = [];
                    var track = tracks[ti];
                    double elapsed = (sourceTimeUs - track.LastSeenUs) / 1_000_000d;
                    if (elapsed <= 0 || elapsed * 1_000_000d > options.MaximumTrackGapUs) continue;
                    var predicted = Predict(track, elapsed, options.MaximumSpeedTableWidthsPerSecond);
                    double gate = options.AssociationSlackTableWidths + options.MaximumSpeedTableWidthsPerSecond * elapsed
                        * (track.PreviousX.HasValue ? .25 : 1);
                    foreach (var (ball, di) in detectionValues.Select((ball, di) => (ball, di)))
                    {
                        double residual = Distance(predicted.X, predicted.Y, ball.X, ball.Y);
                        if (residual > gate) continue;
                        double colourCost = track.Colour == ball.Colour ? 0
                            : track.Colour == "unknown" || ball.Colour == "unknown" ? options.ColourMismatchPenalty * .5
                            : options.ColourMismatchPenalty;
                        var edge = new Edge(ti, di, residual / gate + colourCost, residual);
                        edgesByTrack[ti].Add(edge); allEdges.Add(edge);
                    }
                    edgesByTrack[ti].Sort(CompareEdges);
                }

                var ambiguousTracks = new HashSet<int>();
                var ambiguousDetections = new HashSet<int>();
                foreach (var trackEdges in edgesByTrack)
                    if (trackEdges.Count > 1 && trackEdges[1].Cost - trackEdges[0].Cost <= options.AmbiguityMargin)
                    {
                        ambiguousTracks.Add(trackEdges[0].TrackIndex);
                        ambiguousDetections.Add(trackEdges[0].DetectionIndex); ambiguousDetections.Add(trackEdges[1].DetectionIndex);
                    }
                foreach (var detectionEdges in allEdges.GroupBy(e => e.DetectionIndex))
                {
                    var best = detectionEdges.OrderBy(e => e.Cost).ThenBy(e => tracks[e.TrackIndex].Id).Take(2).ToArray();
                    if (best.Length > 1 && best[1].Cost - best[0].Cost <= options.AmbiguityMargin)
                    {
                        ambiguousDetections.Add(best[0].DetectionIndex);
                        ambiguousTracks.Add(best[0].TrackIndex); ambiguousTracks.Add(best[1].TrackIndex);
                    }
                }
                foreach (var edge in allEdges.Where(e => ambiguousTracks.Contains(e.TrackIndex)))
                    ambiguousDetections.Add(edge.DetectionIndex);

                for (int ti = 0; ti < edgesByTrack.Length; ti++)
                    edgesByTrack[ti] = edgesByTrack[ti].Where(e => !ambiguousTracks.Contains(ti)
                        && !ambiguousDetections.Contains(e.DetectionIndex)).ToList();
                var owner = Enumerable.Repeat(-1, detectionValues.Length).ToArray();
                var selected = new Edge?[tracks.Length];
                foreach (int ti in Enumerable.Range(0, tracks.Length).OrderBy(i => edgesByTrack[i].Count).ThenBy(i => tracks[i].Id))
                    TryAssign(ti, edgesByTrack, owner, selected, new bool[detectionValues.Length]);

                var traceStatus = Enumerable.Repeat("untracked", detectionValues.Length).ToArray();
                var traceTrackId = new long?[detectionValues.Length];
                var traceResidual = new double?[detectionValues.Length];
                var traceLastSeenX = new double?[detectionValues.Length];
                var traceLastSeenY = new double?[detectionValues.Length];
                var traceLastSeenTime = new long?[detectionValues.Length];
                var tracePredictedX = new double?[detectionValues.Length];
                var tracePredictedY = new double?[detectionValues.Length];
                var traceMotion = Enumerable.Repeat(O04MotionState.Unknown.ToString(), detectionValues.Length).ToArray();
                var traceAlternatives = new ImmutableArray<long>[detectionValues.Length];
                var traceEdgesByDetection = allEdges.GroupBy(e => e.DetectionIndex)
                    .ToDictionary(g => g.Key, g => g.ToArray());
                var selectedByDetection = selected.Where(e => e is not null).Select(e => e!)
                    .ToDictionary(e => e.DetectionIndex);
                var oldTrackIds = tracks.Select(t => t.Id).ToHashSet();
                for (int di = 0; di < detectionValues.Length; di++)
                {
                    traceAlternatives[di] = traceEdgesByDetection.TryGetValue(di, out var alternatives)
                        ? alternatives.Select(e => tracks[e.TrackIndex].Id).Distinct().Order().ToImmutableArray()
                        : ImmutableArray<long>.Empty;
                    if (owner[di] >= 0)
                    {
                        var track = tracks[owner[di]];
                        var edge = selectedByDetection[di];
                        double elapsed = (sourceTimeUs - track.LastSeenUs) / 1_000_000d;
                        var predicted = Predict(track, elapsed, options.MaximumSpeedTableWidthsPerSecond);
                        traceStatus[di] = "matched";
                        traceTrackId[di] = track.Id;
                        traceResidual[di] = edge.Residual;
                        traceLastSeenX[di] = track.X;
                        traceLastSeenY[di] = track.Y;
                        traceLastSeenTime[di] = track.LastSeenUs;
                        tracePredictedX[di] = predicted.X;
                        tracePredictedY[di] = predicted.Y;
                        traceMotion[di] = track.Motion.ToString();
                    }
                    else if (ambiguousDetections.Contains(di)) traceStatus[di] = "ambiguous";
                }

                var currentTrackByTruth = new Dictionary<string, (Track Track, bool ContinuedFromSameTruth)>(StringComparer.Ordinal);
                int newTracks = 0, continuedTracks = 0, identitySwitches = 0, untracked = 0;
                bool inventoryConflict = detectionValues.Length > options.MaximumBallInventory;
                int inventoryConstrained = 0;
                int availableSlots = Math.Max(0, options.MaximumBallInventory - activeTracks.Count);
                var unmatchedDetections = Enumerable.Range(0, detectionValues.Length)
                    .Where(di => owner[di] < 0 && !ambiguousDetections.Contains(di)).ToArray();
                if (unmatchedDetections.Length > availableSlots)
                {
                    inventoryConflict = true;
                    inventoryConstrained = unmatchedDetections.Length;
                    foreach (int di in unmatchedDetections)
                    {
                        ambiguousDetections.Add(di);
                        traceStatus[di] = "inventory-limited";
                    }
                }
                int inventoryConflicts = inventoryConflict ? 1 : 0;
                for (int di = 0; di < detectionValues.Length; di++)
                {
                    var ball = detectionValues[di];
                    Track track;
                    if (owner[di] >= 0)
                    {
                        track = tracks[owner[di]]; continuedTracks++;
                        Update(track, ball, sourceTimeUs, options);
                    }
                    else if (ambiguousDetections.Contains(di)) continue;
                    else if (activeTracks.Count < options.MaximumBallInventory)
                    {
                        track = new(nextTrackId++, ball, sourceTimeUs); activeTracks.Add(track.Id, track);
                        newTracks++;
                        traceStatus[di] = "new";
                        traceTrackId[di] = track.Id;
                        traceLastSeenX[di] = ball.X;
                        traceLastSeenY[di] = ball.Y;
                        traceLastSeenTime[di] = sourceTimeUs;
                        tracePredictedX[di] = ball.X;
                        tracePredictedY[di] = ball.Y;
                        traceMotion[di] = track.Motion.ToString();
                    }
                    else { untracked++; continue; }

                    if (!truthForDetection.TryGetValue(ball.Id, out var truthId)) continue;
                    if (track.LastTruthId is { } oldTruth && oldTruth != truthId) identitySwitches++;
                    if (lastTrackByTruth.TryGetValue(truthId, out var oldTrack)
                        && sourceTimeUs > oldTrack.SourceTimeUs && sourceTimeUs - oldTrack.SourceTimeUs <= options.MaximumTrackGapUs
                        && oldTrack.TrackId != track.Id) identitySwitches++;
                    bool sameTruth = track.LastTruthId == truthId
                        && lastTruthById.TryGetValue(truthId, out var truthPrior)
                        && track.PreviousTimeUs == truthPrior.SourceTimeUs;
                    currentTrackByTruth[truthId] = (track, sameTruth);
                    track.LastTruthId = truthId;
                    lastTrackByTruth[truthId] = new(track.Id, sourceTimeUs);
                }

                int truthPairs = 0, classified = 0, abstentions = 0, movingTp = 0, movingFp = 0,
                    movingFn = 0, stationaryTn = 0, unobservedMoving = 0, unobservedStationary = 0;
                foreach (var truth in visibleTruth)
                {
                    var truthState = truthMotion[truth.Id];
                    if (truthState == O04MotionState.Unknown) continue;
                    truthPairs++;
                    if (!currentTrackByTruth.TryGetValue(truth.Id, out var current) || !current.ContinuedFromSameTruth)
                    {
                        if (truthState == O04MotionState.Moving) unobservedMoving++;
                        else unobservedStationary++;
                        continue;
                    }
                    if (current.Track.Motion == O04MotionState.Unknown)
                    {
                        abstentions++;
                        if (truthState == O04MotionState.Moving) unobservedMoving++; else unobservedStationary++;
                        continue;
                    }
                    classified++;
                    if (truthState == O04MotionState.Moving && current.Track.Motion == O04MotionState.Moving) movingTp++;
                    else if (truthState == O04MotionState.Moving) movingFn++;
                    else if (current.Track.Motion == O04MotionState.Moving) movingFp++;
                    else stationaryTn++;
                }

                foreach (var truth in visibleTruth)
                    lastTruthById[truth.Id] = new(truth.X, truth.Y, sourceTimeUs, truthMotion[truth.Id]);
                int frameExpired = expired;
                var result = new FrameResult(sequence.Id, sequence.Session, indexedFrame.FrameId, sourceTimeUs, tags,
                    spatial.VisibleLabels, spatial.Detections, spatial.SpatialTruePositives, spatial.FalsePositives,
                    spatial.MissedVisible, newTracks, continuedTracks, frameExpired, resets,
                    ambiguousTracks.Count, ambiguousDetections.Count, untracked, inventoryConflicts,
                    inventoryConstrained, identitySwitches, truthPairs,
                    classified, abstentions, movingTp, movingFp, movingFn, stationaryTn,
                    unobservedMoving, unobservedStationary);
                results.Add(result);
                if (traceFrames is not null && sequence.Id == traceSequenceId)
                {
                    var traceTracks = activeTracks.Values.OrderBy(t => t.Id).Select(track =>
                    {
                        bool wasActive = oldTrackIds.Contains(track.Id);
                        bool matched = traceTrackId.Contains(track.Id);
                        double predictedX = track.X, predictedY = track.Y;
                        if (wasActive && !matched)
                        {
                            double elapsed = (sourceTimeUs - track.LastSeenUs) / 1_000_000d;
                            var predicted = Predict(track, elapsed, options.MaximumSpeedTableWidthsPerSecond);
                            predictedX = predicted.X; predictedY = predicted.Y;
                        }
                        string state = !wasActive ? "new" : matched ? "matched"
                            : ambiguousTracks.Contains(Array.IndexOf(tracks, track)) ? "ambiguous" : "predicted";
                        string? detectionId = matched
                            ? detectionValues[Array.IndexOf(traceTrackId, track.Id)].Id : null;
                        return new O04AssociationTraceTrack(track.Id, track.X, track.Y, track.LastSeenUs,
                            Math.Max(0, sourceTimeUs - track.LastSeenUs), predictedX, predictedY,
                            track.Motion.ToString(), state, detectionId);
                    }).ToImmutableArray();
                    var traceDetections = detectionValues.Select((ball, di) => new O04AssociationTraceDetection(
                        ball.Id, ball.Colour, ball.X, ball.Y, ball.Confidence, traceStatus[di], traceTrackId[di],
                        traceResidual[di], traceLastSeenX[di], traceLastSeenY[di], traceLastSeenTime[di],
                        tracePredictedX[di], tracePredictedY[di], traceMotion[di], traceAlternatives[di],
                        truthForDetection.GetValueOrDefault(ball.Id))).ToImmutableArray();
                    var traceLabels = labelsForFrame.Balls.OrderBy(b => b.Id, StringComparer.Ordinal)
                        .Select(b => new O04AssociationTraceLabel(b.Id, b.Colour, b.X, b.Y, b.Visibility.ToString()))
                        .ToImmutableArray();
                    traceFrames.Add(new(sequence.Id, sequence.Session, indexedFrame.SampleId, indexedFrame.FrameId,
                        sourceTimeUs, tags, labelsForFrame.CoordinateSpace, indexedFrame.PixelFile,
                        indexedFrame.PixelSha256, indexedFrame.Encoding, indexedFrame.Width, indexedFrame.Height,
                        traceLabels, traceDetections, traceTracks));
                }
                if (IsFailure(result))
                {
                    totalFailureFrames++;
                    if (failureFrames.Count < MaximumFailureFrames)
                        failureFrames.Add(new(sequence.Id, sequence.Session, indexedFrame.SampleId, indexedFrame.FrameId,
                            sourceTimeUs, tags, result.Detections, result.NewTracks, result.ContinuedTracks,
                            result.AmbiguousTracks, result.AmbiguousDetections, result.UntrackedDetections,
                            result.IdentitySwitches, result.InventoryConstrainedCandidates, result.MovingFalsePositive,
                            result.MovingFalseNegative, result.UnobservedMoving, result.MotionAbstentions,
                            result.FalsePositives, result.MissedVisible));
                }
                lastScope = indexedFrame.Scope; lastFrameId = indexedFrame.FrameId; lastSourceTimeUs = sourceTimeUs;
            }
        }

        var overall = Summarize(results);
        var bySession = results.GroupBy(r => r.Session, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new O04AssociationSlice("session", g.Key, Summarize(g))).ToImmutableArray();
        var tagsSeen = results.SelectMany(r => r.Tags).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var byTags = tagsSeen.Select(tag => new O04AssociationSlice("tag", tag,
            Summarize(results.Where(r => r.Tags.Contains(tag, StringComparer.Ordinal))))).ToImmutableArray();
        return new O04AssociationReport(1, manifest.Id, split.ToString(), frozen.ManifestSha256, candidateName, detectionSha256,
            detections.Select(d => d.Model).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            detections.Select(d => d.Provenance).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray(),
            options, overall, bySession, byTags, totalFailureFrames, totalFailureFrames > failureFrames.Count,
            failureFrames.ToImmutable(),
            "Offline tuning only. Track IDs are sequence-local evaluation outputs, not GameState ball identities. "
            + "Movement metrics compare labelled table-2x1 visible-ball displacement and report unobserved movement separately. "
            + "This does not establish live cadence, safe readiness, pot decisions, or production detector improvement.")
        { TraceFrames = traceFrames?.ToImmutable() };
    }

    static void ValidateOptions(O04AssociationOptions options)
    {
        if (options is null || !double.IsFinite(options.CentreTolerance) || options.CentreTolerance <= 0 || options.CentreTolerance > .5
            || !double.IsFinite(options.MaximumSpeedTableWidthsPerSecond) || options.MaximumSpeedTableWidthsPerSecond <= 0
                || options.MaximumSpeedTableWidthsPerSecond > 100
            || !double.IsFinite(options.AssociationSlackTableWidths) || options.AssociationSlackTableWidths <= 0
                || options.AssociationSlackTableWidths > .5
            || !double.IsFinite(options.MovementStopTableWidthsPerSecond) || options.MovementStopTableWidthsPerSecond < 0
            || !double.IsFinite(options.MovementStartTableWidthsPerSecond)
                || options.MovementStartTableWidthsPerSecond <= options.MovementStopTableWidthsPerSecond
                || options.MovementStartTableWidthsPerSecond > 100
            || !double.IsFinite(options.TruthMovementStopTableWidthsPerSecond) || options.TruthMovementStopTableWidthsPerSecond < 0
            || !double.IsFinite(options.TruthMovementStartTableWidthsPerSecond)
                || options.TruthMovementStartTableWidthsPerSecond <= options.TruthMovementStopTableWidthsPerSecond
                || options.TruthMovementStartTableWidthsPerSecond > 100
            || options.MaximumBallInventory is < 1 or > 64
            || options.MaximumTrackGapUs is < 1_000 or > 15_000_000
            || !double.IsFinite(options.AmbiguityMargin) || options.AmbiguityMargin < 0 || options.AmbiguityMargin > 1
            || !double.IsFinite(options.ColourMismatchPenalty) || options.ColourMismatchPenalty < 0 || options.ColourMismatchPenalty > 1)
            throw new ArgumentException("Invalid O04 thresholds: require table-2x1 units, increasing movement start/stop thresholds, and a 1 ms–15 s track gap.");
    }

    static (double X, double Y) Predict(Track track, double elapsed, double maximumSpeed)
    {
        if (track.PreviousX is not { } previousX || track.PreviousY is not { } previousY
            || track.PreviousTimeUs is not { } previousTime || track.LastSeenUs <= previousTime)
            return (track.X, track.Y);
        double priorElapsed = (track.LastSeenUs - previousTime) / 1_000_000d;
        double vx = (track.X - previousX) / priorElapsed, vy = (track.Y - previousY) / priorElapsed;
        double speed = Math.Sqrt(vx * vx + vy * vy);
        if (speed > maximumSpeed) { vx *= maximumSpeed / speed; vy *= maximumSpeed / speed; }
        return (track.X + vx * elapsed, track.Y + vy * elapsed);
    }

    static void Update(Track track, DetectedBall ball, long sourceTimeUs, O04AssociationOptions options)
    {
        double elapsed = (sourceTimeUs - track.LastSeenUs) / 1_000_000d;
        double speed = elapsed > 0 ? Distance(track.X, track.Y, ball.X, ball.Y) / elapsed : double.PositiveInfinity;
        track.Motion = elapsed <= 0 ? O04MotionState.Unknown : NextMotion(track.Motion, speed, options);
        track.PreviousX = track.X; track.PreviousY = track.Y; track.PreviousTimeUs = track.LastSeenUs;
        track.X = ball.X; track.Y = ball.Y; track.LastSeenUs = sourceTimeUs; track.Colour = ball.Colour;
    }

    static O04MotionState NextMotion(O04MotionState prior, double speed, O04AssociationOptions options)
    {
        if (speed >= options.MovementStartTableWidthsPerSecond) return O04MotionState.Moving;
        if (speed <= options.MovementStopTableWidthsPerSecond) return O04MotionState.Stationary;
        return prior;
    }

    static O04MotionState NextTruthMotion(O04MotionState prior, double speed, O04AssociationOptions options)
    {
        if (speed >= options.TruthMovementStartTableWidthsPerSecond) return O04MotionState.Moving;
        if (speed <= options.TruthMovementStopTableWidthsPerSecond) return O04MotionState.Stationary;
        return prior;
    }

    static int CompareEdges(Edge a, Edge b)
    {
        int cost = a.Cost.CompareTo(b.Cost);
        return cost != 0 ? cost : a.DetectionIndex.CompareTo(b.DetectionIndex);
    }

    static bool TryAssign(int trackIndex, List<Edge>[] edgesByTrack, int[] owner, Edge?[] selected, bool[] seen)
    {
        foreach (var edge in edgesByTrack[trackIndex])
        {
            int detection = edge.DetectionIndex;
            if (seen[detection]) continue;
            seen[detection] = true;
            int prior = owner[detection];
            if (prior < 0 || TryAssign(prior, edgesByTrack, owner, selected, seen))
            {
                owner[detection] = trackIndex; selected[trackIndex] = edge; return true;
            }
        }
        return false;
    }

    static double Distance(double ax, double ay, double bx, double by)
        => Math.Sqrt(Math.Pow(ax - bx, 2) + Math.Pow(ay - by, 2));

    static bool IsFailure(FrameResult frame) => frame.AmbiguousTracks > 0 || frame.AmbiguousDetections > 0
        || frame.UntrackedDetections > 0 || frame.IdentitySwitches > 0 || frame.MovingFalsePositive > 0
        || frame.MovingFalseNegative > 0 || frame.UnobservedMoving > 0 || frame.FalsePositives > 0
        || frame.MissedVisible > 0 || frame.InventoryConflictFrames > 0 || frame.MotionAbstentions > 0;

    static O04AssociationMetrics Summarize(IEnumerable<FrameResult> source)
    {
        var frames = source.ToArray();
        int tp = frames.Sum(f => f.MovingTruePositive), fp = frames.Sum(f => f.MovingFalsePositive);
        int fn = frames.Sum(f => f.MovingFalseNegative), tn = frames.Sum(f => f.StationaryTrueNegative);
        int classified = frames.Sum(f => f.ClassifiedMotionPairs), truthPairs = frames.Sum(f => f.GroundTruthMotionPairs);
        return new(frames.Select(f => f.Session).Distinct(StringComparer.Ordinal).Count(),
            frames.Select(f => f.SequenceId).Distinct(StringComparer.Ordinal).Count(), frames.Length,
            frames.Sum(f => f.VisibleLabels), frames.Sum(f => f.Detections), frames.Sum(f => f.SpatialMatches),
            frames.Sum(f => f.FalsePositives), frames.Sum(f => f.MissedVisible), frames.Sum(f => f.NewTracks),
            frames.Sum(f => f.ContinuedTracks), frames.Sum(f => f.ExpiredTracks), frames.Sum(f => f.ScopeOrGapResets),
            frames.Sum(f => f.AmbiguousTracks), frames.Sum(f => f.AmbiguousDetections), frames.Sum(f => f.UntrackedDetections),
            frames.Sum(f => f.InventoryConflictFrames), frames.Sum(f => f.InventoryConstrainedCandidates),
            frames.Sum(f => f.IdentitySwitches), truthPairs, classified, frames.Sum(f => f.MotionAbstentions),
            tp, fp, fn, tn, frames.Sum(f => f.UnobservedMoving), frames.Sum(f => f.UnobservedStationary),
            Ratio(classified, truthPairs), Ratio(tp, tp + fp), Ratio(tp, tp + fn + frames.Sum(f => f.UnobservedMoving)),
            Ratio(tn, tn + fp));
    }

    static double? Ratio(int numerator, int denominator) => denominator == 0 ? null : (double)numerator / denominator;
}
