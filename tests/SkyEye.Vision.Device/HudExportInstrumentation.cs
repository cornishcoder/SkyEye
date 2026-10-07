using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name="uk.co.skyeye.visionchecks.HudExportInstrumentation",TargetPackage="uk.co.skyeye.visionchecks",FunctionalTest=true)]
[global::Android.Runtime.Preserve(AllMembers=true)]
public sealed class HudExportInstrumentation:global::Android.App.Instrumentation
{
    public HudExportInstrumentation() { }
    public HudExportInstrumentation(IntPtr handle,global::Android.Runtime.JniHandleOwnership ownership):base(handle,ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args){base.OnCreate(args);Start();}
    public override void OnStart(){base.OnStart();var json=Run().GetAwaiter().GetResult();using var bundle=new global::Android.OS.Bundle();bundle.PutString("result",json);Finish(global::Android.App.Result.Ok,bundle);}
    async Task<string> Run()
    {
        var context=TargetContext!;var root=context.GetExternalFilesDir(null)!.AbsolutePath;object result;
        try
        {
            string input=Path.Combine(root,"hud-input.mp4");string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
            var (calibration,shot)=HudFixture.From(context);long start=1000000,duration=6_000_000;var timeline=Guid.NewGuid().ToString("N");
            var source=new RecordedGeometrySource(hash,RecordedGeometryClock.CameraRecording,timeline,start,duration,1920,1080,0);
            var pockets=new PocketLayout(calibration.Corners.ToImmutableArray(),[calibration.Unmap(new(1,0)),calibration.Unmap(new(1,1))],.8);
            var geometry=new RecordedGeometryJournal(1,source,[new(start,source.EndUs,1,pockets)]);var origin=new ClipRecordingOrigin(timeline,start,1920,1080,0);
            var outputs=new List<object>();
            foreach(bool portrait in new[]{true,false})
            {
                var target=portrait?SceneTarget.Phone:SceneTarget.Tv;var now=DateTimeOffset.UtcNow;
                var scene=ShotSceneProjection.Create(shot,calibration,1,1,now,now.AddSeconds(10),"Historical geometric candidate",true,ShotAnnotations.All,1,!portrait);
                var historical=JsonSerializer.Deserialize<RecordedScene>(JsonSerializer.Serialize(scene,JsonDefaults.Options),JsonDefaults.Options)!;
                var journal=new ClipAnnotationJournal(2,hash,origin,duration,"source-image-normalized-v1",[
                    new(start,start+2_000_000,target,new('A',64),historical),new(start+2_000_000,start+3_000_000,target,new('A',64),null),
                    new(start+3_000_000,source.EndUs,target,new('A',64),historical)]);
                var serialized=JsonSerializer.Serialize(journal,JsonDefaults.Options);var recovered=JsonSerializer.Deserialize<ClipAnnotationJournal>(serialized,JsonDefaults.Options)!;recovered.Validate();
                var video=RecordedRenderPlan.Create(geometry,source,portrait?1080:1920,portrait?1920:1080,portrait);
                var plan=RecordedAnnotationPlan.Create(video,recovered,target);
                var output=Path.Combine(root,portrait?"hud-export-phone.mp4":"hud-export-tv.mp4");
                var renderer=new SkyEye.Companion.AndroidRecordedRenderer(context);var report=await renderer.RenderAsync(input,output,video,annotations:plan);
                if(plan.At(2500000)!=null||plan.At(3500000)?.Scene.ShotHud?.Model!=CueDirectionProjection.Model)throw new Exception("Historical clear/model alignment failed");
                outputs.Add(new{portrait,output=Path.GetFileName(output),report,journalSha256=plan.JournalSha256,outputSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
                    schema=journal.Schema,model=historical.ShotHud!.Model,assumptions=historical.ShotHud.Assumptions});
            }
            result=new{passed=true,source="Video from labelled still; declared historical geometry and source-timed HUD",outputs};
        }
        catch(Exception error){result=new{passed=false,error=error.ToString()};}
        var json=JsonSerializer.Serialize(result,JsonDefaults.Options);File.WriteAllText(Path.Combine(root,"hud-export-proof.json"),json);return json;
    }
}
