using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class RecordedAnnotationPlanChecks
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static Task Run()
    {
        string hash=new('A',64),timeline=Guid.NewGuid().ToString("N");long start=500_000_000;
        var source=new RecordedGeometrySource(hash,RecordedGeometryClock.CameraRecording,timeline,start,8_000_000,1920,1080,0);
        var pockets=new PocketLayout([new(.1,.1),new(.9,.1),new(.9,.9),new(.1,.9)],[new(.5,.1)],.3);
        var moved=pockets with{Corners=[new(.15,.15),new(.85,.12),new(.88,.85),new(.12,.88)]};
        var geometry=new RecordedGeometryJournal(1,source,[new(start,start+2_000_000,1,pockets),new(start+2_000_000,start+4_000_000,0,null),new(start+4_000_000,start+8_000_000,2,moved)]);
        RecordedScene Scene(long version,string text)=>new(version,7,[new([new(.3,.4),new(.7,.6)],SceneInk.Cyan)],[],[],text,null);
        var origin=new ClipRecordingOrigin(timeline,start,1920,1080,0);
        var annotations=new ClipAnnotationJournal(1,hash,origin,8_000_000,"source-image-normalized-v1",[
            new(start,start+3_000_000,SceneTarget.Phone,new('B',64),Scene(1,"first")),
            new(start+3_000_000,start+5_000_000,SceneTarget.Phone,new('B',64),null),
            new(start+5_000_000,start+7_000_000,SceneTarget.Phone,new('C',64),Scene(2,"second")),
            new(start,start+6_000_000,SceneTarget.Tv,new('B',64),Scene(3,"TV"))]);
        foreach(bool portrait in new[]{false,true})
        {
            var video=RecordedRenderPlan.Create(geometry,source,portrait?1080:1920,portrait?1920:1080,portrait);
            var plan=RecordedAnnotationPlan.Create(video,annotations,SceneTarget.Phone);
            foreach(long pts in new[]{1_000_000L,2_500_000L,5_500_000L})
            {
                var span=plan.At(pts)!;var m=span.PixelMatrix;double x=.4,y=.5,d=m[6]*x+m[7]*y+m[8];
                var mapped=new Point((m[0]*x+m[1]*y+m[2])/d,(m[3]*x+m[4]*y+m[5])/d);
                Check((mapped-video.Pixel(pts,new(x,y))!.Value).Length<1e-6,"annotation and actual video share mapping across fallback, moved geometry and portrait rotation");
            }
            Check(plan.At(3_500_000) is null&&plan.At(7_500_000) is null,"clear/expiry/gaps remove drawings at historical PTS");
            Check(RecordedAnnotationPlan.Create(video,annotations,SceneTarget.Tv).At(3_500_000)?.Scene.Caption=="TV","target-specific scenes cannot leak into another output");
            try{RecordedAnnotationPlan.Create(video,annotations with{SourceSha256=new('D',64)},SceneTarget.Phone);throw new Exception("Expected unrelated-source rejection");}catch(ArgumentException){}
        }
        var changed=annotations with{Spans=annotations.Spans.SetItem(0,annotations.Spans[0] with{Scene=Scene(4,"amended")})};
        Check(RecordedAnnotationPlan.Fingerprint(changed)!=RecordedAnnotationPlan.Fingerprint(annotations),"journal amendment invalidates derivative identity");
        Console.WriteLine("PASS annotation compositor plan: exact video mapping, portrait, moved/fallback geometry, source/target fencing, historical clears and amendment fingerprint");return Task.CompletedTask;
    }
}
