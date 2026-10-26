using SkyEye.Core;
using SkyEye.Core.Analysis;

static class RecordedRenderChecks
{
    static void Check(bool yes,string message){if(!yes)throw new Exception(message);}
    public static Task Run()
    {
        string hash=new('A',64);var source=new RecordedGeometrySource(hash,RecordedGeometryClock.ClipRelative,hash,0,10_000_000,1920,1080,0);
        var pockets=new PocketLayout([new(.1,.12),new(.88,.05),new(.91,.9),new(.08,.84)],[new(.48,.08)],.3);
        var journal=RecordedGeometry.Build(source,[new(0,pockets),new(2_000_000,pockets),new(4_000_000,null),new(6_000_000,pockets),new(8_000_000,pockets)]);
        var plan=RecordedRenderPlan.Create(journal,source);
        var mapped=pockets.Corners.Select(p=>plan.Pixel(1_000_000,p)!.Value).ToArray();
        Check(Math.Abs(mapped[0].Y-mapped[1].Y)<1e-6 && Math.Abs(mapped[2].Y-mapped[3].Y)<1e-6 && Math.Abs(mapped[0].X-mapped[3].X)<1e-6 && Math.Abs(mapped[1].X-mapped[2].X)<1e-6,"recorded corners map to exact rectangle");
        Check(Math.Abs((mapped[1].X-mapped[0].X)/(mapped[3].Y-mapped[0].Y)-2)<1e-6 && mapped.All(p=>p.X>0&&p.X<1920&&p.Y>0&&p.Y<1080),"physical aspect and pocket border retained");
        Check(plan.At(3_000_000)?.Corrected==true && plan.At(3_000_000)?.Held==true
            && (plan.Pixel(3_000_000,pockets.Corners[0])!.Value-plan.Pixel(1_000_000,pockets.Corners[0])!.Value).Length<1e-6,
            "short dropout bracketed by matching corners holds the same correction");
        Check(plan.At(9_000_000)?.Corrected==true && plan.At(9_000_000)?.Held==true
            && (plan.Pixel(9_000_000,pockets.Corners[0])!.Value-plan.Pixel(1_000_000,pockets.Corners[0])!.Value).Length<1e-6,
            "short fixed-camera tail keeps the last accepted render geometry");
        var moved=pockets with{Corners=[new(.14,.16),new(.92,.08),new(.95,.94),new(.12,.88)]};
        var movedJournal=new RecordedGeometryJournal(1,source,[new(0,2_000_000,1,pockets),new(2_000_000,4_000_000,0,null),new(4_000_000,10_000_000,2,moved)]);
        Check(RecordedRenderPlan.Create(movedJournal,source).At(3_000_000)?.Corrected==false,"dropout between different observed layouts keeps raw fallback");
        var movedTailJournal=new RecordedGeometryJournal(1,source,[new(0,2_000_000,1,pockets),new(2_000_000,8_000_000,2,moved),new(8_000_000,10_000_000,0,null)]);
        Check(RecordedRenderPlan.Create(movedTailJournal,source).At(9_000_000)?.Corrected==false,"short tail stays raw when the clip contains differing accepted layouts");
        var longSource=source with{DurationUs=12_000_000};
        var longGap=RecordedGeometry.Build(longSource,[new(0,pockets),new(2_000_000,pockets),new(4_000_000,null),new(6_000_000,null),new(8_000_000,null),new(10_000_000,pockets)]);
        var longPlan=RecordedRenderPlan.Create(longGap,longSource);
        Check(longPlan.At(5_000_000)?.Corrected==false&&!longPlan.At(5_000_000)!.Held,"long blind interval is never filled");
        var longTailSource=source with{DurationUs=16_000_000};
        var longTail=RecordedGeometry.Build(longTailSource,[new(0,pockets),new(2_000_000,pockets),new(4_000_000,pockets)]);
        Check(RecordedRenderPlan.Create(longTail,longTailSource).Spans.Last().Corrected==false,"long unobserved tail is never extended");
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
