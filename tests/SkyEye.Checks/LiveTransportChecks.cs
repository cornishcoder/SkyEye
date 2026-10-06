using SkyEye.Core;
using System.Threading.Channels;

static class LiveTransportChecks
{
    public static async Task Run()
    {
        void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
        var hub = new LiveTransportHub();
        using var first = hub.Subscribe(); using var second = hub.Subscribe(); using var third = hub.Subscribe();
        try { using var refused = hub.Subscribe(); throw new Exception("Expected viewer capacity refusal"); } catch (InvalidOperationException) { }
        hub.Publish([1], false);
        var read = first.ReadAsync(CancellationToken.None).AsTask(); Check(!read.IsCompleted, "new subscriber never starts with a dependent frame");
        hub.Publish([2], true); Check((await read)[0] == 2, "new subscriber begins at independently decodable keyframe");
        for (int i = 0; i < LiveTransportHub.MaximumFrames + 2; i++) hub.Publish([(byte)(3 + i)], false);
        // Overflow disconnects rather than dropping arbitrary dependent AVC frames.
        bool closed = false;
        try { while (true) await first.ReadAsync(CancellationToken.None); } catch (ChannelClosedException) { closed = true; }
        Check(closed, "slow viewer is bounded and must reconnect");
        using var recovered = hub.Subscribe(); hub.Publish([9], false);
        var next = recovered.ReadAsync(CancellationToken.None).AsTask(); Check(!next.IsCompleted, "recovered viewer still waits for newest IDR");
        hub.Publish([10], true); Check((await next)[0] == 10, "reconnection discards historical backlog");
        hub.Reset();
        try { await recovered.ReadAsync(CancellationToken.None); throw new Exception("Expected reset EOF"); } catch (ChannelClosedException) { }
        Console.WriteLine("PASS live transport: keyframe admission, bounded viewer/frame capacity, nonblocking slow-viewer disconnect and timeline reset");
    }
}
