using System.Threading.Channels;

namespace SkyEye.Core;

/// <summary>Nonblocking fan-out of existing encoded video. Slow viewers reconnect at a new IDR;
/// they never delay recording or receive an ever older queue. No extra camera encoder.</summary>
public sealed class LiveTransportHub
{
    readonly object gate = new();
    readonly HashSet<Viewer> viewers = [];
    public const int MaximumViewers = 3, MaximumFrames = 4, MaximumFrameBytes = 1024 * 1024;
    long published, slowViewers, refused;
    public bool HasViewers { get { lock (gate) return viewers.Count > 0; } }
    public object Metrics { get { lock (gate) return new { published, slowViewers, refused, viewers = viewers.Count, maximumFrames = MaximumFrames }; } }
    public Viewer Subscribe()
    {
        lock (gate)
        {
            if (viewers.Count >= MaximumViewers) { refused++; throw new InvalidOperationException("Live phone viewer capacity reached. HLS remains available."); }
            var viewer = new Viewer(this); viewers.Add(viewer); return viewer;
        }
    }
    public void Publish(byte[] bytes, bool key)
    {
        if (bytes.Length is <= 0 or > MaximumFrameBytes) { Reset(); return; }
        lock (gate)
        {
            published++;
            foreach (var viewer in viewers.ToArray())
            {
                if (!viewer.Started && !key) continue;
                viewer.Started = true;
                if (!viewer.Queue.Writer.TryWrite(bytes))
                { slowViewers++; viewers.Remove(viewer); viewer.Close(new IOException("Live viewer fell behind. Reconnect to the latest keyframe.")); }
            }
        }
    }
    public void Reset()
    {
        lock (gate) { foreach (var viewer in viewers) viewer.Close(); viewers.Clear(); }
    }
    public sealed class Viewer : IDisposable
    {
        readonly LiveTransportHub owner;
        internal readonly Channel<byte[]> Queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaximumFrames)
        { SingleReader = false, SingleWriter = true, AllowSynchronousContinuations = false, FullMode = BoundedChannelFullMode.Wait });
        internal bool Started;
        internal Viewer(LiveTransportHub owner) => this.owner = owner;
        public ValueTask<byte[]> ReadAsync(CancellationToken cancellation) => Queue.Reader.ReadAsync(cancellation);
        internal void Close(Exception? error = null) { Queue.Writer.TryComplete(error); while (Queue.Reader.TryRead(out _)) { } }
        public void Dispose() { lock (owner.gate) { owner.viewers.Remove(this); Close(); } }
    }
}
