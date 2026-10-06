using System.Diagnostics;
using SkyEye.Core.Analysis;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using Org.Opencv.Video;

namespace SkyEye.Vision.Android;

public sealed record PixelRoi(string Id, int X, int Y, int Width, int Height);

/// <summary>Bounded fixed-image ROIs and feature flow, not ball identity or shot adjudication.</summary>
public sealed class RoiMotionProcessor : ITableMotionProcessor, IDisposable
{
    readonly PixelRoi[] regions;
    Mat[] previous = [];
    bool disposed;
    public RoiMotionProcessor(IEnumerable<PixelRoi> regions)
    {
        this.regions = regions.ToArray();
        if (this.regions.Length is < 1 or > 16 || this.regions.Select(r => r.Id).Distinct().Count() != this.regions.Length
            || this.regions.Any(r => string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 80 || r.X < 0 || r.Y < 0
                || r.Width is < 32 or > 256 || r.Height is < 32 or > 256)) throw new ArgumentException("Invalid bounded ROI plan.");
    }
    public Task<MotionMeasurement> MeasureAsync(TableFrame frame, MotionFrameContext context, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        if (frame.Image is not BitmapTableImage image || frame.SourceTimeUs != context.SourceTimeUs
            || frame.DecodedReceipt?.MediaGeneration != context.Scope.MediaGeneration)
            throw new InvalidDataException("ROI measurements require the scoped raw decoded image.");
        if (regions.Any(r => (long)r.X + r.Width > image.Bitmap.Width || (long)r.Y + r.Height > image.Bitmap.Height))
            throw new InvalidDataException("ROI exceeds the decoded image.");
        VisionAnalyzer.EnsureNativeLoaded();
        if (context.Reset) ClearPrevious();
        if (previous.Length != 0 && (!context.DeltaSeconds.HasValue || context.DeltaSeconds.Value <= 0 || context.DeltaSeconds.Value > 1))
            throw new InvalidDataException("Flow delta must come from consecutive source time.");
        var current = new List<Mat>(); var results = new List<RoiMotion>();
        double conversionMs = 0, flowMs = 0;
        try
        {
            foreach (var roi in regions)
            {
                cancellation.ThrowIfCancellationRequested(); var clock = Stopwatch.StartNew();
                // Crop BEFORE OpenCV conversion: no full-frame Mat or JPEG.
                var crop = global::Android.Graphics.Bitmap.CreateBitmap(image.Bitmap, roi.X, roi.Y, roi.Width, roi.Height)!;
                using var rgba = new NativeMat(new Mat()); var gray = new Mat();
                try { Utils.BitmapToMat(crop, rgba.Value); Imgproc.CvtColor(rgba.Value, gray, Imgproc.ColorRgba2gray); }
                catch { Release(gray); throw; }
                finally { if (!ReferenceEquals(crop, image.Bitmap)) { crop.Recycle(); crop.Dispose(); } }
                current.Add(gray); clock.Stop(); conversionMs += clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                results.Add(previous.Length == 0 ? Seed(roi.Id, gray) : Measure(roi.Id, previous[current.Count - 1], gray, context.DeltaSeconds!.Value));
                clock.Stop(); flowMs += clock.Elapsed.TotalMilliseconds;
            }
            cancellation.ThrowIfCancellationRequested(); ClearPrevious(); previous = current.ToArray(); current.Clear();
            return Task.FromResult(new MotionMeasurement(context.SourceTimeUs, results.ToArray(), conversionMs, flowMs));
        }
        finally { foreach (var mat in current) Release(mat); }
    }
    static RoiMotion Seed(string id, Mat gray)
    {
        using var points = new NativeMat(new MatOfPoint()); Imgproc.GoodFeaturesToTrack(gray, (MatOfPoint)points.Value, 32, .05, 4);
        return new(id, (int)points.Value.Total(), 0, null, null, null, null, "seed");
    }
    static RoiMotion Measure(string id, Mat before, Mat after, double delta)
    {
        using var corners = new NativeMat(new MatOfPoint()); Imgproc.GoodFeaturesToTrack(before, (MatOfPoint)corners.Value, 32, .05, 4);
        int count = (int)corners.Value.Total();
        if (count < 4) return new(id, count, 0, null, null, null, null, "insufficient-texture");
        using var first = new NativeMat(new MatOfPoint2f()); using var next = new NativeMat(new MatOfPoint2f());
        corners.Value.ConvertTo(first.Value, CvType.Cv32f);
        using var back = new NativeMat(new MatOfPoint2f()); using var forwardStatus = new NativeMat(new MatOfByte());
        using var backwardStatus = new NativeMat(new MatOfByte()); using var error = new NativeMat(new MatOfFloat());
        using var reverseError = new NativeMat(new MatOfFloat()); using var window = new Size(21, 21);
        OpticalFlow.CalcOpticalFlowPyrLK(before, after, (MatOfPoint2f)first.Value, (MatOfPoint2f)next.Value,
            (MatOfByte)forwardStatus.Value, (MatOfFloat)error.Value, window, 2);
        OpticalFlow.CalcOpticalFlowPyrLK(after, before, (MatOfPoint2f)next.Value, (MatOfPoint2f)back.Value,
            (MatOfByte)backwardStatus.Value, (MatOfFloat)reverseError.Value, window, 2);
        // One JNI call per coordinate buffer, rather than Point objects and
        // hundreds of individual JNI field reads for every ROI/frame.
        var found = new float[count * 2]; var moved = new float[count * 2]; var returned = new float[count * 2];
        first.Value.Get(0, 0, found); next.Value.Get(0, 0, moved); back.Value.Get(0, 0, returned);
        var flags = ((MatOfByte)forwardStatus.Value).ToArray(); var reverse = ((MatOfByte)backwardStatus.Value).ToArray();
        var errors = ((MatOfFloat)error.Value).ToArray(); var dx = new List<double>(); var dy = new List<double>();
        int columns = after.Cols(), rows = after.Rows();
        for (int i = 0; i < count; i++)
        {
            int xIndex = i * 2, yIndex = xIndex + 1;
            if (flags[i] == 0 || reverse[i] == 0 || !float.IsFinite(errors[i]) || errors[i] > 12
                || !float.IsFinite(moved[xIndex]) || !float.IsFinite(moved[yIndex]) || !float.IsFinite(returned[xIndex]) || !float.IsFinite(returned[yIndex])
                || moved[xIndex] < 1 || moved[yIndex] < 1 || moved[xIndex] >= columns - 1 || moved[yIndex] >= rows - 1
                || Math.Sqrt(Math.Pow(returned[xIndex] - found[xIndex], 2) + Math.Pow(returned[yIndex] - found[yIndex], 2)) > 1) continue;
            dx.Add(moved[xIndex] - found[xIndex]); dy.Add(moved[yIndex] - found[yIndex]);
        }
        if (dx.Count < 4 || dx.Count < count / 2d) return new(id, count, dx.Count, null, null, null, null, "unresolved");
        var x = Median(dx); var y = Median(dy);
        var spread = dx.Select((value, i) => Math.Sqrt(Math.Pow(value - x, 2) + Math.Pow(dy[i] - y, 2))).Order().ToArray();
        if (!double.IsFinite(x) || !double.IsFinite(y) || spread[(int)Math.Ceiling(.95 * spread.Length) - 1] > 1.5)
            return new(id, count, dx.Count, null, null, null, null, "inconsistent-flow");
        return new(id, count, dx.Count, x, y, x / delta, y / delta, "supported");
    }
    static double Median(List<double> values) { var sorted = values.Order().ToArray(); return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2; }
    void ClearPrevious() { foreach (var mat in previous) Release(mat); previous = []; }
    static void Release(Mat mat) { mat.Release(); mat.Dispose(); }
    public void Dispose() { if (disposed) return; disposed = true; ClearPrevious(); }
    sealed class NativeMat(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() => Release(Value); }
}
