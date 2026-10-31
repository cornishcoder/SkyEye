using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Android;

/// <summary>Owns exactly one raw decoded texture copy. JPEG is produced only for a requested editor.</summary>
public sealed class BitmapTableImage(global::Android.Graphics.Bitmap bitmap) : ITableFrameImage
{
    public global::Android.Graphics.Bitmap Bitmap { get; } = bitmap;
    public byte[] EncodeForReview()
    {
        PipelineDiagnostics.Count("reviewJpegs"); using var timing = PipelineDiagnostics.Measure("reviewJpegMs");
        using var output = new MemoryStream();
        if (!Bitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg!, 90, output)) throw new IOException("Review image could not be prepared.");
        return output.ToArray();
    }
    public EncodedTableFrameEvidence EncodeForEvidence()
    {
        PipelineDiagnostics.Count("evidencePngs"); using var timing = PipelineDiagnostics.Measure("evidencePngMs");
        using var output = new MemoryStream();
        if (!Bitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Png!, 0, output)) throw new IOException("Lossless source evidence could not be prepared.");
        return new(output.ToArray(), ".png", "image/png");
    }
    public void Dispose() { Bitmap.Recycle(); Bitmap.Dispose(); }
}
