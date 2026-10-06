using System.Collections.Immutable;

namespace SkyEye.Core.Analysis;

public sealed record RecordedRenderSpan(long StartUs, long EndUs, bool Corrected, ImmutableArray<double> Matrix);
public sealed record RecordedRenderPlan(RecordedGeometrySource Source, int Width, int Height, bool Portrait,
    ImmutableArray<RecordedRenderSpan> Spans, int Version = 1)
{
    public static RecordedRenderPlan Create(RecordedGeometryJournal journal, RecordedGeometrySource source, int width=1920, int height=1080, bool portrait=false)
    {
        journal.Validate(); source.Validate();
        if (journal.Source != source || source.Rotation != 0 || width is < 16 or > 3840 || height is < 16 or > 3840 || width % 2 != 0 || height % 2 != 0)
            throw new ArgumentException("Unsupported or mismatched recorded render source.");
        var spans=journal.Spans.Select(span => new RecordedRenderSpan(span.StartUs-source.StartUs,span.EndUs-source.StartUs,span.Layout is not null,
            Matrix(source,width,height,portrait,span.Layout).ToImmutableArray())).ToImmutableArray();
        var plan=new RecordedRenderPlan(source,width,height,portrait,spans);plan.Validate();return plan;
    }
    public void Validate()
    {
        if(Source is null)throw new ArgumentException("Missing render source.");Source.Validate();
        if(Version!=1||Source.Rotation!=0||Width is <16 or >3840||Height is <16 or >3840||Width%2!=0||Height%2!=0||Spans.IsDefaultOrEmpty||Spans.Length>512)throw new ArgumentException("Invalid render plan.");
        long cursor=0;
        foreach(var span in Spans)
        {
            if(span is null||span.StartUs!=cursor||span.EndUs<=cursor||span.EndUs>Source.DurationUs||span.Matrix.IsDefault||span.Matrix.Length!=9||span.Matrix.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Invalid render interval.");
            cursor=span.EndUs;
        }
        if(cursor!=Source.DurationUs)throw new ArgumentException("Render extent differs from source.");
    }
    public static double[] PocketPixels(TableCalibration pockets,double width,double height,bool portrait)
    {
        if(!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)throw new ArgumentException("Invalid projection viewport.");
        const double border=.075;
        double scale=Math.Min(width/(portrait?1+2*border:2+2*border),height/(portrait?2+2*border:1+2*border));
        double across=(portrait?1+2*border:2+2*border)*scale,along=(portrait?2+2*border:1+2*border)*scale;
        double left=(width-across)/2,top=(height-along)/2,margin=border*scale;
        double[] target=portrait?[0,-scale,left+across-margin,scale,0,top+margin,0,0,1]:[scale,0,left+margin,0,scale,top+margin,0,0,1];
        var result=Multiply(target,pockets.MatrixValues());
        if(result.Any(v=>!double.IsFinite(v))||new[]{new Point(0,0),new(1,0),new(1,1),new(0,1)}.Any(p=>result[6]*p.X+result[7]*p.Y+result[8]<=1e-6))throw new ArgumentException("Invalid perspective horizon.");
        return result;
    }
    public RecordedRenderSpan? At(long presentationTimeUs) => presentationTimeUs < 0 || presentationTimeUs >= Source.DurationUs ? null
        : Spans.FirstOrDefault(s => presentationTimeUs >= s.StartUs && presentationTimeUs < s.EndUs);
    // Maps original image coordinates to output pixels for video, guides and
    // future historical annotations. GL consumes the very same homography.
    public Point? Pixel(long presentationTimeUs, Point image)
    {
        var span=At(presentationTimeUs); if(span is null || !double.IsFinite(image.X) || !double.IsFinite(image.Y))return null;
        var p=Map(span.Matrix, new(2*image.X-1,1-2*image.Y)); return new((p.X+1)*Width/2,(1-p.Y)*Height/2);
    }
    static double[] Matrix(RecordedGeometrySource source,int width,int height,bool portrait,PocketLayout? pockets)
    {
        double[] image;
        if(pockets is null)
        {
            double scale=Math.Min(width/(double)(portrait?source.Height:source.Width),height/(double)(portrait?source.Width:source.Height));
            double across=(portrait?source.Height:source.Width)*scale, along=(portrait?source.Width:source.Height)*scale;
            double left=(width-across)/2,top=(height-along)/2;
            image=portrait ? [0,-across/width,(left+across)/width,along/height,0,top/height,0,0,1]
                : [across/width,0,left/width,0,along/height,top/height,0,0,1];
        }
        else
        {
            image=PocketPixels(new TableCalibration(pockets.Corners.ToArray()),width,height,portrait);
            for(int i=0;i<3;i++){image[i]/=width;image[i+3]/=height;}
        }
        double[] normalized=Multiply([2,0,-1,0,-2,1,0,0,1],Multiply(image,[.5,0,.5,0,-.5,.5,0,0,1]));
        double divisor=normalized[8]; normalized=normalized.Select(v=>v/divisor).ToArray();
        if(normalized.Any(v=>!double.IsFinite(v)) || new[]{new Point(-1,-1),new(1,-1),new(1,1),new(-1,1)}.Any(p=>normalized[6]*p.X+normalized[7]*p.Y+normalized[8]<=1e-6))
            throw new ArgumentException("Recorded perspective transform has an invalid horizon.");
        return normalized;
    }
    static Point Map(ImmutableArray<double> m,Point p){double w=m[6]*p.X+m[7]*p.Y+m[8];return new((m[0]*p.X+m[1]*p.Y+m[2])/w,(m[3]*p.X+m[4]*p.Y+m[5])/w);}
    static double[] Multiply(double[] a,double[] b)=>Enumerable.Range(0,9).Select(i=>Enumerable.Range(0,3).Sum(k=>a[i/3*3+k]*b[k*3+i%3])).ToArray();
}
