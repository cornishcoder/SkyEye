namespace SkyEye.Companion.Hybrid;

// Native-only media effect state. It contains private media addresses and must
// never be serialized into the agent projection or exposed to JavaScript.
public enum ViewingSource { None, Live, Preview }
public sealed class PlaybackSelection
{
    public long Generation { get; private set; }
    public ViewingSource Kind { get; private set; }
    public string? Source { get; private set; }
    public bool NeedsStart { get; private set; }
    public long Begin(ViewingSource kind)
    {
        ++Generation; Kind = kind; Source = null; NeedsStart = true; return Generation;
    }
    public void Stop() => Begin(ViewingSource.None);
    public void Suspend() => NeedsStart = true;
    public bool Observe(long generation, ViewingSource kind, string? source)
    {
        if (generation != Generation || Kind != kind || kind == ViewingSource.None) return false;
        bool changed = Source != source;
        Source = source;
        if (source is null) NeedsStart = true;
        return source is not null && (changed || NeedsStart);
    }
    public void Started() => NeedsStart = false;
}
