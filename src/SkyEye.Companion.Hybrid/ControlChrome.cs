namespace SkyEye.Companion.Hybrid;

/// <summary>Transient controls, using an elapsed clock rather than wall time.</summary>
public sealed class ControlChrome(Func<TimeSpan> elapsed)
{
    TimeSpan interaction = elapsed();
    public bool Visible { get; private set; } = true;
    public bool Editing { get; private set; }
    public bool Attention { get; private set; }
    public bool Accessible { get; private set; }
    public bool Pinned { get; private set; } = true;
    public bool CanFade { get; set; }
    public void Reveal() { Visible = true; interaction = elapsed(); }
    public void SetPinned(bool value)
    {
        if (Pinned == value) return;
        Pinned = value;
        Reveal();
    }
    public void SetEditing(bool value) { Editing = value; Reveal(); }
    public void SetAttention(bool value) { if (Attention == value) return; Attention = value; Reveal(); }
    public void SetAccessibility(bool value) { if (Accessible == value) return; Accessible = value; Reveal(); }
    public bool Tick()
    {
        bool next = Pinned || !CanFade || Editing || Attention || Accessible || elapsed() - interaction < TimeSpan.FromSeconds(5);
        if (next == Visible) return false;
        Visible = next; return true;
    }
}
