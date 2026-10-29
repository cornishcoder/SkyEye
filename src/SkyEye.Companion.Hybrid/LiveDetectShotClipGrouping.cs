namespace SkyEye.Companion.Hybrid;

internal sealed record LiveDetectEventClipCandidate(LiveDetectActivity Activity, string EventType,
    long EndSourceTimeUs, Guid[] TableEventIds)
{
    public long WindowStartSourceTimeUs => Math.Max(0,
        Activity.StartSourceTimeUs - LiveDetectShotClipWindowPolicy.LeadInSeconds * 1_000_000L);
    public long WindowEndSourceTimeUs => checked(EndSourceTimeUs
        + LiveDetectShotClipWindowPolicy.PostRollSeconds * 1_000_000L);
}

/// <summary>
/// Groups event clips whose padded source ranges overlap. Table events remain separate;
/// one saved range carries links to every contributing activity and event.
/// </summary>
internal sealed class LiveDetectEventClipGroup
{
    readonly List<LiveDetectEventClipCandidate> candidates = [];

    public LiveDetectEventClipGroup(LiveDetectEventClipCandidate first)
    {
        ArgumentNullException.ThrowIfNull(first);
        candidates.Add(first);
        WindowStartSourceTimeUs = first.WindowStartSourceTimeUs;
        WindowEndSourceTimeUs = first.WindowEndSourceTimeUs;
    }

    public string Timeline => candidates[0].Activity.Timeline;
    public long WindowStartSourceTimeUs { get; private set; }
    public long WindowEndSourceTimeUs { get; private set; }
    public long ActivityStartSourceTimeUs => candidates.Min(candidate => candidate.Activity.StartSourceTimeUs);
    public long EndSourceTimeUs => candidates.Max(candidate => candidate.EndSourceTimeUs);
    public LiveDetectActivity ClipActivity => candidates
        .MinBy(candidate => candidate.Activity.StartSourceTimeUs)!.Activity with
        { StartSourceTimeUs = ActivityStartSourceTimeUs };
    public string EventType => candidates.Count == 1 ? candidates[0].EventType : "shot_episode";
    public Guid[] ActivityIds => candidates.Select(candidate => candidate.Activity.Id).Distinct().ToArray();
    public Guid[] TableEventIds => candidates.SelectMany(candidate => candidate.TableEventIds).Distinct().ToArray();
    public string[] EventTypes => candidates.Select(candidate => candidate.EventType)
        .Distinct(StringComparer.Ordinal).ToArray();

    public bool ContainsActivity(Guid activityId) => candidates.Any(candidate => candidate.Activity.Id == activityId);

    public bool TryAdd(LiveDetectEventClipCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Activity.Timeline != Timeline || ContainsActivity(candidate.Activity.Id)) return false;

        long start = Math.Min(WindowStartSourceTimeUs, candidate.WindowStartSourceTimeUs);
        long end = Math.Max(WindowEndSourceTimeUs, candidate.WindowEndSourceTimeUs);
        // Half-open ranges that only touch do not repeat footage and should stay separate.
        if (candidate.WindowStartSourceTimeUs >= WindowEndSourceTimeUs
            || candidate.WindowEndSourceTimeUs <= WindowStartSourceTimeUs)
            return false;
        if ((end - start + 999_999L) / 1_000_000L > 600) return false;

        candidates.Add(candidate);
        WindowStartSourceTimeUs = start;
        WindowEndSourceTimeUs = end;
        return true;
    }
}

internal abstract record LiveDetectEventClipQueueMessage;
internal sealed record LiveDetectEventClipCandidateMessage(LiveDetectEventClipCandidate Candidate)
    : LiveDetectEventClipQueueMessage;
internal sealed record LiveDetectEventClipActivityBoundaryMessage(Guid ActivityId, string Timeline,
    long SourceTimeUs, bool Started) : LiveDetectEventClipQueueMessage;
