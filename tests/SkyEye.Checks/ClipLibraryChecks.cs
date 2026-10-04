using SkyEye.Core;
using SkyEye.Core.Application;
using System.Security.Cryptography;

static class ClipLibraryChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "skyeye-library-" + Guid.NewGuid().ToString("N"));
        byte[] source = Enumerable.Range(0, 128).Select(x => (byte)x).ToArray();
        var clip = new Clip(Guid.NewGuid().ToString("N"), "Fixture", 30, source.Length, Convert.ToHexString(SHA256.HashData(source)), DateTimeOffset.UtcNow);
        var library = new ClipLibrary(root); using var transfer = new Transfer(source); var exporter = new Exporter();
        try
        {
            var stages = new List<ClipTransferStage>();
            var saved = await library.DownloadAsync(clip, transfer, exporter, value => stages.Add(value.Stage));
            Check(library.Available(clip) && library.Local().Single().VideoPath == saved.VideoPath && exporter.Rotation == 90, "verified export publishes existing mp4/json layout");
            Check(stages.Contains(ClipTransferStage.Downloading) && stages.Contains(ClipTransferStage.Preparing) && stages.Last() == ClipTransferStage.Available, "progress describes actual application stages");
            await new ClipLibrary(root).DownloadAsync(clip, transfer, exporter);
            Check(transfer.Downloads == 1 && exporter.Exports == 1, "restart and duplicate completed download do no transfer/export");
            string corrupt = Path.Combine(root, "corrupt.json"); await File.WriteAllTextAsync(corrupt, "{");
            Check(library.Local().Length == 1, "corrupt sibling entry does not hide valid clip");
            var second = clip with { Id = Guid.NewGuid().ToString("N") };
            exporter.Fail = true;
            try { await library.DownloadAsync(second, transfer, exporter); throw new Exception("Expected export failure"); } catch (IOException) { }
            Check(!library.Available(second) && library.Local().Length == 1 && File.Exists(Path.Combine(root, second.Id + ".source.part"))
                && !File.Exists(Path.Combine(root, second.Id + ".portrait.part")), "failed export keeps verified source for retry, removes incomplete portrait and publishes no clip");
            exporter.Fail = false; await library.DownloadAsync(second, transfer, exporter);
            Check(library.Available(second), "explicit retry can recover failed conversion");
            var third = clip with { Id = Guid.NewGuid().ToString("N") };
            await File.WriteAllTextAsync(Path.Combine(root, third.Id + ".mp4"), "interrupted publication without sidecar");
            Check(library.Local().Length == 2 && !library.Available(third), "orphan video is not offered as a complete download");
            using var cancellation = new CancellationTokenSource();
            try
            {
                await library.DownloadAsync(third, transfer, exporter, value => { if (value.Stage == ClipTransferStage.Preparing) cancellation.Cancel(); }, cancellation.Token);
                throw new Exception("Expected cancellation");
            }
            catch (OperationCanceledException) { }
            Check(!library.Available(third) && !File.Exists(Path.Combine(root, third.Id + ".portrait.part")), "cancelled conversion cannot publish a local clip");
            await library.DownloadAsync(third, transfer, exporter); Check(library.Available(third), "cancelled verified-source retry recovers");
            var fourth = clip with { Id = Guid.NewGuid().ToString("N") };
            exporter.Hold(); var one = library.DownloadAsync(fourth, transfer, exporter); await exporter.Entered!.Task;
            int count = transfer.Downloads; var two = new ClipLibrary(root).DownloadAsync(fourth, transfer, exporter);
            Check(!two.IsCompleted && transfer.Downloads == count, "activity replacements serialize same directory publication");
            exporter.Release(); await Task.WhenAll(one, two); Check(transfer.Downloads == count, "concurrent duplicate sees completed artifact without redownload");
            var fifth = clip with { Id = Guid.NewGuid().ToString("N") }; exporter.Hold();
            using var duringExport = new CancellationTokenSource(); var exporting = library.DownloadAsync(fifth, transfer, exporter, cancellation: duringExport.Token);
            await exporter.Entered!.Task; duringExport.Cancel();
            try { await exporting; throw new Exception("Expected export cancellation"); } catch (OperationCanceledException) { }
            Check(!library.Available(fifth) && !File.Exists(Path.Combine(root, fifth.Id + ".portrait.part")), "cancellation during conversion removes incomplete portrait before releasing writer");
            exporter.Release(); await library.DownloadAsync(fifth, transfer, exporter);
            try { await library.DownloadAsync(clip with { Id = "../escape" }, transfer, exporter); throw new Exception("Expected metadata rejection"); } catch (ArgumentException) { }
            Check(!Directory.EnumerateFiles(root, "*.part").Any(), "successful transfers clean bounded part files");
            Console.WriteLine("PASS extracted clip library: verified publication, legacy paths, duplicate/restart, corrupt sibling, export failure, cancellation and serialized retries");
        }
        finally { if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected fixture root"); Directory.Delete(root, true); }
    }
    sealed class Transfer(byte[] source) : IClipTransfer
    {
        public int Downloads;
        public Task<Clip[]> ListAsync(CancellationToken cancellation) => Task.FromResult(Array.Empty<Clip>());
        public async Task DownloadAsync(Clip clip, string path, Action<long> progress, CancellationToken cancellation)
        {
            Downloads++; await File.WriteAllBytesAsync(path, source, cancellation); progress(source.Length);
        }
        public void Dispose() { }
    }
    sealed class Exporter : IPortraitClipExporter
    {
        public bool Fail; public int Rotation, Exports; public TaskCompletionSource? Entered; TaskCompletionSource? release;
        public void Hold() { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public void Release() => release!.TrySetResult();
        public async Task ExportAsync(string input, string output, int rotation, CancellationToken cancellation)
        {
            Exports++; Rotation = rotation; await File.WriteAllTextAsync(output, "fixture portrait", cancellation);
            if (Fail) throw new IOException("fixture encoder failure");
            if (release is { } held) { Entered!.TrySetResult(); await held.Task.WaitAsync(cancellation); release = null; }
        }
    }
}
