using SkyEye.Core;
using SkyEye.Core.Analysis;

static class RecordedRenderChecks
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static Task Run()
    {
        string hash=new('A',64);var source=new RecordedGeometrySource(hash,RecordedGeometryClock.ClipRelative,hash,0,8_000_000,1920,1080,0);
        var pockets=new PocketLayout([new(.1,.12),new(.88,.05),new(.91,.9),new(.08,.84)],[new(.48,.08)],.3);
        var journal=RecordedGeometry.Build(source,[new(0,pockets),new(2_000_000,pockets),new(4_000_000,null),new(6_000_000,pockets)]);
        var plan=RecordedRenderPlan.Create(journal,source);
        var mapped=pockets.Corners.Select(p=>plan.Pixel(1_000_000,p)!.Value).ToArray();
        Check(Math.Abs(mapped[0].Y-mapped[1].Y)<1e-6 && Math.Abs(mapped[2].Y-mapped[3].Y)<1e-6 && Math.Abs(mapped[0].X-mapped[3].X)<1e-6 && Math.Abs(mapped[1].X-mapped[2].X)<1e-6,"recorded corners map to exact rectangle");
        Check(Math.Abs((mapped[1].X-mapped[0].X)/(mapped[3].Y-mapped[0].Y)-2)<1e-6 && mapped.All(p=>p.X>0&&p.X<1920&&p.Y>0&&p.Y<1080),"physical aspect and pocket border retained");
        Check(plan.At(2_000_000)?.Corrected==false && (plan.Pixel(3_000_000,new(.3,.4))!.Value-new Point(576,432)).Length<1e-6,"uncertain intervals use whole-camera pixels and no stale transform");
        var portrait=RecordedRenderPlan.Create(journal,source,1080,1920,true);
        var p0=portrait.Pixel(1_000_000,pockets.Corners[0])!.Value;var p1=portrait.Pixel(1_000_000,pockets.Corners[1])!.Value;
        Check(Math.Abs(p0.X-p1.X)<1e-6&&p0.Y<p1.Y,"portrait orientation rotates geometry exactly once");
        Check(plan.At(-1)==null&&plan.At(source.DurationUs)==null,"presentation timestamp cannot escape source");
        var camera=source with { Clock=RecordedGeometryClock.CameraRecording,Timeline=Guid.NewGuid().ToString("N"),StartUs=500_000_000 };
        var timed=RecordedGeometry.Build(camera,[new(camera.StartUs,pockets),new(camera.StartUs+2_000_000,pockets)]);
        Check(RecordedRenderPlan.Create(timed,camera).At(1_000_000)?.Corrected==true,"camera origin translated to decoder-local PTS");
        try{RecordedRenderPlan.Create(journal,source with{ContentSha256=new('B',64)});throw new Exception("Expected source refusal");}catch(ArgumentException){}
        Console.WriteLine("PASS recorded compositor plan: rectangle/aspect/pocket margin, original fallback, portrait mapping, exclusive PTS and source/clock fencing");return Task.CompletedTask;
    }
}
