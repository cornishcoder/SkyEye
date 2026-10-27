namespace SkyEye.Core.Analysis;

/// <summary>Whole-playable-surface evidence, independent of recognized ball labels.
/// Fixed-anchor and adjacent differences use the same unregistered pixels: camera
/// compensation cannot erase motion. Thresholds require mounted-corpus validation.</summary>
public sealed class VerificationPixels(int width, int height)
{
    int Edge => Math.Max(12, width * 12 / 200);
    byte[]? anchor, previous;
    byte[]? anchorColour, previousColour;
    public VerificationMeasurement Measure(byte[] gray, bool geometryValid, VisionResult? vision, byte[]? colour = null, bool[]? supportedBodyPixels = null)
    {
        if (width < 100 || height < 100 || gray.Length != width * height) throw new ArgumentException("Invalid verification pixels.");
        if (colour is not null && colour.Length != gray.Length * 3) throw new ArgumentException("Invalid verification colour pixels.");
        if (supportedBodyPixels is not null && supportedBodyPixels.Length != gray.Length) throw new ArgumentException("Invalid motion coverage mask.");
        if (anchor is not null && (anchorColour is null) != (colour is null)) throw new ArgumentException("Verification pixel format changed.");
        var perimeter = Enumerable.Range(0, gray.Length).Where(i => IsPerimeter(i % width, i / width)).ToArray();
        int textured = perimeter.Count(i => i % width > 0 && Math.Abs(gray[i] - gray[i - 1]) >= 8);
        bool covered = geometryValid && perimeter.Length > 0 && textured >= 40;
        if (vision?.Obstructed == true) return new(TableMotion.Unknown, ObservationReason.Obstructed, false, 0, 0, 0, vision);
        if (!covered) return new(TableMotion.Unknown, ObservationReason.CalibrationChanged, false, 0, 0, 0, vision);
        if (anchor is null) { anchor = gray.ToArray(); previous = gray.ToArray(); anchorColour = colour?.ToArray(); previousColour = colour?.ToArray(); return new(TableMotion.Unknown, ObservationReason.InsufficientEvidence, true, 0, 0, 0, vision); }
        double lighting = Math.Max(Math.Abs(gray.Average(b => (double)b) - anchor.Average(b => (double)b)), Math.Abs(gray.Average(b => (double)b) - previous!.Average(b => (double)b)));
        double local = 0, photometricResidual = 0, sensitive = 0;
        Point? localAt = null, photometricResidualAt = null, sensitiveAt = null;
        int globalValid = 0, changedRailAnchor = 0, changedRailPrevious = 0;
        var globalGrayAnchor = new PhotometricMoments(); var globalGrayPrevious = new PhotometricMoments();
        int tile = Math.Max(16, width * 16 / 200), edge = Edge;
        for (int y = edge; y < height - edge; y += tile) for (int x = edge; x < width - edge; x += tile)
        {
            int changedAnchor = 0, changedPrevious = 0, residualAnchor = 0, residualPrevious = 0, subtleAnchor = 0, subtlePrevious = 0, count = 0, valid = 0;
            var grayAnchor = new PhotometricMoments(); var grayPrevious = new PhotometricMoments();
            var colourAnchor0 = new PhotometricMoments(); var colourAnchor1 = new PhotometricMoments(); var colourAnchor2 = new PhotometricMoments();
            var colourPrevious0 = new PhotometricMoments(); var colourPrevious1 = new PhotometricMoments(); var colourPrevious2 = new PhotometricMoments();
            for (int py = y; py < Math.Min(y + tile, height - edge); py++) for (int px = x; px < Math.Min(x + tile, width - edge); px++)
            {
                int i = py * width + px;
                count++; // independently supported body pixels remain part of the tile's coverage denominator
                if (supportedBodyPixels?[i] == true) continue; // independently measured by current colour-template displacement/residual
                valid++;
                grayAnchor.Add(anchor[i], gray[i]); grayPrevious.Add(previous![i], gray[i]);
                globalValid++; globalGrayAnchor.Add(anchor[i], gray[i]); globalGrayPrevious.Add(previous[i], gray[i]);
                if (colour is not null)
                {
                    colourAnchor0.Add(anchorColour![i * 3], colour[i * 3]);
                    colourAnchor1.Add(anchorColour[i * 3 + 1], colour[i * 3 + 1]);
                    colourAnchor2.Add(anchorColour[i * 3 + 2], colour[i * 3 + 2]);
                    colourPrevious0.Add(previousColour![i * 3], colour[i * 3]);
                    colourPrevious1.Add(previousColour[i * 3 + 1], colour[i * 3 + 1]);
                    colourPrevious2.Add(previousColour[i * 3 + 2], colour[i * 3 + 2]);
                }
            }
            var grayAnchorFit = grayAnchor.Fit(valid); var grayPreviousFit = grayPrevious.Fit(valid);
            var colourAnchorFit0 = colourAnchor0.Fit(valid); var colourAnchorFit1 = colourAnchor1.Fit(valid); var colourAnchorFit2 = colourAnchor2.Fit(valid);
            var colourPreviousFit0 = colourPrevious0.Fit(valid); var colourPreviousFit1 = colourPrevious1.Fit(valid); var colourPreviousFit2 = colourPrevious2.Fit(valid);
            for (int py = y; py < Math.Min(y + tile, height - edge); py++) for (int px = x; px < Math.Min(x + tile, width - edge); px++)
            {
                int i = py * width + px;
                if (supportedBodyPixels?[i] == true) continue;
                int a = Math.Abs(gray[i] - anchor[i]), p = Math.Abs(gray[i] - previous![i]);
                double correctedA = Math.Abs(gray[i] - grayAnchorFit.Apply(anchor[i]));
                double correctedP = Math.Abs(gray[i] - grayPreviousFit.Apply(previous[i]));
                if (colour is not null) for (int c = 0; c < 3; c++)
                {
                    int oldA = anchorColour![i * 3 + c], oldP = previousColour![i * 3 + c];
                    int colourA = colour[i * 3 + c] - oldA;
                    int colourP = colour[i * 3 + c] - oldP;
                    a = Math.Max(a, Math.Abs(colourA)); p = Math.Max(p, Math.Abs(colourP));
                    var fitA = c == 0 ? colourAnchorFit0 : c == 1 ? colourAnchorFit1 : colourAnchorFit2;
                    var fitP = c == 0 ? colourPreviousFit0 : c == 1 ? colourPreviousFit1 : colourPreviousFit2;
                    correctedA = Math.Max(correctedA, Math.Abs(colour[i * 3 + c] - fitA.Apply(oldA)));
                    correctedP = Math.Max(correctedP, Math.Abs(colour[i * 3 + c] - fitP.Apply(oldP)));
                }
                if (a > 12) changedAnchor++; if (p > 12) changedPrevious++;
                if (correctedA > 12) residualAnchor++; if (correctedP > 12) residualPrevious++;
                // Low-amplitude codec/lighting offsets on uniform cloth do not
                // establish displacement. Require a changed local edge as well;
                // the independently measured body patches still cover smooth balls.
                if (correctedA > 3 && EdgeChanged(anchor, anchorColour, i, grayAnchorFit.Gain, colourAnchorFit0.Gain, colourAnchorFit1.Gain, colourAnchorFit2.Gain)) subtleAnchor++;
                if (correctedP > 3 && EdgeChanged(previous!, previousColour, i, grayPreviousFit.Gain, colourPreviousFit0.Gain, colourPreviousFit1.Gain, colourPreviousFit2.Gain)) subtlePrevious++;
            }
            if (count > 0)
            {
                double strong = Math.Max(changedAnchor, changedPrevious) / (double)count;
                double corrected = Math.Max(residualAnchor, residualPrevious) / (double)count;
                // Remove a tile-wide RGB/luminance offset before interpreting
                // strong changes as motion. Body pixels remain independently
                // checked by their colour templates and detected centres.
                double weak = Math.Min(subtleAnchor, subtlePrevious) / (double)count;
                if (strong > local) { local = strong; localAt = new(x, y); }
                if (corrected > photometricResidual) { photometricResidual = corrected; photometricResidualAt = new(x, y); }
                if (weak > sensitive) { sensitive = weak; sensitiveAt = new(x, y); }
            }
        }
        var globalAnchorFit = globalGrayAnchor.Fit(globalValid); var globalPreviousFit = globalGrayPrevious.Fit(globalValid);
        foreach (int i in perimeter)
        {
            if (Math.Abs(gray[i] - globalAnchorFit.Apply(anchor[i])) > 12) changedRailAnchor++;
            if (Math.Abs(gray[i] - globalPreviousFit.Apply(previous![i])) > 12) changedRailPrevious++;
        }
        double railChange = Math.Max(changedRailAnchor, changedRailPrevious) / (double)perimeter.Length;
        previous = gray.ToArray();
        previousColour = colour?.ToArray();
        var reason = vision?.Obstructed == true ? ObservationReason.Obstructed
            : railChange > .08 ? ObservationReason.CalibrationChanged
            : photometricResidual > .06 ? ObservationReason.Moving
            : photometricResidual > .01 || sensitive > .02 || railChange > .02 ? ObservationReason.InsufficientEvidence : ObservationReason.None;
        return new(reason == ObservationReason.None ? TableMotion.Still : reason == ObservationReason.Moving ? TableMotion.Moving : TableMotion.Unknown,
            reason, true, local, railChange, lighting, vision) { SensitiveTileChange = sensitive, PhotometricResidualTileChange = photometricResidual,
                MaximumTileOrigin = localAt, SensitiveTileOrigin = sensitiveAt, PhotometricResidualTileOrigin = photometricResidualAt };
        bool EdgeChanged(byte[] oldGray, byte[]? oldColour, int i, double grayGain, double colourGain0, double colourGain1, double colourGain2)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                int offset = axis == 0 ? 1 : width;
                if (Math.Abs((gray[i + offset] - gray[i - offset]) - grayGain * (oldGray[i + offset] - oldGray[i - offset])) > 6) return true;
                if (colour is not null) for (int c = 0; c < 3; c++)
                {
                    double gain = c == 0 ? colourGain0 : c == 1 ? colourGain1 : colourGain2;
                    if (Math.Abs((colour[(i + offset) * 3 + c] - colour[(i - offset) * 3 + c])
                        - gain * (oldColour![(i + offset) * 3 + c] - oldColour[(i - offset) * 3 + c])) > 6) return true;
                }
            }
            return false;
        }
    }
    struct PhotometricMoments
    {
        double before, after, beforeSquared, beforeAfter;
        public void Add(int beforeValue, int afterValue)
        {
            before += beforeValue; after += afterValue;
            beforeSquared += beforeValue * beforeValue; beforeAfter += beforeValue * afterValue;
        }
        public PhotometricFit Fit(int count)
        {
            if (count < 8) return new(1, count == 0 ? 0 : (after - before) / count);
            double variance = beforeSquared - before * before / count;
            double covariance = beforeAfter - before * after / count;
            double gain = variance > 1 ? covariance / variance : 1;
            if (!double.IsFinite(gain) || gain is < .5 or > 2) gain = 1;
            return new(gain, (after - gain * before) / count);
        }
    }
    readonly record struct PhotometricFit(double Gain, double Offset)
    {
        public double Apply(int value) => Gain * value + Offset;
    }
    bool IsPerimeter(int x, int y) => (x >= 3 && x < Edge || x >= width - Edge && x < width - 3 || y >= 3 && y < Edge || y >= height - Edge && y < height - 3);
}
