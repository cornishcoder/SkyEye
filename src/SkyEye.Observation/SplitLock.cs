using System.Collections.Immutable;

namespace SkyEye.Observation;

public sealed record FrozenSequence(string Id, string Session, CorpusKind Kind, CorpusSplit Split,
    string SourceSha256, string IndexSha256, string LabelsSha256, ImmutableArray<string> PixelSha256);
public sealed record FrozenCorpus(int SchemaVersion, string CorpusId, string ManifestSha256, ImmutableArray<FrozenSequence> Sequences);
public static class SplitLock
{
    public static FrozenCorpus Freeze(CorpusManifest manifest, CorpusFiles files, string manifestPath)
    {
        CorpusValidation.Validate(manifest, files);
        return new(1, manifest.Id, CorpusFiles.Hash(File.ReadAllBytes(manifestPath)), manifest.Sequences.OrderBy(s => s.Id, StringComparer.Ordinal)
            .Select(s => new FrozenSequence(s.Id, s.Session, s.Kind, s.Split, s.Source.Sha256,
                CorpusFiles.Hash(File.ReadAllBytes(files.Resolve(s.IndexFile))), CorpusFiles.Hash(File.ReadAllBytes(files.Resolve(s.LabelsFile))),
                files.Index(s.IndexFile).Select(f => f.PixelSha256).ToImmutableArray())).ToImmutableArray());
    }
    public static void Verify(FrozenCorpus frozen, FrozenCorpus current)
    {
        if (frozen.SchemaVersion != 1 || CorpusJson.Write(frozen) != CorpusJson.Write(current))
            throw new InvalidDataException("Frozen split, source or labels changed; create a reviewed new corpus version.");
    }
}
