using System.Security.Cryptography;

namespace SkyEye.Observation;

/// <summary>Offline, explicitly rooted corpus assets. There is no live transport or decoder here.</summary>
public sealed class CorpusFiles
{
    readonly string root;
    public CorpusFiles(string root) => this.root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    public string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Use a relative corpus asset path.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Asset leaves the corpus root.");
        return path;
    }
    public byte[] Verified(string relative, string expectedHash)
    {
        if (!IsHash(expectedHash)) throw new InvalidDataException("Expected a SHA-256 asset fingerprint.");
        var path = Resolve(relative);
        if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Frame/asset exceeds offline read bound.");
        var bytes = File.ReadAllBytes(path);
        if (!Hash(bytes).Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Asset checksum mismatch: " + relative);
        return bytes;
    }
    public IEnumerable<IndexedFrame> Index(string relative)
    {
        int count = 0;
        foreach (var line in File.ReadLines(Resolve(relative)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (++count > 100000 || line.Length > 16384) throw new InvalidDataException("Frame index exceeds bounds.");
            yield return System.Text.Json.JsonSerializer.Deserialize<IndexedFrame>(line, CorpusJson.Options) ?? throw new InvalidDataException("Empty indexed frame.");
        }
    }
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    public static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 500 && !value.Any(char.IsControl);
}
