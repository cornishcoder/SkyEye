using SkyEye.Core;
using SkyEye.Core.Analysis;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using CvPoint = Org.Opencv.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    sealed class BurstAppearance { public bool Frozen, ReferenceValid; public double[] Gains = [1, 1, 1]; }
    public ITableVerificationWorker CreateVerification(TableCalibration calibration) => new VerificationWorker(this, calibration);
    sealed class VerificationWorker(VisionAnalyzer owner, TableCalibration calibration) : ITableVerificationWorker
    {
        readonly BurstAppearance appearance = new();
        readonly VerificationPixels motion = new(200, 400);
        bool? geometryValid;
        bool disposed;
        public VerificationMeasurement Measure(TableFrame frame, bool detect, CancellationToken cancellation)
        {
            ObjectDisposedException.ThrowIf(disposed, this); cancellation.ThrowIfCancellationRequested(); Load();
            using var bgr = new MatLease(new Mat());
            if (frame.Image is BitmapTableImage bitmap)
            {
                using var rgba = new MatLease(new Mat()); Utils.BitmapToMat(bitmap.Bitmap, rgba.Value);
                Imgproc.CvtColor(rgba.Value, bgr.Value, Imgproc.ColorRgba2bgr);
            }
            else { using var decoded = Decode(frame.Jpeg); decoded.Value.CopyTo(bgr.Value); }
            if (geometryValid is null)
            {
                // Observe reference geometry once; never overwrite a stored calibration.
                var pockets = owner.FindPockets(frame.ReviewImage(), cancellation);
                geometryValid = pockets is not null && pockets.Corners.All(p => calibration.Corners.Min(c => (c - p).Length) <= .035);
            }
            VisionResult? vision = null;
            if (detect) { PipelineDiagnostics.Count("verificationFullAnalyses"); vision = owner.AnalyzePixels(bgr.Value, calibration, cancellation, appearance); }
            using var gray = new MatLease(new Mat());
            // Downsize first. Perspective motion work retains only two bounded gray buffers.
            using var small = new MatLease(new Mat()); using var sourceSize = new Size(480, 270);
            Imgproc.Resize(bgr.Value, small.Value, sourceSize);
            var points = calibration.Corners.Select(p => new CvPoint(p.X * 480, p.Y * 270)).ToArray();
            CvPoint[] target = [new(200, 0), new(200, 400), new(0, 400), new(0, 0)];
            try
            {
                using var src = new MatLease(new MatOfPoint2f(points)); using var dst = new MatLease(new MatOfPoint2f(target));
                using var transform = new MatLease(Imgproc.GetPerspectiveTransform(src.Value, dst.Value));
                using var warped = new MatLease(new Mat()); using var size = new Size(200, 400);
                Imgproc.WarpPerspective(small.Value, warped.Value, transform.Value, size); Imgproc.CvtColor(warped.Value, gray.Value, Imgproc.ColorBgr2gray);
                cancellation.ThrowIfCancellationRequested();
                var measured = motion.Measure(Read(gray.Value), geometryValid == true, vision);
                return vision?.VerificationReason is { } reason ? measured with { Reason = reason } : measured;
            }
            finally { foreach (var p in points.Concat(target)) p.Dispose(); }
        }
        public void Dispose() => disposed = true;
    }
}
