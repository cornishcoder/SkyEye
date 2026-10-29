using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

/// <summary>Credentials remain native. Freeze the endpoint/token for this request,
/// refresh its non-secret scope, and prove authority before sending a save.</summary>
public sealed class CameraClipSaveEffect(CameraClient current, Action<Clip>? saved = null) : IClipSaveEffect
{
    public async Task<Clip> SaveAsync(Guid operationId, SaveClip command, CancellationToken cancellation)
    {
        var address = current.Address; var token = current.HostToken;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(address))
            throw new ClipSaveRefusedException("Connect and become host before saving a clip.");
        using var request = new CameraClient(); request.Connect(address); request.HostToken = token;
        bool saveSent = false;
        try
        {
            var status = await request.StatusAsync(cancellation).ConfigureAwait(false);
            if (request.SaveScope != command.Scope)
                throw new ClipSaveRefusedException("Camera session or host changed. Refresh status before trying a new request.");
            var range = command.SourceRange;
            if (range is null)
            {
                long endUs = status.TryGetProperty("lastFrameSourceTimeUs", out var frameTime)
                    ? frameTime.GetInt64() : status.GetProperty("sourceTimeUs").GetInt64();
                string timeline = status.GetProperty("timeline").GetString() ?? "";
                if (endUs <= 0 || !Guid.TryParseExact(timeline, "N", out _))
                    throw new ClipSaveRefusedException("Camera has no recorded frame timeline to save yet.");
                range = new(timeline, Math.Max(1, endUs - command.Seconds * 1_000_000L), endUs);
            }
            await request.PostAsync("host/heartbeat", new { }, cancellation).ConfigureAwait(false);
            saveSent = true;
            var result = await request.PostAsync("clips/save-range", new
            {
                commandId = operationId.ToString("N"), timeline = range.Timeline,
                startUs = range.StartUs, endUs = range.EndUs
            }, cancellation).ConfigureAwait(false);
            var clip=result.Deserialize<Clip>(JsonDefaults.Options) ?? throw new IOException("Missing clip acknowledgement.");
            ClipLibrary.Validate(clip);saved?.Invoke(clip);return clip;
        }
        catch (CameraRequestException e) when (!saveSent || e.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden || KnownPreSaveRefusal(e))
        {
            throw new ClipSaveRefusedException("Camera refused the save. Refresh its status before trying a new request.");
        }
    }
    static bool KnownPreSaveRefusal(CameraRequestException e) => e.StatusCode == System.Net.HttpStatusCode.Conflict && e.Message is
        "No recording history is available yet." or
        "Camera storage is low. Free device space before saving more." or
        "Camera is still finalising replay footage. Try saving again shortly." or
        "The camera recording changed or the requested clip range is no longer recent." or
        "The requested clip range is not fully available in camera history." or
        "The requested clip range contains a recording gap.";
}
