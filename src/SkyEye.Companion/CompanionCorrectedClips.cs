using SkyEye.Core;
using SkyEye.Core.Application;
using System.Text.Json;

namespace SkyEye.Companion;

public sealed class CompanionCorrectedClips:IDisposable
{
    readonly CameraClient current;readonly Func<CameraConnection> connection;readonly CompanionClipService clips;
    readonly CorrectedClipStore saved,recent;readonly ICorrectedClipRenderer renderer;
    readonly LocalHttpServer server;readonly string access=Guid.NewGuid().ToString("N");
    readonly object gate=new();string? servedPath,servedId;long servedAt;bool servedTemporary;
    public CompanionCorrectedClips(CameraClient current,Func<CameraConnection> connection,CompanionClipService clips,string directory,ICorrectedClipRenderer renderer)
    {
        this.current=current;this.connection=connection;this.clips=clips;this.renderer=renderer;
        Directory.CreateDirectory(directory);Func<long> space=()=>new Android.OS.StatFs(directory).AvailableBytes;
        saved=new(Path.Combine(directory,"saved"),availableBytes:space);recent=new(Path.Combine(directory,"recent"),true,availableBytes:space);
        server=new(8767,(request,ct)=>{
            lock(gate){if(request.Path=="/status")return Task.FromResult(HttpResponse.Json(new{serving=servedPath is not null,temporary=servedTemporary}));
                if(servedPath is null||request.Path!="/corrected/"+servedId||request.Query.GetValueOrDefault("access")!=access||servedTemporary&&System.Diagnostics.Stopwatch.GetElapsedTime(servedAt)>=RecentReplayStore.Retention)
                    return Task.FromResult(HttpResponse.Json(new{error="Corrected replay unavailable"},410));
                return Task.FromResult(new HttpResponse(200,"video/mp4",FilePath:servedPath));}});server.Start();
    }
    public Clip[] Local()=>saved.Catalogue();
    public async Task<CorrectedClip> SavedAsync(CameraClipCatalog catalog,Clip clip,Action<ClipTransferProgress>? progress,CancellationToken ct)
    {
        _=clips.TvSource(catalog,clip);using var transfer=new CameraClipTransfer(current);
        var prepared=await saved.PrepareAsync(clip,transfer,renderer,progress,ct);Check(catalog.ConnectionId,catalog.CameraIdentity);return prepared;
    }
    public async Task<CorrectedClip> LocalAsync(string id,CancellationToken ct)
    {
        var clip=saved.Catalogue().FirstOrDefault(c=>c.Id==id);
        if(clip is not null){var existing=await saved.FindAsync(clip,renderer,ct);if(existing is not null)return existing;
            using var unavailable=new UnavailableTransfer();try{return await saved.PrepareAsync(clip,unavailable,renderer,cancellation:ct);}catch(IOException){}}
        var catalog=await clips.LoadAsync(ct);clip=catalog.Clips.FirstOrDefault(c=>c.Id==id)??throw new InvalidOperationException("Connect the original camera to prepare this clip.");
        return await SavedAsync(catalog,clip,null,ct);
    }
    public async Task<CorrectedClip> RecentAsync(CameraRecentReplay replay,CancellationToken ct)
    {
        _=clips.TvSource(replay);using var transfer=new ReplayTransfer(current,replay.Asset.Clip);
        var prepared=await recent.PrepareAsync(replay.Asset.Clip,transfer,renderer,cancellation:ct);Check(replay.ConnectionId,replay.CameraIdentity);return prepared;
    }
    void Check(Guid id,string identity){var camera=connection();if(camera.ConnectionId!=id||camera.CameraIdentity!=identity||camera.Reachability!=CameraReachability.Reachable)throw new InvalidOperationException("Camera changed during corrected replay preparation.");}
    public string Serve(CorrectedClip clip,bool temporary)
    {
        lock(gate){servedPath=clip.Landscape;servedId=Guid.NewGuid().ToString("N");servedAt=System.Diagnostics.Stopwatch.GetTimestamp();servedTemporary=temporary;
            var addresses=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address)
                .Where(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&!System.Net.IPAddress.IsLoopback(a)&&!a.ToString().StartsWith("169.254.")).ToArray();
            var ip=addresses.FirstOrDefault(a=>a.ToString().StartsWith("192.168."))??addresses.First();
            return $"http://{ip}:8767/corrected/{servedId}?access={access}";}
    }
    public void ClearServing(){lock(gate){servedPath=servedId=null;}}
    public void Dispose(){_=server.DisposeAsync();}
    sealed class UnavailableTransfer:IClipTransfer{public Task<Clip[]> ListAsync(CancellationToken ct)=>throw new IOException("Original clip unavailable offline");public Task DownloadAsync(Clip clip,string path,Action<long> progress,CancellationToken ct)=>throw new IOException("Original clip unavailable offline");public void Dispose(){}}
    sealed class ReplayTransfer:IClipTransfer
    {
        readonly CameraClient frozen;readonly Clip admitted;
        public ReplayTransfer(CameraClient current,Clip clip){frozen=current.CopyForViewing();admitted=clip;}
        public Task<Clip[]> ListAsync(CancellationToken ct)=>Task.FromResult(new[]{admitted});
        public Task DownloadAsync(Clip clip,string path,Action<long> progress,CancellationToken ct){if(clip!=admitted)throw new ArgumentException("Replay metadata changed");return frozen.DownloadMediaAsync(clip,"/replays/"+clip.Id,path,progress,ct);}
        public void Dispose()=>frozen.Dispose();
    }
}
