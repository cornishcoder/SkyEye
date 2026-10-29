using Android.Content;
using Android.Provider;
using System.Globalization;
using Android.OS;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Text.Json;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion;

public sealed class CameraCalibrationStore(Context context, Func<string> address, Func<string>? geometry = null) : ICalibrationStore
{
    string Key(string identity)
    {
        var endpoint = address();
        // Retain the exact existing key for saved mounts, including URL casing.
        if (CameraEndpoint.Identity(endpoint) != identity) throw new InvalidOperationException("Camera changed while saving calibration.");
        return "calibration-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(endpoint)));
    }
    public Point[]? Load(string cameraIdentity)
    {
        var preferences = context.GetSharedPreferences("skyeye", FileCreationMode.Private)!;
        var key = Key(cameraIdentity);
        var storedGeometry = preferences.GetString(key + "-geometry", "1920x1080:fixed");
        if (geometry is not null && storedGeometry != geometry()) return null;
        var stored = preferences.GetString(key, null);
        return stored is null ? null : JsonSerializer.Deserialize<Point[]>(stored, JsonDefaults.Options);
    }
    public void Save(string cameraIdentity, Point[] corners) => context.GetSharedPreferences("skyeye", FileCreationMode.Private)!.Edit()!
        .PutString(Key(cameraIdentity), JsonSerializer.Serialize(corners, JsonDefaults.Options))!
        .PutString(Key(cameraIdentity) + "-geometry", geometry?.Invoke() ?? "1920x1080:fixed")!.Apply();
    public void Forget(string cameraIdentity) => context.GetSharedPreferences("skyeye", FileCreationMode.Private)!.Edit()!
        .Remove(Key(cameraIdentity))!.Remove(Key(cameraIdentity) + "-geometry")!.Apply();
}

public sealed class AndroidPortraitClipExporter(Context context) : IPortraitClipExporter, IClipGeometryPreparation
{
    readonly AndroidRecordedGeometry geometry = new(Path.Combine(context.FilesDir!.AbsolutePath, "recorded-geometry"));
    public async Task PrepareGeometryAsync(Clip clip, string verifiedSource, CancellationToken cancellation)
    {
        var journal = await geometry.AnalyzeAsync(verifiedSource, clip.Sha256, clip.RecordingOrigin, cancellation);
        // Safe provenance only; no file path, endpoint, token or table image.
        Android.Util.Log.Info("SkyEyeGeometry", JsonSerializer.Serialize(new { clipId = clip.Id, journal.Source,
            intervalCount = journal.Spans.Length, acceptedGeometrySeconds = journal.Spans.Where(s => s.Layout is not null).Sum(s => (s.EndUs - s.StartUs) / 1_000_000d),
            firstAccepted = journal.Spans.FirstOrDefault(s => s.Layout is not null) }, JsonDefaults.Options));
    }
    public async Task ExportAsync(string input, string output, int rotation, CancellationToken cancellation)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Bridge.PortraitExporter? exporter = null; ExportListener? listener = null;
        bool completed = false;
        try
        {
            await OnMainAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                listener = new(finished); exporter = new(); exporter.Start(context, input, output, rotation, listener);
            }).ConfigureAwait(false);
            await finished.Task.WaitAsync(TimeSpan.FromMinutes(20), cancellation).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            // Media3 requires its creating looper for cancellation. Wait for it
            // before the library removes a failed/cancelled portrait part file.
            await OnMainAsync(() => { if (!completed) exporter?.Cancel(); exporter?.Dispose(); listener?.Dispose(); }).ConfigureAwait(false);
        }
    }
    static Task OnMainAsync(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run() { try { work(); done.TrySetResult(); } catch (Exception e) { done.TrySetException(e); } }
        if (Looper.MyLooper() == Looper.MainLooper) Run();
        else { using var handler = new Handler(Looper.MainLooper!); if (!handler.Post(Run)) done.TrySetException(new IOException("Android media dispatcher is unavailable.")); }
        return done.Task;
    }
    sealed class ExportListener(TaskCompletionSource completion) : Java.Lang.Object, Bridge.PortraitExporter.IListener
    {
        public void OnComplete() => completion.TrySetResult();
        public void OnError(string? error) => completion.TrySetException(new IOException(error ?? "Portrait export failed."));
    }
}

/// <summary>Publishes an explicitly selected clip into shared Android video storage.</summary>
public sealed class AndroidGalleryClipPublisher(Context context)
{
    const string RelativePath = "Movies/SkyEye/";
    const string DisplayNameColumn = "_display_name";

    public async Task<string> PublishAsync(string sourcePath, Clip clip, CancellationToken cancellation)
    {
        ClipLibrary.Validate(clip);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The prepared clip is not available on this phone.", sourcePath);

        var name = DisplayName(clip);
        var collection = MediaStore.Video.Media.ExternalContentUri ?? throw new IOException("Android video storage is unavailable.");
        var resolver = context.ContentResolver ?? throw new IOException("Android media storage is unavailable.");

        // A stable, clip-specific name makes repeat taps idempotent and avoids
        // accumulating duplicate copies in the user's Photos library.
        using (var existing = resolver.Query(collection, ["_id"], $"{DisplayNameColumn} = ?", [name], null))
        {
            if (existing?.MoveToFirst() == true)
                return Android.Net.Uri.WithAppendedPath(collection, existing.GetLong(0).ToString(CultureInfo.InvariantCulture))?.ToString()
                    ?? throw new IOException("The existing Photos copy could not be opened.");
        }

        var values = new ContentValues();
        values.Put(MediaStore.IMediaColumns.DisplayName, name);
        values.Put(MediaStore.IMediaColumns.MimeType, "video/mp4");
        values.Put(MediaStore.IMediaColumns.RelativePath, RelativePath);
        values.Put(MediaStore.IMediaColumns.IsPending, 1);
        var uri = resolver.Insert(collection, values) ?? throw new IOException("Android could not create the Photos copy.");
        try
        {
            await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true))
            await using (var destination = resolver.OpenOutputStream(uri) ?? throw new IOException("Android could not open the Photos copy for writing."))
                await source.CopyToAsync(destination, 64 * 1024, cancellation).ConfigureAwait(false);

            cancellation.ThrowIfCancellationRequested();
            var published = new ContentValues();
            published.Put(MediaStore.IMediaColumns.IsPending, 0);
            if (resolver.Update(uri, published, null, null) != 1)
                throw new IOException("Android could not publish the completed Photos copy.");
            return uri.ToString() ?? throw new IOException("Android returned an invalid Photos address.");
        }
        catch
        {
            resolver.Delete(uri, null, null);
            throw;
        }
    }

    static string DisplayName(Clip clip)
    {
        var stem = new string(clip.Name.Select(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' ? character : '_').ToArray()).Trim();
        if (stem.Length > 64) stem = stem[..64].Trim();
        if (stem.Length == 0) stem = "Camera clip";
        return $"{clip.CreatedAt.LocalDateTime:yyyy-MM-dd HH-mm-ss} {stem} {clip.Id[..8]}.mp4";
    }
}
