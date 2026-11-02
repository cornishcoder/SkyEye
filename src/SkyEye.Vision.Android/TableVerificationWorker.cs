using SkyEye.Core;
using SkyEye.Core.Analysis;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using CvPoint = Org.Opencv.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    sealed class BurstAppearance { public bool HasReference, ReferenceValid; public double[] Gains = [1, 1, 1]; }
    public ITableVerificationWorker CreateVerification(TableCalibration calibration) => new VerificationWorker(this, calibration);
    sealed class VerificationWorker(VisionAnalyzer owner, TableCalibration calibration) : ITableVerificationWorker
    {
        readonly BurstAppearance appearance = new();
        const int motionWidth = 400, motionHeight = 800;
        VerificationPixels motion = new(motionWidth, motionHeight);
        byte[]? anchorColour, previousColour;
        CircleObservation[] bodies = [];
        CircleObservation[]? anchorDetections;
        VisionResult? anchorVision;
        bool? geometryValid;
        bool disposed;
        public void ResetAfterObstruction()
        {
            anchorColour = previousColour = null;
            bodies = []; anchorDetections = null; anchorVision = null;
            geometryValid = null;
            appearance.HasReference = false; appearance.ReferenceValid = false; appearance.Gains = [1, 1, 1];
            motion = new(motionWidth, motionHeight);
        }
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
                // FindCorners first uses the stricter pocket layout, then its existing
                // rail/corner fallback. A missed side pocket must not invalidate a
                // fixed, otherwise matching camera view.
                var corners = owner.FindCorners(frame.ReviewImage(), cancellation);
                geometryValid = corners is not null && corners.All(p => calibration.Corners.Min(c => (c - p).Length) <= .035);
            }
            VisionResult? vision = null;
            if (detect) { PipelineDiagnostics.Count("verificationFullAnalyses"); vision = owner.AnalyzePixels(bgr.Value, calibration, cancellation, appearance); }
            double? detectedDisplacement = null;
            if (detect && vision is not null)
            {
                if (anchorDetections is null) { anchorDetections = vision.Circles; anchorVision = vision; }
                else detectedDisplacement = VerificationObjectMotion.MaximumSupportedDisplacement(anchorDetections, vision.Circles);
            }
            using var gray = new MatLease(new Mat());
            // Warp directly from decoded pixels: avoid throwing away ball detail
            // in an intermediate 480x270 resize. Retain bounded motion images only.
            var points = calibration.Corners.Select(p => new CvPoint(p.X * bgr.Value.Cols(), p.Y * bgr.Value.Rows())).ToArray();
            CvPoint[] target = [new(motionWidth, 0), new(motionWidth, motionHeight), new(0, motionHeight), new(0, 0)];
            try
            {
                using var src = new MatLease(new MatOfPoint2f(points)); using var dst = new MatLease(new MatOfPoint2f(target));
                using var transform = new MatLease(Imgproc.GetPerspectiveTransform(src.Value, dst.Value));
                using var warped = new MatLease(new Mat()); using var size = new Size(motionWidth, motionHeight);
                Imgproc.WarpPerspective(bgr.Value, warped.Value, transform.Value, size); Imgproc.CvtColor(warped.Value, gray.Value, Imgproc.ColorBgr2gray);
                cancellation.ThrowIfCancellationRequested();
                var colour = Read(warped.Value);
                var mask = new bool[motionWidth * motionHeight];
                double maximum = 0; int supported = 0, regionCount = 0;
                if (anchorColour is not null)
                {
                    // Include both initial and newly detected bodies; colour labels
                    // are not prerequisites. Full-area colour differences cover extras.
                    var regions = bodies.ToList();
                    foreach (var current in vision?.Circles ?? [])
                        if (!regions.Any(b => (b.Centre - current.Centre).Length < Math.Min(b.Radius, current.Radius) * .5)) regions.Add(current);
                    regionCount = regions.Count;
                    foreach (var body in regions)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        double scale = motionWidth / (double)owner.coordinates.Width;
                        int radius = Math.Max(12, (int)Math.Ceiling(body.Radius * scale * 1.8));
                        int x = (int)(body.Centre.X * scale), y = (int)(body.Centre.Y * scale);
                        var a = VerificationTemplate.CompareClipped(anchorColour, colour, motionWidth, motionHeight, x, y, radius);
                        var p = VerificationTemplate.CompareClipped(previousColour!, colour, motionWidth, motionHeight, x, y, radius);
                        if (a is null || p is null) continue;
                        supported++;
                        maximum = Math.Max(maximum, Math.Max(Math.Sqrt(a.Dx * a.Dx + a.Dy * a.Dy), Math.Sqrt(p.Dx * p.Dx + p.Dy * p.Dy)));
                        for (int py = Math.Max(0, y - radius); py <= Math.Min(motionHeight - 1, y + radius); py++)
                        for (int px = Math.Max(0, x - radius); px <= Math.Min(motionWidth - 1, x + radius); px++)
                            if ((px - x) * (px - x) + (py - y) * (py - y) <= radius * radius) mask[py * motionWidth + px] = true;
                    }
                }
                var measured = motion.Measure(Read(gray.Value), geometryValid == true, vision, colour, mask)
                    with { MaximumBodyDisplacement = anchorColour is null ? null : maximum, MaximumDetectedBodyDisplacement = detectedDisplacement,
                        MinimumTemplateMotionPixels = ObservationPolicy.MinimumTemplateBallMotionPixels, SupportedBodies = supported };
                bool templatesShowNoMotion = false;
                if (anchorColour is not null)
                {
                    if ((measured.Reason is ObservationReason.None or ObservationReason.InsufficientEvidence)
                        && (measured.Motion is TableMotion.Unknown or TableMotion.Still))
                    {
                        if (maximum > ObservationPolicy.MinimumTemplateBallMotionPixels)
                            measured = measured with { Motion = TableMotion.Moving, Reason = ObservationReason.Moving };
                        else if (regionCount == 0 || supported != regionCount)
                            measured = measured with { Motion = TableMotion.Unknown, Reason = ObservationReason.InsufficientEvidence, CoverageValid = false };
                        else
                        {
                            // Complete, unchanged local templates refute detector-centre
                            // jitter. Recognition uncertainty can still block layout
                            // readiness without turning that jitter into ball motion.
                            templatesShowNoMotion = true;
                            var supportedStillness = measured with
                                { Motion = TableMotion.Unknown, Reason = ObservationReason.InsufficientEvidence, Vision = vision ?? anchorVision };
                            if (VerificationStillnessPolicy.CanResolveAsStill(supportedStillness, allTrackedBodiesSupported: true))
                                measured = measured with { Motion = TableMotion.Still, Reason = ObservationReason.None };
                            else if (measured.PerimeterChange > VerificationStillnessPolicy.MaximumSupportedPerimeterChange
                                && VerificationStillnessPolicy.CanResolveAsStill(supportedStillness with { PerimeterChange = 0 }, allTrackedBodiesSupported: true))
                            {
                                // A perimeter-only photometric step must not keep
                                // comparing every later sample to a stale exposure
                                // anchor. Before rebasing, independently confirm the
                                // stored table geometry still matches this frame. The
                                // transition frame remains inconclusive; only later
                                // samples can begin a fresh stillness span.
                                var corners = owner.FindCorners(frame.ReviewImage(), cancellation);
                                bool geometryMatches = corners is not null
                                    && corners.All(p => calibration.Corners.Min(c => (c - p).Length) <= .035);
                                if (geometryMatches && motion.ReanchorToLatest())
                                {
                                    anchorColour = colour;
                                    if (vision is not null) bodies = vision.Circles;
                                    measured = measured with { Motion = TableMotion.Still, Reason = ObservationReason.None,
                                        GeometryRevalidatedAfterPerimeterChange = true };
                                }
                                else
                                {
                                    geometryValid = false;
                                    measured = measured with { Motion = TableMotion.Unknown,
                                        Reason = ObservationReason.CalibrationChanged, CoverageValid = false };
                                }
                            }
                        }
                    }
                }
                else { anchorColour = colour; bodies = vision?.Circles ?? []; }
                // A poor or obstructed layout can shift the same-colour matching
                // assignment when a packed ball is missing, fabricating a large
                // displacement between two different balls. Require clear detections
                // at both ends before trusting detector-centre motion.
                if (!templatesShowNoMotion
                    && VerificationObjectMotion.IsReliablePositiveEvidence(anchorVision, vision, detectedDisplacement,
                        Math.Max(5, owner.coordinates.Width * ObservationPolicy.MinimumDetectedBallMotionFraction),
                        maximum, ObservationPolicy.MinimumTemplateBallMotionPixels))
                    measured = measured with { Motion = TableMotion.Moving, Reason = ObservationReason.Moving };
                previousColour = colour;
                return vision?.VerificationReason is { } reason ? measured with { Reason = reason } : measured;
            }
            finally { foreach (var p in points.Concat(target)) p.Dispose(); }
        }
        public void Dispose() { disposed = true; anchorColour = previousColour = null; bodies = []; anchorVision = null; }
    }
}
