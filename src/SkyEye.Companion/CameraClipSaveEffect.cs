using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

/// <summary>Credentials remain native. Freeze the endpoint/token for this request,
/// refresh its non-secret scope, and prove authority before sending a save.</summary>
public sealed class CameraClipSaveEffect(CameraClient current) : IClipSaveEffect
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
            await request.StatusAsync(cancellation).ConfigureAwait(false);
            if (request.SaveScope != command.Scope)
                throw new ClipSaveRefusedException("Camera session or host changed. Refresh status before trying a new request.");
            await request.PostAsync("host/heartbeat", new { }, cancellation).ConfigureAwait(false);
            saveSent = true;
            var result = await request.PostAsync("clips/save", new { commandId = operationId.ToString("N"), seconds = command.Seconds }, cancellation).ConfigureAwait(false);
            return result.Deserialize<Clip>(JsonDefaults.Options) ?? throw new IOException("Missing clip acknowledgement.");
        }
        catch (CameraRequestException e) when (!saveSent || e.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden || KnownPreSaveRefusal(e))
        {
            throw new ClipSaveRefusedException("Camera refused the save. Refresh its status before trying a new request.");
        }
    }
    static bool KnownPreSaveRefusal(CameraRequestException e) => e.StatusCode == System.Net.HttpStatusCode.Conflict && e.Message is
        "No recording history is available yet." or
        "Camera storage is low. Free device space before saving more." or
        "Camera is still finalising replay footage. Try saving again shortly.";
}
