using System.Diagnostics;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using SkyEye.Core.Analysis;
using Cv = Org.Opencv.Core.Core;
using CvPoint = Org.Opencv.Core.Point;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Android;

/// <summary>D3 image side, first slice: compares two settled table rasters and reports ball-sized change
/// regions, drift gates and a change grid (docs/claude/d3-settled-change-design.md, section 2.1 steps 5-8).
/// Not wired into Live Detect: no settle state machine, reducer use or logging yet. Owns no frames;
/// every Mat is released before a call returns.</summary>
public sealed class SettledChangeComparer(SettledChangeSettings? settings = null)
{
    static readonly Lazy<bool> Loaded = new(() => OpenCVLoader.InitLocal());
    public SettledChangeSettings Settings { get; } = settings ?? new();

    /// <summary>Decodes an encoded image and rectifies it to the raster: calibration corners (normalised
    /// 0..1, VisionAnalyzer.Warp order) map to the table rectangle inside the margin. Returns BGR bytes.</summary>
    public byte[] Rectify(byte[] encodedImage, IReadOnlyList<Point> normalizedCorners)
    {
        EnsureLoaded();
        using var buffer = new MatOfByte(encodedImage);
        var decoded = Imgcodecs.Imdecode(buffer, Imgcodecs.ImreadColor);
        var raster = new Mat();
        try
        {
            if (decoded.Empty()) throw new IOException("Image could not be decoded.");
            using var transform = Homography(normalizedCorners, decoded.Cols(), decoded.Rows());
            using var size = new Size(Settings.RasterWidth, Settings.RasterHeight);
            Imgproc.WarpPerspective(decoded, raster, transform, size);
            return Read(raster);
        }
        finally { Release(decoded); Release(raster); }
    }

    /// <summary>Maps a source pixel through the same homography as <see cref="Rectify"/>.</summary>
    public (double X, double Y) MapToRaster(Point sourcePixel, IReadOnlyList<Point> normalizedCorners, int imageWidth, int imageHeight)
    {
        EnsureLoaded();
        using var h = Homography(normalizedCorners, imageWidth, imageHeight);
        double M(int r, int c) => h.Get(r, c)![0];
        double w = M(2, 0) * sourcePixel.X + M(2, 1) * sourcePixel.Y + M(2, 2);
        return ((M(0, 0) * sourcePixel.X + M(0, 1) * sourcePixel.Y + M(0, 2)) / w,
                (M(1, 0) * sourcePixel.X + M(1, 1) * sourcePixel.Y + M(1, 2)) / w);
    }

    /// <summary>Compares a reference (before) and current (after) BGR raster of the same size.</summary>
    public SettledChangeComparison Compare(byte[] bgrBefore, byte[] bgrAfter)
    {
        EnsureLoaded();
        var s = Settings; int w = s.RasterWidth, h = s.RasterHeight;
        if (bgrBefore.Length != w * h * 3 || bgrAfter.Length != w * h * 3) throw new ArgumentException("Rasters do not match the settings.");
        var watch = Stopwatch.StartNew();
        var (greyBefore, hsvBefore) = GreyAndHsv(bgrBefore, w, h);
        var (greyAfter, hsvAfter) = GreyAndHsv(bgrAfter, w, h);

        // Change map: tile-gain-compensated log ratio, blurred (T4 v3 grey rule).
        var change = SettledChangeMath.GaussianBlur3(
            SettledChangeMath.AbsoluteTileResidual(SettledChangeMath.LogRatio(greyBefore, greyAfter), w, h, s.TilePixels), w, h);
        var (band, bed, p99) = SettledChangeMath.Drift(change, s);
        var mask = new byte[w * h];
        for (int i = 0; i < mask.Length; i++) mask[i] = change[i] > s.Threshold ? (byte)1 : (byte)0;

        var maskMat = new Mat(h, w, CvType.Cv8uc1); var labels = new Mat(); var stats = new Mat(); var centres = new Mat();
        Mat? openKernel = null, closeKernel = null;
        try
        {
            maskMat.Put(0, 0, mask);
            using (var openSize = new Size(3, 3)) openKernel = Imgproc.GetStructuringElement(Imgproc.MorphRect, openSize);
            using (var closeSize = new Size(5, 5)) closeKernel = Imgproc.GetStructuringElement(Imgproc.MorphEllipse, closeSize);
            Imgproc.MorphologyEx(maskMat, maskMat, Imgproc.MorphOpen, openKernel);
            var opened = Read(maskMat);
            var (grid, columns, rows) = SettledChangeMath.ChangeGrid(opened, s);
            Imgproc.MorphologyEx(maskMat, maskMat, Imgproc.MorphClose, closeKernel);
            int count = Imgproc.ConnectedComponentsWithStats(maskMat, labels, stats, centres);

            var cloth = SettledChangeMath.ClothModel(hsvBefore, s);
            var regions = new List<SettledChangeRegion>();
            int rawComponents = 0;
            for (int i = 1; i < count; i++)
            {
                double area = stats.Get(i, Imgproc.CcStatArea)![0];
                if (area < s.MinimumRegionBallAreas * s.BallArea) continue;
                rawComponents++;
                regions.Add(Region(i, area, labels, stats, centres, change, hsvBefore, hsvAfter, cloth));
            }
            var kept = regions.OrderByDescending(r => r.AreaBalls).Take(s.MaximumRegions).ToList();
            var gates = new List<string>();
            if (band > s.LightingDriftGate) gates.Add("lighting_drift");
            return new(gates, Math.Round(band, 4), Math.Round(bed, 4), Math.Round(p99, 3), rawComponents, kept,
                grid, columns, rows, watch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            Release(maskMat); Release(labels); Release(stats); Release(centres);
            if (openKernel is not null) Release(openKernel);
            if (closeKernel is not null) Release(closeKernel);
        }
    }

    SettledChangeRegion Region(int label, double area, Mat labels, Mat stats, Mat centres, float[] change,
        byte[] hsvBefore, byte[] hsvAfter, (double H, double S, double V) cloth)
    {
        var s = Settings; int w = s.RasterWidth, h = s.RasterHeight;
        int left = (int)stats.Get(label, Imgproc.CcStatLeft)![0], top = (int)stats.Get(label, Imgproc.CcStatTop)![0];
        int bw = (int)stats.Get(label, Imgproc.CcStatWidth)![0], bh = (int)stats.Get(label, Imgproc.CcStatHeight)![0];
        double cx = centres.Get(label, 0)![0], cy = centres.Get(label, 1)![0];
        // Crop with a 2 px border (as the prototype does) so the distance transform sees background round the body.
        int x0 = Math.Max(0, left - 2), y0 = Math.Max(0, top - 2), x1 = Math.Min(w, left + bw + 2), y1 = Math.Min(h, top + bh + 2);
        using var bounds = new Rect(x0, y0, x1 - x0, y1 - y0);
        var cropLabels = labels.Submat(bounds); var cropMask = new Mat(); var distance = new Mat();
        try
        {
            using (var value = new Scalar(label)) Cv.Compare(cropLabels, value, cropMask, Cv.CmpEq);
            Imgproc.DistanceTransform(cropMask, distance, Imgproc.DistL2, 5);
            double inscribed = 0;
            for (int y = 0; y < distance.Rows(); y++)
                for (int x = 0; x < distance.Cols(); x++) inscribed = Math.Max(inscribed, distance.Get(y, x)![0]);
            var bytes = Read(cropMask);
            var pixels = new List<int>(); int outside = 0; double peak = 0; int tol = s.RailTolerancePixels, m = s.Margin;
            for (int y = 0; y < y1 - y0; y++)
            for (int x = 0; x < x1 - x0; x++)
            {
                if (bytes[y * (x1 - x0) + x] == 0) continue;
                int gx = x0 + x, gy = y0 + y, index = gy * w + gx;
                pixels.Add(index); peak = Math.Max(peak, change[index]);
                if (gx < m - tol || gx > m + s.TableWidth + tol || gy < m - tol || gy > m + s.TableHeight + tol) outside++;
            }
            double outsideFraction = pixels.Count == 0 ? 0 : outside / (double)pixels.Count;
            var (ballSized, reason) = SettledChangeMath.ClassifyRegion(inscribed, area, outsideFraction, s);
            var before = SettledChangeMath.Appearance(hsvBefore, pixels, cloth);
            var after = SettledChangeMath.Appearance(hsvAfter, pixels, cloth);
            var (tx, ty) = SettledChangeMath.RasterToTable(cx, cy, s);
            return new(Math.Round(tx, 4), Math.Round(ty, 4), Math.Round(cx, 1), Math.Round(cy, 1),
                SettledChangeMath.Zone(cx, cy, s), Math.Round(area / s.BallArea, 3), Math.Round(inscribed / s.BallRadius, 3),
                Math.Round(outsideFraction, 3), Math.Round(peak, 3), ballSized, reason,
                SettledChangeMath.Direction(before.Cloth, after.Cloth),
                Math.Round(before.Cloth, 3), Math.Round(after.Cloth, 3), Math.Round(before.Dark, 3), Math.Round(after.Dark, 3),
                Math.Round(before.White, 3), Math.Round(after.White, 3));
        }
        finally { Release(cropLabels); Release(cropMask); Release(distance); }
    }

    Mat Homography(IReadOnlyList<Point> corners, int imageWidth, int imageHeight)
    {
        if (corners.Count != 4) throw new ArgumentException("Four calibration corners are required.");
        var s = Settings; double m = s.Margin, tw = s.TableWidth, th = s.TableHeight;
        CvPoint[] source = corners.Select(p => new CvPoint(p.X * imageWidth, p.Y * imageHeight)).ToArray();
        CvPoint[] target = [new(m + tw, m), new(m + tw, m + th), new(m, m + th), new(m, m)];
        using var src = new MatOfPoint2f(source); using var dst = new MatOfPoint2f(target);
        try { return Imgproc.GetPerspectiveTransform(src, dst); }
        finally { foreach (var p in source.Concat(target)) p.Dispose(); }
    }

    static (byte[] Grey, byte[] Hsv) GreyAndHsv(byte[] bgr, int width, int height)
    {
        var image = new Mat(height, width, CvType.Cv8uc3); var grey = new Mat(); var hsv = new Mat();
        try
        {
            image.Put(0, 0, bgr);
            Imgproc.CvtColor(image, grey, Imgproc.ColorBgr2gray);
            Imgproc.CvtColor(image, hsv, Imgproc.ColorBgr2hsv);
            return (Read(grey), Read(hsv));
        }
        finally { Release(image); Release(grey); Release(hsv); }
    }

    static byte[] Read(Mat mat) { var data = new byte[mat.Rows() * mat.Cols() * mat.Channels()]; mat.Get(0, 0, data); return data; }
    static void Release(Mat mat) { mat.Release(); mat.Dispose(); }
    static void EnsureLoaded() { if (!Loaded.Value) throw new IOException("Settled-change comparison could not load OpenCV."); }
}
