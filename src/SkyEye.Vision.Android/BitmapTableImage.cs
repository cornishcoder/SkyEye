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
    public void Dispose() { Bitmap.Recycle(); Bitmap.Dispose(); }
}
