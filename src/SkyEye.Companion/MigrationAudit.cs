using Android.Content;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SkyEye.Companion;

// Opt-in, read-only migration evidence. Never emit filenames, addresses, tokens,
// calibration coordinates, encrypted envelopes or the decrypted personal key.
internal static class MigrationAudit
{
    [Conditional("SKYEYE_MIGRATION_AUDIT")]
    public static void Report(Context context)
    {
        try
        {
            var settings = context.GetSharedPreferences("skyeye", FileCreationMode.Private)!;
            var calibrationKeys = settings.All!.Keys.Where(k => k.StartsWith("calibration-", StringComparison.Ordinal)).Order().ToArray();
            var envelope = context.GetSharedPreferences("secrets", FileCreationMode.Private)!.GetString("openai", null);
            bool? keyReadable;
            try { keyReadable = !string.IsNullOrWhiteSpace(SecretStore.Get(context, "openai")); }
            catch { keyReadable = null; }
            var clips = Inventory(Path.Combine(context.FilesDir!.AbsolutePath, "clips"));
            var saves = Inventory(Path.Combine(context.FilesDir.AbsolutePath, "save-operations"));
            Android.Util.Log.Info("SkyEyeMigration", JsonSerializer.Serialize(new
            {
                package = context.PackageName, uid = Android.OS.Process.MyUid(),
                cameraHash = Hash(settings.GetString("camera", "")!),
                calibrationCount = calibrationKeys.Length,
                calibrationHash = Hash(string.Join("\n", calibrationKeys.Select(k => k + ":" + Hash(settings.GetString(k, "")!)))),
                keyStored = envelope is not null, keyReadable,
                keyEnvelopeHash = envelope is null ? null : Hash(envelope),
                clipFiles = clips.Count, clipHash = clips.Hash,
                saveFiles = saves.Count, saveHash = saves.Hash
            }));
        }
        catch (Exception error) { Android.Util.Log.Warn("SkyEyeMigration", "Inventory unavailable: " + error.GetType().Name); }
    }

    static (int Count, string Hash) Inventory(string directory)
    {
        var files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order().ToArray() : [];
        var entries = new List<string>();
        foreach (var file in files)
        {
            using var input = File.OpenRead(file);
            entries.Add(Path.GetRelativePath(directory, file) + ":" + input.Length + ":" + Convert.ToHexString(SHA256.HashData(input)));
        }
        return (files.Length, Hash(string.Join("\n", entries)));
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
