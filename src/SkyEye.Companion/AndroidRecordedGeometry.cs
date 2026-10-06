using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

public sealed class AndroidRecordedGeometry(string directory)
{
    readonly RecordedGeometryStore store = new(directory);
    readonly SemaphoreSlim writer = new(1);
    public async Task<RecordedGeometryJournal> AnalyzeAsync(string path, string expectedSha256, ClipRecordingOrigin? origin, CancellationToken cancellation)
    {
        await writer.WaitAsync(cancellation);
        try
        {
            await using var input = File.OpenRead(path);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation)); input.Close();
            if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new IOException("Recorded geometry source checksum changed.");
            return await Task.Run(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                using var reader = new Bridge.RecordedFrameReader(path);
                try
                {
                _ = reader.Frame(0);
                using var initial = JsonDocument.Parse(reader.Metadata()!); var metadata = initial.RootElement;
                int width = metadata.GetProperty("frameWidth").GetInt32(), height = metadata.GetProperty("frameHeight").GetInt32();
                int rotation = metadata.GetProperty("rotation").GetInt32(); long duration = metadata.GetProperty("durationUs").GetInt64();
                if (origin is not null && (origin.Width != metadata.GetProperty("encodedWidth").GetInt32()
                    || origin.Height != metadata.GetProperty("encodedHeight").GetInt32() || origin.Rotation != rotation))
                    throw new IOException("Recorded geometry orientation differs from its recording provenance.");
                var source = new RecordedGeometrySource(hash, origin is null ? RecordedGeometryClock.ClipRelative : RecordedGeometryClock.CameraRecording,
                    origin?.Timeline ?? hash, origin?.StartUs ?? 0, duration, width, height, rotation);
                source.Validate();
                if (store.Load(source) is { } previous) return previous;
                var detector = new VisionAnalyzer(); var samples = new List<RecordedPocketSample>();
                foreach (long requested in Enumerable.Range(0, (int)((duration - 1) / 2_000_000) + 1).Select(i => i * 2_000_000L).Append(duration - 1))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var jpeg = reader.Frame(requested) ?? throw new IOException("Recorded frame unavailable.");
                    using var measured = JsonDocument.Parse(reader.Metadata()!); var frame = measured.RootElement;
                    if (frame.GetProperty("frameWidth").GetInt32() != width || frame.GetProperty("frameHeight").GetInt32() != height)
                        throw new IOException("Recorded frame dimensions changed within the source.");
                    long time = checked(source.StartUs + frame.GetProperty("actualTimeUs").GetInt64());
                    if (samples.Count > 0 && time == samples[^1].SourceTimeUs) continue;
                    var pockets = detector.FindPockets(jpeg, cancellation);
                    samples.Add(new(time, pockets));
                }
                var journal = RecordedGeometry.Build(source, samples);
                await store.SaveAsync(journal, cancellation);
                return journal;
                }
                finally { reader.Close(); }
            }, cancellation);
        }
        finally { writer.Release(); }
    }
}
