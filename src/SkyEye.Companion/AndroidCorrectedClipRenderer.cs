using Android.Content;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Security.Cryptography;
using System.Text.Json;

namespace SkyEye.Companion;

public sealed class AndroidCorrectedClipRenderer(Context context):ICorrectedClipRenderer
{
    readonly AndroidRecordedGeometry geometry=new(Path.Combine(context.FilesDir!.AbsolutePath,"recorded-geometry"));
    async Task<RecordedGeometryJournal> Journal(Clip clip,string original,CancellationToken ct)=>await geometry.AnalyzeAsync(original,clip.Sha256,clip.RecordingOrigin,ct);
    static CorrectedRenderInfo Info(RecordedGeometryJournal journal)=>new("perspective-v1:extract-v2:1080p:audio-original",
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(journal,JsonDefaults.Options))),journal.Spans.Where(s=>s.Layout is not null).Sum(s=>(s.EndUs-s.StartUs)/1_000_000d),journal.Source.DurationUs/1_000_000d);
    public async Task<CorrectedRenderInfo> PrepareAsync(Clip clip,string original,CancellationToken cancellation)=>Info(await Journal(clip,original,cancellation));
    public async Task RenderAsync(Clip clip,string original,string portrait,string landscape,CorrectedRenderInfo expected,CancellationToken cancellation)
    {
        var journal=await Journal(clip,original,cancellation);if(Info(journal)!=expected)throw new IOException("Historical geometry changed during preparation.");
        var renderer=new AndroidRecordedRenderer(context);
        var portraitReport=await renderer.RenderAsync(original,portrait,RecordedRenderPlan.Create(journal,journal.Source,1080,1920,true),cancellation);
        var landscapeReport=await renderer.RenderAsync(original,landscape,RecordedRenderPlan.Create(journal,journal.Source),cancellation);
        Android.Util.Log.Info("SkyEyeCorrected",JsonSerializer.Serialize(new{clipId=clip.Id,sourceSha256=clip.Sha256,expected.GeometrySha256,expected.CorrectedSeconds,expected.Duration,
            portrait=portraitReport.GetProperty("after"),landscape=landscapeReport.GetProperty("after")},JsonDefaults.Options));
    }
}
