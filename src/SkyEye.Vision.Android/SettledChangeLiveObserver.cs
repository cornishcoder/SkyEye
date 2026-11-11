using System.Diagnostics;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using SkyEye.Core;
using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Android;

/// <summary>D3 slice 4: runs <see cref="SettledChangeProcessor"/> on live Live Detect frames, in shadow.
/// Called from the Live Detect frame observer while the frame's bitmap is still alive; it copies what it
/// needs into its own Mats and keeps no frame or bitmap. One instance per Live Detect run.
/// <list type="bullet">
/// <item>Resets (new epoch) when the frame's timeline, decoder generation or the calibration changes.</item>
/// <item>Catches its own errors: an exception resets the processor and returns an unavailable pass.</item>
/// <item>Turns itself off for the run when <see cref="SettledChangeCostGuard"/> trips (median over 40 ms).</item>
/// </list></summary>
public sealed class SettledChangeLiveObserver : IDisposable
{
    readonly SettledChangeComparer comparer = new();
    readonly SettledChangeProcessor processor;
    readonly SettledChangeCostGuard costGuard = new();
    readonly TableCalibration calibration;
    readonly string calibrationId;
    Mat? rgba, bgr;
    (string Timeline, long Generation)? key;
    bool disposed;

    public SettledChangeLiveObserver(TableCalibration calibration, string calibrationId)
    {
        this.calibration = calibration; this.calibrationId = calibrationId;
        processor = new(comparer);
    }

    public string? DisabledReason { get; private set; }
    public Guid Epoch => processor.Epoch;

    public SettledChangePass Observe(TableFrame frame)
    {
        if (disposed) return SettledChangePass.Unavailable("disposed");
        if (DisabledReason is { } reason) return SettledChangePass.Unavailable(reason);
        if (frame.Image is not BitmapTableImage image) return SettledChangePass.Unavailable("no raw image");
        if (frame.SourceTimeUs is not { } sourceTimeUs) return SettledChangePass.Unavailable("no source time");
        var frameKey = (frame.Timeline, frame.DecodedReceipt?.MediaGeneration ?? 0);
        if (key is { } previous && previous != frameKey) processor.Reset();
        key = frameKey;
        var watch = Stopwatch.StartNew();
        try
        {
            rgba ??= new Mat(); bgr ??= new Mat();
            Utils.BitmapToMat(image.Bitmap, rgba);
            Imgproc.CvtColor(rgba, bgr, Imgproc.ColorRgba2bgr);
            var raster = comparer.Warp(bgr, calibration.Corners);
            var pass = processor.Observe(raster, sourceTimeUs);
            double ms = watch.Elapsed.TotalMilliseconds;
            if (costGuard.Add(ms))
            {
                DisabledReason = $"off for this run: median {costGuard.MedianMs:0} ms over {costGuard.Window} passes exceeds {costGuard.LimitMs:0} ms";
                processor.Reset();
            }
            return pass with { ComputeMs = ms };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            processor.Reset();
            return SettledChangePass.Unavailable(error.GetType().Name, watch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>Ask the processor for one still check on a later pass (see <see cref="SettledChangeProcessor.RequestStillCheck"/>).</summary>
    public void RequestStillCheck() { if (!disposed && DisabledReason is null) processor.RequestStillCheck(); }

    /// <summary>JPEG anchor and current rasters of the latest still check; null when there is none.</summary>
    public (byte[] Anchor, byte[] Current)? EncodeLatestStillCheck()
    {
        if (processor.LastStillCheckRasters is not { } rasters) return null;
        return (comparer.EncodeJpeg(rasters.Anchor), comparer.EncodeJpeg(rasters.Current));
    }

    /// <summary>JPEG reference and current rasters of the latest comparison; null when there is none.</summary>
    public (byte[] Reference, byte[] Current)? EncodeLatestComparison()
    {
        if (processor.LastComparisonRasters is not { } rasters) return null;
        return (comparer.EncodeJpeg(rasters.Reference), comparer.EncodeJpeg(rasters.Current));
    }

    /// <summary>The calibration this observer rectifies with; Live Detect stops on a calibration change.</summary>
    public string CalibrationId => calibrationId;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var mat in new[] { rgba, bgr }) if (mat is not null) { mat.Release(); mat.Dispose(); }
        rgba = bgr = null;
    }
}
