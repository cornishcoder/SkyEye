using SkyEye.Core.Application;

static class BackgroundDeliveryChecks
{
    public static void Run()
    {
        void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
        var time = new Clock(); var owner = new BackgroundDeliveryLease(time);
        var source = new DeliverySource("camera", "timeline", "geometry");
        var first = owner.Request(source, true);
        Check(owner.Request(source, true) == first, "background/local phone transitions do not recreate a requested TV lease");
        var second = owner.Request(source with { Timeline = "new" }, true);
        Check(!owner.Matches(first.Generation, source) && second.Generation > first.Generation, "source replacement fences late publication");
        owner.ObserveReceiver(false); time.Advance(29); Check(!owner.ExpireReceiver(), "short receiver loss retains bounded reconnect grace");
        owner.ObserveReceiver(true); time.Advance(10); Check(!owner.ExpireReceiver(), "observed receiver return cancels expiry");
        owner.ObserveReceiver(false); time.Advance(30); Check(owner.ExpireReceiver() && owner.Current is null, "receiver grace expires and releases work");
        var clear = owner.Request(source, false); owner.Clear(); Check(!owner.Matches(clear.Generation, source), "explicit clear fences late completion");
        Console.WriteLine("PASS background delivery ownership: one pipeline, phone-independent lease, source/clear fencing and receiver-loss grace");
    }
    sealed class Clock : TimeProvider
    {
        long now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => now;
        public void Advance(int seconds) => now += seconds * 1000L;
    }
}
