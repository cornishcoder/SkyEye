using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name="uk.co.skyeye.visionchecks.WatermarkInstrumentation",TargetPackage="uk.co.skyeye.visionchecks",FunctionalTest=true)]
[global::Android.Runtime.Preserve(AllMembers=true)]
public sealed class WatermarkInstrumentation : global::Android.App.Instrumentation
{
    public WatermarkInstrumentation() { }
    public WatermarkInstrumentation(IntPtr handle,global::Android.Runtime.JniHandleOwnership ownership):base(handle,ownership) { }
    bool portrait;
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args);portrait=args?.GetString("portrait")=="true";Start(); }
    public override void OnStart()
    {
        base.OnStart();var result=RunAsync().GetAwaiter().GetResult();using var bundle=new global::Android.OS.Bundle();bundle.PutString("result",result);Finish(global::Android.App.Result.Ok,bundle);
    }
    async Task<string> RunAsync()
    {
        var context=TargetContext!;string root=context.GetExternalFilesDir(null)!.AbsolutePath;object result;
        try
        {
            string source=Path.Combine(root,"render-input.mp4"),marked=Path.Combine(root,portrait?"watermarked-portrait.mp4":"watermarked-landscape.mp4"),plain=Path.Combine(root,"watermark-control.mp4");
            foreach(var path in new[]{marked,plain})if(File.Exists(path))File.Delete(path); // owned derivative fixtures only
            await using var file=File.OpenRead(source);var hash=Convert.ToHexString(await SHA256.HashDataAsync(file));file.Close();
            var journal=await new SkyEye.Companion.AndroidRecordedGeometry(Path.Combine(context.FilesDir!.AbsolutePath,"render-geometry")).AnalyzeAsync(source,hash,null,default);
            var plan=RecordedRenderPlan.Create(journal,journal.Source,portrait?1080:1920,portrait?1920:1080,portrait);
            var renderer=new SkyEye.Companion.AndroidRecordedRenderer(context);
            var markedReport=await renderer.RenderAsync(source,marked,plan);
            var plainReport=await renderer.RenderAsync(source,plain,plan,watermark:false);
            var pixels=JsonSerializer.Deserialize<JsonElement>(UK.CO.Skyeye.Companion.Nativebridge.WatermarkPixelProof.Verify(context,marked,plain)!);
            result=new{passed=true,portrait,sourceSha256=hash,pixels,markedReport,plainReport};
        }
        catch(Exception e){result=new{passed=false,portrait,error=e.ToString()};}
        string json=JsonSerializer.Serialize(result,JsonDefaults.Options);await File.WriteAllTextAsync(Path.Combine(root,portrait?"watermark-portrait-checks.json":"watermark-landscape-checks.json"),json);return json;
    }
}
