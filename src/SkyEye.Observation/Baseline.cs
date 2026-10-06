using System.Collections.Immutable;

namespace SkyEye.Observation;

public sealed record DetectedBall(string Id, string Colour, double X, double Y, double? Confidence);
public sealed record StoredDetection(string SampleId, string PixelSha256, string CoordinateSpace, string Model,
    string Provenance, bool Uncertain, ImmutableArray<DetectedBall> Balls);
public sealed record DetectionMatch(string TruthId, string DetectionId, double CentreError, bool ColourCorrect);
public sealed record BaselineScore(string SampleId, string Model, string Provenance, bool Uncertain, string CoordinateSpace,
    int VisibleLabels, int Detections, int SpatialTruePositives, int FalsePositives, int MissedVisible,
    int ColourCorrect, double? Precision, double? Recall, double? CentreErrorP95,
    ImmutableArray<string> MissedIds, ImmutableArray<string> ExtraIds, ImmutableArray<DetectionMatch> Matches);

/// <summary>Maximum-cardinality spatial association. This does not establish temporal ball identity.</summary>
public static class DetectorBaseline
{
    public static BaselineScore Score(FrameLabels labels, StoredDetection detection, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 1 || labels.PixelSha256 != detection.PixelSha256
            || labels.SampleId != detection.SampleId || labels.CoordinateSpace != detection.CoordinateSpace || detection.Balls.IsDefault
            || detection.Balls.Length > 64 || !CorpusFiles.Text(detection.Model) || !CorpusFiles.Text(detection.Provenance)
            || detection.Balls.Select(b => b.Id).Distinct().Count() != detection.Balls.Length
            || detection.Balls.Any(b => !CorpusFiles.Text(b.Id) || !double.IsFinite(b.X) || !double.IsFinite(b.Y)
                || b.X < 0 || b.X > (labels.CoordinateSpace == "table-2x1" ? 2 : 1) || b.Y is < 0 or > 1
                || b.Colour is not ("red" or "yellow" or "cue" or "black" or "unknown")
                || b.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1)))
            throw new InvalidDataException("Detection provenance, frame identity, coordinates or threshold is invalid.");
        var truth = labels.Balls.Where(b => b.Visibility == LabelVisibility.Visible).OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
        var found = detection.Balls.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
        double Distance(int t, int d) => Math.Sqrt(Math.Pow(truth[t].X - found[d].X, 2) + Math.Pow(truth[t].Y - found[d].Y, 2));
        var edges = truth.Select((_, t) => Enumerable.Range(0, found.Length).Where(d => Distance(t, d) <= tolerance)
            .OrderBy(d => Distance(t, d)).ThenBy(d => found[d].Id, StringComparer.Ordinal).ToArray()).ToArray();
        var detectionOwner = Enumerable.Repeat(-1, found.Length).ToArray();
        bool Assign(int t, HashSet<int> visited)
        {
            foreach (var d in edges[t])
                if (visited.Add(d) && (detectionOwner[d] < 0 || Assign(detectionOwner[d], visited))) { detectionOwner[d] = t; return true; }
            return false;
        }
        for (int t = 0; t < truth.Length; t++) Assign(t, []);
        var matches = Enumerable.Range(0, found.Length).Where(d => detectionOwner[d] >= 0)
            .Select(d => new DetectionMatch(truth[detectionOwner[d]].Id, found[d].Id, Distance(detectionOwner[d], d), truth[detectionOwner[d]].Colour == found[d].Colour))
            .OrderBy(m => m.TruthId, StringComparer.Ordinal).ToImmutableArray();
        var errors = matches.Select(m => m.CentreError).Order().ToArray();
        var matchedTruth = matches.Select(m => m.TruthId).ToHashSet();
        return new(labels.SampleId, detection.Model, detection.Provenance, detection.Uncertain, labels.CoordinateSpace,
            truth.Length, found.Length, matches.Length, found.Length - matches.Length, truth.Length - matches.Length, matches.Count(m => m.ColourCorrect),
            found.Length == 0 ? null : (double)matches.Length / found.Length, truth.Length == 0 ? null : (double)matches.Length / truth.Length,
            errors.Length == 0 ? null : errors[(int)Math.Ceiling(.95 * errors.Length) - 1],
            truth.Where(t => !matchedTruth.Contains(t.Id)).Select(t => t.Id).ToImmutableArray(),
            Enumerable.Range(0, found.Length).Where(d => detectionOwner[d] < 0).Select(d => found[d].Id).ToImmutableArray(), matches);
    }
}
