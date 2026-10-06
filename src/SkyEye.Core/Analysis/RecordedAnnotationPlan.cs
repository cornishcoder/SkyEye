using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core.Application;

namespace SkyEye.Core.Analysis;

public sealed record RecordedAnnotationRenderSpan(long StartUs,long EndUs,RecordedScene Scene,ImmutableArray<double> PixelMatrix);
public sealed record RecordedAnnotationPlan(int Version,RecordedGeometrySource Source,int Width,int Height,SceneTarget Target,
    string JournalSha256,ImmutableArray<RecordedAnnotationRenderSpan> Spans)
{
    public static string Fingerprint(ClipAnnotationJournal journal)
    { journal.Validate();return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(journal,JsonDefaults.Options))); }
    public static RecordedAnnotationPlan Create(RecordedRenderPlan video,ClipAnnotationJournal journal,SceneTarget target)
    {
        video.Validate();journal.Validate();
        if(target is not(SceneTarget.Phone or SceneTarget.Tv)||journal.SourceSha256!=video.Source.ContentSha256
            ||Math.Abs(journal.DurationUs-video.Source.DurationUs)>100_000
            ||journal.Origin is{} origin&&(video.Source.Clock!=RecordedGeometryClock.CameraRecording||origin.Timeline!=video.Source.Timeline
                ||origin.StartUs!=video.Source.StartUs||origin.Width!=video.Source.Width||origin.Height!=video.Source.Height||origin.Rotation!=video.Source.Rotation)
            ||journal.Origin is null&&journal.Spans.Length>0)throw new ArgumentException("Annotation journal differs from video source.");
        var result=new List<RecordedAnnotationRenderSpan>();
        foreach(var annotation in journal.Spans.Where(s=>s.Target==target&&s.Scene is not null))
            foreach(var geometry in video.Spans)
            {
                long start=Math.Max(annotation.StartUs-video.Source.StartUs,geometry.StartUs),end=Math.Min(annotation.EndUs-video.Source.StartUs,geometry.EndUs);
                if(end<=start)continue;
                double[] pixel=Multiply([video.Width/2d,0,video.Width/2d,0,-video.Height/2d,video.Height/2d,0,0,1],
                    Multiply(geometry.Matrix.ToArray(),[2,0,-1,0,-2,1,0,0,1]));
                result.Add(new(start,end,annotation.Scene!,pixel.ToImmutableArray()));
            }
        var plan=new RecordedAnnotationPlan(1,video.Source,video.Width,video.Height,target,Fingerprint(journal),result.ToImmutableArray());plan.Validate();return plan;
    }
    public void Validate()
    {
        Source.Validate();
        if(Version!=1||Width is <16 or >3840||Height is <16 or >3840||Target is not(SceneTarget.Phone or SceneTarget.Tv)
            ||!ClipAnnotationJournal.Hash(JournalSha256)||Spans.IsDefault||Spans.Length>12512)throw new ArgumentException("Invalid annotation render plan.");
        long end=0;
        foreach(var span in Spans)
        {
            if(span is null||span.StartUs<end||span.EndUs<=span.StartUs||span.EndUs>Source.DurationUs||span.Scene is null
                ||span.PixelMatrix.IsDefault||span.PixelMatrix.Length!=9||span.PixelMatrix.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Invalid annotation render span.");
            span.Scene.Validate();end=span.EndUs;
        }
    }
    public RecordedAnnotationRenderSpan? At(long ptsUs)=>Spans.FirstOrDefault(s=>s.StartUs<=ptsUs&&ptsUs<s.EndUs);
    static double[] Multiply(double[] a,double[] b)=>Enumerable.Range(0,9).Select(i=>Enumerable.Range(0,3).Sum(k=>a[i/3*3+k]*b[k*3+i%3])).ToArray();
}
