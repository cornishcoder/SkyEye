using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkyEye.Core.Application;

public sealed record CorrectedRenderInfo(string Profile, string GeometrySha256, double CorrectedSeconds, double Duration);
public interface ICorrectedClipRenderer
{
    Task<CorrectedRenderInfo> PrepareAsync(Clip clip,string original,CancellationToken cancellation);
    Task RenderAsync(Clip clip,string original,string portrait,string landscape,CorrectedRenderInfo expected,CancellationToken cancellation);
}
public interface ICorrectedClipSnapshotRenderer
{
    Task<CorrectedRenderInfo> PrepareSnapshotAsync(Clip clip,string original,CancellationToken cancellation);
}
public sealed record CorrectedClipManifest(int Schema,Clip Clip,CorrectedRenderInfo Render,string PortraitSha256,string LandscapeSha256,long Bytes,DateTimeOffset CreatedAt);
public sealed record CorrectedClip(CorrectedClipManifest Manifest,string Original,string Portrait,string Landscape);

// Permanent downloads and ephemeral recent replays use different directories.
// A directory move publishes both orientations and their manifest together.
public sealed class CorrectedClipStore
{
    public const long MaximumSourceBytes=512L*1024*1024;
    public const long TemporaryBudgetBytes=512L*1024*1024;
    public const long RecoveryBudgetBytes=1024L*1024*1024;
    static readonly ConcurrentDictionary<string,SemaphoreSlim> locks=new(StringComparer.OrdinalIgnoreCase);
    readonly string root;readonly bool temporary;readonly TimeProvider clock;readonly SemaphoreSlim writer;
    readonly Func<long> freeBytes;
    readonly Action<Clip,string>? metadata;
    readonly ConcurrentDictionary<string,long> timestamps=[];
    public CorrectedClipStore(string directory,bool temporary=false,TimeProvider? clock=null,Func<long>? availableBytes=null,Action<Clip,string>? metadata=null)
    {
        root=Path.GetFullPath(directory);this.temporary=temporary;this.clock=clock??TimeProvider.System;Directory.CreateDirectory(root);writer=locks.GetOrAdd(root,_=>new(1));
        freeBytes=availableBytes??(()=>new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace);this.metadata=metadata;
        if(writer.Wait(0))try
        {
        foreach(var previous in Directory.EnumerateDirectories(root,"*.previous")){string id=Path.GetFileName(previous)[..^9];if(Guid.TryParseExact(id,"N",out _)&&!Directory.Exists(Folder(id)))Shift(previous,Folder(id));}
        // Temporary references do not survive companion process restart. Sweep
        // only generated complete directories in this explicitly separate root.
        if(temporary)foreach(var directoryPath in Directory.EnumerateDirectories(root)){if(Guid.TryParseExact(Path.GetFileName(directoryPath),"N",out _))Drop(directoryPath);}
        // Incomplete derivatives are never published. Verified originals alone may
        // survive a killed process, within a bounded recovery cache.
        foreach(var partial in Directory.EnumerateDirectories(root,"*.part"))Drop(partial);
        TrimRecovery();
        }finally{writer.Release();}
    }
    void Scoped(string path){if(!Path.GetFullPath(path).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Generated media path is outside its store.");}
    void Drop(string path){Scoped(path);Directory.Delete(path,true);}
    void Shift(string from,string to){Scoped(from);Scoped(to);Directory.Move(from,to);}
    string Folder(string id)=>Path.Combine(root,id);
    string Recovery(string id)=>Folder(id)+".recovery";
    void TrimRecovery()
    {
        long bytes=0;int count=0;
        foreach(var path in Directory.EnumerateDirectories(root,"*.recovery").OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            string id=Path.GetFileName(path)[..^9];long size=Directory.EnumerateFiles(path).Sum(p=>new FileInfo(p).Length);
            if(temporary||!Guid.TryParseExact(id,"N",out _)||++count>2||size>MaximumSourceBytes+8*1024*1024||bytes+size>RecoveryBudgetBytes)Drop(path);
            else bytes+=size;
        }
    }
    public Clip[] Catalogue()=>Directory.EnumerateDirectories(root).Select(path=>Read(path)?.Clip).Where(clip=>clip is not null).Cast<Clip>().OrderByDescending(c=>c.CreatedAt).ToArray();
    static CorrectedClipManifest? Read(string directory)
    {
        try{var path=Path.Combine(directory,"manifest.json");if(new FileInfo(path) is not{Exists:true,Length:>0 and <131072})return null;var manifest=JsonSerializer.Deserialize<CorrectedClipManifest>(File.ReadAllText(path),JsonDefaults.Options);
            if(manifest is null||manifest.Schema!=1||manifest.Render is null||manifest.Clip is null)return null;ClipLibrary.Validate(manifest.Clip);
            if(Path.GetFileName(directory)!=manifest.Clip.Id||manifest.Bytes<=0||!Hash(manifest.PortraitSha256)||!Hash(manifest.LandscapeSha256)||!Hash(manifest.Render.GeometrySha256)
                ||string.IsNullOrWhiteSpace(manifest.Render.Profile)||!double.IsFinite(manifest.Render.CorrectedSeconds)||manifest.Render.CorrectedSeconds<0||manifest.Render.CorrectedSeconds>manifest.Render.Duration||manifest.Render.Duration<=0)return null;return manifest;
        }catch(Exception e)when(e is IOException or JsonException or ArgumentException){return null;}
    }
    static bool Hash(string value)=>value is{Length:64}&&value.All(Uri.IsHexDigit);
    static async Task<bool> Matches(string path,string hash,CancellationToken ct){if(!File.Exists(path))return false;await using var input=File.OpenRead(path);return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input,ct)),hash,StringComparison.OrdinalIgnoreCase);}
    public async Task<CorrectedClip?> FindAsync(Clip clip,ICorrectedClipRenderer renderer,CancellationToken ct)
    {
        ClipLibrary.Validate(clip);string directory=Folder(clip.Id);var manifest=Read(directory);
        if(manifest is null||manifest.Clip!=clip||temporary&&(!timestamps.TryGetValue(clip.Id,out var created)||clock.GetElapsedTime(created)>=RecentReplayStore.Retention))return null;
        string original=Path.Combine(directory,"original.mp4"),portrait=Path.Combine(directory,"portrait.mp4"),landscape=Path.Combine(directory,"landscape.mp4");
        if(!await Matches(original,clip.Sha256,ct)||!await Matches(portrait,manifest.PortraitSha256,ct)||!await Matches(landscape,manifest.LandscapeSha256,ct))return null;
        var current=await renderer.PrepareAsync(clip,original,ct);return current==manifest.Render?new(manifest,original,portrait,landscape):null;
    }
    public async Task<CorrectedClip> PrepareAsync(Clip clip,IClipTransfer transfer,ICorrectedClipRenderer renderer,Action<ClipTransferProgress>? progress=null,CancellationToken cancellation=default)
    {
        ClipLibrary.Validate(clip);if(clip.Bytes>MaximumSourceBytes||clip.Duration>605)throw new ArgumentException("Clip exceeds corrected export limits.");
        await writer.WaitAsync(cancellation);string stage=Path.Combine(root,Guid.NewGuid().ToString("N")+".part");
        try
        {
            CleanupCore();if(await FindAsync(clip,renderer,cancellation) is{} available)return available;
            if(temporary&&(timestamps.Count>=2||TotalBytes()+clip.Bytes*4>TemporaryBudgetBytes))throw new InvalidOperationException("Recent replay storage is full. Wait for an earlier replay to expire.");
            if(freeBytes()<clip.Bytes*4+128L*1024*1024)throw new InvalidOperationException("Not enough space to prepare corrected footage.");
            Directory.CreateDirectory(stage);string original=Path.Combine(stage,"original.mp4"),portrait=Path.Combine(stage,"portrait.mp4"),landscape=Path.Combine(stage,"landscape.mp4");
            var old=Read(Folder(clip.Id));string retained=Path.Combine(Folder(clip.Id),"original.mp4");
            string recovery=Recovery(clip.Id),recoveredOriginal=Path.Combine(recovery,"original.mp4");
            if(await Matches(recoveredOriginal,clip.Sha256,cancellation)&&new FileInfo(recoveredOriginal).Length==clip.Bytes)File.Copy(recoveredOriginal,original);
            else if(old?.Clip==clip&&await Matches(retained,clip.Sha256,cancellation))File.Copy(retained,original);
            else{progress?.Invoke(new(ClipTransferStage.Downloading,0,clip.Bytes));await transfer.DownloadAsync(clip,original,n=>progress?.Invoke(new(ClipTransferStage.Downloading,n,clip.Bytes)),cancellation);}
            if(new FileInfo(original).Length!=clip.Bytes||!await Matches(original,clip.Sha256,cancellation))throw new IOException("Corrected export source verification failed.");
            if(!temporary)
            {
                Directory.CreateDirectory(recovery);string pending=Path.Combine(recovery,"original.part");File.Copy(original,pending,true);
                File.Move(pending,recoveredOriginal,true);TrimRecovery();
            }
            progress?.Invoke(new(ClipTransferStage.Preparing,clip.Bytes,clip.Bytes));
            string frozenAnnotations=Path.Combine(recovery,"annotations.json"),stageAnnotations=Path.Combine(stage,"annotations.json");
            if(!temporary&&File.Exists(frozenAnnotations)&&new FileInfo(frozenAnnotations).Length is >0 and <=8*1024*1024)File.Copy(frozenAnnotations,stageAnnotations);
            else metadata?.Invoke(clip,stage);
            if(!temporary&&File.Exists(stageAnnotations))
            {string pendingAnnotations=frozenAnnotations+".part";File.Copy(stageAnnotations,pendingAnnotations,true);File.Move(pendingAnnotations,frozenAnnotations,true);}
            var info=renderer is ICorrectedClipSnapshotRenderer snapshot?await snapshot.PrepareSnapshotAsync(clip,original,cancellation):await renderer.PrepareAsync(clip,original,cancellation);
            await renderer.RenderAsync(clip,original,portrait,landscape,info,cancellation);cancellation.ThrowIfCancellationRequested();
            foreach(var path in new[]{portrait,landscape})if(new FileInfo(path) is not{Exists:true,Length:>0})throw new IOException("Corrected output unavailable.");
            async Task<string> Digest(string path){await using var file=File.OpenRead(path);return Convert.ToHexString(await SHA256.HashDataAsync(file,cancellation));}
            cancellation.ThrowIfCancellationRequested();
            long bytes=Directory.EnumerateFiles(stage).Sum(path=>new FileInfo(path).Length);if(temporary&&TotalBytes()+bytes>TemporaryBudgetBytes)throw new IOException("Corrected recent replay exceeds temporary storage budget.");
            var manifest=new CorrectedClipManifest(1,clip,info,await Digest(portrait),await Digest(landscape),bytes,clock.GetUtcNow());
            await File.WriteAllTextAsync(Path.Combine(stage,"manifest.json"),JsonSerializer.Serialize(manifest,JsonDefaults.Options),cancellation);cancellation.ThrowIfCancellationRequested();
            string destination=Folder(clip.Id),backup=destination+".previous";
            if(Directory.Exists(backup))Drop(backup);
            if(Directory.Exists(destination))Shift(destination,backup);
            try{Shift(stage,destination);}catch{if(Directory.Exists(backup))Shift(backup,destination);throw;}
            if(Directory.Exists(backup))Drop(backup);if(temporary)timestamps[clip.Id]=clock.GetTimestamp();
            if(Directory.Exists(recovery))Drop(recovery);
            progress?.Invoke(new(ClipTransferStage.Available,clip.Bytes,clip.Bytes));
            return new(manifest,Path.Combine(destination,"original.mp4"),Path.Combine(destination,"portrait.mp4"),Path.Combine(destination,"landscape.mp4"));
        }
        finally{try{if(Directory.Exists(stage))Drop(stage);}finally{writer.Release();}}
    }
    long TotalBytes()=>Directory.EnumerateDirectories(root).Select(path=>Read(path)?.Bytes??0).Sum();
    void CleanupCore(){if(!temporary)return;foreach(var item in timestamps.ToArray())if(clock.GetElapsedTime(item.Value)>=RecentReplayStore.Retention){Drop(Folder(item.Key));timestamps.TryRemove(item.Key,out _);}}
    public void Cleanup(){if(!writer.Wait(0))return;try{CleanupCore();}finally{writer.Release();}}
}
