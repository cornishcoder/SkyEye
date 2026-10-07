using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class ShotHudChecks
{
    public static void Run()
    {
        var now = DateTimeOffset.UnixEpoch;
        var info = new ShotHudInfo("fixture-shot", "direct", 1, "Geometric", "candidate-geometry-v1", []);
        var corridor = new SceneCorridor([new(.2,.3), new(.7,.3), new(.7,.32), new(.2,.32)], SceneInk.White);
        var scene = new TableScene(1, 2, 3, now, TimeSpan.FromSeconds(10),
            [new([new(.2,.31),new(.7,.31)],SceneInk.White,true,ScenePathRole.CueIncoming)],
            [new(new(.72,.31),.005,SceneInk.Yellow,SceneMarkerKind.Contact)], [], "Geometry", corridors:[corridor], shotHud:info);
        Check(scene.Schema == 2 && scene.Corridors.Length == 1 && scene.Paths[0].Role == ScenePathRole.CueIncoming, "Typed HUD geometry is a version-two scene");
        var recorded = JsonSerializer.Deserialize<RecordedScene>(JsonSerializer.Serialize(scene, JsonDefaults.Options), JsonDefaults.Options)!;
        recorded.Validate(); Check(recorded.ShotHud == info && recorded.Corridors.Length == 1, "Live scene captures semantic HUD and provenance for replay");
        var legacy = new RecordedScene(1, 3, [new([new(.2,.3),new(.7,.4)], SceneInk.White)], [], [], "Legacy", null);
        var legacyJson = JsonSerializer.Serialize(legacy, JsonDefaults.Options);
        Check(!legacyJson.Contains("schema") && !legacyJson.Contains("role") && !legacyJson.Contains("corridors") && !legacyJson.Contains("shotHud"),
            "Old journal scene bytes do not gain default extension fields or change fingerprints");
        JsonSerializer.Deserialize<RecordedScene>(legacyJson,JsonDefaults.Options)!.Validate();
        Refused(() => (recorded with { Schema = 1 }).Validate());
        Refused(() => new TableScene(1,2,3,now,TimeSpan.FromSeconds(10), [],[],[],corridors:[corridor with { Boundary=[new(-.1,.3),new(.5,.3),new(.5,.4)] }]));
        Refused(() => new TableScene(1,2,3,now,TimeSpan.FromSeconds(10), [],[],[],shotHud:info with { Provenance="Observed" }));
        Check(((int)ShotAnnotations.CueBallPath == 1) && ((int)ShotAnnotations.ShotLabel == 32)
            && ShotAnnotations.All.HasFlag(ShotAnnotations.ContactPoint), "Original masks retain their meanings while new roles are selectable");
        var shot = new Shot("cut", "red", 2, new(.946,.5), new(1,.5), new(1.9,.5), .8, "Fixture", 3)
            { CuePath = [new(.5,.7),new(.946,.5)], TargetPath = [new(1,.5),new(1.9,.5)] };
        var projection = GeometricCueProjection.From(shot);
        Check(projection.Direction is { X: 0, Y: < 0 } && projection.DisplayEnd is not null, "Cut direction is the correctly signed tangent, not the incoming cue line");
        Check(GeometricCueProjection.From(shot with { CuePath=[new(.5,.5),shot.GhostBall] }).Direction is null, "Full-ball contact has no arbitrary tangent");
        Check(GeometricCueProjection.From(shot with { Type="kick" }).Direction is null, "Cue-cushion routes remain unsupported");
        var calibration = new TableCalibration([new(.1,.1),new(.9,.1),new(.9,.9),new(.1,.9)]);
        var hudScene = ShotSceneProjection.Create(shot,calibration,2,2,now,now.AddSeconds(10),"Candidate 1",true,number:1);
        Check(hudScene.Paths.Count(p=>p.Role==ScenePathRole.CueProjected)==1 && hudScene.Corridors.Length==1
            && hudScene.Markers.Any(m=>m.Kind==SceneMarkerKind.Contact) && hudScene.ShotHud!.Assumptions.Length==2,
            "Geometric HUD separates corridor/contact/projected direction and preserves assumptions");
        var contact = calibration.Map(new(hudScene.Markers.Single(m=>m.Kind==SceneMarkerKind.Contact).Point.X,hudScene.Markers.Single(m=>m.Kind==SceneMarkerKind.Contact).Point.Y));
        Check((contact - new Point(.973,.5)).Length < 1e-9, "Surface contact is halfway between ghost/target, not ghost centre");
        var directionOnly = ShotSceneProjection.Create(shot,calibration,3,2,now,now.AddSeconds(10),"Candidate",true,ShotAnnotations.WhiteDirection);
        Check(directionOnly.Corridors.Length==0 && directionOnly.ShotHud!.Model==CueDirectionProjection.Model, "Direction-only mask retains provenance with minimal density");
        var bank = shot with { Type="bank", TargetPath=[shot.Target,new(1.973,.5),new(1.5,.1)] };
        var bankScene = ShotSceneProjection.Create(bank,calibration,4,2,now,now.AddSeconds(10),"Bank",true);
        Check(bankScene.Markers.Count(m=>m.Kind==SceneMarkerKind.Cushion)==1 && GeometricCueProjection.From(bank).Direction==projection.Direction,
            "Object-bank geometry marks its one planned rail while first-impact stun direction remains unchanged");
        var kick = shot with { Type="kick", CuePath=[new(.5,.7),new(.8,.973),shot.GhostBall] };
        var kickScene = ShotSceneProjection.Create(kick,calibration,5,2,now,now.AddSeconds(10),"Kick",true);
        Check(kickScene.Corridors.Length==2 && kickScene.Markers.Count(m=>m.Kind==SceneMarkerKind.Cushion)==1
            && !kickScene.Paths.Any(p=>p.Role==ScenePathRole.CueProjected) && kickScene.ShotHud!.ProjectionUnavailable is not null,
            "Kick HUD retains incoming rail geometry and explains unavailable post-contact projection");
        Check(GeometricCueProjection.From(shot with { TargetPath=[new(1.1,.5),shot.Destination] }).Direction is null
            && GeometricCueProjection.From(shot with { GhostBall=new(double.NaN,.5) }).Direction is null,
            "Mismatched and nonfinite collision geometry cannot invent a direction");
        var nearRail = shot with { GhostBall=new(.946,.05),Target=new(1,.05),CuePath=[new(.5,.25),new(.946,.05)],TargetPath=[new(1,.05),new(1.9,.05)] };
        Check(GeometricCueProjection.From(nearRail).Direction is null,"Too little rail clearance leaves direction unknown instead of implying a cushion model");
        for(int angle=-50;angle<=50;angle+=5)
        {
            if(angle==0)continue;
            double radians=angle*Math.PI/180;
            var before = shot.GhostBall - new Point(Math.Cos(radians),Math.Sin(radians)) * .4;
            var tested = GeometricCueProjection.From(shot with{CuePath=[before,shot.GhostBall]});
            Check(tested.Direction is{} d && Math.Abs(d.X)<1e-10 && Math.Sign(d.Y)==Math.Sign(angle), "Tangent sign and orthogonality hold on both cut sides");
        }
        var captured = JsonSerializer.Deserialize<RecordedScene>(JsonSerializer.Serialize(hudScene,JsonDefaults.Options),JsonDefaults.Options)!;
        var origin = new ClipRecordingOrigin(Guid.NewGuid().ToString("N"),1_000_000,1920,1080,0);
        var journal = new ClipAnnotationJournal(2,new('A',64),origin,2_000_000,"source-image-normalized-v1",
            [new(1_000_000,2_000_000,SceneTarget.Phone,new('B',64),captured)]);
        journal.Validate(); var restarted = JsonSerializer.Deserialize<ClipAnnotationJournal>(JsonSerializer.Serialize(journal,JsonDefaults.Options),JsonDefaults.Options)!;
        restarted.Validate(); Check(restarted.At(new('A',64),origin,500000,SceneTarget.Phone)!.ShotHud!.Model==CueDirectionProjection.Model,
            "Historical model/assumptions and corridor roles survive journal serialization at source time");
        Refused(()=> (journal with{Schema=1}).Validate());
        var directory=Path.Combine(Path.GetTempPath(),"skyeye-hud-"+Guid.NewGuid().ToString("N"));
        try
        {
            var clip=new Clip(Guid.NewGuid().ToString("N"),"Mixed HUD history",2,100,new('A',64),now,RecordingOrigin:origin);
            var store=new AnnotationJournalStore(directory);
            void Frame(long offset,RecordedScene? content)=>store.Observe(new(origin.Timeline,origin.StartUs+offset,SceneTarget.Phone,new('B',64),content));
            Frame(0,legacy);Frame(200000,legacy);Frame(400000,captured);Frame(600000,captured);Frame(800000,null);Frame(1000000,null);
            store.Flush(origin.Timeline);store.Preserve(clip);
            Frame(700_000_000,null);Frame(700_200_000,null);store.Flush(origin.Timeline);
            var retained=new AnnotationJournalStore(directory).Preserved(clip)!;
            Check(retained.Schema==2 && retained.At(clip.Sha256,origin,100000,SceneTarget.Phone)!.Caption=="Legacy"
                && retained.At(clip.Sha256,origin,500000,SceneTarget.Phone)!.ShotHud!.Assumptions.SequenceEqual(captured.ShotHud!.Assumptions)
                && retained.At(clip.Sha256,origin,900000,SceneTarget.Phone) is null,
                "Mixed legacy/v2 history, assumptions and clear survive durable preservation, rolling trim and process restart");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
        Console.WriteLine("PASS shot HUD contracts: typed corridor/contact/path roles, bounded provenance, live-to-recorded capture and legacy serialization compatibility");
    }
    static void Check(bool ok,string why) { if(!ok)throw new Exception(why); }
    static void Refused(Action action) { try{action();}catch(ArgumentException){return;}throw new Exception("Expected rejected HUD data"); }
}
