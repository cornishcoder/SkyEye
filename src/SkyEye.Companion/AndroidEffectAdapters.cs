using Android.Content;
using Android.OS;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Text.Json;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

public sealed class CameraCalibrationStore(Context context, Func<string> address) : ICalibrationStore
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
        var stored = context.GetSharedPreferences("skyeye", FileCreationMode.Private)!.GetString(Key(cameraIdentity), null);
        return stored is null ? null : JsonSerializer.Deserialize<Point[]>(stored, JsonDefaults.Options);
    }
    public void Save(string cameraIdentity, Point[] corners) => context.GetSharedPreferences("skyeye", FileCreationMode.Private)!.Edit()!
        .PutString(Key(cameraIdentity), JsonSerializer.Serialize(corners, JsonDefaults.Options))!.Apply();
}

public sealed class AndroidPortraitClipExporter(Context context) : IPortraitClipExporter
{
    public async Task ExportAsync(string input, string output, int rotation, CancellationToken cancellation)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Bridge.PortraitExporter? exporter = null; ExportListener? listener = null;
        try
        {
            await OnMainAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                listener = new(finished); exporter = new(); exporter.Start(context, input, output, rotation, listener);
            }).ConfigureAwait(false);
            await finished.Task.WaitAsync(TimeSpan.FromMinutes(20), cancellation).ConfigureAwait(false);
        }
        finally
        {
            // Media3 requires its creating looper for cancellation. Wait for it
            // before the library removes a failed/cancelled portrait part file.
            await OnMainAsync(() => { exporter?.Cancel(); exporter?.Dispose(); listener?.Dispose(); }).ConfigureAwait(false);
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
