using System.Collections.Immutable;
using System.Text.Json;

namespace SkyEye.Observation;

/// <summary>Imports bounded paired snapshots as unreviewed development material.
/// Human labels and a session split must be supplied before O08 evaluation.</summary>
public static class VerificationCaptureImport
{
    public static string Import(string captureRoot, string outputRoot)
    {
        if (Directory.Exists(outputRoot)) throw new IOException("Choose a new corpus output directory to preserve earlier evidence.");
        var input = new CorpusFiles(captureRoot); var output = new CorpusFiles(outputRoot);
        var receipts = Directory.EnumerateFiles(captureRoot, "*.json", SearchOption.AllDirectories).Take(121).ToArray();
        if (receipts.Length is < 1 or > 120) throw new InvalidDataException("Import 1 to 120 bounded captured samples.");
        var records = receipts.Select(p =>
        {
            if (new FileInfo(p).Length > 512 * 1024) throw new InvalidDataException("Capture metadata exceeds bounds.");
            using var json = JsonDocument.Parse(File.ReadAllBytes(p));
            var frame = json.RootElement.GetProperty("indexedFrame").Deserialize<IndexedFrame>(CorpusJson.Options) ?? throw new InvalidDataException("Missing paired receipt.");
            var path = Path.GetRelativePath(captureRoot, Path.Combine(Path.GetDirectoryName(p)!, frame.PixelFile));
            var pixels = input.Verified(path, frame.PixelSha256); PixelDimensions.Verify(frame, pixels);
            return (Frame: frame with { PixelFile = path.Replace('\\', '/') }, Pixels: pixels, ReceiptPath: p);
        }).OrderBy(p => p.Frame.SampleId, StringComparer.Ordinal).ToArray();
        Directory.CreateDirectory(outputRoot); var sequences = ImmutableArray.CreateBuilder<CorpusSequence>();
        var compact = new JsonSerializerOptions(CorpusJson.Options) { WriteIndented = false };
        foreach (var group in records.GroupBy(p => Path.GetDirectoryName(p.Frame.PixelFile)!))
        {
            var frames = group.OrderBy(p => p.Frame.SourceTimeUs).ToArray(); var first = frames[0].Frame;
            foreach (var record in frames)
            {
                var dest = output.Resolve(record.Frame.PixelFile); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.WriteAllBytes(dest, record.Pixels);
                File.Copy(record.ReceiptPath, output.Resolve(Path.ChangeExtension(record.Frame.PixelFile, ".receipt.json")));
            }
            var id = Path.GetFileName(group.Key); var index = $"{id}/frames.jsonl"; var labels = $"{id}/labels.json";
            File.WriteAllLines(output.Resolve(index), frames.Select(p => JsonSerializer.Serialize(p.Frame, compact)));
            File.WriteAllText(output.Resolve(labels), CorpusJson.Write(new SequenceLabels(1, id, "pending independent human annotation", first.Scope.Calibration, false,
                frames.Select(p => new FrameLabels(p.Frame.SampleId, p.Frame.PixelSha256, "image-normalized", [], "Unlabelled; do not copy detector centres into truth.")).ToImmutableArray(), [])));
            sequences.Add(new(id, first.Scope.Timeline, CorpusKind.Real, CorpusSplit.Development, SequencePurpose.StillRegression,
                new("mounted-camera", "Paired decoded development snapshots; not original source clips", first.PixelFile, first.PixelSha256, first.Width, first.Height,
                    SourceClock.RecordingMicroseconds, first.Scope.Calibration), index, labels,
                "Unreviewed O08 material. Label conditions/centres/visibility before selecting StillVerification or NegativeVerification; related windows share the recording timeline session.", ["o08", "unreviewed"]));
        }
        var manifest = new CorpusManifest(1, "o08-development-" + Guid.NewGuid().ToString("N"), sequences.ToImmutable());
        var manifestPath = output.Resolve("manifest.json"); File.WriteAllText(manifestPath, CorpusJson.Write(manifest));
        CorpusValidation.Validate(manifest, output);
        return manifestPath;
    }
}
