using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Text.Json;

static class RecordedAnnotationChecks
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Refused(Action action) { try { action(); throw new Exception("Expected rejected annotations"); } catch (ArgumentException) { } }
    public static Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "skyeye-annotations-" + Guid.NewGuid().ToString("N"));
        string timeline = Guid.NewGuid().ToString("N"), geometry = AnnotationJournalStore.Fingerprint("mount-A"), moved = AnnotationJournalStore.Fingerprint("mount-B");
        var origin = new ClipRecordingOrigin(timeline, 100_000_000, 1920, 1080, 0);
        var clip = new Clip(Guid.NewGuid().ToString("N"), "Annotated fixture", 12, 100, new('A', 64), DateTimeOffset.UtcNow, RecordingOrigin: origin);
        RecordedScene Scene(long version, string caption) => new(version, 7,
            [new([new(.2,.3),new(.8,.7)],SceneInk.Cyan,true)],[],[new(new(.4,.5),"Recorded highlight")],caption,new("A",1,"B",2));
        var first = Scene(1, "Candidate 1" ); var second = Scene(2, "Candidate 2"); var pinned = Scene(3,"Pinned");
        try
        {
            var store = new AnnotationJournalStore(root);
            void Frame(double seconds, RecordedScene? scene, SceneTarget target=SceneTarget.Phone, string? mount=null)
                => store.Observe(new(timeline, origin.StartUs+(long)(seconds*1_000_000),target,mount??geometry,scene));
            // Arrival time is irrelevant: the fixture's camera/source PTS is authoritative.
            for(int i=0;i<=10;i++)Frame(i*.2,first);
            Frame(2,second); // repeated frame must not rewrite what was displayed
            for(int i=11;i<=20;i++)Frame(i*.2,second);
            for(int i=21;i<=25;i++)Frame(i*.2,pinned);
            for(int i=26;i<=30;i++)Frame(i*.2,null); // actual clear/expiry observed
            Frame(8,second);Frame(8.2,second); // stall gap is unannotated
            Frame(8.4,null, mount:moved);Frame(8.6,null,mount:moved);
            Frame(9,Scene(4,"Recovered"),mount:moved);Frame(9.2,Scene(4,"Recovered"),mount:moved);
            for(int i=0;i<=10;i++)Frame(i*.2,Scene(10,"TV pinned"),SceneTarget.Tv);
            store.Flush(timeline);
            var journal=store.Window(clip);
            Check(journal.At(clip.Sha256,origin,1_000_000,SceneTarget.Phone)?.Caption=="Candidate 1", "source-time delayed frame retains first candidate");
            Check(journal.At(clip.Sha256,origin,3_000_000,SceneTarget.Phone)?.Caption=="Candidate 2", "two-second rotation survives historical selection");
            Check(journal.At(clip.Sha256,origin,4_800_000,SceneTarget.Phone)?.Caption=="Pinned", "pin follows observed publication");
            Check(journal.At(clip.Sha256,origin,5_800_000,SceneTarget.Phone) is null && journal.At(clip.Sha256,origin,7_000_000,SceneTarget.Phone) is null,"clear, expiry and stalled delivery do not carry stale scenes");
            Check(journal.At(clip.Sha256,origin,1_000_000,SceneTarget.Tv)?.Caption=="TV pinned", "phone and TV histories remain distinct");
            Check(journal.At(clip.Sha256,origin,3_000_000,SceneTarget.Phone,8) is null && journal.At(clip.Sha256,origin,9_100_000,SceneTarget.Phone,7,geometry) is null,"invalidated layout and moved mount reject incompatible scene");
            Check(journal.At(clip.Sha256,origin,9_100_000,SceneTarget.Phone,7,moved)?.Caption=="Recovered", "new mount uses new geometry provenance");
            Check(journal.At(new('B',64),origin,1_000_000,SceneTarget.Phone) is null && journal.At(clip.Sha256,origin with{Timeline=Guid.NewGuid().ToString("N")},1_000_000,SceneTarget.Phone) is null,"source checksum and unrelated timeline fence replay");
            Check(store.Window(clip with{RecordingOrigin=null}).Spans.Length==0,"legacy footage never receives current annotations");
            store.Preserve(clip);
            Frame(700,null);Frame(700.2,null);store.Flush(timeline);
            var restarted=new AnnotationJournalStore(root);
            Check(restarted.Window(clip).Spans.Length==0 && restarted.Preserved(clip)?.At(clip.Sha256,origin,3_000_000,SceneTarget.Phone)?.Caption=="Candidate 2","permanent sidecar survives rolling trim and restart");
            restarted.Preserve(clip);
            Check(restarted.Preserved(clip)?.Spans.Length==journal.Spans.Length,"later empty rolling window cannot erase retained history");
            var decoded=JsonSerializer.Deserialize<ClipAnnotationJournal>(JsonSerializer.Serialize(journal,JsonDefaults.Options),JsonDefaults.Options)!;decoded.Validate();
            Check(decoded.At(clip.Sha256,origin,1_000_000,SceneTarget.Phone)?.Paths[0].Points[1]==new ScenePoint(.8,.7),"moving-frame source coordinates survive round trip without viewport warp");
            Refused(()=>store.Observe(new(timeline,1,SceneTarget.Both,geometry,first)));
            Refused(()=>store.Observe(new(timeline,1,SceneTarget.Phone,geometry,first with{Caption=new('x',97)})));
            Refused(()=> (journal with{Spans=[new(100_000_000,101_000_000,SceneTarget.Phone,geometry,null!),null!]}).Validate());
            File.WriteAllText(Path.Combine(root,timeline+".json"),"{");
            Check(new AnnotationJournalStore(root).Window(clip).Spans.Length==0,"corrupt recovery leaves absent annotations rather than fabricating content");
            Console.WriteLine("PASS recorded annotations: source timestamps, cycle/pin/clear/expiry, target and geometry/layout fencing, gaps, legacy footage, bounded trim, permanent clip preservation and restart/corruption recovery");
        }
        finally { if(!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected fixture root"); Directory.Delete(root,true); }
        return Task.CompletedTask;
    }
}
