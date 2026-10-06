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
        var found = ((MatOfPoint)corners.Value).ToArray();
        try
        {
            if (found.Length < 4) return new(id, found.Length, 0, null, null, null, null, "insufficient-texture");
            using var first = new NativeMat(new MatOfPoint2f(found)); using var next = new NativeMat(new MatOfPoint2f());
            using var back = new NativeMat(new MatOfPoint2f()); using var forwardStatus = new NativeMat(new MatOfByte());
            using var backwardStatus = new NativeMat(new MatOfByte()); using var error = new NativeMat(new MatOfFloat());
            using var reverseError = new NativeMat(new MatOfFloat()); using var window = new Size(21, 21);
            OpticalFlow.CalcOpticalFlowPyrLK(before, after, (MatOfPoint2f)first.Value, (MatOfPoint2f)next.Value,
                (MatOfByte)forwardStatus.Value, (MatOfFloat)error.Value, window, 2);
            OpticalFlow.CalcOpticalFlowPyrLK(after, before, (MatOfPoint2f)next.Value, (MatOfPoint2f)back.Value,
                (MatOfByte)backwardStatus.Value, (MatOfFloat)reverseError.Value, window, 2);
            var moved = ((MatOfPoint2f)next.Value).ToArray(); var returned = ((MatOfPoint2f)back.Value).ToArray();
            try
            {
                var flags = ((MatOfByte)forwardStatus.Value).ToArray(); var reverse = ((MatOfByte)backwardStatus.Value).ToArray();
                var errors = ((MatOfFloat)error.Value).ToArray(); var dx = new List<double>(); var dy = new List<double>();
                for (int i = 0; i < found.Length; i++)
                {
                    if (flags[i] == 0 || reverse[i] == 0 || !float.IsFinite(errors[i]) || errors[i] > 12
                        || moved[i].X < 1 || moved[i].Y < 1 || moved[i].X >= after.Cols() - 1 || moved[i].Y >= after.Rows() - 1
                        || Math.Sqrt(Math.Pow(returned[i].X - found[i].X, 2) + Math.Pow(returned[i].Y - found[i].Y, 2)) > 1) continue;
                    dx.Add(moved[i].X - found[i].X); dy.Add(moved[i].Y - found[i].Y);
                }
                if (dx.Count < 4 || dx.Count < found.Length / 2d) return new(id, found.Length, dx.Count, null, null, null, null, "unresolved");
                var x = Median(dx); var y = Median(dy);
                var spread = dx.Select((value, i) => Math.Sqrt(Math.Pow(value - x, 2) + Math.Pow(dy[i] - y, 2))).Order().ToArray();
                if (!double.IsFinite(x) || !double.IsFinite(y) || spread[(int)Math.Ceiling(.95 * spread.Length) - 1] > 1.5)
                    return new(id, found.Length, dx.Count, null, null, null, null, "inconsistent-flow");
                return new(id, found.Length, dx.Count, x, y, x / delta, y / delta, "supported");
            }
            finally { foreach (var point in moved.Concat(returned)) point.Dispose(); }
        }
        finally { foreach (var point in found) point.Dispose(); }
    }
    static double Median(List<double> values) { var sorted = values.Order().ToArray(); return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2; }
    void ClearPrevious() { foreach (var mat in previous) Release(mat); previous = []; }
    static void Release(Mat mat) { mat.Release(); mat.Dispose(); }
    public void Dispose() { if (disposed) return; disposed = true; ClearPrevious(); }
    sealed class NativeMat(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() => Release(Value); }
}
