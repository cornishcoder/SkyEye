using System.Collections.Concurrent;
using System.Text.Json;

namespace SkyEye.Core.Application;

public interface IClipTransfer : IDisposable
{
    Task<Clip[]> ListAsync(CancellationToken cancellation);
    Task DownloadAsync(Clip clip, string destination, Action<long> progress, CancellationToken cancellation);
}
public interface IPortraitClipExporter { Task ExportAsync(string input, string output, int rotation, CancellationToken cancellation); }
public enum ClipTransferStage { Downloading, Preparing, Available }
public sealed record ClipTransferProgress(ClipTransferStage Stage, long Bytes, long TotalBytes);
public sealed record LocalClip(Clip Clip, string VideoPath);

/// <summary>Owns the existing local clip layout and transfer/publication workflow.
/// The supplied native transfer is already bound to one camera. Incomplete source
/// files may resume; a converted clip is visible only with a valid sidecar.</summary>
public sealed class ClipLibrary
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> writers = new(StringComparer.OrdinalIgnoreCase);
    readonly string directory;
    readonly SemaphoreSlim writer;
    public ClipLibrary(string directory)
    {
        this.directory = Path.GetFullPath(directory); Directory.CreateDirectory(this.directory);
        writer = writers.GetOrAdd(this.directory, _ => new(1));
    }
    public static void Validate(Clip clip)
    {
        if (!Guid.TryParseExact(clip.Id, "N", out _) || clip.Bytes <= 0 || !double.IsFinite(clip.Duration) || clip.Duration <= 0
            || clip.Sha256 is not { Length: 64 } || !clip.Sha256.All(Uri.IsHexDigit) || clip.PortraitRotation is not (0 or 90 or 180 or 270))
            throw new ArgumentException("Invalid clip metadata.");
    }
    string Video(string id) => Path.Combine(directory, id + ".mp4");
    public bool Available(Clip clip)
    {
        try
        {
            Validate(clip);
            var saved = JsonSerializer.Deserialize<Clip>(File.ReadAllText(Path.Combine(directory, clip.Id + ".json")), JsonDefaults.Options);
            return saved is not null && saved.Id == clip.Id && saved.Sha256 == clip.Sha256 && saved.Bytes == clip.Bytes && saved.PortraitRotation == clip.PortraitRotation
                && new FileInfo(Video(clip.Id)) is { Exists: true, Length: > 0 };
        }
        catch { return false; }
    }
    public LocalClip[] Local()
    {
        var clips = new List<LocalClip>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderDescending())
        {
            try
            {
                var clip = JsonSerializer.Deserialize<Clip>(File.ReadAllText(file), JsonDefaults.Options);
                if (clip is not null && Path.GetFileName(file) == clip.Id + ".json" && Available(clip)) clips.Add(new(clip, Video(clip.Id)));
            }
            catch { /* Ignore an incomplete/unreadable entry, preserving other local clips. */ }
        }
        return clips.ToArray();
    }
    public async Task<LocalClip> DownloadAsync(Clip clip, IClipTransfer transfer, IPortraitClipExporter exporter, Action<ClipTransferProgress>? progress = null, CancellationToken cancellation = default)
    {
        Validate(clip); cancellation.ThrowIfCancellationRequested();
        await writer.WaitAsync(cancellation).ConfigureAwait(false);
        string master = Path.Combine(directory, clip.Id + ".source.part"), portrait = Path.Combine(directory, clip.Id + ".portrait.part"), metadata = Path.Combine(directory, clip.Id + ".json.part");
        try
        {
            if (Available(clip)) return new(clip, Video(clip.Id));
            progress?.Invoke(new(ClipTransferStage.Downloading, 0, clip.Bytes));
            await transfer.DownloadAsync(clip, master, bytes => progress?.Invoke(new(ClipTransferStage.Downloading, bytes, clip.Bytes)), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(new(ClipTransferStage.Preparing, clip.Bytes, clip.Bytes));
            if (File.Exists(portrait)) File.Delete(portrait);
            await exporter.ExportAsync(master, portrait, clip.PortraitRotation, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (new FileInfo(portrait) is not { Exists: true, Length: > 0 }) throw new IOException("Portrait export produced no video.");
            await File.WriteAllTextAsync(metadata, JsonSerializer.Serialize(clip, JsonDefaults.Options), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            // Publish video first, then metadata: enumeration never offers half an export.
            File.Move(portrait, Video(clip.Id), overwrite: true);
            File.Move(metadata, Path.Combine(directory, clip.Id + ".json"), overwrite: true);
            File.Delete(master);
            progress?.Invoke(new(ClipTransferStage.Available, clip.Bytes, clip.Bytes));
            return new(clip, Video(clip.Id));
        }
        finally
        {
            try { if (File.Exists(portrait)) File.Delete(portrait); if (File.Exists(metadata)) File.Delete(metadata); }
            finally { writer.Release(); }
        }
    }
}
