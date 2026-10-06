namespace SkyEye.Companion.Hybrid;

public sealed class FramingGeometryFence
{
    (int Width, int Height, string Configuration)? known;
    public bool Observe(int width, int height, string configuration)
    {
        if (width <= 0 || height <= 0) return false;
        var next = (width, height, configuration);
        bool changed = known is { } previous && previous != next;
        known = next; return changed;
    }
}
