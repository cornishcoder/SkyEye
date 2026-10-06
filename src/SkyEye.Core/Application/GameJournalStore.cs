using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.GameState;

namespace SkyEye.Core.Application;

public interface IGameJournal
{
    GameSession Load();
    void Save(GameSession previous, GameSession next);
}

/// <summary>Companion-owned journal I/O, outside the pure domain. Fail closed on invalid history.</summary>
public sealed class GameJournalStore(string directory) : IGameJournal
{
    const int MaximumBytes = 16 * 1024 * 1024;
    readonly object gate = new();
    readonly string root = Path.GetFullPath(directory);
    sealed record Document(int SchemaVersion, string Sha256, ImmutableArray<EventEnvelope> History);

    public GameSession Load()
    {
        lock (gate) { using var owner = Acquire(); return Read(); }
    }
    public void Save(GameSession previous, GameSession next)
    {
        lock (gate)
        {
            using var owner = Acquire();
            var current = Read();
            if (!Same(current.History, previous.History)) throw new IOException("Game journal changed in another owner; reload before applying evidence.");
            if (next.History.Length < previous.History.Length || !Same(next.History.Take(previous.History.Length).ToImmutableArray(), previous.History))
                throw new ArgumentException("Game history must be append-only.");
            if (Same(previous.History, next.History)) return;
            var history = JsonSerializer.SerializeToUtf8Bytes(next.History);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document(1, Convert.ToHexString(SHA256.HashData(history)), next.History));
            if (bytes.Length > MaximumBytes) throw new IOException("Game journal capacity reached.");
            var target = Path.Combine(root, "game.json");
            var temporary = Path.Combine(root, "game-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(bytes); file.Flush(flushToDisk: true); }
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    FileStream Acquire()
    {
        Directory.CreateDirectory(root);
        return new(Path.Combine(root, "game.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    GameSession Read()
    {
        var path = Path.Combine(root, "game.json");
        if (!File.Exists(path)) return GameSession.Empty;
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Game journal exceeds capacity.");
        try
        {
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(path));
            if (document is null || document.SchemaVersion != 1 || document.History.IsDefault || document.History.Length > 10000
                || document.Sha256 != Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document.History))))
                throw new InvalidDataException("Game journal schema or checksum is invalid.");
            return GameSession.Restore(document.History);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw new InvalidDataException("Game journal cannot be reconstructed; preserve it for repair.", e); }
    }
    static bool Same(ImmutableArray<EventEnvelope> first, ImmutableArray<EventEnvelope> second)
        => JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);
}
