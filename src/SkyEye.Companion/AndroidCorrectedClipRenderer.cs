using Android.Content;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkyEye.Companion;

public sealed class AndroidCorrectedClipRenderer(Context context):ICorrectedClipRenderer,ICorrectedClipSnapshotRenderer
{
    readonly AndroidRecordedGeometry geometry=new(Path.Combine(context.FilesDir!.AbsolutePath,"recorded-geometry"));
    async Task<RecordedGeometryJournal> Journal(Clip clip,string original,CancellationToken ct)=>await geometry.AnalyzeAsync(original,clip.Sha256,clip.RecordingOrigin,ct);
    ClipAnnotationJournal? LocalJournal(Clip clip,string original)
    {
        var path=Path.Combine(Path.GetDirectoryName(original)!,"annotations.json");if(!File.Exists(path))return null;
        if(new FileInfo(path).Length>=4_000_000)throw new IOException("Annotation snapshot exceeds budget.");
        var result=JsonSerializer.Deserialize<ClipAnnotationJournal>(File.ReadAllText(path),JsonDefaults.Options)??throw new IOException("Missing annotation snapshot.");
        result.Validate();if(result.SourceSha256!=clip.Sha256||result.Origin!=clip.RecordingOrigin)throw new IOException("Annotation snapshot differs from clip.");return result;
    }
    ClipAnnotationJournal Latest(Clip clip,string original)
    {
        var store=new AnnotationJournalStore(Path.Combine(context.FilesDir!.AbsolutePath,"recorded-annotations"));
        return store.Preserved(clip)??LocalJournal(clip,original)??store.Window(clip);
    }
    CorrectedRenderInfo Info(RecordedGeometryJournal journal,ClipAnnotationJournal annotations)
    {
        var plan=RecordedRenderPlan.Create(journal,journal.Source);
        return new("perspective-v2-stable-gap-hold:extract-v2:1080p:audio-original:"+UK.CO.Skyeye.Companion.Nativebridge.WatermarkRenderer.Profile(context)+":annotations-v1:"+RecordedAnnotationPlan.Fingerprint(annotations),
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(journal,JsonDefaults.Options))),plan.Spans.Where(s=>s.Corrected).Sum(s=>(s.EndUs-s.StartUs)/1_000_000d),journal.Source.DurationUs/1_000_000d);
    }
    public async Task<CorrectedRenderInfo> PrepareAsync(Clip clip,string original,CancellationToken cancellation)=>Info(await Journal(clip,original,cancellation),Latest(clip,original));
    public async Task<CorrectedRenderInfo> PrepareSnapshotAsync(Clip clip,string original,CancellationToken cancellation)=>Info(await Journal(clip,original,cancellation),LocalJournal(clip,original)??throw new IOException("Missing frozen annotation snapshot."));
    public async Task RenderAsync(Clip clip,string original,string portrait,string landscape,CorrectedRenderInfo expected,CancellationToken cancellation)
    {
        var journal=await Journal(clip,original,cancellation);var annotations=LocalJournal(clip,original)??Latest(clip,original);if(Info(journal,annotations)!=expected)throw new IOException("Historical geometry changed during preparation.");
        var renderer=new AndroidRecordedRenderer(context);
        var phonePlan=RecordedRenderPlan.Create(journal,journal.Source,1080,1920,true);var tvPlan=RecordedRenderPlan.Create(journal,journal.Source);
        var portraitReport=await renderer.RenderAsync(original,portrait,phonePlan,cancellation,annotations:RecordedAnnotationPlan.Create(phonePlan,annotations,SkyEye.Core.Application.SceneTarget.Phone));
        var landscapeReport=await renderer.RenderAsync(original,landscape,tvPlan,cancellation,annotations:RecordedAnnotationPlan.Create(tvPlan,annotations,SkyEye.Core.Application.SceneTarget.Tv));
        Android.Util.Log.Info("SkyEyeCorrected",JsonSerializer.Serialize(new{clipId=clip.Id,sourceSha256=clip.Sha256,expected.GeometrySha256,expected.CorrectedSeconds,expected.Duration,
            portrait=portraitReport.GetProperty("after"),landscape=landscapeReport.GetProperty("after")},JsonDefaults.Options));
    }
}
