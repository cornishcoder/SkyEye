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
        double railChange = Math.Max(perimeter.Count(i => Math.Abs(gray[i] - anchor[i]) > 12), perimeter.Count(i => Math.Abs(gray[i] - previous![i]) > 12)) / (double)perimeter.Length;
        double local = 0, photometricResidual = 0, sensitive = 0;
        Point? localAt = null, photometricResidualAt = null, sensitiveAt = null;
        int tile = Math.Max(16, width * 16 / 200), edge = Edge;
        for (int y = edge; y < height - edge; y += tile) for (int x = edge; x < width - edge; x += tile)
        {
            int changedAnchor = 0, changedPrevious = 0, residualAnchor = 0, residualPrevious = 0, subtleAnchor = 0, subtlePrevious = 0, count = 0, valid = 0;
            long grayAnchorOffset = 0, grayPreviousOffset = 0;
            long colourAnchor0 = 0, colourAnchor1 = 0, colourAnchor2 = 0;
            long colourPrevious0 = 0, colourPrevious1 = 0, colourPrevious2 = 0;
            for (int py = y; py < Math.Min(y + tile, height - edge); py++) for (int px = x; px < Math.Min(x + tile, width - edge); px++)
            {
                int i = py * width + px;
                count++; // independently supported body pixels remain part of the tile's coverage denominator
                if (supportedBodyPixels?[i] == true) continue; // independently measured by current colour-template displacement/residual
                valid++;
                grayAnchorOffset += gray[i] - anchor[i]; grayPreviousOffset += gray[i] - previous![i];
                if (colour is not null)
                {
                    colourAnchor0 += colour[i * 3] - anchorColour![i * 3];
                    colourAnchor1 += colour[i * 3 + 1] - anchorColour[i * 3 + 1];
                    colourAnchor2 += colour[i * 3 + 2] - anchorColour[i * 3 + 2];
                    colourPrevious0 += colour[i * 3] - previousColour![i * 3];
                    colourPrevious1 += colour[i * 3 + 1] - previousColour[i * 3 + 1];
                    colourPrevious2 += colour[i * 3 + 2] - previousColour[i * 3 + 2];
                }
            }
            double grayAnchorMean = valid == 0 ? 0 : grayAnchorOffset / (double)valid;
            double grayPreviousMean = valid == 0 ? 0 : grayPreviousOffset / (double)valid;
            double colourAnchorMean0 = valid == 0 ? 0 : colourAnchor0 / (double)valid;
            double colourAnchorMean1 = valid == 0 ? 0 : colourAnchor1 / (double)valid;
            double colourAnchorMean2 = valid == 0 ? 0 : colourAnchor2 / (double)valid;
            double colourPreviousMean0 = valid == 0 ? 0 : colourPrevious0 / (double)valid;
            double colourPreviousMean1 = valid == 0 ? 0 : colourPrevious1 / (double)valid;
            double colourPreviousMean2 = valid == 0 ? 0 : colourPrevious2 / (double)valid;
            for (int py = y; py < Math.Min(y + tile, height - edge); py++) for (int px = x; px < Math.Min(x + tile, width - edge); px++)
            {
                int i = py * width + px;
                if (supportedBodyPixels?[i] == true) continue;
                int a = Math.Abs(gray[i] - anchor[i]), p = Math.Abs(gray[i] - previous![i]);
                double correctedA = Math.Abs((gray[i] - anchor[i]) - grayAnchorMean);
                double correctedP = Math.Abs((gray[i] - previous[i]) - grayPreviousMean);
                if (colour is not null) for (int c = 0; c < 3; c++)
                {
                    int colourA = colour[i * 3 + c] - anchorColour![i * 3 + c];
                    int colourP = colour[i * 3 + c] - previousColour![i * 3 + c];
                    a = Math.Max(a, Math.Abs(colourA)); p = Math.Max(p, Math.Abs(colourP));
                    double meanA = c == 0 ? colourAnchorMean0 : c == 1 ? colourAnchorMean1 : colourAnchorMean2;
                    double meanP = c == 0 ? colourPreviousMean0 : c == 1 ? colourPreviousMean1 : colourPreviousMean2;
                    correctedA = Math.Max(correctedA, Math.Abs(colourA - meanA));
                    correctedP = Math.Max(correctedP, Math.Abs(colourP - meanP));
                }
                if (a > 12) changedAnchor++; if (p > 12) changedPrevious++;
                if (correctedA > 12) residualAnchor++; if (correctedP > 12) residualPrevious++;
                // Low-amplitude codec/lighting offsets on uniform cloth do not
                // establish displacement. Require a changed local edge as well;
                // the independently measured body patches still cover smooth balls.
                if (a > 3 && EdgeChanged(anchor, anchorColour, i)) subtleAnchor++;
                if (p > 3 && EdgeChanged(previous!, previousColour, i)) subtlePrevious++;
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
        previous = gray.ToArray();
        previousColour = colour?.ToArray();
        var reason = vision?.Obstructed == true ? ObservationReason.Obstructed
            : lighting > 8 ? ObservationReason.LightingChanged : railChange > .08 ? ObservationReason.CalibrationChanged
            : photometricResidual > .06 ? ObservationReason.Moving
            : photometricResidual > .01 || sensitive > .02 || railChange > .02 ? ObservationReason.InsufficientEvidence : ObservationReason.None;
        return new(reason == ObservationReason.None ? TableMotion.Still : reason == ObservationReason.Moving ? TableMotion.Moving : TableMotion.Unknown,
            reason, true, local, railChange, lighting, vision) { SensitiveTileChange = sensitive, PhotometricResidualTileChange = photometricResidual,
                MaximumTileOrigin = localAt, SensitiveTileOrigin = sensitiveAt, PhotometricResidualTileOrigin = photometricResidualAt };
        bool EdgeChanged(byte[] oldGray, byte[]? oldColour, int i)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                int offset = axis == 0 ? 1 : width;
                if (Math.Abs((gray[i + offset] - gray[i - offset]) - (oldGray[i + offset] - oldGray[i - offset])) > 6) return true;
                if (colour is not null) for (int c = 0; c < 3; c++)
                    if (Math.Abs((colour[(i + offset) * 3 + c] - colour[(i - offset) * 3 + c])
                        - (oldColour![(i + offset) * 3 + c] - oldColour[(i - offset) * 3 + c])) > 6) return true;
            }
            return false;
        }
    }
    bool IsPerimeter(int x, int y) => (x >= 3 && x < Edge || x >= width - Edge && x < width - 3 || y >= 3 && y < Edge || y >= height - Edge && y < height - 3);
}
