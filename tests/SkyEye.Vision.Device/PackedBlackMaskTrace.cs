using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using Cv = Org.Opencv.Core.Core;
using CvPoint = Org.Opencv.Core.Point;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Device;

/// <summary>Evaluation-only trace of the packed and sparse black-ball proposal masks. It never supplies a point to production decisions.</summary>
internal static class PackedBlackMaskTrace
{
    const int ManualImageX = 1282;
    const int ManualImageY = 536;
    const int DarkValueCeiling = 40;
    const int PackedClusterImageEdgeMargin = 23;
    const int MaximumRankedSparseBlackPocketArcProbes = 32;
    const double RadialBoundaryProbeExtentFraction = 1.3;
    const double RadialBoundaryProbeMinimumSupport = 21d / 32;

    internal readonly record struct Peak(int ComponentIndex, double X, double Y, double Radius, int Area, double DistanceFromLabel,
        bool PassesMargin, bool PassesMinimumRadius, bool InSearchWindow);

    internal readonly record struct PreparedPeak(int ComponentIndex, double X, double Y, double Radius, int Area,
        bool PassesMargin, bool PassesMinimumRadius, bool InSearchWindow)
    {
        public Peak At(Point label) => new(ComponentIndex, X, Y, Radius, Area,
            Math.Sqrt(Math.Pow(X - label.X, 2) + Math.Pow(Y - label.Y, 2)),
            PassesMargin, PassesMinimumRadius, InSearchWindow);
    }

    internal readonly record struct PreparedComponent(int X, int Y, int Width, int Height, int Area, double CentreX, double CentreY);

    internal sealed class PreparedFrame
    {
        internal readonly int Width, SourceWidth, SourceHeight, CandidateAuditGeometryWidth;
        internal readonly int NearbyPeakComponentCount, GlobalPeakComponentCount, CroppedQualifiedSeedCount;
        internal readonly int WindowLeft, WindowRight, WindowTop, WindowBottom, WindowPixelCount;
        internal readonly int RawDarkPixelsInWindow, OpenedDarkPixelsInWindow;
        internal readonly double Scale, BallRadius, MinimumPeakRadius, MinimumPeakRadiusFactor;
        internal readonly double AuditScale, PocketOcclusionRadius;
        internal readonly double Left, Right, Top, Bottom;
        internal readonly bool HasPackedWindow;
        internal readonly string SearchWindowSource;
        internal readonly Point[] SelectedCentres, CalibratedPocketCentres;
        internal readonly PreparedPeak[] LocalMaxima, QualifiedSeeds, ProductionSeeds;
        internal readonly PreparedComponent[] DarkComponents;
        internal readonly HashSet<(int X, int Y)> LocalProductionSeedPixels;
        internal readonly byte[] BgrBytes, HsvBytes, ThresholdMaskBytes, OpenedMaskBytes;
        internal readonly float[] DistanceValues;

        internal PreparedFrame(int width, int sourceWidth, int sourceHeight, int candidateAuditGeometryWidth,
            int nearbyPeakComponentCount, int globalPeakComponentCount, int croppedQualifiedSeedCount,
            int windowLeft, int windowRight, int windowTop, int windowBottom, int windowPixelCount,
            int rawDarkPixelsInWindow, int openedDarkPixelsInWindow, double scale, double ballRadius,
            double minimumPeakRadius, double minimumPeakRadiusFactor, double auditScale, double pocketOcclusionRadius,
            double left, double right, double top, double bottom, bool hasPackedWindow, string searchWindowSource,
            Point[] selectedCentres, Point[] calibratedPocketCentres, PreparedPeak[] localMaxima,
            PreparedPeak[] qualifiedSeeds, PreparedPeak[] productionSeeds, PreparedComponent[] darkComponents,
            HashSet<(int X, int Y)> localProductionSeedPixels, byte[] bgrBytes, byte[] hsvBytes,
            byte[] thresholdMaskBytes, byte[] openedMaskBytes, float[] distanceValues)
        {
            Width = width; SourceWidth = sourceWidth; SourceHeight = sourceHeight;
            CandidateAuditGeometryWidth = candidateAuditGeometryWidth;
            NearbyPeakComponentCount = nearbyPeakComponentCount; GlobalPeakComponentCount = globalPeakComponentCount;
            CroppedQualifiedSeedCount = croppedQualifiedSeedCount;
            WindowLeft = windowLeft; WindowRight = windowRight; WindowTop = windowTop; WindowBottom = windowBottom;
            WindowPixelCount = windowPixelCount; RawDarkPixelsInWindow = rawDarkPixelsInWindow;
            OpenedDarkPixelsInWindow = openedDarkPixelsInWindow;
            Scale = scale; BallRadius = ballRadius; MinimumPeakRadius = minimumPeakRadius;
            MinimumPeakRadiusFactor = minimumPeakRadiusFactor; AuditScale = auditScale;
            PocketOcclusionRadius = pocketOcclusionRadius; Left = left; Right = right; Top = top; Bottom = bottom;
            HasPackedWindow = hasPackedWindow; SearchWindowSource = searchWindowSource;
            SelectedCentres = selectedCentres; CalibratedPocketCentres = calibratedPocketCentres;
            LocalMaxima = localMaxima; QualifiedSeeds = qualifiedSeeds; ProductionSeeds = productionSeeds;
            DarkComponents = darkComponents; LocalProductionSeedPixels = localProductionSeedPixels;
            BgrBytes = bgrBytes; HsvBytes = hsvBytes; ThresholdMaskBytes = thresholdMaskBytes;
            OpenedMaskBytes = openedMaskBytes; DistanceValues = distanceValues;
        }
    }

    public static PreparedFrame Prepare(byte[] encodedImage, TableCalibration calibration, int width, VisionResult vision,
        double minimumPeakRadiusFactor = 8,
        double pocketExclusionRadiusAtWidth1000 = VisionSettings.DefaultPocketExclusionRadius)
    {
        using var encoded = new MatOfByte(encodedImage);
        using var source = Imgcodecs.Imdecode(encoded, Imgcodecs.ImreadColor);
        int sourceWidth = source.Cols(), sourceHeight = source.Rows();
        CvPoint[] original = calibration.Corners.Select(p => new CvPoint(p.X * sourceWidth, p.Y * sourceHeight)).ToArray();
        CvPoint[] portrait = [new(width, 0), new(width, width * 2), new(0, width * 2), new(0, 0)];
        try
        {
            using var from = new MatOfPoint2f(original); using var to = new MatOfPoint2f(portrait);
            using var transform = Imgproc.GetPerspectiveTransform(from, to);
            using var warped = new Mat(); using var outputSize = new Size(width, width * 2);
            Imgproc.WarpPerspective(source, warped, transform, outputSize);
            using var hsv = new Mat(); Imgproc.CvtColor(warped, hsv, Imgproc.ColorBgr2hsv);
            using var thresholdMask = new Mat(); using var low = new Scalar(0, 0, 0); using var high = new Scalar(179, 255, DarkValueCeiling);
            Cv.InRange(hsv, low, high, thresholdMask);
            using var openedMask = new Mat(); thresholdMask.CopyTo(openedMask);
            using var cleanupSize = new Size(3, 3); using var cleanupKernel = Imgproc.GetStructuringElement(Imgproc.MorphEllipse, cleanupSize);
            Imgproc.MorphologyEx(openedMask, openedMask, Imgproc.MorphOpen, cleanupKernel);

            double scale = width / 1000d;
            double ballRadius = PoolGeometry.Radius * width;
            double minimumPeakRadius = minimumPeakRadiusFactor * scale;
            int candidateAuditGeometryWidth = width;
            if (vision.PackedClusterFallback is { Attempted: true } fallback &&
                (fallback.Recovered || fallback.FallbackBallCount > fallback.PrimaryBallCount))
                candidateAuditGeometryWidth = fallback.FallbackAnalysisWidth;
            double auditScale = width / (double)candidateAuditGeometryWidth;
            var selectedCentres = vision.CandidateAudits.Where(a => a.Pass == "initial" && a.Stage == "packed-window" && a.Decision == "selected")
                .Select(a => a.ProposalCentre * auditScale).ToArray();
            bool hasPackedWindow = selectedCentres.Length >= 12;
            double left = hasPackedWindow ? selectedCentres.Min(p => p.X) - 55 * scale : PackedClusterImageEdgeMargin * scale;
            double right = hasPackedWindow ? selectedCentres.Max(p => p.X) + 55 * scale : width - PackedClusterImageEdgeMargin * scale;
            double top = hasPackedWindow ? selectedCentres.Min(p => p.Y) - 55 * scale : PackedClusterImageEdgeMargin * scale;
            double bottom = hasPackedWindow ? selectedCentres.Max(p => p.Y) + 55 * scale : 2 * width - PackedClusterImageEdgeMargin * scale;
            string searchWindowSource = hasPackedWindow ? "packed-cluster" : "full-table-sparse-black";

            using var distance = new Mat(); Imgproc.DistanceTransform(openedMask, distance, Imgproc.DistL2, 3);
            float[] distanceValues = new float[width * width * 2];
            if (distance.Get(0, 0, distanceValues) < distanceValues.Length)
                throw new IOException("Could not read the prepared dark-mask distance transform.");
            int neighbourhood = Math.Max(5, (int)Math.Round(17 * scale) | 1);
            using var neighbourhoodSize = new Size(neighbourhood, neighbourhood);
            using var neighbourhoodKernel = Imgproc.GetStructuringElement(Imgproc.MorphRect, neighbourhoodSize);
            using var dilated = new Mat(); Imgproc.Dilate(distance, dilated, neighbourhoodKernel);
            using var rawLocalMaxima = new Mat(); Cv.Compare(distance, dilated, rawLocalMaxima, Cv.CmpEq);
            using var positiveDistance = new Mat(); using var positiveFloor = new Scalar(.5);
            Cv.Compare(distance, positiveFloor, positiveDistance, Cv.CmpGt);
            using var visibleLocalMaxima = new Mat(); Cv.Bitwise_and(rawLocalMaxima, positiveDistance, visibleLocalMaxima);
            using var nearbyLabels = new Mat(); using var nearbyStats = new Mat(); using var nearbyCentres = new Mat();
            int nearbyPeakCount = Imgproc.ConnectedComponentsWithStats(visibleLocalMaxima, nearbyLabels, nearbyStats, nearbyCentres);
            var localMaxima = new PreparedPeak[Math.Max(0, nearbyPeakCount - 1)];
            for (int i = 1; i < nearbyPeakCount; i++)
            {
                double x = nearbyCentres.Get(i, 0)![0], y = nearbyCentres.Get(i, 1)![0];
                double radius = distanceValues[(int)Math.Round(y) * width + (int)Math.Round(x)];
                bool passesMargin = x >= PackedClusterImageEdgeMargin * scale && y >= PackedClusterImageEdgeMargin * scale && x < width - PackedClusterImageEdgeMargin * scale && y < 2 * width - PackedClusterImageEdgeMargin * scale;
                bool passesMinimumRadius = radius >= minimumPeakRadius;
                bool inWindow = x >= left && x <= right && y >= top && y <= bottom;
                localMaxima[i - 1] = new(i, x, y, radius, (int)nearbyStats.Get(i, Imgproc.CcStatArea)![0],
                    passesMargin, passesMinimumRadius, inWindow);
            }

            using var aboveMinimum = new Mat(); using var minimum = new Scalar(minimumPeakRadius);
            Cv.Compare(distance, minimum, aboveMinimum, Cv.CmpGt);
            using var productionPeakMap = new Mat(); Cv.Bitwise_and(rawLocalMaxima, aboveMinimum, productionPeakMap);
            using var globalPeakLabels = new Mat(); using var globalPeakStats = new Mat(); using var globalPeakCentres = new Mat();
            int globalPeakCount = Imgproc.ConnectedComponentsWithStats(productionPeakMap, globalPeakLabels,
                globalPeakStats, globalPeakCentres);
            int searchLeft = Math.Clamp((int)Math.Floor(left), 0, width - 1);
            int searchRight = Math.Clamp((int)Math.Ceiling(right), searchLeft, width - 1);
            int searchTop = Math.Clamp((int)Math.Floor(top), 0, width * 2 - 1);
            int searchBottom = Math.Clamp((int)Math.Ceiling(bottom), searchTop, width * 2 - 1);
            using var croppedPeakMap = new Mat(productionPeakMap, new Rect(searchLeft, searchTop,
                searchRight - searchLeft + 1, searchBottom - searchTop + 1));
            using var productionPeakLabels = new Mat(); using var productionPeakStats = new Mat(); using var productionPeakCentres = new Mat();
            int productionPeakCount = Imgproc.ConnectedComponentsWithStats(croppedPeakMap, productionPeakLabels,
                productionPeakStats, productionPeakCentres);
            var qualifiedSeeds = new PreparedPeak[Math.Max(0, globalPeakCount - 1)];
            for (int i = 1; i < globalPeakCount; i++)
            {
                double x = globalPeakCentres.Get(i, 0)![0], y = globalPeakCentres.Get(i, 1)![0];
                double radius = distanceValues[(int)Math.Round(y) * width + (int)Math.Round(x)];
                bool passesMargin = x >= PackedClusterImageEdgeMargin * scale && y >= PackedClusterImageEdgeMargin * scale && x < width - PackedClusterImageEdgeMargin * scale && y < 2 * width - PackedClusterImageEdgeMargin * scale;
                bool inWindow = x >= left && x <= right && y >= top && y <= bottom;
                qualifiedSeeds[i - 1] = new(i, x, y, radius,
                    (int)globalPeakStats.Get(i, Imgproc.CcStatArea)![0], passesMargin, true, inWindow);
            }
            int croppedQualifiedSeedCount = productionPeakCount - 1;
            var productionSeeds = new List<PreparedPeak>();
            for (int i = 1; i < productionPeakCount && i <= 64; i++)
            {
                double x = productionPeakCentres.Get(i, 0)![0] + searchLeft;
                double y = productionPeakCentres.Get(i, 1)![0] + searchTop;
                if (x < PackedClusterImageEdgeMargin * scale || y < PackedClusterImageEdgeMargin * scale || x >= width - PackedClusterImageEdgeMargin * scale || y >= 2 * width - PackedClusterImageEdgeMargin * scale) continue;
                double radius = distanceValues[(int)Math.Round(y) * width + (int)Math.Round(x)];
                if (radius < minimumPeakRadius) continue;
                productionSeeds.Add(new(i, x, y, radius,
                    (int)productionPeakStats.Get(i, Imgproc.CcStatArea)![0], true, true, true));
            }
            var localProductionSeedPixels = productionSeeds.Select(p => ((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToHashSet();

            byte[] bgrBytes = new byte[width * width * 2 * 3]; warped.Get(0, 0, bgrBytes);
            byte[] hsvBytes = new byte[width * width * 2 * 3]; hsv.Get(0, 0, hsvBytes);
            byte[] thresholdMaskBytes = new byte[width * width * 2]; thresholdMask.Get(0, 0, thresholdMaskBytes);
            byte[] openedMaskBytes = new byte[width * width * 2]; openedMask.Get(0, 0, openedMaskBytes);
            int windowLeft = Math.Clamp((int)Math.Floor(left), 0, width - 1);
            int windowRight = Math.Clamp((int)Math.Ceiling(right), windowLeft, width - 1);
            int windowTop = Math.Clamp((int)Math.Floor(top), 0, width * 2 - 1);
            int windowBottom = Math.Clamp((int)Math.Ceiling(bottom), windowTop, width * 2 - 1);
            int windowPixelCount = (windowRight - windowLeft + 1) * (windowBottom - windowTop + 1);
            int rawDarkPixelsInWindow = 0, openedDarkPixelsInWindow = 0;
            for (int y = windowTop; y <= windowBottom; y++)
            for (int x = windowLeft; x <= windowRight; x++)
            {
                int pixel = y * width + x;
                if (thresholdMaskBytes[pixel] != 0) rawDarkPixelsInWindow++;
                if (openedMaskBytes[pixel] != 0) openedDarkPixelsInWindow++;
            }

            using var componentLabels = new Mat(); using var componentStats = new Mat(); using var componentCentres = new Mat();
            int componentCount = Imgproc.ConnectedComponentsWithStats(openedMask, componentLabels, componentStats, componentCentres);
            var darkComponents = new PreparedComponent[Math.Max(0, componentCount - 1)];
            for (int i = 1; i < componentCount; i++)
            {
                darkComponents[i - 1] = new(
                    (int)componentStats.Get(i, Imgproc.CcStatLeft)![0],
                    (int)componentStats.Get(i, Imgproc.CcStatTop)![0],
                    (int)componentStats.Get(i, Imgproc.CcStatWidth)![0],
                    (int)componentStats.Get(i, Imgproc.CcStatHeight)![0],
                    (int)componentStats.Get(i, Imgproc.CcStatArea)![0],
                    componentCentres.Get(i, 0)![0], componentCentres.Get(i, 1)![0]);
            }
            Point[] calibratedPocketCentres =
            [
                new(0, 0), new(width, 0), new(0, width), new(width, width),
                new(0, width * 2), new(width, width * 2)
            ];
            return new PreparedFrame(width, sourceWidth, sourceHeight, candidateAuditGeometryWidth,
                nearbyPeakCount, globalPeakCount, croppedQualifiedSeedCount,
                windowLeft, windowRight, windowTop, windowBottom, windowPixelCount,
                rawDarkPixelsInWindow, openedDarkPixelsInWindow, scale, ballRadius, minimumPeakRadius,
                minimumPeakRadiusFactor, auditScale, pocketExclusionRadiusAtWidth1000 * scale,
                left, right, top, bottom, hasPackedWindow, searchWindowSource, selectedCentres,
                calibratedPocketCentres, localMaxima, qualifiedSeeds, productionSeeds.ToArray(), darkComponents,
                localProductionSeedPixels, bgrBytes, hsvBytes, thresholdMaskBytes, openedMaskBytes, distanceValues);
        }
        finally { foreach (var point in original.Concat(portrait)) point.Dispose(); }
    }

    public static object Capture(byte[] encodedImage, TableCalibration calibration, int width, VisionResult vision,
        string outputDirectory, string frameId, int manualImageX = ManualImageX, int manualImageY = ManualImageY,
        double minimumPeakRadiusFactor = 8,
        double pocketExclusionRadiusAtWidth1000 = VisionSettings.DefaultPocketExclusionRadius,
        VisionAnalyzer? classifierAnalyzer = null, byte[]? classifierPixels = null,
        double targetFitAcceptanceRadiusInBallRadii = 0,
        string referencePointSource = "reviewed-label", PreparedFrame? preparedFrame = null)
    {
        if (referencePointSource is not "reviewed-label" and not "unreviewed-model-proposal" and not "unreviewed-negative-control")
            throw new ArgumentOutOfRangeException(nameof(referencePointSource), "Reference point source must be reviewed-label, unreviewed-model-proposal, or unreviewed-negative-control.");
        if ((classifierAnalyzer is null) != (classifierPixels is null))
            throw new ArgumentException("The classifier analyzer and its captured pixel buffer must be supplied together.");
        if (classifierAnalyzer is not null && width != 1000)
            throw new ArgumentOutOfRangeException(nameof(width), "Exact pocket-arc classifier evidence is supported only at width 1000.");
        if (!double.IsFinite(targetFitAcceptanceRadiusInBallRadii)
            || targetFitAcceptanceRadiusInBallRadii < 0 || targetFitAcceptanceRadiusInBallRadii > 5)
            throw new ArgumentOutOfRangeException(nameof(targetFitAcceptanceRadiusInBallRadii),
                "Targeted pocket-arc fit acceptance radius must be between 0 and 5 ball radii.");
        var prepared = preparedFrame ?? Prepare(encodedImage, calibration, width, vision,
            minimumPeakRadiusFactor, pocketExclusionRadiusAtWidth1000);
        if (prepared.Width != width)
            throw new ArgumentException("Prepared trace frame width does not match the requested capture width.", nameof(preparedFrame));
        if (manualImageX < 0 || manualImageX >= prepared.SourceWidth || manualImageY < 0 || manualImageY >= prepared.SourceHeight)
            throw new ArgumentOutOfRangeException(nameof(manualImageX),
                $"Manual source label ({manualImageX},{manualImageY}) is outside the {prepared.SourceWidth}x{prepared.SourceHeight} replay image.");

        var labelTablePoint = calibration.Map(new Point(manualImageX / (double)prepared.SourceWidth, manualImageY / (double)prepared.SourceHeight));
        var label = new AnalysisCoordinates(width).FromTable(labelTablePoint);
        double scale = prepared.Scale, ballRadius = prepared.BallRadius, minimumPeakRadius = prepared.MinimumPeakRadius;
        int candidateAuditGeometryWidth = prepared.CandidateAuditGeometryWidth;
        double auditScale = prepared.AuditScale;
        var selectedCentres = prepared.SelectedCentres;
        bool hasPackedWindow = prepared.HasPackedWindow;
        double left = prepared.Left, right = prepared.Right, top = prepared.Top, bottom = prepared.Bottom;
        string searchWindowSource = prepared.SearchWindowSource;
        var nearbyPeaks = prepared.LocalMaxima.Select(peak => peak.At(label))
            .Where(peak => peak.DistanceFromLabel <= 3 * ballRadius).ToList();
        int nearbyPeakCount = prepared.NearbyPeakComponentCount;
        int globalPeakCount = prepared.GlobalPeakComponentCount;
        var allQualifiedSeeds = prepared.QualifiedSeeds.Select(seed => seed.At(label))
            .Where(seed => seed.DistanceFromLabel <= 3 * ballRadius).ToList();
        var productionSeeds = prepared.ProductionSeeds.Select(seed => seed.At(label)).ToList();
        int croppedQualifiedSeedCount = prepared.CroppedQualifiedSeedCount;
        var localProductionSeedPixels = prepared.LocalProductionSeedPixels;
        byte[] hsvBytes = prepared.HsvBytes;
        byte[] beforeBytes = prepared.ThresholdMaskBytes;
        byte[] openedBytes = prepared.OpenedMaskBytes;
            int cx = (int)Math.Round(label.X), cy = (int)Math.Round(label.Y), regionRadius = (int)Math.Ceiling(1.5 * ballRadius);
            int rawDarkPixelsInExpectedBall = 0, openedDarkPixelsInExpectedBall = 0, expectedBallPixelCount = 0;
            double maxDistanceWithinExpectedBall = 0, maxDistanceWithin1_5BallRadii = 0;
            var valuesInExpectedBall = new List<int>();
            for (int y = Math.Max(0, cy - regionRadius); y <= Math.Min(width * 2 - 1, cy + regionRadius); y++)
            for (int x = Math.Max(0, cx - regionRadius); x <= Math.Min(width - 1, cx + regionRadius); x++)
            {
                double dx = x - label.X, dy = y - label.Y, radial = Math.Sqrt(dx * dx + dy * dy);
                if (radial <= ballRadius)
                {
                    int pixel = y * width + x, offset = pixel * 3;
                    valuesInExpectedBall.Add(hsvBytes[offset + 2]);
                    rawDarkPixelsInExpectedBall += beforeBytes[pixel] == 0 ? 0 : 1;
                    openedDarkPixelsInExpectedBall += openedBytes[pixel] == 0 ? 0 : 1;
                    expectedBallPixelCount++;
                }
                double distanceValue = prepared.DistanceValues[y * width + x];
                maxDistanceWithin1_5BallRadii = Math.Max(maxDistanceWithin1_5BallRadii, distanceValue);
                if (radial <= ballRadius) maxDistanceWithinExpectedBall = Math.Max(maxDistanceWithinExpectedBall, distanceValue);
            }
            valuesInExpectedBall.Sort();

            int windowLeft = prepared.WindowLeft, windowRight = prepared.WindowRight;
            int windowTop = prepared.WindowTop, windowBottom = prepared.WindowBottom;
            int windowPixelCount = prepared.WindowPixelCount;
            int rawDarkPixelsInWindow = prepared.RawDarkPixelsInWindow;
            int openedDarkPixelsInWindow = prepared.OpenedDarkPixelsInWindow;
            var nearbyComponents = new List<object>();
            foreach (var component in prepared.DarkComponents)
            {
                int x = component.X, y = component.Y, w = component.Width, h = component.Height;
                double componentX = component.CentreX, componentY = component.CentreY;
                if (Math.Abs(componentX - label.X) <= 3 * ballRadius && Math.Abs(componentY - label.Y) <= 3 * ballRadius)
                    nearbyComponents.Add(new { x, y, width = w, height = h, area = component.Area, centreX = componentX, centreY = componentY });
            }

            Directory.CreateDirectory(outputDirectory);
            byte[] bgrBytes = prepared.BgrBytes;
            double pocketOcclusionRadius = prepared.PocketOcclusionRadius;
            Point[] calibratedPocketCentres = prepared.CalibratedPocketCentres;
            // Diagnostic-only reference-point probe: give the boundary refiner
            // the supplied reference point once, to distinguish a missing
            // dark-mask seed from a boundary that cannot be fitted. This point
            // is never passed into VisionAnalyzer or a table-state decision.
            var manualCentreProbe = BallBoundaryRefiner.Refine(bgrBytes, width, width * 2, label,
                ballRadius, minimumSupportedFraction: .5, collectSectorEvidence: true, expectedKind: "black");
            var fixedCentreSectors = BallBoundaryRefiner.Inspect(bgrBytes, width, width * 2,
                label, ballRadius, label, ballRadius, expectedKind: "black",
                allowDarkBodyEdgeTransitions: true, darkBodyValueCeiling: DarkValueCeiling);
            var normalFixedCentreSectors = BallBoundaryRefiner.Inspect(bgrBytes, width, width * 2,
                label, ballRadius, label, ballRadius, expectedKind: "black");
            // A reference point can lie too close to a frame edge or have too
            // little usable exterior context, in which case Inspect returns no
            // sectors. Preserve that as an unavailable measurement instead of
            // aborting the entire multi-anchor replay.
            var pocketOccludedAtLabel = normalFixedCentreSectors.Length == 32
                ? BallBoundaryRefiner.GetPocketOccludedSectorIndexes(
                    normalFixedCentreSectors, calibratedPocketCentres, pocketOcclusionRadius).ToHashSet()
                : new HashSet<int>();
            var visibleAtLabel = normalFixedCentreSectors.Where(sector => !pocketOccludedAtLabel.Contains(sector.Index)).ToArray();
            var supportedVisibleAtLabel = visibleAtLabel.Where(sector => sector.Supported).ToArray();
            var fixedCentreEvidence = new
            {
                evaluationOnly = true,
                centre = label,
                radius = ballRadius,
                measuredSectorCount = fixedCentreSectors.Length,
                normalMeasuredSectorCount = normalFixedCentreSectors.Length,
                pocketOcclusionMeasurementAvailable = normalFixedCentreSectors.Length == 32,
                normalSupportedSectorIndices = fixedCentreSectors.Where(sector => sector.Supported
                    && !sector.DarkBodyTransitionUsed).Select(sector => sector.Index).ToArray(),
                darkTransitionSectorIndices = fixedCentreSectors.Where(sector => sector.DarkBodyTransitionUsed)
                    .Select(sector => sector.Index).ToArray(),
                combinedSupportedSectorIndices = fixedCentreSectors.Where(sector => sector.Supported)
                    .Select(sector => sector.Index).ToArray(),
                combinedSupportedSectorCount = fixedCentreSectors.Count(sector => sector.Supported)
            };
            var fixedCentreThresholdSweep = new[] { 30, 35, 40, 45, 50, 55 }.Select(ceiling =>
            {
                var sectors = BallBoundaryRefiner.Inspect(bgrBytes, width, width * 2,
                    label, ballRadius, label, ballRadius, expectedKind: "black",
                    allowDarkBodyEdgeTransitions: true, darkBodyValueCeiling: ceiling);
                return new
                {
                    darkBodyValueCeiling = ceiling,
                    normalSupportedSectorCount = sectors.Count(sector => sector.Supported && !sector.DarkBodyTransitionUsed),
                    darkTransitionSectorCount = sectors.Count(sector => sector.DarkBodyTransitionUsed),
                    combinedSupportedSectorCount = sectors.Count(sector => sector.Supported),
                    combinedSupportedSectorIndices = sectors.Where(sector => sector.Supported).Select(sector => sector.Index).ToArray()
                };
            }).ToArray();
            var radialOffsetComparison = width == 1000
                ? BuildRadialOffsetComparison()
                : null;

            object BuildRadialOffsetComparison()
            {
                Point FromSource(Point sourcePixel)
                {
                    var tablePoint = calibration.Map(new(sourcePixel.X / prepared.SourceWidth,
                        sourcePixel.Y / prepared.SourceHeight));
                    return new AnalysisCoordinates(width).FromTable(tablePoint);
                }

                object Compare(string name, Point proposal, Point? sourcePixel = null)
                {
                    var squareFit = BallBoundaryRefiner.Refine(bgrBytes, width, width * 2,
                        proposal, ballRadius, RadialBoundaryProbeMinimumSupport,
                        maximumCentreOffsetFraction: RadialBoundaryProbeExtentFraction);
                    var radialFit = BallBoundaryRefiner.RefineWithRadialCentreOffsetForDiagnostic(
                        bgrBytes, width, width * 2, proposal, ballRadius,
                        RadialBoundaryProbeMinimumSupport,
                        maximumCentreOffsetFraction: RadialBoundaryProbeExtentFraction);
                    object? Classify(BallBoundary? fit)
                    {
                        if (fit is null || classifierAnalyzer is null || classifierPixels is null) return null;
                        var evidence = classifierAnalyzer.ClassifyDiagnosticCandidate(fit.Centre,
                            fit.Radius, classifierPixels);
                        return new
                        {
                            kind = evidence.Classification.Kind,
                            pattern = evidence.Classification.Pattern,
                            quality = evidence.Classification.Quality,
                            bodyDarkFraction = evidence.Classification.BodyDarkFraction,
                            rimDarkFraction = evidence.Classification.RimDarkFraction,
                            bodySampleCount = evidence.BodySampleCount,
                            usableBodySampleCount = evidence.UsableBodySampleCount,
                            rimSampleCount = evidence.RimSampleCount,
                            usableRimSampleCount = evidence.UsableRimSampleCount
                        };
                    }
                    object? Fit(BallBoundary? fit) => fit is null ? null : new
                    {
                        centre = fit.Centre,
                        radius = fit.Radius,
                        proposalOffsetPx = (fit.Centre - proposal).Length,
                        proposalOffsetBallRadii = (fit.Centre - proposal).Length / ballRadius,
                        supportedSectors = (int)Math.Round(fit.SupportedSectors * 32),
                        supportFraction = fit.SupportedSectors,
                        meetsSparseBlackSupportGate = fit.SupportedSectors >= RadialBoundaryProbeMinimumSupport,
                        score = fit.Score,
                        classification = Classify(fit)
                    };
                    return new
                    {
                        name,
                        proposalSourcePixel = sourcePixel,
                        proposalAnalysisPixel = proposal,
                        expectedRadius = ballRadius,
                        maximumCentreOffsetFraction = RadialBoundaryProbeExtentFraction,
                        maximumRadialOffsetPx = ballRadius * RadialBoundaryProbeExtentFraction,
                        minimumSupportFraction = RadialBoundaryProbeMinimumSupport,
                        squareExtentFit = Fit(squareFit),
                        radialBoundedFit = Fit(radialFit)
                    };
                }

                double scale = width / 1000d;
                var cases = new List<object>
                {
                    Compare("positive-black-model-proposal", label,
                        new Point(manualImageX, manualImageY)),
                    Compare("positive-frame-known-rail-shadow-seed",
                        new Point(587.5 * scale, 56 * scale))
                };
                if (long.TryParse(frameId, out long parsedFrameId) && parsedFrameId == 1365)
                {
                    var interiorYellowSource = new Point(816.3, 533.7);
                    cases.Add(Compare("positive-frame-interior-yellow-proposal",
                        FromSource(interiorYellowSource), interiorYellowSource));
                }
                return new
                {
                    diagnosticOnly = true,
                    productionOutputChanged = false,
                    feedsDetectorCandidates = false,
                    feedsVisionResult = false,
                    feedsGameState = false,
                    rasterSource = "captured width-1000 BGR and HSV bytes from the exact replay analyzer invocation",
                    candidateSources = new
                    {
                        positiveBlack = "source pixel (99,897) from hash-matched frame-1365 local-model proposal; frame-978 run reuses the same source pixel and maps it with that frame's own receipt calibration",
                        railShadow = "saved frame-1365 sparse-black rank-0 seed (587.5,56) at analysis width 1000",
                        interiorPositive = "source pixel (816.3,533.7), the interior yellow proposal in the hash-matched frame-1365 local-model output"
                    },
                    fitterComparison = new
                    {
                        squareExtent = "unchanged production Refine search and fine-search behavior",
                        radialBound = "same fitter/grid/score; candidate centres beyond Euclidean 1.3R are skipped",
                        extentFraction = RadialBoundaryProbeExtentFraction,
                        minimumSupportFraction = RadialBoundaryProbeMinimumSupport
                    },
                    cases
                };
            }
            static object? FitSummary(BallBoundary? fit, Point reference) => fit is null ? null : new
            {
                centre = fit.Centre,
                radius = fit.Radius,
                distanceFromLabel = (fit.Centre - reference).Length,
                supportFraction = fit.SupportedSectors,
                supportedSectorCount = fit.SectorEvidence.Count(sector => sector.Supported),
                supportedSectorIndices = fit.SectorEvidence.Where(sector => sector.Supported).Select(sector => sector.Index).ToArray(),
                score = fit.Score
            };
            var nearestMarginSeed = nearbyPeaks.Where(peak => peak.PassesMargin && peak.PassesMinimumRadius)
                .OrderBy(peak => peak.DistanceFromLabel).FirstOrDefault();
            bool hasNearestMarginSeed = nearestMarginSeed.PassesMargin && nearestMarginSeed.PassesMinimumRadius;
            Point nearestSeedCentre = new(nearestMarginSeed.X, nearestMarginSeed.Y);
            Point pocketArcProposal = hasNearestMarginSeed ? nearestSeedCentre : label;
            var pocketArcProbe = BallBoundaryRefiner.ProbePocketAwarePartialArc(bgrBytes,
                width, width * 2, pocketArcProposal, ballRadius, calibratedPocketCentres,
                pocketOcclusionRadius, expectedKind: "black", maximumCentreOffsetFraction: .8);
            object? candidateColourClassifier = null;
            string? classifierUnavailableReason = null;
            if (pocketArcProbe.Candidate is { } classifierFit
                && classifierAnalyzer is not null && classifierPixels is not null)
            {
                var evidence = classifierAnalyzer.ClassifyDiagnosticCandidate(classifierFit.Centre,
                    classifierFit.Radius, classifierPixels);
                candidateColourClassifier = new
                {
                    evaluationOnly = true,
                    diagnosticOnly = true,
                    productionOutputChanged = false,
                    feedsDetectorCandidates = false,
                    feedsVisionResult = false,
                    feedsGameState = false,
                    source = "exact HSV pixels captured from the same width-1000 baseline analyzer invocation",
                    classifier = "existing VisionAnalyzer.Classify at the pocket-aware probe candidate",
                    kind = evidence.Classification.Kind,
                    pattern = evidence.Classification.Pattern,
                    quality = evidence.Classification.Quality,
                    qualityIsProbability = false,
                    sampleCoverage = new
                    {
                        bodySampleCount = evidence.BodySampleCount,
                        usableBodySampleCount = evidence.UsableBodySampleCount,
                        minimumUsableBodySamplesForClassification = 8,
                        bodySamplesBelowMinimum = evidence.UsableBodySampleCount < 8,
                        rimSampleCount = evidence.RimSampleCount,
                        usableRimSampleCount = evidence.UsableRimSampleCount,
                        minimumUsableRimSamplesForBlackRecovery = 8,
                        rimSamplesBelowMinimum = evidence.UsableRimSampleCount < 8,
                        bodyDarkFraction = evidence.Classification.BodyDarkFraction,
                        rimDarkFraction = evidence.Classification.RimDarkFraction
                    },
                    clusters = evidence.Classification.Clusters
                };
            }
            else
                classifierUnavailableReason = pocketArcProbe.Candidate is null
                    ? "The pocket-aware partial-arc probe returned no candidate."
                    : "The exact width-1000 analyzer pixels were not supplied for this trace.";

            // Evaluate the pocket-aware arc fit from every already-ranked
            // sparse-black candidate audit. This is intentionally separate
            // from the nearest-seed/manual-label probes above and is capped at
            // the analyzer's maximum candidate budget. None of these fits are
            // fed back into production detection or canonical table state.
            var rankedSparseBlackCandidateAudits = vision.CandidateAudits
                .Where(audit => audit.Pass == "initial" && audit.Stage == "sparse-black-add")
                .OrderBy(audit => audit.Index)
                .Take(MaximumRankedSparseBlackPocketArcProbes)
                .ToArray();
            double targetMaximumSeedToFitOffsetFraction = BallBoundaryRefiner.MaximumPocketArcCandidateOffsetFraction;
            double targetMaximumSeedDistanceFromLabelRadii = targetFitAcceptanceRadiusInBallRadii > 0
                ? targetFitAcceptanceRadiusInBallRadii + targetMaximumSeedToFitOffsetFraction : 0;
            var rankedSparseBlackPocketArcProbes = rankedSparseBlackCandidateAudits.Select(audit =>
            {
                Point seedCentre = audit.ProposalCentre * auditScale;
                double seedRadius = audit.ProposalRadius * auditScale;
                double seedDistanceToLabel = (seedCentre - label).Length;
                bool skippedOutsideTargetReach = targetFitAcceptanceRadiusInBallRadii > 0
                    && seedDistanceToLabel > ballRadius * targetMaximumSeedDistanceFromLabelRadii;
                var probe = skippedOutsideTargetReach ? null : BallBoundaryRefiner.ProbePocketAwarePartialArc(bgrBytes,
                    width, width * 2, seedCentre, ballRadius, calibratedPocketCentres,
                    pocketOcclusionRadius, expectedKind: "black",
                    maximumCentreOffsetFraction: targetMaximumSeedToFitOffsetFraction);

                object? classifierEvidence = null;
                string? classifierUnavailableReason = skippedOutsideTargetReach
                    ? "Skipped: this seed cannot reach the configured label-fit acceptance radius under the probe's maximum seed-to-fit offset."
                    : null;
                if (probe?.Candidate is { } fit
                    && classifierAnalyzer is not null && classifierPixels is not null)
                {
                    var evidence = classifierAnalyzer.ClassifyDiagnosticCandidate(fit.Centre,
                        fit.Radius, classifierPixels);
                    classifierEvidence = new
                    {
                        kind = evidence.Classification.Kind,
                        pattern = evidence.Classification.Pattern,
                        quality = evidence.Classification.Quality,
                        qualityIsProbability = false,
                        bodySampleCount = evidence.BodySampleCount,
                        usableBodySampleCount = evidence.UsableBodySampleCount,
                        rimSampleCount = evidence.RimSampleCount,
                        usableRimSampleCount = evidence.UsableRimSampleCount,
                        bodyDarkFraction = evidence.Classification.BodyDarkFraction,
                        rimDarkFraction = evidence.Classification.RimDarkFraction
                    };
                }
                else if (!skippedOutsideTargetReach)
                    classifierUnavailableReason = probe!.Candidate is null
                        ? "The candidate-seeded pocket-aware partial-arc probe returned no fit."
                        : "The exact width-1000 analyzer pixels were not supplied for this trace.";

                var arcFit = probe?.Candidate;
                return new
                {
                    evaluationOnly = true,
                    diagnosticOnly = true,
                    productionOutputChanged = false,
                    feedsDetectorCandidates = false,
                    feedsVisionResult = false,
                    feedsGameState = false,
                    candidateRank = audit.Index,
                    candidateRankIsZeroBased = true,
                    pass = audit.Pass,
                    stage = audit.Stage,
                    decision = audit.Decision,
                    classifiedKindFromProductionAudit = audit.ClassifiedKind,
                    seedCentre,
                    seedRadius,
                    distanceFromLabel = seedDistanceToLabel,
                    probeExecuted = !skippedOutsideTargetReach,
                    probeSkipReason = skippedOutsideTargetReach ? "seed-outside-target-fit-reach" : null,
                    targetFitAcceptanceRadiusInBallRadii = targetFitAcceptanceRadiusInBallRadii > 0
                        ? targetFitAcceptanceRadiusInBallRadii : (double?)null,
                    maximumSeedToFitOffsetFraction = targetFitAcceptanceRadiusInBallRadii > 0
                        ? targetMaximumSeedToFitOffsetFraction : (double?)null,
                    maximumSeedDistanceFromLabelRadii = targetFitAcceptanceRadiusInBallRadii > 0
                        ? targetMaximumSeedDistanceFromLabelRadii : (double?)null,
                    candidateAuditCentreBeforeGeometryScale = audit.ProposalCentre,
                    candidateAuditRadiusBeforeGeometryScale = audit.ProposalRadius,
                    candidateAuditGeometryWidth,
                    arcFitAvailable = probe is null ? (bool?)null : arcFit is not null,
                    arcFitCentre = arcFit?.Centre,
                    arcFitRadius = arcFit?.Radius,
                    arcFitDistanceFromLabel = arcFit is null ? (double?)null : (arcFit.Centre - label).Length,
                    candidateToSeedOffset = probe?.CandidateToProposalOffset,
                    candidateToSeedOffsetFraction = probe?.CandidateToProposalOffsetFraction,
                    maximumCandidateToSeedOffset = probe?.MaximumCandidateToProposalOffset,
                    maximumCandidateToSeedOffsetFraction = probe?.MaximumCandidateToProposalOffsetFraction,
                    visibleSectorCount = probe?.VisibleSectorCount,
                    visibleSectorIndices = probe?.VisibleSectorIndices,
                    supportedVisibleSectorCount = probe?.SupportedVisibleSectorCount,
                    supportedVisibleSectorIndices = probe?.SupportedVisibleSectorIndices,
                    unexplainedVisibleSectorCount = probe is null ? (int?)null : probe.UnexplainedVisibleSectorIndices.Length,
                    unexplainedVisibleSectorIndices = probe?.UnexplainedVisibleSectorIndices,
                    globalSupportedSectorCount = arcFit?.SectorEvidence.Count(sector => sector.Supported),
                    globalSupportFraction = arcFit?.SupportedSectors,
                    longestContiguousSupportedRun = probe?.LongestContiguousSupportedRun,
                    supportedArcSpanDegrees = probe?.SupportedArcSpanDegrees,
                    minimumVisibleSectors = probe?.MinimumVisibleSectors,
                    minimumSupportedVisibleSectors = probe?.MinimumSupportedVisibleSectors,
                    minimumContiguousSupportedRun = probe?.MinimumContiguousSupportedRun,
                    minimumSupportedArcSpanDegrees = probe?.MinimumSupportedArcSpanDegrees,
                    diagnosticArcChecksPass = probe is null ? (bool?)null : probe.DiagnosticArcChecksPass,
                    score = arcFit?.Score,
                    classifier = new
                    {
                        available = classifierEvidence is not null,
                        unavailableReason = classifierUnavailableReason,
                        evidence = classifierEvidence
                    }
                };
            }).ToArray();
            var pocketAwarePartialArcExperiment = new
            {
                evaluationOnly = true,
                diagnosticOnly = true,
                productionOutputChanged = false,
                referencePointSource,
                referencePointIsReviewedLabel = referencePointSource == "reviewed-label",
                matchedAbsentPocketControlAvailable = false,
                controlNote = "This trace contains no matched ball-absent pocket capture; compare only with an explicitly recorded control replay.",
                geometry = new
                {
                    source = "canonical calibrated table coordinates mapped into warped analysis pixels",
                    pocketCentres = calibratedPocketCentres,
                    pocketOcclusionRadius,
                    rule = "A sector is excluded only when its measured inner-to-outer radial sample segment intersects a pocket occlusion disk."
                },
                diagnosticChecks = new
                {
                    minimumVisibleSectors = pocketArcProbe.MinimumVisibleSectors,
                    minimumSupportedVisibleSectors = pocketArcProbe.MinimumSupportedVisibleSectors,
                    minimumContiguousSupportedRun = pocketArcProbe.MinimumContiguousSupportedRun,
                    minimumSupportedArcSpanDegrees = pocketArcProbe.MinimumSupportedArcSpanDegrees,
                    maximumCandidateToProposalOffset = pocketArcProbe.MaximumCandidateToProposalOffset,
                    maximumCandidateToProposalOffsetFraction = pocketArcProbe.MaximumCandidateToProposalOffsetFraction
                },
                referenceCentre = new
                {
                    centre = label,
                    pointSource = referencePointSource,
                    isReviewedLabel = referencePointSource == "reviewed-label",
                    normalSupportedSectorIndices = normalFixedCentreSectors.Where(sector => sector.Supported)
                        .Select(sector => sector.Index).ToArray(),
                    pocketOccludedSectorIndices = pocketOccludedAtLabel.Order().ToArray(),
                    visibleSectorIndices = visibleAtLabel.Select(sector => sector.Index).ToArray(),
                    visibleSectorCount = visibleAtLabel.Length,
                    supportedVisibleSectorIndices = supportedVisibleAtLabel.Select(sector => sector.Index).ToArray(),
                    supportedVisibleSectorCount = supportedVisibleAtLabel.Length,
                    unsupportedVisibleSectorIndices = visibleAtLabel.Where(sector => !sector.Supported)
                        .Select(sector => sector.Index).ToArray()
                },
                proposalSource = hasNearestMarginSeed ? "nearest qualifying dark-mask seed" : "reference-point fallback",
                proposalCentre = pocketArcProposal,
                candidate = pocketArcProbe.Candidate is { } pocketArcFit ? new
                {
                    centre = pocketArcFit.Centre,
                    radius = pocketArcFit.Radius,
                    distanceFromLabel = (pocketArcFit.Centre - label).Length,
                    candidateToProposalOffset = pocketArcProbe.CandidateToProposalOffset,
                    candidateToProposalOffsetFraction = pocketArcProbe.CandidateToProposalOffsetFraction,
                    maximumCandidateToProposalOffset = pocketArcProbe.MaximumCandidateToProposalOffset,
                    maximumCandidateToProposalOffsetFraction = pocketArcProbe.MaximumCandidateToProposalOffsetFraction,
                    globalSupportedSectorCount = pocketArcFit.SectorEvidence.Count(sector => sector.Supported),
                    globalSupportFraction = pocketArcFit.SupportedSectors,
                    pocketOccludedSectorIndices = pocketArcProbe.PocketOccludedSectorIndices,
                    visibleSectorCount = pocketArcProbe.VisibleSectorCount,
                    supportedVisibleSectorIndices = pocketArcProbe.SupportedVisibleSectorIndices,
                    supportedVisibleSectorCount = pocketArcProbe.SupportedVisibleSectorCount,
                    unexplainedVisibleSectorIndices = pocketArcProbe.UnexplainedVisibleSectorIndices,
                    longestContiguousSupportedRun = pocketArcProbe.LongestContiguousSupportedRun,
                    supportedArcSpanDegrees = pocketArcProbe.SupportedArcSpanDegrees,
                    minimumVisibleSectors = pocketArcProbe.MinimumVisibleSectors,
                    minimumSupportedVisibleSectors = pocketArcProbe.MinimumSupportedVisibleSectors,
                    minimumContiguousSupportedRun = pocketArcProbe.MinimumContiguousSupportedRun,
                    minimumSupportedArcSpanDegrees = pocketArcProbe.MinimumSupportedArcSpanDegrees,
                    diagnosticArcChecksPass = pocketArcProbe.DiagnosticArcChecksPass,
                    score = pocketArcFit.Score
                } : null,
                candidateColourClassifier = new
                {
                    available = candidateColourClassifier is not null,
                    unavailableReason = classifierUnavailableReason,
                    evidence = candidateColourClassifier
                }
            };
            var nearestSeedProbe08 = hasNearestMarginSeed
                ? BallBoundaryRefiner.Refine(bgrBytes, width, width * 2, nearestSeedCentre, ballRadius,
                    minimumSupportedFraction: .5, collectSectorEvidence: true, expectedKind: "black",
                    maximumCentreOffsetFraction: .8)
                : null;
            var nearestSeedProbe13 = hasNearestMarginSeed
                ? BallBoundaryRefiner.Refine(bgrBytes, width, width * 2, nearestSeedCentre, ballRadius,
                    minimumSupportedFraction: .5, collectSectorEvidence: true, expectedKind: "black",
                    maximumCentreOffsetFraction: 1.3)
                : null;
            var productionLikeSeedProbe04 = hasNearestMarginSeed
                ? BallBoundaryRefiner.Refine(bgrBytes, width, width * 2, nearestSeedCentre, ballRadius,
                    minimumSupportedFraction: ObservationPolicy.MinimumSupportedBoundaryFraction,
                    collectSectorEvidence: true, maximumCentreOffsetFraction: .4)
                : null;
            var productionLikeSeedProbe13 = hasNearestMarginSeed
                ? BallBoundaryRefiner.Refine(bgrBytes, width, width * 2, nearestSeedCentre, ballRadius,
                    minimumSupportedFraction: ObservationPolicy.MinimumSupportedBoundaryFraction,
                    collectSectorEvidence: true, maximumCentreOffsetFraction: 1.3)
                : null;
            var manualCentreProbeReport = new
            {
                evaluationOnly = true,
                referencePointSource,
                referencePointIsReviewedLabel = referencePointSource == "reviewed-label",
                expectedRadius = ballRadius,
                probeSupportFloor = .5,
                fixedCentreEvidence,
                fixedCentreThresholdSweep,
                pocketAwarePartialArcExperiment,
                selectedFit = FitSummary(manualCentreProbe, label),
                exactProposalFit = FitSummary(manualCentreProbe?.ProposalFit, label),
                nearestDarkSeed = hasNearestMarginSeed ? new
                {
                    centre = nearestSeedCentre,
                    radius = nearestMarginSeed.Radius,
                    distanceFromLabel = nearestMarginSeed.DistanceFromLabel,
                    source = "nearest threshold-mask local maximum inside the production image margin"
                } : null,
                nearestSeedFit08 = FitSummary(nearestSeedProbe08, label),
                nearestSeedFit13 = FitSummary(nearestSeedProbe13, label),
                productionLikeNearestSeedFit04 = FitSummary(productionLikeSeedProbe04, label),
                productionLikeNearestSeedFit13 = FitSummary(productionLikeSeedProbe13, label)
            };
            byte[] overlayBgrBytes = (byte[])bgrBytes.Clone();
            for (int i = 0; i < openedBytes.Length; i++)
                if (openedBytes[i] != 0)
                {
                    int offset = i * 3;
                    overlayBgrBytes[offset] = (byte)((overlayBgrBytes[offset] + 255) / 2);
                    overlayBgrBytes[offset + 1] /= 2;
                    overlayBgrBytes[offset + 2] = (byte)((overlayBgrBytes[offset + 2] + 255) / 2);
                }
            using var overlay = new Mat(width * 2, width, CvType.Cv8uc3); overlay.Put(0, 0, overlayBgrBytes);
            using var windowInk = new Scalar(255, 180, 0); using var labelInk = new Scalar(0, 255, 255);
            using var radiusInk = new Scalar(255, 255, 0); using var acceptedInk = new Scalar(0, 255, 0); using var rejectedInk = new Scalar(0, 0, 255);
            using var windowTopLeft = new CvPoint((int)Math.Round(left), (int)Math.Round(top));
            using var windowBottomRight = new CvPoint((int)Math.Round(right), (int)Math.Round(bottom));
            Imgproc.Rectangle(overlay, windowTopLeft, windowBottomRight, windowInk, 2);
            using var mark = new CvPoint(cx, cy); Imgproc.Circle(overlay, mark, (int)Math.Round(ballRadius), radiusInk, 2);
            Imgproc.Circle(overlay, mark, 7, labelInk, 2);
            using var markLeft = new CvPoint(cx - 10, cy); using var markRight = new CvPoint(cx + 10, cy);
            using var markTop = new CvPoint(cx, cy - 10); using var markBottom = new CvPoint(cx, cy + 10);
            Imgproc.Line(overlay, markLeft, markRight, labelInk, 2); Imgproc.Line(overlay, markTop, markBottom, labelInk, 2);
            foreach (var peak in nearbyPeaks.Concat(allQualifiedSeeds).DistinctBy(p => (p.ComponentIndex, p.X, p.Y)))
            {
                using var point = new CvPoint((int)Math.Round(peak.X), (int)Math.Round(peak.Y));
                bool admittedByCroppedCap = localProductionSeedPixels.Contains(((int)Math.Round(peak.X), (int)Math.Round(peak.Y)));
                var ink = peak.PassesMinimumRadius && peak.PassesMargin && peak.InSearchWindow
                    ? admittedByCroppedCap ? acceptedInk : windowInk
                    : rejectedInk;
                Imgproc.Circle(overlay, point, 4, ink, -1);
            }
            string maskOverlayName = $"{frameId}-{width}-dark-mask-overlay.png";
            SavePng(Path.Combine(outputDirectory, maskOverlayName), overlay);

            int cropRadius = (int)Math.Ceiling(3 * ballRadius), cropLeft = Math.Max(0, cx - cropRadius), cropTop = Math.Max(0, cy - cropRadius);
            int cropRight = Math.Min(width, cx + cropRadius + 1), cropBottom = Math.Min(width * 2, cy + cropRadius + 1);
            using var distanceOverlay = new Mat(cropBottom - cropTop, cropRight - cropLeft, CvType.Cv8uc3);
            byte[] distancePixels = new byte[(cropBottom - cropTop) * (cropRight - cropLeft) * 3];
            int distanceIndex = 0;
            for (int y = cropTop; y < cropBottom; y++)
            for (int x = cropLeft; x < cropRight; x++)
            {
                byte intensity = (byte)Math.Clamp((int)Math.Round(prepared.DistanceValues[y * width + x] * 255 / (2 * ballRadius)), 0, 255);
                distancePixels[distanceIndex++] = intensity; distancePixels[distanceIndex++] = intensity; distancePixels[distanceIndex++] = intensity;
            }
            distanceOverlay.Put(0, 0, distancePixels);
            using var cropMark = new CvPoint(cx - cropLeft, cy - cropTop); using var cropRadiusInk = new Scalar(255, 255, 0); using var cropLabelInk = new Scalar(0, 255, 255);
            Imgproc.Circle(distanceOverlay, cropMark, (int)Math.Round(ballRadius), cropRadiusInk, 1);
            Imgproc.Circle(distanceOverlay, cropMark, 4, cropLabelInk, 2);
            string distanceOverlayName = $"{frameId}-{width}-distance-window.png";
            SavePng(Path.Combine(outputDirectory, distanceOverlayName), distanceOverlay);

            string blackSeedStage = hasPackedWindow ? "packed-black" : "sparse-black-add";
            var auditedBlackSeeds = vision.CandidateAudits.Where(a => a.Pass == "initial" && a.Stage == blackSeedStage)
                .OrderBy(a => a.Index).ToArray();
            int auditedProductionSeeds = auditedBlackSeeds.Length;
            string acceptedBlackDecision = hasPackedWindow ? "added-black-body" : "added-strong-isolated-black";
            var acceptedBlackSeed = auditedBlackSeeds.FirstOrDefault(a => a.Decision == acceptedBlackDecision);
            int expectedAttemptedSeedCount = hasPackedWindow
                ? acceptedBlackSeed is null ? productionSeeds.Count : acceptedBlackSeed.Index + 1
                : auditedBlackSeeds.Length;
            bool seedCountComparable = hasPackedWindow && candidateAuditGeometryWidth == width;
            return new
            {
                frameId, analysisWidth = width, candidateAuditGeometryWidth,
                referencePointSource,
                referencePointIsReviewedLabel = referencePointSource == "reviewed-label",
                manualBlackLabel = new { sourcePixel = new { x = manualImageX, y = manualImageY }, analysisPixel = label,
                    pointSource = referencePointSource,
                    isReviewedLabel = referencePointSource == "reviewed-label",
                    note = referencePointSource switch
                    {
                        "reviewed-label" => "Reviewed black-ball reference point in the saved frame; evaluation-only and never supplied to analyzer decisions.",
                        "unreviewed-model-proposal" => "Unreviewed model proposal anchor in the saved frame; evaluation-only and never supplied to analyzer decisions.",
                        _ => "Unreviewed negative-control reference point in the saved frame; evaluation-only and never supplied to analyzer decisions."
                    } },
                targetFitAcceptanceRadiusInBallRadii = targetFitAcceptanceRadiusInBallRadii > 0
                    ? targetFitAcceptanceRadiusInBallRadii : (double?)null,
                maximumSeedToFitOffsetFraction = targetFitAcceptanceRadiusInBallRadii > 0
                    ? targetMaximumSeedToFitOffsetFraction : (double?)null,
                maximumSeedDistanceFromLabelRadii = targetFitAcceptanceRadiusInBallRadii > 0
                    ? targetMaximumSeedDistanceFromLabelRadii : (double?)null,
                darkThreshold = new { minimumH = 0, maximumH = 179, minimumS = 0, maximumS = 255, minimumV = 0, maximumV = DarkValueCeiling, openKernel = "3x3 ellipse" },
                analysisSearchWindow = new { source = searchWindowSource, left, top, right, bottom, selectedColourCentres = selectedCentres.Length,
                    containsManualLabel = label.X >= left && label.X <= right && label.Y >= top && label.Y <= bottom,
                    containsReferencePoint = label.X >= left && label.X <= right && label.Y >= top && label.Y <= bottom },
                darkThresholdCoverageInSearchWindow = new { width = windowRight - windowLeft + 1,
                    height = windowBottom - windowTop + 1, pixelCount = windowPixelCount,
                    rawMaskPixels = rawDarkPixelsInWindow, rawMaskFraction = rawDarkPixelsInWindow / (double)windowPixelCount,
                    openedMaskPixels = openedDarkPixelsInWindow, openedMaskFraction = openedDarkPixelsInWindow / (double)windowPixelCount },
                labelledBallRegion = new { expectedRadius = ballRadius, pixelCount = expectedBallPixelCount,
                    referencePointSource,
                    valueMin = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[0],
                    valueMedian = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[valuesInExpectedBall.Count / 2],
                    valueP90 = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[(int)(valuesInExpectedBall.Count * .9)],
                    valueMax = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[^1],
                    thresholdMaskPixelsBeforeOpen = rawDarkPixelsInExpectedBall, thresholdMaskPixelsAfterOpen = openedDarkPixelsInExpectedBall,
                    maximumDistanceWithinExpectedBall = maxDistanceWithinExpectedBall, maximumDistanceWithin1_5BallRadii = maxDistanceWithin1_5BallRadii,
                    minimumPeakRadiusFactor, minimumPeakRadius },
                manualCentreProbe = manualCentreProbeReport,
                radialOffsetComparison,
                rankedSparseBlackPocketArcProbeCount = rankedSparseBlackPocketArcProbes.Length,
                rankedSparseBlackPocketArcProbeCap = MaximumRankedSparseBlackPocketArcProbes,
                rankedSparseBlackPocketArcProbeExecutedCount = rankedSparseBlackPocketArcProbes.Count(probe => probe.probeExecuted),
                rankedSparseBlackPocketArcProbeSkippedCount = rankedSparseBlackPocketArcProbes.Count(probe => !probe.probeExecuted),
                rankedSparseBlackPocketArcProbes,
                nearbyComponents = nearbyComponents.ToArray(), nearbyLocalMaxima = nearbyPeaks,
                peakComponentCounts = new { positiveLocalMaximumComponents = nearbyPeakCount - 1,
                    minimumRadiusQualifiedComponents = globalPeakCount - 1, croppedMinimumRadiusQualifiedComponents = croppedQualifiedSeedCount,
                    hardIndexLimit = 64 },
                nearbyQualifiedSeeds = allQualifiedSeeds.ToArray(),
                nearbySeedsSkippedByHardIndexLimit = allQualifiedSeeds.Where(p => p.ComponentIndex > 64).ToArray(),
                productionSeeds = productionSeeds.ToArray(), expectedAttemptedSeedCount, auditedProductionSeedCount = auditedProductionSeeds,
                productionSeedCountComparisonApplicable = seedCountComparable,
                productionSeedCountMatchesAnalyzer = seedCountComparable ? expectedAttemptedSeedCount == auditedProductionSeeds : (bool?)null,
                overlays = new[] { maskOverlayName, distanceOverlayName }
            };
    }

    static void SavePng(string path, Mat image)
    {
        using var encoded = new MatOfByte();
        if (!Imgcodecs.Imencode(".png", image, encoded)) throw new IOException("Could not encode opt-in dark-mask trace overlay.");
        File.WriteAllBytes(path, encoded.ToArray());
    }
}
