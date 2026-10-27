using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Cv = Org.Opencv.Core.Core;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    void AddDarkBodies(Mat raw, List<Circle> circles, double scale,
        Dictionary<Circle, BallClassification>? packedClassifications,
        List<VisionCandidateAudit>? candidateAudits, string pass)
    {
        using var hsv = new MatLease(new Mat()); Imgproc.CvtColor(raw, hsv.Value, Imgproc.ColorBgr2hsv);
        using var mask = new MatLease(new Mat());
        using var low = new Scalar(0, 0, 0); using var high = new Scalar(179, 255, settings.DarkBodyValueCeiling);
        Cv.InRange(hsv.Value, low, high, mask.Value);
        using var size = new Size(3, 3);
        using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, size));
        Imgproc.MorphologyEx(mask.Value, mask.Value, Imgproc.MorphOpen, kernel.Value);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(mask.Value, labels.Value, stats.Value, centres.Value);
        byte[]? rawPixels = null, colour = null; int examined = 0;
        for (int i = 1; i < count && circles.Count <= 32; i++)
        {
            double w = stats.Value.Get(i, Imgproc.CcStatWidth)![0], h = stats.Value.Get(i, Imgproc.CcStatHeight)![0];
            double area = stats.Value.Get(i, Imgproc.CcStatArea)![0], radius = (w + h) / 4;
            if (radius < settings.MinimumRadius * scale || radius > settings.MaximumRadius * scale
                || w / h is < .6 or > 1.6 || area / (w * h) < .55) continue;
            var proposal = new Circle(centres.Value.Get(i, 0)![0], centres.Value.Get(i, 1)![0], radius);
            if (PocketCentres().Any(p => (new Point(proposal.X, proposal.Y) - p).Length < settings.PocketExclusionRadius * scale))
            { Audit("rejected-pocket-exclusion", proposal); continue; }
            if (++examined > 4) break;
            rawPixels ??= Read(raw); colour ??= Read(hsv.Value);
            var fit = BallBoundaryRefiner.Refine(rawPixels, coordinates.Width, coordinates.Height,
                new(proposal.X, proposal.Y), PoolGeometry.Radius * coordinates.Width);
            if (fit is null)
            { Audit("rejected-no-boundary-fit", proposal); continue; }
            if (fit.SupportedSectors < ObservationPolicy.MinimumSupportedBoundaryFraction)
            { Audit("rejected-insufficient-boundary-support", proposal, fit, fit.SupportedSectors); continue; }
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            string kind = Classify(body, colour, true).Kind;
            if (kind != "black")
            { Audit("rejected-classification", proposal, fit, fit.SupportedSectors, kind); continue; }
            if (circles.Any(p => Distance(p, body) < settings.Separation * scale && IsSameOrUnclearKind(p, kind)))
            { Audit("rejected-duplicate", proposal, fit, fit.SupportedSectors, kind); continue; }
            circles.Add(body);
            if (packedClassifications is not null)
                packedClassifications[body] = Classify(body, colour, true);
            Audit("added-strong-dark-body", proposal, fit, fit.SupportedSectors, kind);

            bool IsSameOrUnclearKind(Circle existing, string candidateKind)
            {
                string existingKind = packedClassifications is not null && packedClassifications.TryGetValue(existing, out var known)
                    ? known.Kind : Classify(existing, colour, true).Kind;
                return existingKind == "unknown" || candidateKind == "unknown" || existingKind == candidateKind;
            }

            void Audit(string decision, Circle seed, BallBoundary? boundary = null,
                double? support = null, string? classifiedKind = null)
                => candidateAudits?.Add(new(pass, "dark-body", "black", i,
                    new(seed.X, seed.Y), seed.Radius, boundary?.Centre, boundary?.Radius,
                    support, classifiedKind, decision));
        }
    }
}
