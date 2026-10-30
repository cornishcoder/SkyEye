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

    readonly record struct Peak(int ComponentIndex, double X, double Y, double Radius, int Area, double DistanceFromLabel,
        bool PassesMargin, bool PassesMinimumRadius, bool InSearchWindow);

    public static object Capture(byte[] encodedImage, TableCalibration calibration, int width, VisionResult vision,
        string outputDirectory, string frameId, int manualImageX = ManualImageX, int manualImageY = ManualImageY)
    {
        using var encoded = new MatOfByte(encodedImage);
        using var source = Imgcodecs.Imdecode(encoded, Imgcodecs.ImreadColor);
        if (manualImageX < 0 || manualImageX >= source.Cols() || manualImageY < 0 || manualImageY >= source.Rows())
            throw new ArgumentOutOfRangeException(nameof(manualImageX),
                $"Manual source label ({manualImageX},{manualImageY}) is outside the {source.Cols()}x{source.Rows()} replay image.");
        CvPoint[] original = calibration.Corners.Select(p => new CvPoint(p.X * source.Cols(), p.Y * source.Rows())).ToArray();
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

            var labelTablePoint = calibration.Map(new Point(manualImageX / (double)source.Cols(), manualImageY / (double)source.Rows()));
            var label = new AnalysisCoordinates(width).FromTable(labelTablePoint);
            double scale = width / 1000d;
            double ballRadius = PoolGeometry.Radius * width;
            double minimumPeakRadius = 8 * scale;
            int candidateAuditGeometryWidth = width;
            if (vision.PackedClusterFallback is { Attempted: true } fallback &&
                (fallback.Recovered || fallback.FallbackBallCount > fallback.PrimaryBallCount))
                candidateAuditGeometryWidth = fallback.FallbackAnalysisWidth;
            double auditScale = width / (double)candidateAuditGeometryWidth;
            var selectedCentres = vision.CandidateAudits.Where(a => a.Pass == "initial" && a.Stage == "packed-window" && a.Decision == "selected")
                .Select(a => a.ProposalCentre * auditScale).ToArray();
            bool hasPackedWindow = selectedCentres.Length >= 12;
            double left = hasPackedWindow ? selectedCentres.Min(p => p.X) - 55 * scale : 65 * scale;
            double right = hasPackedWindow ? selectedCentres.Max(p => p.X) + 55 * scale : width - 65 * scale;
            double top = hasPackedWindow ? selectedCentres.Min(p => p.Y) - 55 * scale : 65 * scale;
            double bottom = hasPackedWindow ? selectedCentres.Max(p => p.Y) + 55 * scale : 2 * width - 65 * scale;
            string searchWindowSource = hasPackedWindow ? "packed-cluster" : "full-table-sparse-black";

            using var distance = new Mat(); Imgproc.DistanceTransform(openedMask, distance, Imgproc.DistL2, 3);
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
            var nearbyPeaks = new List<Peak>();
            for (int i = 1; i < nearbyPeakCount; i++)
            {
                double x = nearbyCentres.Get(i, 0)![0], y = nearbyCentres.Get(i, 1)![0];
                double radius = distance.Get((int)Math.Round(y), (int)Math.Round(x))![0];
                double fromLabel = Math.Sqrt(Math.Pow(x - label.X, 2) + Math.Pow(y - label.Y, 2));
                bool passesMargin = x >= 65 * scale && y >= 65 * scale && x < width - 65 * scale && y < 2 * width - 65 * scale;
                bool passesMinimumRadius = radius >= minimumPeakRadius;
                bool inWindow = x >= left && x <= right && y >= top && y <= bottom;
                var peak = new Peak(i, x, y, radius, (int)nearbyStats.Get(i, Imgproc.CcStatArea)![0], fromLabel,
                    passesMargin, passesMinimumRadius, inWindow);
                if (fromLabel <= 3 * ballRadius) nearbyPeaks.Add(peak);
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
            var allQualifiedSeeds = new List<Peak>(); var productionSeeds = new List<Peak>();
            for (int i = 1; i < globalPeakCount; i++)
            {
                double x = globalPeakCentres.Get(i, 0)![0], y = globalPeakCentres.Get(i, 1)![0];
                double radius = distance.Get((int)Math.Round(y), (int)Math.Round(x))![0];
                double fromLabel = Math.Sqrt(Math.Pow(x - label.X, 2) + Math.Pow(y - label.Y, 2));
                bool passesMargin = x >= 65 * scale && y >= 65 * scale && x < width - 65 * scale && y < 2 * width - 65 * scale;
                bool inWindow = x >= left && x <= right && y >= top && y <= bottom;
                var seed = new Peak(i, x, y, radius, (int)globalPeakStats.Get(i, Imgproc.CcStatArea)![0],
                    fromLabel, passesMargin, true, inWindow);
                if (fromLabel <= 3 * ballRadius) allQualifiedSeeds.Add(seed);
            }
            int croppedQualifiedSeedCount = productionPeakCount - 1;
            for (int i = 1; i < productionPeakCount && i <= 64; i++)
            {
                double x = productionPeakCentres.Get(i, 0)![0] + searchLeft;
                double y = productionPeakCentres.Get(i, 1)![0] + searchTop;
                if (x < 65 * scale || y < 65 * scale || x >= width - 65 * scale || y >= 2 * width - 65 * scale) continue;
                double radius = distance.Get((int)Math.Round(y), (int)Math.Round(x))![0];
                if (radius < minimumPeakRadius) continue;
                double fromLabel = Math.Sqrt(Math.Pow(x - label.X, 2) + Math.Pow(y - label.Y, 2));
                productionSeeds.Add(new(i, x, y, radius,
                    (int)productionPeakStats.Get(i, Imgproc.CcStatArea)![0], fromLabel, true, true, true));
            }
            var localProductionSeedPixels = productionSeeds.Select(p => ((int)Math.Round(p.X), (int)Math.Round(p.Y))).ToHashSet();

            byte[] hsvBytes = new byte[width * width * 2 * 3]; hsv.Get(0, 0, hsvBytes);
            byte[] beforeBytes = new byte[width * width * 2]; thresholdMask.Get(0, 0, beforeBytes);
            byte[] openedBytes = new byte[width * width * 2]; openedMask.Get(0, 0, openedBytes);
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
                double distanceValue = distance.Get(y, x)![0];
                maxDistanceWithin1_5BallRadii = Math.Max(maxDistanceWithin1_5BallRadii, distanceValue);
                if (radial <= ballRadius) maxDistanceWithinExpectedBall = Math.Max(maxDistanceWithinExpectedBall, distanceValue);
            }
            valuesInExpectedBall.Sort();

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
                if (beforeBytes[pixel] != 0) rawDarkPixelsInWindow++;
                if (openedBytes[pixel] != 0) openedDarkPixelsInWindow++;
            }

            using var componentLabels = new Mat(); using var componentStats = new Mat(); using var componentCentres = new Mat();
            int componentCount = Imgproc.ConnectedComponentsWithStats(openedMask, componentLabels, componentStats, componentCentres);
            var nearbyComponents = new List<object>();
            for (int i = 1; i < componentCount; i++)
            {
                int x = (int)componentStats.Get(i, Imgproc.CcStatLeft)![0], y = (int)componentStats.Get(i, Imgproc.CcStatTop)![0];
                int w = (int)componentStats.Get(i, Imgproc.CcStatWidth)![0], h = (int)componentStats.Get(i, Imgproc.CcStatHeight)![0];
                double componentX = componentCentres.Get(i, 0)![0], componentY = componentCentres.Get(i, 1)![0];
                if (Math.Abs(componentX - label.X) <= 3 * ballRadius && Math.Abs(componentY - label.Y) <= 3 * ballRadius)
                    nearbyComponents.Add(new { x, y, width = w, height = h, area = (int)componentStats.Get(i, Imgproc.CcStatArea)![0], centreX = componentX, centreY = componentY });
            }

            Directory.CreateDirectory(outputDirectory);
            byte[] bgrBytes = new byte[width * width * 2 * 3]; warped.Get(0, 0, bgrBytes);
            // Diagnostic-only oracle probe: give the boundary refiner the
            // manually labelled black centre once, to distinguish a missing
            // dark-mask seed from a boundary that cannot be fitted. This point
            // is never passed into VisionAnalyzer or a table-state decision.
            var manualCentreProbe = BallBoundaryRefiner.Refine(bgrBytes, width, width * 2, label,
                ballRadius, minimumSupportedFraction: .5, collectSectorEvidence: true, expectedKind: "black");
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
                expectedRadius = ballRadius,
                probeSupportFloor = .5,
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
            for (int i = 0; i < openedBytes.Length; i++)
                if (openedBytes[i] != 0)
                {
                    int offset = i * 3;
                    bgrBytes[offset] = (byte)((bgrBytes[offset] + 255) / 2);
                    bgrBytes[offset + 1] /= 2;
                    bgrBytes[offset + 2] = (byte)((bgrBytes[offset + 2] + 255) / 2);
                }
            using var overlay = new Mat(width * 2, width, CvType.Cv8uc3); overlay.Put(0, 0, bgrBytes);
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
                byte intensity = (byte)Math.Clamp((int)Math.Round(distance.Get(y, x)![0] * 255 / (2 * ballRadius)), 0, 255);
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
                manualBlackLabel = new { sourcePixel = new { x = manualImageX, y = manualImageY }, analysisPixel = label,
                    note = "Manually inspected black-ball interior point in the saved frame; evaluation-only and never supplied to analyzer decisions." },
                darkThreshold = new { minimumH = 0, maximumH = 179, minimumS = 0, maximumS = 255, minimumV = 0, maximumV = DarkValueCeiling, openKernel = "3x3 ellipse" },
                analysisSearchWindow = new { source = searchWindowSource, left, top, right, bottom, selectedColourCentres = selectedCentres.Length,
                    containsManualLabel = label.X >= left && label.X <= right && label.Y >= top && label.Y <= bottom },
                darkThresholdCoverageInSearchWindow = new { width = windowRight - windowLeft + 1,
                    height = windowBottom - windowTop + 1, pixelCount = windowPixelCount,
                    rawMaskPixels = rawDarkPixelsInWindow, rawMaskFraction = rawDarkPixelsInWindow / (double)windowPixelCount,
                    openedMaskPixels = openedDarkPixelsInWindow, openedMaskFraction = openedDarkPixelsInWindow / (double)windowPixelCount },
                labelledBallRegion = new { expectedRadius = ballRadius, pixelCount = expectedBallPixelCount,
                    valueMin = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[0],
                    valueMedian = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[valuesInExpectedBall.Count / 2],
                    valueP90 = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[(int)(valuesInExpectedBall.Count * .9)],
                    valueMax = valuesInExpectedBall.Count == 0 ? (int?)null : valuesInExpectedBall[^1],
                    thresholdMaskPixelsBeforeOpen = rawDarkPixelsInExpectedBall, thresholdMaskPixelsAfterOpen = openedDarkPixelsInExpectedBall,
                    maximumDistanceWithinExpectedBall = maxDistanceWithinExpectedBall, maximumDistanceWithin1_5BallRadii = maxDistanceWithin1_5BallRadii,
                    minimumPeakRadius },
                manualCentreProbe = manualCentreProbeReport,
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
        finally { foreach (var point in original.Concat(portrait)) point.Dispose(); }
    }

    static void SavePng(string path, Mat image)
    {
        using var encoded = new MatOfByte();
        if (!Imgcodecs.Imencode(".png", image, encoded)) throw new IOException("Could not encode opt-in dark-mask trace overlay.");
        File.WriteAllBytes(path, encoded.ToArray());
    }
}
