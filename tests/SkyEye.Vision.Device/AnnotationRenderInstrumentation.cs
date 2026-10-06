using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name="uk.co.skyeye.visionchecks.AnnotationRenderInstrumentation",TargetPackage="uk.co.skyeye.visionchecks",FunctionalTest=true)]
[global::Android.Runtime.Preserve(AllMembers=true)]
public sealed class AnnotationRenderInstrumentation:global::Android.App.Instrumentation
{
    public AnnotationRenderInstrumentation() { }
    public AnnotationRenderInstrumentation(IntPtr handle,global::Android.Runtime.JniHandleOwnership ownership):base(handle,ownership) { }
    bool portrait;
    public override void OnCreate(global::Android.OS.Bundle? args){base.OnCreate(args);portrait=args?.GetString("portrait")=="true";Start();}
    public override void OnStart(){base.OnStart();var json=RunAsync().GetAwaiter().GetResult();using var bundle=new global::Android.OS.Bundle();bundle.PutString("result",json);Finish(global::Android.App.Result.Ok,bundle);}
    async Task<string> RunAsync()
    {
        var context=TargetContext!;string root=context.GetExternalFilesDir(null)!.AbsolutePath;object result;
        try
        {
            string input=Path.Combine(root,"render-input.mp4"),output=Path.Combine(root,portrait?"annotated-portrait.mp4":"annotated-landscape.mp4"),plain=Path.Combine(root,"annotation-control.mp4");
            foreach(var path in new[]{output,plain})if(File.Exists(path))File.Delete(path);
            await using var file=File.OpenRead(input);string hash=Convert.ToHexString(await SHA256.HashDataAsync(file));file.Close();
            var raw=await new SkyEye.Companion.AndroidRecordedGeometry(Path.Combine(context.FilesDir!.AbsolutePath,"render-geometry")).AnalyzeAsync(input,hash,null,default);
            string timeline=Guid.NewGuid().ToString("N");long start=1_000_000_000;
            var source=raw.Source with{Clock=RecordedGeometryClock.CameraRecording,Timeline=timeline,StartUs=start};
            var geometry=raw with{Source=source,Spans=raw.Spans.Select(s=>s with{StartUs=s.StartUs+start,EndUs=s.EndUs+start}).ToImmutableArray()};
            var video=RecordedRenderPlan.Create(geometry,source,portrait?1080:1920,portrait?1920:1080,portrait);
            var origin=new ClipRecordingOrigin(timeline,start,source.Width,source.Height,0);var target=portrait?SceneTarget.Phone:SceneTarget.Tv;
            RecordedScene Scene(long version,SceneInk ink)=>new(version,7,[new([new(.3,.4),new(.7,.6)],ink,true)],
                [new(new(.2,.3),.025,ink,SceneMarkerKind.Ghost)],[new(new(.7,.6),"Historical note")],"Declared annotation fixture",new("A",2,"B",1));
            var journal=new ClipAnnotationJournal(1,hash,origin,source.DurationUs,"source-image-normalized-v1",[
                new(start,start+2_000_000,target,new('A',64),Scene(1,SceneInk.Cyan)),new(start+2_000_000,start+4_000_000,target,new('A',64),Scene(2,SceneInk.Red)),
                new(start+4_000_000,start+6_000_000,target,new('A',64),null),new(start+6_000_000,start+8_000_000,target,new('A',64),Scene(3,SceneInk.Yellow)),new(start+8_000_000,source.EndUs,target,new('A',64),null)]);
            var plan=RecordedAnnotationPlan.Create(video,journal,target);var renderer=new SkyEye.Companion.AndroidRecordedRenderer(context);
            var report=await renderer.RenderAsync(input,output,video,annotations:plan);await renderer.RenderAsync(input,plain,video);
            var points=new[]{(500_000L,1),(2_500_000L,3),(4_500_000L,-1),(6_500_000L,2),(9_000_000L,-1)}.Select(t=>{var p=video.Pixel(t.Item1,new(.5,.5))!.Value;return new{ptsUs=t.Item1,ink=t.Item2,x=p.X,y=p.Y};});
            var pixels=JsonSerializer.Deserialize<JsonElement>(UK.CO.Skyeye.Companion.Nativebridge.AnnotationPixelProof.Verify(output,plain,JsonSerializer.Serialize(points,JsonDefaults.Options))!);
            result=new{passed=true,declaredFixture=true,portrait,journalSha256=plan.JournalSha256,pixels,report,outputSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output)))};
        }
        catch(Exception e){result=new{passed=false,portrait,error=e.ToString()};}
        var json=JsonSerializer.Serialize(result,JsonDefaults.Options);await File.WriteAllTextAsync(Path.Combine(root,portrait?"annotation-portrait-checks.json":"annotation-landscape-checks.json"),json);return json;
    }
}
