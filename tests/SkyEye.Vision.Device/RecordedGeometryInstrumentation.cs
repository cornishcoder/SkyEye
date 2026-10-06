using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name="uk.co.skyeye.visionchecks.RecordedGeometryInstrumentation",TargetPackage="uk.co.skyeye.visionchecks",FunctionalTest=true)]
[global::Android.Runtime.Preserve(AllMembers=true)]
public sealed class RecordedGeometryInstrumentation : global::Android.App.Instrumentation
{
    public RecordedGeometryInstrumentation() { }
    public RecordedGeometryInstrumentation(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership) : base(handle,ownership) { }
    bool real;
    public override void OnCreate(global::Android.OS.Bundle? arguments)
    { base.OnCreate(arguments); real=arguments?.GetString("mode")=="real"; Start(); }
    public override void OnStart()
    {
        base.OnStart(); var result=RunAsync().GetAwaiter().GetResult();
        using var bundle=new global::Android.OS.Bundle(); bundle.PutString("result",result);
        Finish(global::Android.App.Result.Ok,bundle);
    }
    async Task<string> RunAsync()
    {
        var context=TargetContext!; string external=context.GetExternalFilesDir(null)!.AbsolutePath;
        object result;
        try
        {
            var watch=System.Diagnostics.Stopwatch.StartNew(); string path=Path.Combine(external,"geometry-fixture.mp4");
            await using var file=File.OpenRead(path); string hash=Convert.ToHexString(await SHA256.HashDataAsync(file)); file.Close();
            ClipRecordingOrigin? origin=null;
            if(real)
            {
                var clip=JsonSerializer.Deserialize<Clip>(File.ReadAllText(Path.Combine(external,"geometry-source.json")),JsonDefaults.Options)!;
                if(!string.Equals(hash,clip.Sha256,StringComparison.OrdinalIgnoreCase))throw new Exception("Recorded clip checksum differs from acknowledgement");
                origin=clip.RecordingOrigin??throw new Exception("Missing camera recording origin");
            }
            var root=Path.Combine(context.FilesDir!.AbsolutePath,"instrumented-geometry");
            var geometry=new SkyEye.Companion.AndroidRecordedGeometry(root);
            var journal=await geometry.AnalyzeAsync(path,hash,origin,default);
            if(journal.Source.ExtractionVersion!=2)throw new Exception("Obsolete extraction metadata");
            if(real)
            {
                if(journal.Source.Clock!=RecordedGeometryClock.CameraRecording || journal.Source.Timeline!=origin!.Timeline || journal.Source.StartUs!=origin.StartUs
                    || !journal.Spans.Any(s=>s.Layout is not null))throw new Exception("Real recording has no accepted source-scoped geometry");
            }
            else
            {
                if(journal.At(journal.Source,1_000_000)?.Layout is not {} original || journal.At(journal.Source,5_000_000)?.Layout is not {} moved
                    || RecordedGeometry.Same(original,moved) || journal.At(journal.Source,3_000_000)?.Layout is not null
                    || journal.At(journal.Source,9_000_000)?.Layout is null || journal.At(journal.Source,13_000_000)?.Layout is not null
                    || journal.At(journal.Source,17_000_000)?.Layout is null || journal.At(journal.Source,19_000_000)?.Layout is not null)
                    throw new Exception("Recorded fixture mount/dim/covered/recovery intervals differ from source times");
            }
            var recovered=await new SkyEye.Companion.AndroidRecordedGeometry(root).AnalyzeAsync(path,hash,origin,default);
            if(JsonSerializer.Serialize(journal)!=JsonSerializer.Serialize(recovered))throw new Exception("Restart changed recorded intervals");
            bool checksumRejected=false;
            try{await geometry.AnalyzeAsync(path,new string('0',64),origin,default);}catch(IOException){checksumRejected=true;}
            if(!checksumRejected)throw new Exception("Wrong recorded checksum accepted");
            result=new { passed=true,real,model=global::Android.OS.Build.Model,journal,recovered=true,checksumRejected,elapsedMs=watch.ElapsedMilliseconds };
        }
        catch(Exception error){result=new {passed=false,real,error=error.ToString()};}
        string json=JsonSerializer.Serialize(result,JsonDefaults.Options);
        await File.WriteAllTextAsync(Path.Combine(external,"recorded-geometry-checks.json"),json);
        return json;
    }
}
