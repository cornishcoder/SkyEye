namespace SkyEye.Core.Application;

public sealed record DeliverySource(string CameraIdentity, string Timeline, string Geometry);
public sealed record DeliveryLease(long Generation, DeliverySource Source, bool Corrected, long? ReceiverLostAt);

/// <summary>One requested TV pipeline. Phone visibility/playback never owns this lease.</summary>
public sealed class BackgroundDeliveryLease(TimeProvider? clock = null)
{
    readonly TimeProvider time = clock ?? TimeProvider.System;
    long generation;
    public DeliveryLease? Current { get; private set; }
    public const int ReceiverGraceSeconds = 30;
    public DeliveryLease Request(DeliverySource source, bool corrected)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.CameraIdentity) || string.IsNullOrWhiteSpace(source.Timeline) || string.IsNullOrWhiteSpace(source.Geometry))
            throw new ArgumentException("A source and geometry identity are required.");
        if (Current is { } existing && existing.Source == source && existing.Corrected == corrected) return existing;
        return Current = new(checked(++generation), source, corrected, null);
    }
    public bool Matches(long expected, DeliverySource source) => Current is { } lease && lease.Generation == expected && lease.Source == source;
    public void ObserveReceiver(bool connected)
    {
        if (Current is not { } lease) return;
        if (connected) Current = lease with { ReceiverLostAt = null };
        else if (lease.ReceiverLostAt is null) Current = lease with { ReceiverLostAt = time.GetTimestamp() };
    }
    public bool ExpireReceiver()
    {
        if (Current?.ReceiverLostAt is not { } lost || time.GetElapsedTime(lost) < TimeSpan.FromSeconds(ReceiverGraceSeconds)) return false;
        Clear(); return true;
    }
    public void Clear() { ++generation; Current = null; }
}
