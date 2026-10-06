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
            var beforeRecovery=transfer.Downloads;
            var resumed=await new CorrectedClipStore(Path.Combine(root,"saved")).PrepareAsync(other,transfer,renderer);
            Check(transfer.Downloads==beforeRecovery&&File.ReadAllBytes(resumed.Original).SequenceEqual(bytes),"interrupted preparation reuses only verified original across restart");
            // Isolate the recovery fixture from the existing permanent-catalogue assertions.
            Directory.Delete(Path.GetDirectoryName(resumed.Original)!,true);
            var clock=new Clock();var temporary=new CorrectedClipStore(Path.Combine(root,"recent"),true,clock);
            await temporary.PrepareAsync(clip,transfer,renderer);await temporary.PrepareAsync(other,transfer,renderer);
            try{await temporary.PrepareAsync(clip with{Id=Guid.NewGuid().ToString("N")},transfer,renderer);throw new Exception("Expected temporary capacity refusal");}catch(InvalidOperationException){}
            Check(store.Catalogue().Length==1,"recent replays never enter permanent library");clock.Advance(600);temporary.Cleanup();Check(!temporary.Catalogue().Any(),"monotonic expiry removes paired recent media");
            string saved=Path.Combine(root,"saved",clip.Id);Directory.Move(saved,saved+".previous");var recovered=new CorrectedClipStore(Path.Combine(root,"saved"));Check(await recovered.FindAsync(clip,renderer,default)is not null,"publication-interruption backup recovers original and both derivatives");
            var metadataStore=new CorrectedClipStore(Path.Combine(root,"with-annotations"),metadata:(c,path)=>File.WriteAllText(Path.Combine(path,"annotations.json"),"declared fixture"));
            var annotated=await metadataStore.PrepareAsync(clip,transfer,renderer);
            Check(File.Exists(Path.Combine(Path.GetDirectoryName(annotated.Original)!,"annotations.json")),"clip journal publishes alongside verified media");
            var failingMetadata=new CorrectedClipStore(Path.Combine(root,"failed-annotations"),metadata:(c,path)=>throw new IOException("fixture metadata failure"));
            try{await failingMetadata.PrepareAsync(clip,transfer,renderer);throw new Exception("Expected annotation publication failure");}catch(IOException){}
            Check(failingMetadata.Catalogue().Length==0&&!Directory.EnumerateDirectories(Path.Combine(root,"failed-annotations"),"*.part").Any(),"failed sidecar cannot publish incomplete media bundle");
            var snapshotRenderer=new SnapshotRenderer();
            var snapshotStore=new CorrectedClipStore(Path.Combine(root,"frozen"),metadata:(c,path)=>File.WriteAllText(Path.Combine(path,"annotations.json"),snapshotRenderer.Version.ToString()));
            var frozen=await snapshotStore.PrepareAsync(clip,transfer,snapshotRenderer);
            Check(frozen.Manifest.Render.Profile=="scene1"&&File.ReadAllText(frozen.Portrait)=="scene1"&&File.ReadAllText(frozen.Landscape)=="scene1","mutable live journal cannot change one orientation midway through frozen render");
            var amended=await snapshotStore.PrepareAsync(clip,transfer,snapshotRenderer);
            Check(amended.Manifest.Render.Profile=="scene2"&&File.ReadAllText(amended.Portrait)=="scene2","amended journal invalidates prior derivative and publishes a new frozen pair");
            Console.WriteLine("PASS corrected derivatives: originals, paired atomic publication, restart/hash/geometry invalidation, retained-source repair, cancellation/failure, temporary isolation/capacity/expiry and interrupted publication recovery");
        }
        finally{if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected fixture root");Directory.Delete(root,true);}
    }
    sealed class Transfer(byte[] bytes):IClipTransfer{public int Downloads;public Task<Clip[]> ListAsync(CancellationToken ct)=>Task.FromResult(Array.Empty<Clip>());public async Task DownloadAsync(Clip clip,string path,Action<long> progress,CancellationToken ct){Downloads++;await File.WriteAllBytesAsync(path,bytes,ct);progress(bytes.Length);}public void Dispose(){}}
    sealed class Renderer:ICorrectedClipRenderer{public int Renders,Version=1;public bool Fail;public CancellationTokenSource? Cancel;public Task<CorrectedRenderInfo> PrepareAsync(Clip clip,string source,CancellationToken ct)=>Task.FromResult(new CorrectedRenderInfo("profile"+Version,new string('A',64),10,15));public async Task RenderAsync(Clip clip,string source,string portrait,string landscape,CorrectedRenderInfo expected,CancellationToken ct){Renders++;await File.WriteAllTextAsync(portrait,"portrait"+Version,ct);if(Fail)throw new IOException("fixture failure");Cancel?.Cancel();ct.ThrowIfCancellationRequested();await File.WriteAllTextAsync(landscape,"landscape"+Version,ct);}}
    sealed class SnapshotRenderer:ICorrectedClipRenderer,ICorrectedClipSnapshotRenderer
    {
        public int Version=1;
        static CorrectedRenderInfo Info(int version)=>new("scene"+version,new string('A',64),10,15);
        public Task<CorrectedRenderInfo> PrepareAsync(Clip clip,string original,CancellationToken ct)=>Task.FromResult(Info(Version));
        public Task<CorrectedRenderInfo> PrepareSnapshotAsync(Clip clip,string original,CancellationToken ct)=>Task.FromResult(Info(int.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(original)!,"annotations.json")))));
        public async Task RenderAsync(Clip clip,string original,string portrait,string landscape,CorrectedRenderInfo expected,CancellationToken ct)
        {await File.WriteAllTextAsync(portrait,expected.Profile,ct);Version=2;await File.WriteAllTextAsync(landscape,expected.Profile,ct);}
    }
    sealed class Clock:TimeProvider{long ticks;public override long GetTimestamp()=>ticks;public override long TimestampFrequency=>TimeSpan.TicksPerSecond;public void Advance(int seconds)=>ticks+=seconds*TimeSpan.TicksPerSecond;}
}
