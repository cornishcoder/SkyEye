using SkyEye.Core;
using SkyEye.Core.Application;
using System.Security.Cryptography;

static class CorrectedClipChecks
{
    static void Check(bool yes,string text){if(!yes)throw new Exception(text);}
    public static async Task Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"skyeye-corrected-"+Guid.NewGuid().ToString("N"));byte[] bytes=[1,2,3,4,5];
        var clip=new Clip(Guid.NewGuid().ToString("N"),"Fixture",15,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),DateTimeOffset.UtcNow);
        var transfer=new Transfer(bytes);var renderer=new Renderer();var store=new CorrectedClipStore(Path.Combine(root,"saved"));
        try
        {
            var prepared=await store.PrepareAsync(clip,transfer,renderer);Check(File.ReadAllBytes(prepared.Original).SequenceEqual(bytes)&&store.Catalogue().Single()==clip,"verified original retained separately from paired derivatives");
            await new CorrectedClipStore(Path.Combine(root,"saved")).PrepareAsync(clip,transfer,renderer);Check(transfer.Downloads==1&&renderer.Renders==1,"restart reuses only hash-verified current geometry outputs");
            await File.WriteAllTextAsync(prepared.Portrait,"corrupt");await store.PrepareAsync(clip,transfer,renderer);Check(renderer.Renders==2&&transfer.Downloads==1,"corrupt derivative rerenders from verified original without download");
            renderer.Version=2;await store.PrepareAsync(clip,transfer,renderer);Check(renderer.Renders==3,"changed geometry/profile invalidates cached derivative");
            renderer.Fail=true;var other=clip with{Id=Guid.NewGuid().ToString("N")};try{await store.PrepareAsync(other,transfer,renderer);throw new Exception("Expected failed render");}catch(IOException){}
            Check(store.Catalogue().Length==1&&!Directory.EnumerateDirectories(Path.Combine(root,"saved"),"*.part").Any(),"failure publishes no partial pair and preserves existing clip");renderer.Fail=false;
            using var cancel=new CancellationTokenSource();renderer.Cancel=cancel;try{await store.PrepareAsync(other,transfer,renderer,cancellation:cancel.Token);throw new Exception("Expected cancellation");}catch(OperationCanceledException){}
            Check(store.Catalogue().Length==1&&!Directory.EnumerateDirectories(Path.Combine(root,"saved"),"*.part").Any(),"cancellation removes staged media without adding library entry");renderer.Cancel=null;
            var clock=new Clock();var temporary=new CorrectedClipStore(Path.Combine(root,"recent"),true,clock);
            await temporary.PrepareAsync(clip,transfer,renderer);await temporary.PrepareAsync(other,transfer,renderer);
            try{await temporary.PrepareAsync(clip with{Id=Guid.NewGuid().ToString("N")},transfer,renderer);throw new Exception("Expected temporary capacity refusal");}catch(InvalidOperationException){}
            Check(store.Catalogue().Length==1,"recent replays never enter permanent library");clock.Advance(600);temporary.Cleanup();Check(!temporary.Catalogue().Any(),"monotonic expiry removes paired recent media");
            string saved=Path.Combine(root,"saved",clip.Id);Directory.Move(saved,saved+".previous");var recovered=new CorrectedClipStore(Path.Combine(root,"saved"));Check(await recovered.FindAsync(clip,renderer,default)is not null,"publication-interruption backup recovers original and both derivatives");
            Console.WriteLine("PASS corrected derivatives: originals, paired atomic publication, restart/hash/geometry invalidation, retained-source repair, cancellation/failure, temporary isolation/capacity/expiry and interrupted publication recovery");
        }
        finally{if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected fixture root");Directory.Delete(root,true);}
    }
    sealed class Transfer(byte[] bytes):IClipTransfer{public int Downloads;public Task<Clip[]> ListAsync(CancellationToken ct)=>Task.FromResult(Array.Empty<Clip>());public async Task DownloadAsync(Clip clip,string path,Action<long> progress,CancellationToken ct){Downloads++;await File.WriteAllBytesAsync(path,bytes,ct);progress(bytes.Length);}public void Dispose(){}}
    sealed class Renderer:ICorrectedClipRenderer{public int Renders,Version=1;public bool Fail;public CancellationTokenSource? Cancel;public Task<CorrectedRenderInfo> PrepareAsync(Clip clip,string source,CancellationToken ct)=>Task.FromResult(new CorrectedRenderInfo("profile"+Version,new string('A',64),10,15));public async Task RenderAsync(Clip clip,string source,string portrait,string landscape,CorrectedRenderInfo expected,CancellationToken ct){Renders++;await File.WriteAllTextAsync(portrait,"portrait"+Version,ct);if(Fail)throw new IOException("fixture failure");Cancel?.Cancel();ct.ThrowIfCancellationRequested();await File.WriteAllTextAsync(landscape,"landscape"+Version,ct);}}
    sealed class Clock:TimeProvider{long ticks;public override long GetTimestamp()=>ticks;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public void Advance(int seconds)=>ticks+=seconds*TimeSpan.TicksPerSecond;}
}
