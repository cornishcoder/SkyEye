using System.Collections.Concurrent;
using System.Text.Json;

namespace SkyEye.Core.Application;

public interface IClipSaveEffect
{
    Task<Clip> SaveAsync(Guid operationId, SaveClip command, CancellationToken cancellation);
}

// Only explicit refusals known to precede the save use this exception. A transport
// failure or malformed acknowledgement cannot establish that no clip was created.
public sealed class ClipSaveRefusedException(string safeMessage) : Exception(safeMessage);

/// <summary>Durable, fail-closed replay protection for companion-originated saves.
/// An interrupted pending operation remains uncertain and is never sent again.
/// This does not make a camera response lost during a crash recoverable.</summary>
public sealed class ClipSaveExecutor
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> writers = new(StringComparer.OrdinalIgnoreCase);
    readonly string directory;
    readonly IClipSaveEffect effect;
    readonly SemaphoreSlim serial;
    readonly int capacity;
    public ClipSaveExecutor(string directory, IClipSaveEffect effect, int capacity = 10000)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.directory = Path.GetFullPath(directory); this.effect = effect; this.capacity = capacity;
        Directory.CreateDirectory(this.directory);
        serial = writers.GetOrAdd(this.directory, _ => new(1));
    }

    // Also prevents a preference command from reusing a durable save operation ID.
    public CommandReceipt? FindPrevious(CommandEnvelope envelope, long currentVersion)
        => ReadPrevious(envelope, currentVersion);

    public async Task<CommandReceipt> ExecuteAsync(CommandEnvelope envelope, long decisionVersion, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.OperationId == Guid.Empty || envelope.ExpectedVersion < 0 || decisionVersion < 0 || !Enum.IsDefined(envelope.Origin))
            return new(envelope.OperationId, CommandOutcome.Rejected, decisionVersion, "Invalid save command envelope.");
        await serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            // Cross-process writers cannot race the durable reservation. The OS
            // releases this handle on process death; pending JSON remains as proof.
            using var ownership = TakeOwnership();
            if (ownership is null) return Receipt(CommandOutcome.Uncertain, "Another save writer is active. No new save was sent.");
            var previous = ReadPrevious(envelope, decisionVersion);
            if (previous is not null) return previous;
            if (envelope.Command is not SaveClip command || !Valid(command))
                return Receipt(CommandOutcome.Rejected, "Choose a supported duration and refresh camera host status.");
            if (Directory.EnumerateFiles(directory, "*.json").Take(capacity).Count() >= capacity)
                return Receipt(CommandOutcome.Failed, "The save journal is full. No save was sent.");
            var record = new SaveRecord(1, envelope.OperationId, envelope.Origin, envelope.ExpectedVersion, command, decisionVersion, null);
            try { Write(record, replace: false); }
            catch (IOException) { return Receipt(CommandOutcome.Failed, "Could not record this operation. No save was sent."); }
            catch (UnauthorizedAccessException) { return Receipt(CommandOutcome.Failed, "Could not record this operation. No save was sent."); }
            CommandReceipt result;
            if (envelope.ExpectedVersion != decisionVersion)
                result = Receipt(CommandOutcome.Stale, "The app changed. Use its current state before trying a new request.");
            else
            {
                try
                {
                    var clip = await effect.SaveAsync(envelope.OperationId, command, cancellation).ConfigureAwait(false);
                    result = ValidClip(clip, command.Seconds)
                        ? Receipt(CommandOutcome.Applied, $"Saved {clip.Duration:0.0} seconds on camera.") with { SavedClip = clip }
                        : Receipt(CommandOutcome.Uncertain, "The camera returned an invalid save acknowledgement. Check its clips; this request will not be repeated.");
                }
                catch (ClipSaveRefusedException e) { result = Receipt(CommandOutcome.Failed, e.Message); }
                catch (Exception)
                {
                    // Includes cancellation after reservation. It can race a remote
                    // commit, so neither automatic retry nor a success claim is safe.
                    result = Receipt(CommandOutcome.Uncertain, "The save result is unknown. Check camera clips; this request will not be repeated.");
                }
            }
            try { Write(record with { Receipt = result }, replace: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Receipt(CommandOutcome.Uncertain, "Could not retain the save result. Check camera clips; this request will not be repeated.");
            }
            return result;
            CommandReceipt Receipt(CommandOutcome outcome, string message) => new(envelope.OperationId, outcome, decisionVersion, message);
        }
        finally { serial.Release(); }
    }

    FileStream? TakeOwnership()
    {
        try { return new(Path.Combine(directory, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    CommandReceipt? ReadPrevious(CommandEnvelope envelope, long currentVersion)
    {
        var path = PathFor(envelope.OperationId);
        if (!File.Exists(path)) return null;
        try
        {
            if (new FileInfo(path).Length > 32768) return Unknown();
            var record = JsonSerializer.Deserialize<SaveRecord>(File.ReadAllText(path), JsonDefaults.Options);
            if (record is null || record.Schema != 1 || record.OperationId != envelope.OperationId || record.Command is null) return Unknown();
            if (record.Origin != envelope.Origin || record.ExpectedVersion != envelope.ExpectedVersion || record.Command != envelope.Command)
                return new(envelope.OperationId, CommandOutcome.Conflict, currentVersion, "Operation ID was already used for a different request.");
            if (record.Receipt is null) return Unknown(record.DecisionVersion);
            var receipt = record.Receipt;
            if (receipt.OperationId != envelope.OperationId || !Enum.IsDefined(receipt.Outcome) || receipt.DecisionVersion != record.DecisionVersion
                || receipt.Outcome == CommandOutcome.Applied && !ValidClip(receipt.SavedClip, record.Command.Seconds)) return Unknown(record.DecisionVersion);
            return receipt with { Duplicate = true };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return Unknown(); }
        CommandReceipt Unknown(long? version = null) => new(envelope.OperationId, CommandOutcome.Uncertain, version ?? currentVersion,
            "An interrupted or unreadable save has an unknown result. Check camera clips; this request will not be repeated.", Duplicate: true);
    }
    string PathFor(Guid operationId) => Path.Combine(directory, operationId.ToString("N") + ".json");
    void Write(SaveRecord record, bool replace)
    {
        var final = PathFor(record.OperationId);
        var temporary = final + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, record, JsonDefaults.Options); file.Flush(flushToDisk: true);
            }
            File.Move(temporary, final, overwrite: replace);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    static bool Valid(SaveClip command) => ReplaySelection.Durations.Contains(command.Seconds) && command.Scope is { } scope
        && scope.CameraIdentity is { Length: 64 } identity && identity.All(Uri.IsHexDigit)
        && Guid.TryParseExact(scope.SessionId, "N", out _) && scope.HostGeneration > 0 && scope.CaptureRevision >= 0;
    static bool ValidClip(Clip? clip, int seconds) => clip is not null && Guid.TryParseExact(clip.Id, "N", out _)
        && clip.Name is { Length: > 0 and <= 200 } && double.IsFinite(clip.Duration) && clip.Duration > 0 && clip.Duration <= seconds + 2
        && clip.Bytes > 0 && clip.Sha256 is { Length: 64 } hash && hash.All(Uri.IsHexDigit);
    sealed record SaveRecord(int Schema, Guid OperationId, CommandOrigin Origin, long ExpectedVersion, SaveClip Command, long DecisionVersion, CommandReceipt? Receipt);
}
