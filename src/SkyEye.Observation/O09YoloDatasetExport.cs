using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SkyEye.Observation;

public sealed record O09ExportSession(string Session, string Role);
public sealed record O09ExportSample(string SequenceId, string Session, string Split, string SampleId,
    string ImageFile, string PixelSha256, string LabelFile, int VisibleBoxes);
public sealed record O09TrainingExportReport(int SchemaVersion, string CorpusId, string ManifestSha256,
    string SourceSplit, ImmutableArray<O09ExportSession> SessionAssignments,
    ImmutableArray<O09ExportSample> Samples, ImmutableArray<string> Classes, string AugmentationPolicy,
    string HoldoutPolicy);

/// <summary>Exports reviewed development labels for a YOLO-compatible trainer without copying O01 holdout pixels.</summary>
public static class O09YoloDatasetExport
{
    static readonly ImmutableArray<string> Classes = ["cue", "black", "red", "yellow", "unknown"];
    sealed record PreparedSample(string SequenceId, string Session, string Role, string SampleId,
        IndexedFrame Frame, string ImageFile, string LabelFile, string LabelText, int VisibleBoxes);

    public static O09TrainingExportReport Export(CorpusManifest manifest, FrozenCorpus frozen, CorpusFiles files, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Provide a new O09 dataset output directory.", nameof(outputDirectory));
        var development = manifest.Sequences.Where(s => s.Kind == CorpusKind.Real && s.Split == CorpusSplit.Development)
            .OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        if (development.Length == 0) throw new InvalidDataException("No real Development sequences are available for O09 training export.");

        var sessions = development.Select(s => s.Session).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (sessions.Length < 3)
            throw new InvalidDataException("O09 requires at least three independent Development sessions to form train and validation splits; O01 Holdout sessions are never exported.");
        int validationCount = Math.Clamp((int)Math.Round(sessions.Length * .2, MidpointRounding.AwayFromZero), 1, sessions.Length - 2);
        var validationSessions = sessions.OrderBy(s => StableSessionHash(manifest.Id, s), StringComparer.Ordinal)
            .ThenBy(s => s, StringComparer.Ordinal).Take(validationCount).ToHashSet(StringComparer.Ordinal);
        var assignments = sessions.Select(s => new O09ExportSession(s, validationSessions.Contains(s) ? "validation" : "train"))
            .ToImmutableArray();

        var target = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("O09 export destination already exists; choose a new versioned directory.");
        var prepared = new List<PreparedSample>();
        long totalBytes = 0;
        foreach (var sequence in development)
        {
            var role = validationSessions.Contains(sequence.Session) ? "validation" : "train";
            var folder = role == "validation" ? "val" : "train";
            var labels = CorpusJson.Read<SequenceLabels>(files.Resolve(sequence.LabelsFile));
            if (!labels.Reviewed || labels.Frames.IsDefaultOrEmpty)
                throw new InvalidDataException("O09 export requires reviewed labels: " + sequence.Id);
            var index = files.Index(sequence.IndexFile).ToDictionary(f => f.SampleId, StringComparer.Ordinal);
            foreach (var frameLabels in labels.Frames.OrderBy(f => f.SampleId, StringComparer.Ordinal))
            {
                if (frameLabels.CoordinateSpace != "image-normalized")
                    throw new InvalidDataException("YOLO export requires reviewed image-normalized labels; no calibration warp is inferred: " + frameLabels.SampleId);
                if (frameLabels.Balls.Any(b => b.Visibility is LabelVisibility.Occluded or LabelVisibility.Uncertain))
                    throw new InvalidDataException("YOLO export cannot turn ambiguous/occluded labels into background; resolve them before export: " + frameLabels.SampleId);
                if (!index.TryGetValue(frameLabels.SampleId, out var indexed) || indexed.Encoding != PixelEncoding.Jpeg)
                    throw new InvalidDataException("Every exported label needs an indexed JPEG frame: " + frameLabels.SampleId);
                var visible = frameLabels.Balls.Where(b => b.Visibility == LabelVisibility.Visible).ToArray();
                if (visible.Any(b => b.RadiusX is null || b.RadiusY is null))
                    throw new InvalidDataException("Every visible training ball needs independently reviewed X/Y radii: " + frameLabels.SampleId);

                totalBytes = checked(totalBytes + new FileInfo(files.Resolve(indexed.PixelFile)).Length);
                if (totalBytes > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("O09 exported image set exceeds the 2 GiB bound.");
                string stem = CorpusFiles.Hash(Encoding.UTF8.GetBytes(sequence.Id + "\n" + frameLabels.SampleId)).ToLowerInvariant();
                string imageRelative = $"images/{folder}/{stem}.jpg";
                string labelRelative = $"labels/{folder}/{stem}.txt";
                var yoloLines = visible.Select(ball => YoloBox(ball)).ToArray();
                string labelText = yoloLines.Length == 0 ? string.Empty : string.Join("\n", yoloLines) + "\n";
                prepared.Add(new(sequence.Id, sequence.Session, role, frameLabels.SampleId, indexed,
                    imageRelative, labelRelative, labelText, visible.Length));
            }
        }

        Directory.CreateDirectory(Path.Combine(target, "images", "train"));
        Directory.CreateDirectory(Path.Combine(target, "images", "val"));
        Directory.CreateDirectory(Path.Combine(target, "labels", "train"));
        Directory.CreateDirectory(Path.Combine(target, "labels", "val"));
        var exported = ImmutableArray.CreateBuilder<O09ExportSample>(prepared.Count);
        foreach (var sample in prepared)
        {
            var pixels = files.Verified(sample.Frame.PixelFile, sample.Frame.PixelSha256);
            string imagePath = Path.Combine(target, sample.ImageFile.Replace('/', Path.DirectorySeparatorChar));
            string labelPath = Path.Combine(target, sample.LabelFile.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(imagePath, pixels);
            File.WriteAllText(labelPath, sample.LabelText, new UTF8Encoding(false));
            exported.Add(new(sample.SequenceId, sample.Session, sample.Role, sample.SampleId,
                sample.ImageFile, sample.Frame.PixelSha256, sample.LabelFile, sample.VisibleBoxes));
        }
        File.WriteAllText(Path.Combine(target, "dataset.yaml"),
            "path: .\ntrain: images/train\nval: images/val\nnames:\n  0: cue\n  1: black\n  2: red\n  3: yellow\n  4: unknown\n", new UTF8Encoding(false));
        var report = new O09TrainingExportReport(1, manifest.Id, frozen.ManifestSha256, "Development", assignments,
            exported.ToImmutable(), Classes,
            "No augmentation is applied during export. Apply exposure changes up to plus/minus two stops, brightness/gamma/contrast, warm/cool white balance, saturation, directional/partial shadows, blind-like stripes, highlights, dim/bright-room variation, motion blur, slight defocus, compression/noise, overlap and partial occlusion to train sessions only. Keep validation and O01 Holdout pixels unchanged.",
            "O01 Holdout sessions and pixels are not copied or listed as training samples; evaluate a frozen candidate against them only after model and thresholds are fixed.");
        File.WriteAllText(Path.Combine(target, "o09-export.json"), CorpusJson.Write(report) + "\n", new UTF8Encoding(false));
        return report;
    }

    static string YoloBox(BallLabel ball)
    {
        var bounds = ball.Colour switch
        {
            "cue" => 0,
            "black" => 1,
            "red" => 2,
            "yellow" => 3,
            "unknown" => 4,
            _ => throw new InvalidDataException("Visible ball has unsupported O09 training class: " + ball.Colour)
        };
        double rx = ball.RadiusX!.Value, ry = ball.RadiusY!.Value;
        double left = Math.Clamp(ball.X - rx, 0, 1), right = Math.Clamp(ball.X + rx, 0, 1);
        double top = Math.Clamp(ball.Y - ry, 0, 1), bottom = Math.Clamp(ball.Y + ry, 0, 1);
        if (right <= left || bottom <= top) throw new InvalidDataException("Visible ball has an empty clipped training box: " + ball.Id);
        string N(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        return string.Join(' ', bounds.ToString(CultureInfo.InvariantCulture), N((left + right) / 2), N((top + bottom) / 2), N(right - left), N(bottom - top));
    }

    static string StableSessionHash(string corpusId, string session) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(corpusId + "\n" + session)));
}
