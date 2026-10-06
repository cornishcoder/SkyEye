using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name="uk.co.skyeye.visionchecks.RecordedRenderInstrumentation",TargetPackage="uk.co.skyeye.visionchecks",FunctionalTest=true)]
[global::Android.Runtime.Preserve(AllMembers=true)]
public sealed class RecordedRenderInstrumentation:global::Android.App.Instrumentation
{
    public RecordedRenderInstrumentation(){}
    public RecordedRenderInstrumentation(IntPtr handle,global::Android.Runtime.JniHandleOwnership ownership):base(handle,ownership){}
    bool real,portrait;
    public override void OnCreate(global::Android.OS.Bundle? args){base.OnCreate(args);real=args?.GetString("mode")=="real";portrait=args?.GetString("portrait")=="true";Start();}
    public override void OnStart(){base.OnStart();string report=RunAsync().GetAwaiter().GetResult();using var bundle=new global::Android.OS.Bundle();bundle.PutString("result",report);Finish(global::Android.App.Result.Ok,bundle);}
    async Task<string> RunAsync()
    {
        var context=TargetContext!;string root=context.GetExternalFilesDir(null)!.AbsolutePath;object result;
        try
        {
            string input=Path.Combine(root,"render-input.mp4"),output=Path.Combine(root,portrait?"rendered-portrait.mp4":"rendered-landscape.mp4");
            // Owned test artifact only; originals and saved camera clips are untouched.
            if(File.Exists(output))File.Delete(output);
            await using var file=File.OpenRead(input);string hash=Convert.ToHexString(await SHA256.HashDataAsync(file));file.Close();
            ClipRecordingOrigin? origin=null;
            if(real){var clip=JsonSerializer.Deserialize<Clip>(File.ReadAllText(Path.Combine(root,"geometry-source.json")),JsonDefaults.Options)!;if(clip.Sha256!=hash)throw new Exception("Recorded source differs from acknowledgement");origin=clip.RecordingOrigin;}
            var journal=await new SkyEye.Companion.AndroidRecordedGeometry(Path.Combine(context.FilesDir!.AbsolutePath,"render-geometry")).AnalyzeAsync(input,hash,origin,default);
            var plan=RecordedRenderPlan.Create(journal,journal.Source,portrait?1080:1920,portrait?1920:1080,portrait);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var report=await new SkyEye.Companion.AndroidRecordedRenderer(context).RenderAsync(input,output,plan);
            if(report.GetProperty("correctedFrameCallbacks").GetInt64()<=0||!File.Exists(output))throw new Exception("No corrected frames rendered");
            bool wrongSourceRefused=false;
            var wrong=plan.Source with{ContentSha256=new string('0',64),Timeline=plan.Source.Clock==RecordedGeometryClock.ClipRelative?new string('0',64):plan.Source.Timeline};
            try{await new SkyEye.Companion.AndroidRecordedRenderer(context).RenderAsync(input,output+".wrong.mp4",plan with{Source=wrong});}catch(IOException){wrongSourceRefused=true;}
            if(!wrongSourceRefused)throw new Exception("Mismatched source accepted");
            result=new {passed=true,real,portrait,model=global::Android.OS.Build.Model,plan,report,wrongSourceRefused,outputSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),elapsedMs=watch.ElapsedMilliseconds};
        }
        catch(Exception error){result=new {passed=false,real,portrait,error=error.ToString()};}
        string json=JsonSerializer.Serialize(result,JsonDefaults.Options);await File.WriteAllTextAsync(Path.Combine(root,"recorded-render-checks.json"),json);return json;
    }
}
