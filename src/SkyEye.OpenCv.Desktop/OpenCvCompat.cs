// Desktop look-alike of the subset of the Org.Opencv Android binding that the detector uses.
// Each member mirrors the Java binding's managed name, signature and native defaults, and is
// implemented over OpenCvSharp (OpenCV 4.13). See docs/claude/t2-desktop-runner-design.md, section 2.
// Parity notes:
// - Java Point/Size hold doubles; the JNI layer truncates them to int for cv::Point/cv::Size.
// - MatOfPoint2f is an Nx1 CV_32FC2 Mat, so perspective transforms see float32 corners.
// - HoughCircles writes a 1xN CV_32FC3 Mat, or releases the output when nothing is found.
using System.Runtime.InteropServices;
using Ocv = OpenCvSharp;

namespace Org.Opencv.Core
{
    public class Mat : IDisposable
    {
        internal Ocv.Mat Native;

        public Mat() => Native = new Ocv.Mat();
        public Mat(int rows, int cols, int type) => Native = new Ocv.Mat(rows, cols, (Ocv.MatType)type);
        internal Mat(Ocv.Mat native) => Native = native;

        public int Rows() => Native.Rows;
        public int Cols() => Native.Cols;
        public int Channels() => Native.Channels();
        public bool Empty() => Native.Empty();
        public int Type() => (int)Native.Type();
        public long Total() => Native.Total();
        public void CopyTo(Mat m) => Native.CopyTo(m.Native);
        public Mat SetTo(Scalar s) { Native.SetTo(s.ToNative()); return this; }
        public Mat Submat(Rect roi) => new(new Ocv.Mat(Native, roi.ToNative()));
        public void Release() => Native.Release();

        /// <summary>Java Mat.get(row, col): one element's channels widened to double, or null outside the Mat.</summary>
        public unsafe double[]? Get(int row, int col)
        {
            if (row < 0 || col < 0 || row >= Native.Rows || col >= Native.Cols) return null;
            int channels = Native.Channels();
            var values = new double[channels];
            byte* p = (byte*)Native.Ptr(row, col);
            int depth = Native.Depth();
            for (int c = 0; c < channels; c++)
                values[c] = depth switch
                {
                    Ocv.MatType.CV_8U => p[c],
                    Ocv.MatType.CV_8S => ((sbyte*)p)[c],
                    Ocv.MatType.CV_16U => ((ushort*)p)[c],
                    Ocv.MatType.CV_16S => ((short*)p)[c],
                    Ocv.MatType.CV_32S => ((int*)p)[c],
                    Ocv.MatType.CV_32F => ((float*)p)[c],
                    Ocv.MatType.CV_64F => ((double*)p)[c],
                    _ => throw new NotSupportedException($"Mat depth {depth}")
                };
            return values;
        }

        /// <summary>Java Mat.get(row, col, byte[]): copies 8-bit data from (row, col) onwards; returns the byte count.</summary>
        public int Get(int row, int col, byte[] data) => Copy(row, col, data, toMat: false);

        /// <summary>Java Mat.put(row, col, byte[]): copies 8-bit data into the Mat from (row, col) onwards.</summary>
        public int Put(int row, int col, byte[] data) => Copy(row, col, data, toMat: true);

        int Copy(int row, int col, byte[] data, bool toMat)
        {
            int depth = Native.Depth();
            if (depth != Ocv.MatType.CV_8U && depth != Ocv.MatType.CV_8S) throw new NotSupportedException("Mat data type is not compatible: " + Native.Type());
            if (data.Length % Native.Channels() != 0) throw new NotSupportedException("Provided data element number should be multiple of the Mat channels count");
            if (!Native.IsContinuous()) throw new NotSupportedException("Desktop compat copies continuous Mats only.");
            long offset = ((long)row * Native.Cols + col) * Native.ElemSize();
            long available = Native.Total() * Native.ElemSize() - offset;
            int count = (int)Math.Max(0, Math.Min(data.Length, available));
            IntPtr start = Native.Data + (nint)offset;
            if (toMat) Marshal.Copy(data, 0, start, count); else Marshal.Copy(start, data, 0, count);
            return count;
        }

        public virtual void Dispose() { Native.Dispose(); GC.SuppressFinalize(this); }
    }

    public class MatOfByte : Mat
    {
        public MatOfByte() { }
        public MatOfByte(params byte[] values) : base(new Ocv.Mat(values.Length, 1, Ocv.MatType.CV_8UC1))
        {
            if (values.Length > 0) Marshal.Copy(values, 0, Native.Data, values.Length);
        }
        internal void Assign(byte[] values)
        {
            Native.Dispose();
            Native = new Ocv.Mat(values.Length, 1, Ocv.MatType.CV_8UC1);
            if (values.Length > 0) Marshal.Copy(values, 0, Native.Data, values.Length);
        }
        public byte[] ToArray()
        {
            var values = new byte[(int)Native.Total()];
            if (values.Length > 0) Marshal.Copy(Native.Data, values, 0, values.Length);
            return values;
        }
    }

    public class MatOfPoint2f : Mat
    {
        public MatOfPoint2f(params Point[] points) : base(new Ocv.Mat(points.Length, 1, Ocv.MatType.CV_32FC2))
        {
            for (int i = 0; i < points.Length; i++) Native.Set(i, 0, new Ocv.Vec2f((float)points[i].X, (float)points[i].Y));
        }
    }

    public class Point : IDisposable
    {
        public Point() { }
        public Point(double x, double y) { X = x; Y = y; }
        public double X { get; set; }
        public double Y { get; set; }
        internal Ocv.Point ToNative() => new((int)X, (int)Y);
        public void Dispose() { }
    }

    public class Size : IDisposable
    {
        public Size() { }
        public Size(double width, double height) { Width = width; Height = height; }
        public double Width { get; set; }
        public double Height { get; set; }
        internal Ocv.Size ToNative() => new((int)Width, (int)Height);
        public void Dispose() { }
    }

    public class Rect : IDisposable
    {
        public Rect(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        internal Ocv.Rect ToNative() => new(X, Y, Width, Height);
        public void Dispose() { }
    }

    public class Scalar : IDisposable
    {
        public Scalar(double v0) : this(v0, 0, 0, 0) { }
        public Scalar(double v0, double v1) : this(v0, v1, 0, 0) { }
        public Scalar(double v0, double v1, double v2) : this(v0, v1, v2, 0) { }
        public Scalar(double v0, double v1, double v2, double v3) => Val = [v0, v1, v2, v3];
        public double[] Val { get; }
        internal Ocv.Scalar ToNative() => new(Val[0], Val[1], Val[2], Val[3]);
        public void Dispose() { }
    }

    public static class CvType
    {
        public const int Cv8uc1 = 0;
        public const int Cv8uc3 = 16;
        public const int Cv32f = 5;
        public const int Cv32s = 4;
    }

    public static class Core
    {
        public const int CmpEq = 0;
        public const int CmpGt = 1;
        public const int CmpGe = 2;
        public const int CmpLt = 3;
        public const int CmpLe = 4;
        public const int CmpNe = 5;

        public static string Version => Ocv.Cv2.GetVersionString() ?? "";
        public static int NumThreads { get => Ocv.Cv2.GetNumThreads(); set => Ocv.Cv2.SetNumThreads(value); }

        public static void InRange(Mat src, Scalar lowerb, Scalar upperb, Mat dst) => Ocv.Cv2.InRange(src.Native, lowerb.ToNative(), upperb.ToNative(), dst.Native);
        public static void Bitwise_or(Mat src1, Mat src2, Mat dst) => Ocv.Cv2.BitwiseOr(src1.Native, src2.Native, dst.Native);
        public static void Bitwise_and(Mat src1, Mat src2, Mat dst) => Ocv.Cv2.BitwiseAnd(src1.Native, src2.Native, dst.Native);
        public static void Bitwise_not(Mat src, Mat dst) => Ocv.Cv2.BitwiseNot(src.Native, dst.Native);
        public static void Compare(Mat src1, Mat src2, Mat dst, int cmpop) => Ocv.Cv2.Compare(src1.Native, src2.Native, dst.Native, (Ocv.CmpTypes)cmpop);
        public static void Compare(Mat src1, Scalar src2, Mat dst, int cmpop) => Ocv.Cv2.Compare(src1.Native, Ocv.InputArray.Create(src2.ToNative()), dst.Native, (Ocv.CmpTypes)cmpop);
        public static int CountNonZero(Mat src) => Ocv.Cv2.CountNonZero(src.Native);
    }
}

namespace Org.Opencv.Imgproc
{
    using Org.Opencv.Core;

    public static class Imgproc
    {
        public const int ColorRgba2bgr = 3;
        public const int ColorBgr2gray = 6;
        public const int ColorRgba2gray = 11;
        public const int ColorBgr2hsv = 40;
        public const int CcStatLeft = 0, CcStatTop = 1, CcStatWidth = 2, CcStatHeight = 3, CcStatArea = 4;
        public const int DistL2 = 2;
        public const int HoughGradient = 3;
        public const int MorphRect = 0, MorphCross = 1, MorphEllipse = 2;
        public const int MorphErode = 0, MorphDilate = 1, MorphOpen = 2, MorphClose = 3;

        public static void CvtColor(Mat src, Mat dst, int code) => Ocv.Cv2.CvtColor(src.Native, dst.Native, (Ocv.ColorConversionCodes)code);
        public static void GaussianBlur(Mat src, Mat dst, Size ksize, double sigmaX) => Ocv.Cv2.GaussianBlur(src.Native, dst.Native, ksize.ToNative(), sigmaX);
        public static void Blur(Mat src, Mat dst, Size ksize) => Ocv.Cv2.Blur(src.Native, dst.Native, ksize.ToNative());
        public static void Canny(Mat image, Mat edges, double threshold1, double threshold2) => Ocv.Cv2.Canny(image.Native, edges.Native, threshold1, threshold2);
        public static void Resize(Mat src, Mat dst, Size dsize) => Ocv.Cv2.Resize(src.Native, dst.Native, dsize.ToNative());
        public static Mat GetStructuringElement(int shape, Size ksize) => new(Ocv.Cv2.GetStructuringElement((Ocv.MorphShapes)shape, ksize.ToNative()));
        public static void MorphologyEx(Mat src, Mat dst, int op, Mat kernel) => Ocv.Cv2.MorphologyEx(src.Native, dst.Native, (Ocv.MorphTypes)op, kernel.Native);
        public static void Dilate(Mat src, Mat dst, Mat kernel) => Ocv.Cv2.Dilate(src.Native, dst.Native, kernel.Native);
        public static void DistanceTransform(Mat src, Mat dst, int distanceType, int maskSize) =>
            Ocv.Cv2.DistanceTransform(src.Native, dst.Native, (Ocv.DistanceTypes)distanceType, (Ocv.DistanceTransformMasks)maskSize);
        public static int ConnectedComponentsWithStats(Mat image, Mat labels, Mat stats, Mat centroids) =>
            Ocv.Cv2.ConnectedComponentsWithStats(image.Native, labels.Native, stats.Native, centroids.Native);
        public static Mat GetPerspectiveTransform(Mat src, Mat dst) => new(Ocv.Cv2.GetPerspectiveTransform(src.Native, dst.Native));
        public static void WarpPerspective(Mat src, Mat dst, Mat m, Size dsize) => Ocv.Cv2.WarpPerspective(src.Native, dst.Native, m.Native, dsize.ToNative());
        public static void Circle(Mat img, Point center, int radius, Scalar color, int thickness) =>
            Ocv.Cv2.Circle(img.Native, center.ToNative(), radius, color.ToNative(), thickness);

        public static void HoughCircles(Mat image, Mat circles, int method, double dp, double minDist, double param1, double param2, int minRadius, int maxRadius)
        {
            var found = Ocv.Cv2.HoughCircles(image.Native, (Ocv.HoughModes)method, dp, minDist, param1, param2, minRadius, maxRadius);
            if (found.Length == 0) { circles.Native.Release(); return; }
            circles.Native.Create(1, found.Length, Ocv.MatType.CV_32FC3);
            for (int i = 0; i < found.Length; i++)
                circles.Native.Set(0, i, new Ocv.Vec3f(found[i].Center.X, found[i].Center.Y, found[i].Radius));
        }
    }
}

namespace Org.Opencv.Imgcodecs
{
    using Org.Opencv.Core;

    public static class Imgcodecs
    {
        public const int ImreadColor = 1;
        public static Mat Imdecode(Mat buf, int flags) => new(Ocv.Cv2.ImDecode(((MatOfByte)buf).ToArray(), (Ocv.ImreadModes)flags));
        public static bool Imencode(string ext, Mat img, MatOfByte buf)
        {
            bool ok = Ocv.Cv2.ImEncode(ext, img.Native, out byte[] encoded);
            buf.Assign(encoded);
            return ok;
        }
    }
}

namespace Org.Opencv.Android
{
    using Org.Opencv.Core;

    /// <summary>Stands in for Android.Graphics.Bitmap. Never constructed on desktop.</summary>
    public sealed class DesktopBitmap { private DesktopBitmap() { } }

    public static class Utils
    {
        public static void BitmapToMat(DesktopBitmap bmp, Mat mat) => throw new PlatformNotSupportedException("Live bitmap frames are Android-only.");
    }

    public static class OpenCVLoader
    {
        public static bool InitLocal() => true;
    }
}
