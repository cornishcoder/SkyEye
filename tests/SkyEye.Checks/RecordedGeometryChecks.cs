using System.Collections.Immutable;
using SkyEye.Core;
using SkyEye.Core.Analysis;

static class RecordedGeometryChecks
{
    static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
    static void Refused(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected geometry refusal"); }
    public static async Task Run()
    {
        string hash = new('A', 64); var source = new RecordedGeometrySource(hash, RecordedGeometryClock.ClipRelative, hash, 0, 14_000_000, 1920, 1080, 0);
        PocketLayout Layout(double shift, double contrast = .2) => new([new(.1 + shift,.1),new(.8 + shift,.12),new(.8 + shift,.9),new(.1 + shift,.88)], [new(.45 + shift,.11)], contrast);
        var first = Layout(0); var moved = Layout(.04);
        var samples = new[] { new RecordedPocketSample(0,first), new(2_000_000,first), new(4_000_000,moved), new(6_000_000,moved), new(8_000_000,null), new(10_000_000, moved), new(12_000_000,moved) };
        var journal = RecordedGeometry.Build(source, samples);
        Check(journal.At(source,1_000_000)?.GeometryVersion == 1 && journal.At(source,3_000_000)?.Layout is null, "mount change invalidates entire unobserved transition, not just new frame");
        Check(journal.At(source,5_000_000)?.GeometryVersion == 2 && journal.At(source,7_000_000)?.Layout is null && journal.At(source,9_000_000)?.Layout is null, "covered pockets clear geometry and do not carry it across blind interval");
        Check(journal.At(source,11_000_000)?.GeometryVersion == 3 && journal.At(source,13_000_000)?.Layout is null, "recovery needs stable recorded frames and tail remains uncertain");
        Check(journal.At(source,source.EndUs) is null && journal.At(source with { ContentSha256 = new('B',64) },1_000_000) is null, "exclusive intervals cannot cross content boundaries");
        var window = journal.Window(source,1_000_000,5_000_000);
        Check(window[0].StartUs == 1_000_000 && window[^1].EndUs == 5_000_000 && window.Any(s=>s.Layout is null), "clip window preserves historical geometry and uncertain spans");
        Refused(()=>journal.Window(source with { Rotation = 90 },0,2_000_000));
        Refused(()=>RecordedGeometry.Build(source,[new(2_000_000,first),new(1_000_000,first)]));
        Refused(()=>RecordedGeometry.Build(source,[new(14_000_000,first)]));
        Check(RecordedGeometry.Build(source,[new(0,first),new(6_000_000,first)]).Spans.All(s=>s.Layout is null), "long observation gap cannot masquerade as stable corners");
        Check(RecordedGeometry.Build(source,[new(0,Layout(0,.01)),new(2_000_000,Layout(0,.01))]).Spans.All(s=>s.Layout is null), "weak pocket feature score remains uncertain");
        var timed = source with { Clock = RecordedGeometryClock.CameraRecording, Timeline = Guid.NewGuid().ToString("N"), StartUs = 800_000_000 };
        var timedJournal = RecordedGeometry.Build(timed,[new(800_000_000, first),new(802_000_000,first)]);
        Check(timedJournal.At(timed,801_000_000)?.Layout is not null && timedJournal.At(source,1_000_000) is null, "camera monotonic origin remains separate from legacy clip-local PTS");
        Refused(()=>RecordedGeometry.Build(source with { Timeline = timed.Timeline },[]));
        Refused(()=> (journal with { Spans = [new(0,source.EndUs,1,null)] }).Validate());
        string root = Path.Combine(Path.GetTempPath(),"skyeye-geometry-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RecordedGeometryStore(root); await store.SaveAsync(journal,default);
            var loaded = new RecordedGeometryStore(root).Load(source);
            Check(loaded?.At(source,5_000_000)?.GeometryVersion == 2 && loaded.At(source,7_000_000)?.Layout is null, "atomic geometry metadata recovers exact historical intervals after restart");
            Check(store.Load(source with { Width = 1280 }) is null && store.Load(timed) is null, "orientation/dimensions and timeline cannot reuse another journal");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await store.SaveAsync(timedJournal,cancel.Token); throw new Exception("Expected cancelled publication"); } catch(OperationCanceledException) { }
            Check(store.Load(timed) is null && !Directory.EnumerateFiles(root,"*.part").Any(), "cancelled publication leaves no partial journal");
            await File.WriteAllTextAsync(Directory.GetFiles(root,"*.json").Single(),System.Text.Json.JsonSerializer.Serialize(new { schema=1,source,spans=new object?[]{null} },JsonDefaults.Options));
            Check(store.Load(source) is null, "null interval in otherwise valid JSON cannot crash journal recovery");
            await File.WriteAllTextAsync(Directory.GetFiles(root,"*.json").Single(),"{");
            Check(store.Load(source) is null, "corrupt geometry metadata requests re-analysis rather than applying corners");
            Console.WriteLine("PASS recorded geometry: source/timeline/dimension fencing, moved mounts, dim/covered/gap fallback, recovery, exact windows and atomic restart/cancellation/corruption handling");
        }
        finally { if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected fixture root"); Directory.Delete(root,true); }
    }
}
