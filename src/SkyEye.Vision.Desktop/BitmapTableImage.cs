using Org.Opencv.Android;
using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Android;

/// <summary>Desktop stand-in for the Android bitmap frame. Never constructed: desktop frames are encoded images.</summary>
public sealed class BitmapTableImage : ITableFrameImage
{
    private BitmapTableImage() { }
    public DesktopBitmap Bitmap => throw new PlatformNotSupportedException("Live bitmap frames are Android-only.");
    public byte[] EncodeForReview() => throw new PlatformNotSupportedException();
    public EncodedTableFrameEvidence EncodeForEvidence() => throw new PlatformNotSupportedException();
    public void Dispose() { }
}
