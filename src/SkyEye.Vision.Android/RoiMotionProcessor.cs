using System.Diagnostics;
using SkyEye.Core.Analysis;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using Org.Opencv.Video;

namespace SkyEye.Vision.Android;

public sealed record PixelRoi(string Id, int X, int Y, int Width, int Height)
{
    public int? TargetX { get; init; }
    public int? TargetY { get; init; }
    public int TemplateRadius { get; init; } = 10;
}

/// <summary>Bounded fixed-image ROIs and feature flow, not ball identity or shot adjudication.</summary>
public sealed class RoiMotionProcessor : ITableMotionProcessor, IDisposable
{
    const int ChangeThresholdGray = 12;
    readonly PixelRoi[] regions;
    readonly double sourcePixelsPerAnalysisPixelX, sourcePixelsPerAnalysisPixelY;
    Mat[] previous = [];
    readonly double[] trackedX, trackedY;
    readonly byte[][] firstTemplatePixels, nextTemplatePixels;
    bool disposed;
    public RoiMotionProcessor(IEnumerable<PixelRoi> regions, double sourcePixelsPerAnalysisPixelX = 1, double sourcePixelsPerAnalysisPixelY = 1)
    {
        this.regions = regions.ToArray();
        if (!double.IsFinite(sourcePixelsPerAnalysisPixelX) || sourcePixelsPerAnalysisPixelX is < 0.25 or > 4
            || !double.IsFinite(sourcePixelsPerAnalysisPixelY) || sourcePixelsPerAnalysisPixelY is < 0.25 or > 4)
            throw new ArgumentException("Invalid ROI motion coordinate scale.");
        this.sourcePixelsPerAnalysisPixelX = sourcePixelsPerAnalysisPixelX;
        this.sourcePixelsPerAnalysisPixelY = sourcePixelsPerAnalysisPixelY;
        if (this.regions.Length is < 1 or > 16 || this.regions.Select(r => r.Id).Distinct().Count() != this.regions.Length
            || this.regions.Any(r => string.IsNullOrWhiteSpace(r.Id) || r.Id.Length > 80 || r.X < 0 || r.Y < 0
                || r.Width is < 32 or > 256 || r.Height is < 32 or > 256
                || (r.TargetX.HasValue != r.TargetY.HasValue)
                || (r.TargetX is { } targetX && (targetX < 0 || targetX >= r.Width))
                || (r.TargetY is { } targetY && (targetY < 0 || targetY >= r.Height))
                || r.TemplateRadius is < 4 or > 60)) throw new ArgumentException("Invalid bounded ROI plan.");
        trackedX = this.regions.Select(region => (double)(region.TargetX ?? region.Width / 2)).ToArray();
        trackedY = this.regions.Select(region => (double)(region.TargetY ?? region.Height / 2)).ToArray();
        firstTemplatePixels = this.regions.Select(region => new byte[checked(region.Width * region.Height)]).ToArray();
        nextTemplatePixels = this.regions.Select(region => new byte[checked(region.Width * region.Height)]).ToArray();
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
        if (context.Reset) ClearPrevious(resetTrackCentres: true);
        if (previous.Length != 0 && (!context.DeltaSeconds.HasValue || context.DeltaSeconds.Value <= 0 || context.DeltaSeconds.Value > 1))
            throw new InvalidDataException("Flow delta must come from consecutive source time.");
        var current = new List<Mat>(); var results = new List<RoiMotion>();
        double conversionMs = 0, flowMs = 0;
        try
        {
            for (int regionIndex = 0; regionIndex < regions.Length; regionIndex++)
            {
                var roi = regions[regionIndex];
                cancellation.ThrowIfCancellationRequested(); var clock = Stopwatch.StartNew();
                // Crop BEFORE OpenCV conversion: no full-frame Mat or JPEG.
                var crop = global::Android.Graphics.Bitmap.CreateBitmap(image.Bitmap, roi.X, roi.Y, roi.Width, roi.Height)!;
                using var rgba = new NativeMat(new Mat()); var gray = new Mat();
                try { Utils.BitmapToMat(crop, rgba.Value); Imgproc.CvtColor(rgba.Value, gray, Imgproc.ColorRgba2gray); }
                catch { Release(gray); throw; }
                finally { if (!ReferenceEquals(crop, image.Bitmap)) { crop.Recycle(); crop.Dispose(); } }
                current.Add(gray); clock.Stop(); conversionMs += clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                results.Add(previous.Length == 0 ? Seed(roi, gray) : Measure(roi, previous[regionIndex], gray,
                    context.DeltaSeconds!.Value, sourcePixelsPerAnalysisPixelX, sourcePixelsPerAnalysisPixelY,
                    firstTemplatePixels[regionIndex], nextTemplatePixels[regionIndex],
                    ref trackedX[regionIndex], ref trackedY[regionIndex]));
                clock.Stop(); flowMs += clock.Elapsed.TotalMilliseconds;
            }
            cancellation.ThrowIfCancellationRequested(); ClearPrevious(); previous = current.ToArray(); current.Clear();
            return Task.FromResult(new MotionMeasurement(context.SourceTimeUs, results.ToArray(), conversionMs, flowMs)
            { AnalysisImageWidth = image.Bitmap.Width, AnalysisImageHeight = image.Bitmap.Height });
        }
        finally { foreach (var mat in current) Release(mat); }
    }
    static RoiMotion Seed(PixelRoi roi, Mat gray)
    {
        using var points = new NativeMat(new MatOfPoint()); Imgproc.GoodFeaturesToTrack(gray, (MatOfPoint)points.Value, 32, .05, 4);
        return new(roi.Id, (int)points.Value.Total(), 0, null, null, null, null, "seed")
        { TemplateStatus = roi.TargetX.HasValue ? "seed" : "unconfigured" };
    }
    static RoiMotion Measure(PixelRoi roi, Mat before, Mat after, double delta, double sourceScaleX, double sourceScaleY,
        byte[] firstPixels, byte[] nextPixels, ref double trackedX, ref double trackedY)
    {
        var change = MeasureIlluminationNormalizedChange(before, after);
        TemplateDisplacement? template = null;
        string templateStatus = "unconfigured";
        if (roi.TargetX.HasValue)
        {
            before.Get(0, 0, firstPixels); after.Get(0, 0, nextPixels);
            int centreX = (int)Math.Round(trackedX), centreY = (int)Math.Round(trackedY);
            template = VerificationTemplate.CompareGray(firstPixels, nextPixels, before.Cols(), before.Rows(),
                centreX, centreY, roi.TemplateRadius);
            templateStatus = template is null ? "unresolved" : "matched";
            if (template is { } displacement)
            {
                trackedX += displacement.Dx;
                trackedY += displacement.Dy;
            }
        }
        using var corners = new NativeMat(new MatOfPoint()); Imgproc.GoodFeaturesToTrack(before, (MatOfPoint)corners.Value, 32, .05, 4);
        int cornersCount = (int)corners.Value.Total();
        RoiMotion Result(int supported, double? dx, double? dy, double? velocityX, double? velocityY, string status) =>
            new(roi.Id, cornersCount, supported, dx, dy, velocityX, velocityY, status)
            {
                IlluminationNormalizedChangedPixelFraction = change.Fraction,
                MedianBrightnessOffsetGray = change.MedianOffsetGray,
                ChangeThresholdGray = ChangeThresholdGray,
                TemplateDxPixels = template is { } match ? match.Dx * sourceScaleX : null,
                TemplateDyPixels = template is { } matchY ? matchY.Dy * sourceScaleY : null,
                TemplateResidual = template?.Residual,
                TemplateStatus = templateStatus
            };
        if (cornersCount < 4) return Result(0, null, null, null, null, "insufficient-texture");
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
        var found = new float[cornersCount * 2]; var moved = new float[cornersCount * 2]; var returned = new float[cornersCount * 2];
        first.Value.Get(0, 0, found); next.Value.Get(0, 0, moved); back.Value.Get(0, 0, returned);
        var flags = ((MatOfByte)forwardStatus.Value).ToArray(); var reverse = ((MatOfByte)backwardStatus.Value).ToArray();
        var errors = ((MatOfFloat)error.Value).ToArray(); var dx = new List<double>(); var dy = new List<double>();
        int columns = after.Cols(), rows = after.Rows();
        for (int i = 0; i < cornersCount; i++)
        {
            int xIndex = i * 2, yIndex = xIndex + 1;
            if (flags[i] == 0 || reverse[i] == 0 || !float.IsFinite(errors[i]) || errors[i] > 12
                || !float.IsFinite(moved[xIndex]) || !float.IsFinite(moved[yIndex]) || !float.IsFinite(returned[xIndex]) || !float.IsFinite(returned[yIndex])
                || moved[xIndex] < 1 || moved[yIndex] < 1 || moved[xIndex] >= columns - 1 || moved[yIndex] >= rows - 1
                || Math.Sqrt(Math.Pow(returned[xIndex] - found[xIndex], 2) + Math.Pow(returned[yIndex] - found[yIndex], 2)) > 1) continue;
            dx.Add(moved[xIndex] - found[xIndex]); dy.Add(moved[yIndex] - found[yIndex]);
        }
        if (dx.Count < 4 || dx.Count < cornersCount / 2d) return Result(dx.Count, null, null, null, null, "unresolved");
        var x = Median(dx); var y = Median(dy);
        var spread = dx.Select((value, i) => Math.Sqrt(Math.Pow(value - x, 2) + Math.Pow(dy[i] - y, 2))).Order().ToArray();
        if (!double.IsFinite(x) || !double.IsFinite(y) || spread[(int)Math.Ceiling(.95 * spread.Length) - 1] > 1.5)
            return Result(dx.Count, null, null, null, null, "inconsistent-flow");
        return Result(dx.Count, x * sourceScaleX, y * sourceScaleY,
            x * sourceScaleX / delta, y * sourceScaleY / delta, "supported");
    }
    static (double Fraction, double MedianOffsetGray) MeasureIlluminationNormalizedChange(Mat before, Mat after)
    {
        int pixelCount = checked(before.Rows() * before.Cols());
        var beforePixels = new byte[pixelCount]; var afterPixels = new byte[pixelCount];
        before.Get(0, 0, beforePixels); after.Get(0, 0, afterPixels);
        Span<int> histogram = stackalloc int[511];
        for (int i = 0; i < pixelCount; i++) histogram[afterPixels[i] - beforePixels[i] + 255]++;
        double medianOffset = (HistogramRank(histogram, (pixelCount - 1) / 2) + HistogramRank(histogram, pixelCount / 2)) / 2d;
        int changed = 0;
        for (int i = 0; i < pixelCount; i++)
            if (Math.Abs(afterPixels[i] - beforePixels[i] - medianOffset) >= ChangeThresholdGray) changed++;
        return (changed / (double)pixelCount, medianOffset);
    }
    static int HistogramRank(ReadOnlySpan<int> histogram, int rank)
    {
        for (int i = 0; i < histogram.Length; i++)
        {
            rank -= histogram[i];
            if (rank < 0) return i - 255;
        }
        throw new InvalidDataException("Brightness-offset histogram did not contain the requested rank.");
    }
    static double Median(List<double> values) { var sorted = values.Order().ToArray(); return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2; }
    void ClearPrevious(bool resetTrackCentres = false)
    {
        foreach (var mat in previous) Release(mat);
        previous = [];
        if (!resetTrackCentres) return;
        for (int i = 0; i < regions.Length; i++)
        {
            trackedX[i] = regions[i].TargetX ?? regions[i].Width / 2;
            trackedY[i] = regions[i].TargetY ?? regions[i].Height / 2;
        }
    }
    static void Release(Mat mat) { mat.Release(); mat.Dispose(); }
    public void Dispose() { if (disposed) return; disposed = true; ClearPrevious(); }
    sealed class NativeMat(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() => Release(Value); }
}
