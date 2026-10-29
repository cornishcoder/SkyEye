using System.Diagnostics;
using SkyEye.Core;

namespace SkyEye.Companion.Hybrid;

internal readonly record struct LiveDetectShotClipWindow(Guid ActivityId, string Timeline,
    long StartSourceTimeUs, long EndSourceTimeUs, int SaveSeconds);

/// <summary>Maps a completed Live Detect activity to a bounded source-time clip range.</summary>
internal static class LiveDetectShotClipWindowPolicy
{
    public const int LeadInSeconds = 2;
    public const int PostRollSeconds = 2;
    public const int MaximumInterruptedActivitySeconds = 30;
    const int MaximumClipSeconds = 600;
    public static readonly TimeSpan PostRollWaitTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan PostRollWaitPollInterval = TimeSpan.FromMilliseconds(100);

    internal enum PostRollWaitStatus { Ready, TimelineChanged, SessionUnavailable, TimedOut }
    internal readonly record struct PostRollObservation(string? Timeline, long? SourceTimeUs, bool SessionAvailable);
    internal readonly record struct PostRollWaitResult(PostRollWaitStatus Status, PostRollObservation LastObservation);

    public static async Task<PostRollWaitResult> WaitForPostRollAsync(string timeline, long eventEndSourceTimeUs,
        Func<CancellationToken, Task<PostRollObservation>> readObservation,
        TimeSpan? timeout = null, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readObservation);
        long requiredSourceTimeUs = checked(eventEndSourceTimeUs + PostRollSeconds * 1_000_000L);
        var maximumWait = timeout ?? PostRollWaitTimeout;
        var interval = pollInterval ?? PostRollWaitPollInterval;
        if (maximumWait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        long startedAt = Stopwatch.GetTimestamp();
        PostRollObservation last = default;
        using var timeoutCancellation = new CancellationTokenSource(maximumWait);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutCancellation.Token);
        var waitCancellation = linkedCancellation.Token;
        while (true)
        {
            var remainingForRead = maximumWait - Stopwatch.GetElapsedTime(startedAt);
            if (remainingForRead <= TimeSpan.Zero)
            {
                timeoutCancellation.Cancel();
                return new(PostRollWaitStatus.TimedOut, last);
            }
            try
            {
                last = await readObservation(waitCancellation).WaitAsync(remainingForRead, waitCancellation)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                timeoutCancellation.Cancel();
                return new(PostRollWaitStatus.TimedOut, last);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                return new(PostRollWaitStatus.TimedOut, last);
            }
            if (!last.SessionAvailable) return new(PostRollWaitStatus.SessionUnavailable, last);
            if (last.Timeline != timeline) return new(PostRollWaitStatus.TimelineChanged, last);
            if (last.SourceTimeUs is { } sourceTimeUs && sourceTimeUs >= requiredSourceTimeUs)
                return new(PostRollWaitStatus.Ready, last);

            var remaining = maximumWait - Stopwatch.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                timeoutCancellation.Cancel();
                return new(PostRollWaitStatus.TimedOut, last);
            }
            try
            {
                await Task.Delay(remaining < interval ? remaining : interval, waitCancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                return new(PostRollWaitStatus.TimedOut, last);
            }
        }
    }

    public static bool TryCreate(Guid activityId, string timeline, long activityStartSourceTimeUs,
        long eventEndSourceTimeUs, long saveRequestSourceTimeUs, out LiveDetectShotClipWindow window)
    {
        window = default;
        if (activityId == Guid.Empty || !Guid.TryParseExact(timeline, "N", out _)
            || activityStartSourceTimeUs <= 0 || eventEndSourceTimeUs < activityStartSourceTimeUs)
            return false;

        long start = Math.Max(0, activityStartSourceTimeUs - LeadInSeconds * 1_000_000L);
        long end = checked(eventEndSourceTimeUs + PostRollSeconds * 1_000_000L);
        if (saveRequestSourceTimeUs < end) return false;
        long requestedSeconds = (end - start + 999_999) / 1_000_000;
        if (requestedSeconds is <= 0 or > MaximumClipSeconds) return false;

        window = new(activityId, timeline, start, end, (int)requestedSeconds);
        return true;
    }
}
