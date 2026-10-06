using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    CancellationTokenSource? tvDeliveryLifetime;
    string? tvDeliverySource;
    bool tvDeliveryRequested;
    string BackgroundTvUrl => BackgroundTvService.Instance is { Ready: true } service ? service.Url : "";
    void EnsureBackgroundTv()
    {
        if (disposed || TvReplaySelected || effects.Latest is not { } view || !CanCastLive) { StopBackgroundTv(); return; }
        var needed = TvCorrectionRequested || app.State.Presentation.Tv.Mode is ShotDisplayMode.Cycling or ShotDisplayMode.Pinned;
        if (!needed) { StopBackgroundTv(); cast?.Configure(view.Stream, true); return; }
        if (!TvConnected && BackgroundTvService.Instance is null) return;
        if (!view.Status.TryGetProperty("timeline", out var timelineValue) || timelineValue.GetString() is not { } timeline) return;
        var calibration = table.State.Calibration; var geometry = "";
        if (TvCorrectionRequested && calibration is not null)
        {
            var projection = SceneProjection.Pockets(960, 540, view.Status.GetProperty("width").GetInt32(), view.Status.GetProperty("height").GetInt32(), calibration, portrait: false);
            geometry = JsonSerializer.Serialize(new { width = 960, height = 540, values = projection.Values });
        }
        BackgroundTvService.Request(new(new(app.State.Camera.CameraIdentity!, timeline, AnnotationJournalStore.Fingerprint(geometry)),
            client.Address, view.PhoneStream, geometry, timeline, annotations));
        tvDeliveryRequested = true;
        if (tvDeliveryLifetime is null || tvDeliveryLifetime.IsCancellationRequested)
        { tvDeliveryLifetime?.Dispose(); tvDeliveryLifetime = new(); _ = BackgroundTvLoopAsync(tvDeliveryLifetime.Token); }
        if (BackgroundTvUrl.Length > 0) { tvDeliverySource = BackgroundTvUrl; cast?.Configure(tvDeliverySource, true); }
    }
    async Task BackgroundTvLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(1000, token);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (disposed || token.IsCancellationRequested) return;
                    ObserveCast();
                    if (BackgroundTvService.Instance is { Ready: true } service && !TvReplaySelected)
                    { tvDeliverySource = service.Url; cast?.Configure(service.Url, true); }
                    if (BackgroundTvService.Instance is null && !active) { tvDeliveryLifetime?.Cancel(); tvDeliveryRequested = false; }
                });
            }
        }
        catch (OperationCanceledException) { }
    }
    void StopBackgroundTv()
    {
        tvDeliveryLifetime?.Cancel(); tvDeliveryLifetime?.Dispose(); tvDeliveryLifetime = null;
        if (tvDeliverySource is { } source) cast?.ReturnToSourceIfOwned(source, effects.Latest?.Stream ?? "");
        tvDeliverySource = null; tvDeliveryRequested = false; BackgroundTvService.Clear();
    }
}
