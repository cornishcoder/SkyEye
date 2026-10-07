using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    AnnotationJournalStore annotations = null!;
    string? annotationTimeline;
    Task annotationWrite = Task.CompletedTask;
    long nextAnnotationFlush, nextAnnotationCatalogue;
    void InitializeAnnotations() => annotations = new(Path.Combine(FileSystem.AppDataDirectory, "recorded-annotations"));
    void BindAnnotationClock()
    {
        if (player is null || selection.Kind != ViewingSource.Live || !CanCastLive || effects.Latest is not { } view) return;
        if (!view.Status.TryGetProperty("timeline", out var timeline) || !view.Status.TryGetProperty("sourceTimeUs", out var time)) return;
        var id = timeline.GetString(); if (!Guid.TryParseExact(id, "N", out _)) return;
        annotationTimeline = id;
        player.SetRecordingClockSample(id, time.GetInt64(), MediaGeneration(),
            view.StatusReceiptTimestamp == 0 ? 10001 : System.Diagnostics.Stopwatch.GetElapsedTime(view.StatusReceiptTimestamp).TotalMilliseconds, view.StatusRoundTripMs);
    }
    void CollectAnnotations()
    {
        if (player is null) return;
        try
        {
            using var batch = JsonDocument.Parse(player.DrainAnnotations()!);
            foreach (var sample in batch.RootElement.EnumerateArray())
            {
                var scene = sample.GetProperty("scene");
                var content = scene.ValueKind == JsonValueKind.Null ? null : scene.Deserialize<RecordedScene>(JsonDefaults.Options);
                annotations.Observe(new(sample.GetProperty("timeline").GetString()!, sample.GetProperty("sourceUs").GetInt64(),
                    (SceneTarget)sample.GetProperty("target").GetInt32(),
                    AnnotationJournalStore.Fingerprint(sample.GetProperty("geometry").GetString()!), content));
            }
            if (annotationTimeline is { } timeline && annotationWrite.IsCompleted && clock.ElapsedMilliseconds >= nextAnnotationFlush)
            {
                nextAnnotationFlush = clock.ElapsedMilliseconds + 1000;
                bool catalogue = clock.ElapsedMilliseconds >= nextAnnotationCatalogue && CanLoadClips;
                if (catalogue) nextAnnotationCatalogue = clock.ElapsedMilliseconds + 10000;
                // No per-frame disk or network IO on the UI thread.
                annotationWrite = PersistAnnotationsAsync(timeline, catalogue);
            }
        }
        catch (Exception) { Android.Util.Log.Warn("SkyEyeAnnotations", "Annotation observation unavailable; leaving historical gap."); }
    }
    void CheckpointAnnotations()
    {
        CollectAnnotations();
        if(annotationTimeline is{} timeline) _=PersistAnnotationsAsync(timeline,false);
    }
    async Task PersistAnnotationsAsync(string timeline, bool catalogue)
    {
        try
        {
            await Task.Run(() => annotations.Flush(timeline));
            if (!catalogue) return;
            using var frozen = client.CopyForViewing();
            var clips = await frozen.ClipsAsync(clipLifetime.Token);
            await Task.Run(() => { foreach (var clip in clips.Where(c => c.RecordingOrigin?.Timeline == timeline)) annotations.Preserve(clip); });
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Android.Util.Log.Warn("SkyEyeAnnotations", "Annotation persistence unavailable; capture continues."); }
    }
    public Task CheckAnnotationJournalAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!IsLab || !CanSceneCheck || foreground is null || player is null) return;
        var token = foreground.Token;
        try
        {
            await ToggleSceneCheckAsync();
            if (!SceneCheck || player is null || annotationTimeline is null) return;
            var owner = player; var media = MediaGeneration(); var timeline = annotationTimeline;
            using var before = JsonDocument.Parse(owner.AnnotationMetrics()!);
            long start = before.RootElement.GetProperty("phoneSourceUs").GetInt64();
            if (start < 0) throw new InvalidOperationException("No mapped live frame.");
            foreach (var label in new[] { "Journal candidate 1", "Journal candidate 2", "Journal pinned" })
            {
                var scene = new TableScene(++sceneRevision, media, 7, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(3),
                    [new([new(.25,.3),new(.75,.7)],SceneInk.Cyan,true)], [], [], label);
                if (!owner.SetTableScene(JsonSerializer.Serialize(scene, sceneJson))) throw new InvalidOperationException("Fixture publication rejected.");
                await Task.Delay(2000, token); CollectAnnotations();
            }
            owner.ClearTableScene(); await Task.Delay(1000,token); CollectAnnotations();
            var expiring = new TableScene(++sceneRevision,media,7,DateTimeOffset.UtcNow,TimeSpan.FromSeconds(1),[],[],[],"Journal expiry");
            if (!owner.SetTableScene(JsonSerializer.Serialize(expiring,sceneJson))) throw new InvalidOperationException("Expiry publication rejected.");
            await Task.Delay(2000,token);CollectAnnotations();
            using var after=JsonDocument.Parse(owner.AnnotationMetrics()!);
            long end=after.RootElement.GetProperty("phoneSourceUs").GetInt64();
            if(end<=start)throw new InvalidOperationException("Mapped frames did not advance.");
            var fixture=new Clip(Guid.NewGuid().ToString("N"),"Declared journal fixture",(end-start)/1_000_000d,1,new('0',64),DateTimeOffset.UtcNow,
                RecordingOrigin:new(timeline,start,1920,1080,0));
            var journal=annotations.Window(fixture);
            string[] expected=["Journal candidate 1","Journal candidate 2","Journal pinned","Journal expiry"];
            foreach(var target in new[]{SceneTarget.Phone,SceneTarget.Tv})
                if(expected.Any(label=>!journal.Spans.Any(s=>s.Target==target&&s.Scene?.Caption==label))
                    || !journal.Spans.Any(s=>s.Target==target&&s.Scene is null))throw new InvalidOperationException("Observed journal fixture incomplete.");
            annotations.Flush(timeline);
            var reloaded=new AnnotationJournalStore(Path.Combine(FileSystem.AppDataDirectory,"recorded-annotations")).Window(fixture);
            if(JsonSerializer.Serialize(journal,JsonDefaults.Options)!=JsonSerializer.Serialize(reloaded,JsonDefaults.Options))throw new InvalidOperationException("Journal changed after reload.");
            var path=Path.Combine(Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath,"annotation-check.json");
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new{passed=true,declaredSceneFixture=true,sourceTimestampMethod="decoded TS PTS minus extractor adjustment, camera wrap epoch",journal,decoder=after.RootElement.Clone()},JsonDefaults.Options),token);
            SceneCheckStatus="Annotation journal passed: cycle, pin, clear, expiry and reload.";
        }
        catch(OperationCanceledException){}
        catch(Exception e){SceneCheckStatus="Annotation journal check failed: "+e.Message;}
        finally{var result=SceneCheckStatus;ClearSceneCheck();SceneCheckStatus=result;if(!disposed)Publish();}
    });
    CorrectedClip? checkedAnnotatedReplay;
    public Task CheckAnnotatedExportAsync()=>MainThread.InvokeOnMainThreadAsync(async()=>
    {
        if(!IsLab||foreground is null||!CanSceneCheck)return;var token=foreground.Token;
        try
        {
            await CheckAnnotationJournalAsync();
            if(!SceneCheckStatus.StartsWith("Annotation journal passed"))return;
            SceneCheckStatus="Preparing declared annotated replay…";Publish();
            var replay=await clips.RecentAsync(30,token);var prepared=await correctedClips.RecentAsync(replay,token);
            var journal=JsonSerializer.Deserialize<ClipAnnotationJournal>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(prepared.Original)!,"annotations.json")),JsonDefaults.Options)!;
            journal.Validate();if(journal.SourceSha256!=replay.Asset.Clip.Sha256||journal.Origin!=replay.Asset.Clip.RecordingOrigin
                ||new[]{"Journal candidate 1","Journal candidate 2","Journal pinned","Journal expiry"}.Any(label=>!journal.Spans.Any(s=>s.Scene?.Caption==label)))throw new IOException("Actual replay history incomplete.");
            string root=Android.App.Application.Context.GetExternalFilesDir(null)!.AbsolutePath;
            foreach(var pair in new[]{(prepared.Original,"live-original.mp4"),(prepared.Portrait,"live-portrait.mp4"),(prepared.Landscape,"live-landscape.mp4")}){var path=Path.Combine(root,pair.Item2);File.Copy(pair.Item1,path,true);File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.GroupRead|UnixFileMode.OtherRead);}
            await File.WriteAllTextAsync(Path.Combine(root,"live-annotations.json"),JsonSerializer.Serialize(journal,JsonDefaults.Options),token);
            await File.WriteAllTextAsync(Path.Combine(root,"live-clip.json"),JsonSerializer.Serialize(replay.Asset.Clip,JsonDefaults.Options),token);
            await File.WriteAllTextAsync(Path.Combine(root,"annotated-export-check.json"),JsonSerializer.Serialize(new{passed=true,declaredLiveFixture=true,clip=replay.Asset.Clip,prepared.Manifest.Render,
                prepared.Manifest.PortraitSha256,prepared.Manifest.LandscapeSha256,journalSha256=RecordedAnnotationPlan.Fingerprint(journal)},JsonDefaults.Options),token);
            checkedAnnotatedReplay=prepared;SceneCheckStatus="Annotated replay prepared · phone and TV files ready.";
            if(TvConnected){var clip=replay.Asset.Clip;tvLive.Clear();var request=tvReplay.Begin(replay.ConnectionId,clip.Id,"Declared annotated replay",clip.Duration);
                cast!.PlayClip(correctedClips.Serve(prepared,true),clip.Id,"Declared annotated replay",(long)Math.Ceiling(clip.Duration*1000),request);}
        }
        catch(OperationCanceledException){}
        catch(Exception e){SceneCheckStatus="Annotated export check failed: "+e.Message;}
        finally{if(!disposed)Publish();}
    });
}
