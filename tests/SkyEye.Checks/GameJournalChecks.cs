using System.Text.Json;
using SkyEye.GameState;
using SkyEye.Core.Application;

static class GameJournalChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-game-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameJournalStore(root);
            var empty = store.Load();
            var start = new GameInputEnvelope(Guid.NewGuid(), "match", "frame", DateTimeOffset.UnixEpoch,
                new StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3), Provenance: Provenance.PlayerConfirmed);
            var started = empty.Accept(start).Session;
            store.Save(empty, started);
            var loaded = new GameJournalStore(root).Load();
            Check(JsonSerializer.Serialize(loaded.State) == JsonSerializer.Serialize(started.State), "Restart rebuilds canonical state");
            Check(loaded.Accept(start).Outcome == InputOutcome.Duplicate, "Persistent input IDs cannot count twice after restart");
            var frame = loaded.Accept(start with { Id = Guid.NewGuid(), At = start.At.AddSeconds(1), Input = new StartFrame("rich") }).Session;
            store.Save(loaded, frame);
            Throws<IOException>(() => store.Save(started, frame));
            Throws<ArgumentException>(() => store.Save(frame, started));
            using (var competing = File.Open(Path.Combine(root, "game.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Throws<IOException>(() => store.Load());
            var path = Path.Combine(root, "game.json");
            var saved = File.ReadAllText(path);
            File.WriteAllText(path, saved.Replace("Rich", "Forged"));
            Throws<InvalidDataException>(() => store.Load());
            File.WriteAllText(path, saved[..(saved.Length / 2)]);
            Throws<InvalidDataException>(() => store.Load());
            File.WriteAllText(path, saved.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":99"));
            Throws<InvalidDataException>(() => store.Load());
            File.WriteAllText(path, saved);
            Check(new GameJournalStore(root).Load().State.Frame!.CurrentPlayer.Value == "rich", "Failed writes preserve recoverable accepted history");
            Console.WriteLine("PASS durable game journal: restart, duplicates, append-only/concurrent-owner fences, corruption/truncation/schema refusal");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
