namespace SkyEye.Core.Analysis;

/// <summary>Per-frame red/yellow membership built from the analyzer's raw OpenCV HSV plane.
/// Boundary scoring samples this byte mask by pixel index rather than repeating RGB-to-HSV
/// conversion for each candidate sector.</summary>
public sealed class BoundaryColourMask
{
    const byte Red = 1;
    const byte Yellow = 2;
    readonly byte[] pixels;

    BoundaryColourMask(byte[] pixels) => this.pixels = pixels;

    public static BoundaryColourMask FromHsvPixels(byte[] hsvPixels)
    {
        PipelineDiagnostics.Count("boundaryColourMaskBuilds");
        using var buildTiming = PipelineDiagnostics.Enabled ? PipelineDiagnostics.Measure("boundaryColourMaskBuildMs") : null;
        ArgumentNullException.ThrowIfNull(hsvPixels);
        if (hsvPixels.Length == 0 || hsvPixels.Length % 3 != 0)
            throw new ArgumentException("HSV pixels must contain complete H/S/V triplets.", nameof(hsvPixels));

        var pixels = new byte[hsvPixels.Length / 3];
        for (int source = 0, target = 0; source < hsvPixels.Length; source += 3, target++)
        {
            int hue = hsvPixels[source], saturation = hsvPixels[source + 1], value = hsvPixels[source + 2];
            if (saturation < 100 || value < 30) continue;
            if (hue <= 10 || hue >= 170) pixels[target] |= Red;
            if (hue is >= 10 and <= 40) pixels[target] |= Yellow;
        }
        return new(pixels);
    }

    public bool Matches(string kind, int bgrOffset)
    {
        if (bgrOffset < 0 || bgrOffset % 3 != 0) return false;
        int pixel = bgrOffset / 3;
        if ((uint)pixel >= (uint)pixels.Length) return false;
        byte flag = kind switch { "red" => Red, "yellow" => Yellow, _ => 0 };
        return flag != 0 && (pixels[pixel] & flag) != 0;
    }
}
