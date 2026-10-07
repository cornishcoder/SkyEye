using System.Diagnostics;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using Cv = Org.Opencv.Core.Core;
using Point = SkyEye.Core.Point;
using CvPoint = Org.Opencv.Core.Point;

namespace SkyEye.Vision.Android;

/// <summary>C# port of the upstream HSV/morphology/Hough pipeline with calibrated
/// pocket masks, cue-reference normalization and bounded C# colour clustering.</summary>
public sealed partial class VisionAnalyzer : ITableAnalyzer, ITableVerificationAnalyzer
{
    static readonly object loadGate = new(); static bool loaded;
    readonly AnalysisCoordinates coordinates;
    readonly VisionSettings settings;
    readonly record struct Circle(double X, double Y, double Radius);
    public VisionAnalyzer(int width = 1000) : this(new VisionSettings(Width: width)) { }
    public VisionAnalyzer(VisionSettings settings) { settings.Validate(); this.settings = settings; coordinates = new(settings.Width); }
    static void Load()
    {
        lock (loadGate)
        {
            if (loaded) return;
            if (!OpenCVLoader.InitLocal()) throw new IOException("Table analysis could not load OpenCV.");
            // Leave mobile CPU capacity for playback, voice and Android's UI thread.
            Cv.NumThreads = Math.Min(4, Environment.ProcessorCount); loaded = true;
        }
    }
    internal static void EnsureNativeLoaded() => Load();
    public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default)
    {
        Load(); using var source = Decode(jpeg); return AnalyzePixels(source.Value, calibration, cancellation);
    }
    public VisionResult Analyze(TableFrame frame, TableCalibration calibration, CancellationToken cancellation = default)
    {
        if (frame.Image is not BitmapTableImage pixels) return Analyze(frame.Jpeg, calibration, cancellation);
        Load(); cancellation.ThrowIfCancellationRequested();
        using var rgba = new MatLease(new Mat()); using var bgr = new MatLease(new Mat());
        using (PipelineDiagnostics.Measure("bitmapConversionMs"))
        { Utils.BitmapToMat(pixels.Bitmap, rgba.Value); Imgproc.CvtColor(rgba.Value, bgr.Value, Imgproc.ColorRgba2bgr); }
        return AnalyzePixels(bgr.Value, calibration, cancellation);
    }
    VisionResult AnalyzePixels(Mat source, TableCalibration calibration, CancellationToken cancellation, BurstAppearance? appearance = null)
    {
        cancellation.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew(); var warnings = new List<string>();
        using var warped = Warp(source, calibration);
        using var hsv = new MatLease(new Mat()); Imgproc.CvtColor(warped.Value, hsv.Value, Imgproc.ColorBgr2hsv);
        byte[] pixels = Read(hsv.Value); var circles = Detect(hsv.Value, pixels, settings.InitialVotes, appearance is not null && settings.VerificationBodyRefinement);
        var proposals = circles.ToArray();
        var boundaries = new Dictionary<Circle, BallBoundary>();
        var candidateBoundaries = new Dictionary<Circle, BallBoundary>();
        bool boundaryUncertain = false;
        if (appearance is not null)
        {
            var raw = Read(warped.Value);
            for (int i = 0; i < circles.Length; i++)
            {
                var fit = BallBoundaryRefiner.Refine(raw, coordinates.Width, coordinates.Height, new(circles[i].X, circles[i].Y), PoolGeometry.Radius * coordinates.Width);
                if (fit is null) { boundaryUncertain = true; continue; }
                candidateBoundaries[circles[i]] = fit;
                // Support is sampled at 32 fixed angles. The 18-sector minimum
                // recovers a clear mounted ball with 18-20 supported sectors;
                // repeated current-frame consensus still gates publication.
                if (fit.SupportedSectors < ObservationPolicy.MinimumSupportedBoundaryFraction) { boundaryUncertain = true; continue; }
                var refined = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
                boundaries[refined] = fit; candidateBoundaries[refined] = fit; circles[i] = refined;
            }
        }
        var first = circles.Select(c => Classify(c, pixels, appearance is not null)).ToArray();
        int rawCircleCount = circles.Length;
        bool normalized = false, referenceValid = false, referenceConflict = false; double[] colourGains = [1, 1, 1]; var cueIndices = Enumerable.Range(0, first.Length).Where(i => first[i].Kind == "cue").ToArray();
        if (cueIndices.Length == 1)
        {
            byte[] bgr = Read(warped.Value); var cue = circles[cueIndices[0]];
            var channels = new[] { new List<byte>(), new List<byte>(), new List<byte>() };
            ForPixels(cue, .7, (x, y) => { int offset = (y * coordinates.Width + x) * 3; for (int k = 0; k < 3; k++) channels[k].Add(bgr[offset + k]); });
            double[] gains = channels.Select(c => { c.Sort(); return c.Count == 0 || c[(int)((c.Count - 1) * .95)] < 10 ? 0 : Math.Min(3, 255.0 / c[(int)((c.Count - 1) * .95)]); }).ToArray();
            if (gains.All(g => g > 0)) { colourGains = gains; referenceValid = true; }
        }
        if (appearance?.Frozen == true)
        {
            referenceConflict = referenceValid && Enumerable.Range(0, 3).Any(i => Math.Abs(colourGains[i] / appearance.Gains[i] - 1) > .15);
            colourGains = appearance.Gains.ToArray(); referenceValid = appearance.ReferenceValid;
        }
        if (appearance is not null && !appearance.Frozen && referenceValid) { appearance.Gains = colourGains.ToArray(); appearance.ReferenceValid = true; appearance.Frozen = true; }
        if (referenceValid)
        {
            byte[] bgr = Read(warped.Value);
            {
                for (int i = 0; i < bgr.Length; i++) bgr[i] = (byte)Math.Clamp((int)(bgr[i] * colourGains[i % 3]), 0, 255);
                warped.Value.Put(0, 0, bgr); Imgproc.CvtColor(warped.Value, hsv.Value, Imgproc.ColorBgr2hsv); pixels = Read(hsv.Value);
                // A slightly lower vote threshold retains circles merged with a cushion shadow.
                // Verification freezes the geometry branch as well as gains. The
                // normalized re-detection can fit a cream number patch instead of
                // its surrounding black body. Every burst body still comes from
                // this frame's raw detection; nothing is carried from old frames.
                if (appearance is null) circles = Detect(hsv.Value, pixels, settings.NormalizedVotes);
                normalized = true;
            }
        }
        if (!normalized) warnings.Add("No unique cue reference; colours were analyzed without normalization.");
        cancellation.ThrowIfCancellationRequested(); var balls = new List<Ball>(); var observations = new List<CircleObservation>();
        var ordered = circles.OrderBy(c => c.Y).ThenBy(c => c.X).ToArray();
        var rawLabels = ordered.Select(c => Classify(c, pixels, appearance is not null)).ToArray();
        var labels = BallClassifier.ConstrainInventory(rawLabels);
        if (appearance is not null && !referenceValid)
            labels = labels.Select(l => l with { Kind = "unknown", Quality = 0 }).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            var circle = ordered[i]; var label = labels[i]; var position = coordinates.ToTable(new(circle.X, circle.Y));
            balls.Add(new(label.Kind + "-" + balls.Count, label.Kind, position, label.Quality));
            observations.Add(new(balls[^1].Id, new(circle.X, circle.Y), circle.Radius, label.Pattern, label.Clusters)
            { BodyDarkFraction = label.BodyDarkFraction, RimDarkFraction = label.RimDarkFraction,
                ProposalCentre = appearance is null ? null : new(proposals[Array.IndexOf(circles, circle)].X, proposals[Array.IndexOf(circles, circle)].Y),
                ProposalRadius = appearance is null ? null : proposals[Array.IndexOf(circles, circle)].Radius,
                BoundarySupport = boundaries.GetValueOrDefault(circle)?.SupportedSectors,
                CandidateBoundarySupport = candidateBoundaries.GetValueOrDefault(circle)?.SupportedSectors });
        }
        bool uncertain = balls.Any(b => b.Kind == "unknown") || balls.Count > 16 || balls.Count(b => b.Kind == "cue") != 1 || balls.Count(b => b.Kind == "black") != 1 || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7;
        if (boundaryUncertain) { uncertain = true; warnings.Add("Current ball boundary lacks sufficient edge support."); }
        if (appearance is not null && (!referenceValid || referenceConflict))
        { uncertain = true; warnings.Add(referenceConflict ? "Current cue reference conflicts with the frozen burst gains." : "No validated burst colour reference. Review the table."); }
        if (uncertain) warnings.Add("Check uncertain colours or ball counts before using advice.");
        if (PocketColourNeedsReview(pixels)) { uncertain = true; warnings.Add("Check a coloured ball at a pocket opening; the pocket mask may hide part of it."); }
        bool obstructed = LargeObstruction(hsv.Value, pixels);
        if (obstructed) { uncertain = true; warnings.Add("A large object covers part of the playable surface."); }
        for (int i = 0; i < balls.Count; i++) for (int j = i + 1; j < balls.Count; j++) if ((balls[i].Position - balls[j].Position).Length < .040) { uncertain = true; warnings.Add("Overlapping circle candidates need checking."); }
        return new(balls.ToArray(), uncertain, warnings.Distinct().ToArray(), watch.Elapsed.TotalMilliseconds, circles.Length, normalized, Cv.Version, colourGains, ClothHue(pixels), obstructed, observations.ToArray())
        { Recognition = new(normalized ? "cue-normalized" : "raw", rawCircleCount, circles.Length, rawLabels.Select(l => l.Kind).ToArray(), colourGains, ClothHue(pixels), warnings.Distinct().ToArray()),
            VerificationReason = referenceConflict ? ObservationReason.LightingChanged : null };
    }
    MatLease Warp(Mat source, TableCalibration calibration)
    {
        CvPoint[] original = calibration.Corners.Select(p => new CvPoint(p.X * source.Cols(), p.Y * source.Rows())).ToArray();
        CvPoint[] portrait = [new(coordinates.Width, 0), new(coordinates.Width, coordinates.Height), new(0, coordinates.Height), new(0, 0)];
        try
        {
            using var src = new MatLease(new MatOfPoint2f(original)); using var dst = new MatLease(new MatOfPoint2f(portrait));
            using var transform = new MatLease(Imgproc.GetPerspectiveTransform(src.Value, dst.Value)); var output = new MatLease(new Mat());
            try { using var size = new Size(coordinates.Width, coordinates.Height); Imgproc.WarpPerspective(source, output.Value, transform.Value, size); return output; }
            catch { output.Dispose(); throw; }
        }
        finally { foreach (var point in original.Concat(portrait)) point.Dispose(); }
    }
    Circle[] Detect(Mat hsv, byte[] pixels, double votes, bool refineGeometry = false)
    {
        using var mask = ClothMask(hsv, pixels); using var circles = new MatLease(new Mat());
        double scale = coordinates.Width / 1000.0;
        Imgproc.HoughCircles(mask.Value, circles.Value, Imgproc.HoughGradient, settings.HoughDp, settings.Separation * scale, settings.EdgeThreshold, votes * scale, (int)(settings.MinimumRadius * scale), (int)(settings.MaximumRadius * scale));
        var result = new List<Circle>(); int count = Math.Max(circles.Value.Cols(), circles.Value.Rows()); var clothRange = ClothRange(pixels); int clothHue = clothRange.Hue;
        for (int i = 0; i < count && result.Count <= 32; i++)
        {
            var value = circles.Value.Get(circles.Value.Rows() == 1 ? 0 : i, circles.Value.Rows() == 1 ? i : 0);
            if (value is null || value.Length < 3) continue;
            var c = new Circle(value[0], value[1], value[2]); double margin = 23 * scale;
            if (c.X < margin || c.Y < margin || c.X > coordinates.Width - margin || c.Y > coordinates.Height - margin) continue;
            // An opening is not a ball; ball-adjacent uncertain cases remain visible through review.
            if (PocketCentres().Any(p => (new Point(c.X, c.Y) - p).Length < settings.PocketExclusionRadius * scale)) continue;
            // Cloth shadows can produce circle edges anywhere. A wider hue band
            // tolerates dim texture only when S/V also resemble the cloth, so it
            // cannot swallow saturated yellows or the bright cream cue.
            {
                int sampled = 0, cloth = 0;
                ForPixels(c, .85, (x, y) =>
                {
                    int offset = (y * coordinates.Width + x) * 3; int difference = Math.Abs(pixels[offset] - clothHue); sampled++;
                    if ((Math.Min(difference, 180 - difference) <= Math.Max(12, settings.HueBand) || pixels[offset + 1] < 45) &&
                        pixels[offset + 1] < Math.Max(60, clothRange.SaturationFloor / .45 * 1.5) &&
                        pixels[offset + 2] > 20 && pixels[offset + 2] < Math.Max(80, clothRange.ValueFloor / .45 * 1.8)) cloth++;
                });
                if (sampled > 0 && cloth / (double)sampled > .9) continue;
            }
            result.Add(c);
        }
        AddColourBodies(hsv, pixels, result, scale);
        if (refineGeometry) RefineBodyGeometry(mask.Value, result, scale);
        return result.ToArray();
    }
    void RefineBodyGeometry(Mat cloth, List<Circle> circles, double scale)
    {
        // A fresh connected body can refine a jittering Hough edge. Require a
        // bounded round component containing exactly one current candidate;
        // merged/touching bodies retain their unresolved original geometry.
        using var bodies = new MatLease(new Mat()); Cv.Bitwise_not(cloth, bodies.Value);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(bodies.Value, labels.Value, stats.Value, centres.Value);
        for (int i = 1; i < count; i++)
        {
            double w = stats.Value.Get(i, Imgproc.CcStatWidth)![0], h = stats.Value.Get(i, Imgproc.CcStatHeight)![0], area = stats.Value.Get(i, Imgproc.CcStatArea)![0];
            double radius = (w + h) / 4;
            if (radius < settings.MinimumRadius * scale || radius > settings.MaximumRadius * scale || w / h is < .75 or > 1.33 || area / (w * h) < .65) continue;
            var included = Enumerable.Range(0, circles.Count).Where(n => labels.Value.Get((int)circles[n].Y, (int)circles[n].X)![0] == i).ToArray();
            if (included.Length != 1) continue;
            var observed = new Circle(centres.Value.Get(i, 0)![0], centres.Value.Get(i, 1)![0], radius);
            var old = circles[included[0]];
            if (Math.Sqrt(Math.Pow(old.X - observed.X, 2) + Math.Pow(old.Y - observed.Y, 2)) <= radius * .5)
                circles[included[0]] = observed;
        }
    }
    void AddColourBodies(Mat hsv, byte[] pixels, List<Circle> circles, double scale)
    {
        // Recover isolated painted bodies that the cloth mask loses beside a
        // cushion or warm shadow. Size, fill and aspect bounds exclude streaks,
        // rails and merged groups; the normal uncertainty gates still apply.
        using var mask = new MatLease(new Mat());
        using var low = new Scalar(0, 160, 40); using var high = new Scalar(179, 255, 255);
        Cv.InRange(hsv, low, high, mask.Value);
        byte[] body = Read(mask.Value);
        for (int i = 0; i < body.Length; i++)
            if (pixels[i * 3] > 37 && pixels[i * 3] < 163) body[i] = 0;
        mask.Value.Put(0, 0, body);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(mask.Value, labels.Value, stats.Value, centres.Value);
        for (int i = 1; i < count && circles.Count <= 32; i++)
        {
            double width = stats.Value.Get(i, Imgproc.CcStatWidth)![0], height = stats.Value.Get(i, Imgproc.CcStatHeight)![0], area = stats.Value.Get(i, Imgproc.CcStatArea)![0];
            double radius = (width + height) / 4;
            if (radius < settings.MinimumRadius * scale || radius > settings.MaximumRadius * scale || width / height is < .6 or > 1.6 || area / (width * height) < .55) continue;
            var c = new Circle(centres.Value.Get(i, 0)![0], centres.Value.Get(i, 1)![0], radius);
            double margin = 23 * scale;
            if (c.X < margin || c.Y < margin || c.X > coordinates.Width - margin || c.Y > coordinates.Height - margin ||
                PocketCentres().Any(p => (new Point(c.X, c.Y) - p).Length < settings.PocketExclusionRadius * scale) ||
                circles.Any(p => Math.Sqrt((p.X - c.X) * (p.X - c.X) + (p.Y - c.Y) * (p.Y - c.Y)) < settings.Separation * scale)) continue;
            circles.Add(c);
        }
    }
    int ClothHue(byte[] pixels)
        => ClothRange(pixels).Hue;
    (int Hue, int SaturationFloor, int ValueFloor) ClothRange(byte[] pixels)
    {
        // Low-saturation blue/grey cloth is the normal mounted-camera lighting.
        // Estimate both its hue and usable S/V floors from many interior samples.
        int[] histogram = new int[60]; var samples = new List<HsvPixel>(); int w = coordinates.Width, h = coordinates.Height;
        for (int y = h / 10; y < h * 9 / 10; y += 10) for (int x = w / 10; x < w * 9 / 10; x += 10)
        {
            int offset = (y * w + x) * 3;
            if (pixels[offset + 1] > 20 && pixels[offset + 2] > 35) { histogram[pixels[offset] / 3]++; samples.Add(new(pixels[offset], pixels[offset + 1], pixels[offset + 2])); }
        }
        int hue = Array.IndexOf(histogram, histogram.Max()) * 3 + 1;
        var cloth = samples.Where(p => Math.Min(Math.Abs(p.H - hue), 180 - Math.Abs(p.H - hue)) <= settings.HueBand).ToArray();
        if (cloth.Length < 20) return (hue, 60, 60);
        double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
        return (hue, (int)Math.Clamp(Median(cloth.Select(p => p.S)) * .45, 8, 60), (int)Math.Clamp(Median(cloth.Select(p => p.V)) * .45, 15, 60));
    }
    MatLease ClothMask(Mat hsv, byte[] pixels, bool separateBalls = true)
    {
        int w = coordinates.Width; var range = ClothRange(pixels); int hue = range.Hue;
        int band = separateBalls ? settings.HueBand : Math.Max(12, settings.HueBand);
        var mask = new MatLease(new Mat());
        try
        {
            // Warm cloth and the cue/yellows can share a hue. Bound saturation
            // by the sampled cloth rather than treating every matching hue as cloth.
            double saturationCeiling = separateBalls ? Math.Clamp(range.SaturationFloor / .45 * 1.8, 90, 255) : 255;
            using var low = new Scalar(Math.Max(0, hue - band), range.SaturationFloor, range.ValueFloor); using var high = new Scalar(Math.Min(179, hue + band), saturationCeiling, 255);
            Cv.InRange(hsv, low, high, mask.Value);
            if (hue < band || hue > 179 - band)
            {
                using var wrapped = new MatLease(new Mat()); using var l = new Scalar(hue < band ? 180 + hue - band : 0, range.SaturationFloor, range.ValueFloor); using var u = new Scalar(hue < band ? 179 : hue + band - 180, saturationCeiling, 255);
                Cv.InRange(hsv, l, u, wrapped.Value); Cv.Bitwise_or(mask.Value, wrapped.Value, mask.Value);
            }
            // Video snapshot decoders quantize dim neutral cloth: its hue can
            // swing wildly while saturation stays low. Bright cue paint and
            // genuinely dark black bodies fall outside this bounded fallback.
            using var neutral = new MatLease(new Mat()); using var neutralLow = new Scalar(0, 0, range.ValueFloor);
            using var neutralHigh = new Scalar(179, 45, Math.Min(130, Math.Max(80, range.ValueFloor / .45 * 1.8)));
            Cv.InRange(hsv, neutralLow, neutralHigh, neutral.Value); Cv.Bitwise_or(mask.Value, neutral.Value, mask.Value);
            int kernelWidth = Math.Max(3, (int)Math.Round(settings.MorphologyKernel * w / 1000.0) | 1); using var size = new Size(kernelWidth, kernelWidth);
            using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphRect, size));
            Imgproc.MorphologyEx(mask.Value, mask.Value, Imgproc.MorphClose, kernel.Value); Imgproc.MorphologyEx(mask.Value, mask.Value, Imgproc.MorphOpen, kernel.Value);
            using var white = new Scalar(255);
            foreach (var pocket in PocketCentres()) { using var point = new CvPoint(pocket.X, pocket.Y); Imgproc.Circle(mask.Value, point, (int)(settings.PocketExclusionRadius * w / 1000.0), white, -1); }
            return mask;
        }
        catch { mask.Dispose(); throw; }
    }
    bool LargeObstruction(Mat hsv, byte[] pixels)
    {
        // Obstruction checks retain the broad cloth range: local saturation
        // variations in shadows are not themselves evidence of a large object.
        using var mask = ClothMask(hsv, pixels, separateBalls: false); Cv.Bitwise_not(mask.Value, mask.Value);
        // Crop only the uncertain calibration border; a hand connected to an image
        // edge must still count as an obstruction inside the playable surface.
        int border = (int)(65 * coordinates.Width / 1000.0);
        byte[] interior = Read(mask.Value);
        for (int y = 0; y < coordinates.Height; y++) for (int x = 0; x < coordinates.Width; x++)
            if (x < border || y < border || x >= coordinates.Width - border || y >= coordinates.Height - border) interior[y * coordinates.Width + x] = 0;
        mask.Value.Put(0, 0, interior);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(mask.Value, labels.Value, stats.Value, centres.Value);
        double scale = coordinates.Width / 1000.0;
        for (int i = 1; i < count; i++)
        {
            double x = stats.Value.Get(i, Imgproc.CcStatLeft)![0], y = stats.Value.Get(i, Imgproc.CcStatTop)![0];
            double width = stats.Value.Get(i, Imgproc.CcStatWidth)![0], height = stats.Value.Get(i, Imgproc.CcStatHeight)![0], area = stats.Value.Get(i, Imgproc.CcStatArea)![0];
            if (width > 120 * scale && height > 120 * scale && area > 5000 * scale * scale) return true;
        }
        return false;
    }
    Point[] PocketCentres() => [new(0, 0), new(coordinates.Width, 0), new(0, coordinates.Width), new(coordinates.Width, coordinates.Width), new(0, coordinates.Height), new(coordinates.Width, coordinates.Height)];
    bool PocketColourNeedsReview(byte[] pixels)
    {
        double scale = coordinates.Width / 1000.0, radius = settings.PocketExclusionRadius * scale;
        foreach (var pocket in PocketCentres())
        {
            int coloured = 0;
            ForPixels(new(pocket.X, pocket.Y, radius), 1, (x, y) =>
            {
                int i = (y * coordinates.Width + x) * 3;
                if (pixels[i + 1] > 120 && pixels[i + 2] > 100 && (pixels[i] <= 37 || pixels[i] >= 163)) coloured++;
            });
            if (coloured > 100 * scale * scale) return true;
        }
        return false;
    }
    BallClassification Classify(Circle circle, byte[] hsv, bool inspectRim = false)
    {
        var samples = new List<HsvPixel>();
        // Include enough of the ball body to distinguish the black from its large
        // cream number patch when the fitted circle centre lands on a highlight.
        ForPixels(circle, .85, (x, y) => { int offset = (y * coordinates.Width + x) * 3; samples.Add(new(hsv[offset], hsv[offset + 1], hsv[offset + 2])); });
        if (!inspectRim) return BallClassifier.Classify(samples.ToArray());
        var rim = new List<HsvPixel>();
        ForPixels(circle, .95, (x, y) =>
        {
            if ((x - circle.X) * (x - circle.X) + (y - circle.Y) * (y - circle.Y) < circle.Radius * circle.Radius * .8 * .8) return;
            int offset = (y * coordinates.Width + x) * 3; rim.Add(new(hsv[offset], hsv[offset + 1], hsv[offset + 2]));
        });
        return BallClassifier.ClassifyWithRim(samples.ToArray(), rim.ToArray());
    }
    void ForPixels(Circle circle, double fraction, Action<int, int> action)
    {
        double radius = circle.Radius * fraction;
        for (int y = Math.Max(0, (int)(circle.Y - radius)); y <= Math.Min(coordinates.Height - 1, (int)(circle.Y + radius)); y++)
        for (int x = Math.Max(0, (int)(circle.X - radius)); x <= Math.Min(coordinates.Width - 1, (int)(circle.X + radius)); x++)
            if ((x - circle.X) * (x - circle.X) + (y - circle.Y) * (y - circle.Y) <= radius * radius) action(x, y);
    }
    static byte[] Read(Mat mat) { byte[] data = new byte[mat.Rows() * mat.Cols() * mat.Channels()]; mat.Get(0, 0, data); return data; }
    static MatLease Decode(byte[] jpeg)
    {
        if (jpeg.Length is < 4 or > 16 * 1024 * 1024) throw new IOException("Invalid analysis image size.");
        using var encoded = new MatLease(new MatOfByte(jpeg)); var decoded = new MatLease(Imgcodecs.Imdecode(encoded.Value, Imgcodecs.ImreadColor));
        if (decoded.Value.Empty() || decoded.Value.Rows() > 6000 || decoded.Value.Cols() > 6000) { decoded.Dispose(); throw new IOException("Snapshot could not be decoded."); }
        return decoded;
    }
    public Point[]? FindCorners(byte[] jpeg)
    {
        PipelineDiagnostics.Count("calibrationDetections");
        var pockets = FindPockets(jpeg);
        if (pockets is not null) return pockets.Corners.ToArray();
        Load(); using var frame = Decode(jpeg); using var gray = new MatLease(new Mat()); using var edges = new MatLease(new Mat()); using var circles = new MatLease(new Mat()); using var size = new Size(7, 7);
        Imgproc.CvtColor(frame.Value, gray.Value, Imgproc.ColorBgr2gray); Imgproc.GaussianBlur(gray.Value, gray.Value, size, 0); Imgproc.Canny(gray.Value, edges.Value, 170, 200);
        double scale = Math.Min(frame.Value.Rows(), frame.Value.Cols()) / 1000.0;
        Imgproc.HoughCircles(edges.Value, circles.Value, Imgproc.HoughGradient, 1.6, 300 * scale, 30, 30, (int)(35 * scale), (int)(100 * scale));
        var candidates = new List<Point>();
        for (int i = 0; i < circles.Value.Cols(); i++) { var c = circles.Value.Get(0, i); if (c is { Length: >= 3 }) candidates.Add(new(c[0] / frame.Value.Cols(), c[1] / frame.Value.Rows())); }
        if (candidates.Count < 4) return null;
        Point[] corners = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        var picks = corners.Select(p => candidates.MinBy(c => (c - p).Length)).ToArray();
        if (picks.Distinct().Count() != 4 || picks.Where((p, i) => (p - corners[i]).Length > .35).Any()) return null;
        try { _ = new TableCalibration(picks); return picks; } catch (ArgumentException) { return null; }
    }
    sealed class MatLease(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() { Value.Release(); Value.Dispose(); } }
}

