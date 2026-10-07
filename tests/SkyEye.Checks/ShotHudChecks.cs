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
        Console.WriteLine("PASS shot HUD contracts: typed corridor/contact/path roles, bounded provenance, live-to-recorded capture and legacy serialization compatibility");
    }
    static void Check(bool ok,string why) { if(!ok)throw new Exception(why); }
    static void Refused(Action action) { try{action();}catch(ArgumentException){return;}throw new Exception("Expected rejected HUD data"); }
}
