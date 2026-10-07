namespace SkyEye.Core.Analysis;

/// <summary>Whole-playable-surface evidence, independent of recognized ball labels.
/// Fixed-anchor and adjacent differences use the same unregistered pixels: camera
/// compensation cannot erase motion. Thresholds require mounted-corpus validation.</summary>
public sealed class VerificationPixels(int width, int height)
{
    byte[]? anchor, previous;
    public VerificationMeasurement Measure(byte[] gray, bool geometryValid, VisionResult? vision)
    {
        if (width < 100 || height < 100 || gray.Length != width * height) throw new ArgumentException("Invalid verification pixels.");
        var perimeter = Enumerable.Range(0, gray.Length).Where(i => IsPerimeter(i % width, i / width)).ToArray();
        int textured = perimeter.Count(i => i % width > 0 && Math.Abs(gray[i] - gray[i - 1]) >= 8);
        bool covered = geometryValid && perimeter.Length > 0 && textured >= 40;
        if (!covered) return new(TableMotion.Unknown, ObservationReason.CalibrationChanged, false, 0, 0, 0, vision);
        if (anchor is null) { anchor = gray.ToArray(); previous = gray.ToArray(); return new(TableMotion.Unknown, ObservationReason.InsufficientEvidence, true, 0, 0, 0, vision); }
        double lighting = Math.Max(Math.Abs(gray.Average(b => (double)b) - anchor.Average(b => (double)b)), Math.Abs(gray.Average(b => (double)b) - previous!.Average(b => (double)b)));
        double railChange = Math.Max(perimeter.Count(i => Math.Abs(gray[i] - anchor[i]) > 12), perimeter.Count(i => Math.Abs(gray[i] - previous![i]) > 12)) / (double)perimeter.Length;
        double local = 0;
        const int tile = 16;
        for (int y = 12; y < height - 12; y += tile) for (int x = 12; x < width - 12; x += tile)
        {
            int changedAnchor = 0, changedPrevious = 0, count = 0;
            for (int py = y; py < Math.Min(y + tile, height - 12); py++) for (int px = x; px < Math.Min(x + tile, width - 12); px++)
            { int i = py * width + px; count++; if (Math.Abs(gray[i] - anchor[i]) > 12) changedAnchor++; if (Math.Abs(gray[i] - previous![i]) > 12) changedPrevious++; }
            local = Math.Max(local, Math.Max(changedAnchor, changedPrevious) / (double)count);
        }
        previous = gray.ToArray();
        var reason = vision?.Obstructed == true ? ObservationReason.Obstructed
            : lighting > 8 ? ObservationReason.LightingChanged : railChange > .08 ? ObservationReason.CalibrationChanged
            : local > .06 ? ObservationReason.Moving
            : local > .01 || railChange > .02 ? ObservationReason.InsufficientEvidence : ObservationReason.None;
        return new(reason == ObservationReason.None ? TableMotion.Still : reason == ObservationReason.Moving ? TableMotion.Moving : TableMotion.Unknown,
            reason, true, local, railChange, lighting, vision);
    }
    bool IsPerimeter(int x, int y) => (x is >= 3 and < 12 || x >= width - 12 && x < width - 3 || y is >= 3 and < 12 || y >= height - 12 && y < height - 3);
}
