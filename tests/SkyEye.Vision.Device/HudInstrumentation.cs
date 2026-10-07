using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name="uk.co.skyeye.visionchecks.HudInstrumentation",TargetPackage="uk.co.skyeye.visionchecks",FunctionalTest=true)]
[global::Android.Runtime.Preserve(AllMembers=true)]
public sealed class HudInstrumentation : global::Android.App.Instrumentation
{
    public HudInstrumentation() { }
    public HudInstrumentation(IntPtr handle,global::Android.Runtime.JniHandleOwnership ownership):base(handle,ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart();object result;var root=TargetContext!.GetExternalFilesDir(null)!.AbsolutePath;
        try
        {
            using(var source=TargetContext.Assets!.Open("pixel-table.jpg"))using(var output=File.Create(Path.Combine(root,"hud-source.jpg")))source.CopyTo(output);
            var (calibration,shot)=HudFixture.From(TargetContext);
            var results=new List<object>();long version=1;
            foreach(var portrait in new[]{true,false})foreach(var mask in new[]{ShotAnnotations.All,ShotAnnotations.Lines,ShotAnnotations.WhiteDirection})
            {
                int width=portrait?1080:1920,height=portrait?1920:1080;var now=DateTimeOffset.UtcNow;
                var scene=ShotSceneProjection.Create(shot,calibration,version++,1,now,now.AddSeconds(15),portrait?$"{shot.Type} · pocket {shot.Pocket+1}":$"Candidate 1 · {shot.Type} · pocket {shot.Pocket+1}",true,mask,1,!portrait);
                var matrix=RecordedRenderPlan.PocketPixels(calibration,width,height,portrait);
                string name=$"hud-{(portrait?"phone":"tv")}-{(int)mask}.png";
                var proof=JsonSerializer.Deserialize<JsonElement>(UK.CO.Skyeye.Companion.Nativebridge.HudPixelProof.Render(
                    JsonSerializer.Serialize(scene,JsonDefaults.Options),Path.Combine(root,name),width,height,JsonSerializer.Serialize(matrix,JsonDefaults.Options))!);
                results.Add(new{portrait,mask=(int)mask,png=name,scene,proof});
            }
            result=new{passed=true,declaredGeometry=true,actualNativeRenderer=true,results};
        }
        catch(Exception error){result=new{passed=false,error=error.ToString()};}
        var json=JsonSerializer.Serialize(result,JsonDefaults.Options);File.WriteAllText(Path.Combine(root,"hud-proof.json"),json);
        using var bundle=new global::Android.OS.Bundle();bundle.PutString("result",json);Finish(global::Android.App.Result.Ok,bundle);
    }
}
