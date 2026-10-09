using SkyEye.Core.Analysis;
using SkyEye.GameState;

namespace SkyEye.Core.Rules;

/// <summary>A stable detector change reconciled with the current canonical match.</summary>
public sealed record StableTableChangeOutcome(ActivityWindowContext Context,
    SettledTableEvidence After, SettledInventoryComparison Comparison, RuleRuling Ruling);

/// <summary>
/// Converts one ordered, three-sample table-watcher event into the same canonical
/// settled-outcome contract used by the bounded verifier and Scenario Lab.
/// </summary>
public static class StableTableChangeOutcomeInference
{
    public static bool TryCreate(GameSnapshot? state, StableTableChangeEvent? change,
        out StableTableChangeOutcome outcome)
    {
        outcome = null!;
        if (state?.Frame is not { Winner: null } frame || state.Match is not { Finished: false } match
            || change is null || change.StableClearSampleCount < StableTableChangeTracker.RequiredStableSamples
            || change.Source is null || !change.Before.IsComplete || !change.After.IsComplete
            || !SettledTableEvidence.TryCapture(state, out var canonicalBefore)
            || !FrozenGameContext.TryCapture(state, out _)
            || canonicalBefore.Source != change.Source || canonicalBefore.Counts != change.Before
            || canonicalBefore.SourceTimeUs is not { } beforeTime
            || change.AfterSourceTimeUs <= beforeTime
            || change.AfterSourceTimeUs <= 0
            || change.CapturedAt < canonicalBefore.CapturedAt)
            return false;

        var frameStart = match.Frames.FirstOrDefault(item => item.Id == frame.Id)?.StartedAt;
        if (frameStart is { } startedAt && change.CapturedAt < startedAt) return false;

        var beforeLayout = change.BeforeLayout.IsDefaultOrEmpty
            ? canonicalBefore.Layout : change.BeforeLayout;
        long? beforeLayoutTime = beforeLayout.IsDefaultOrEmpty
            ? canonicalBefore.LayoutSourceTimeUs
            : change.BeforeLayoutSourceTimeUs ?? change.BeforeSourceTimeUs;
        var afterLayout = change.AfterLayout.IsDefault ? [] : change.AfterLayout;
        long? afterLayoutTime = afterLayout.IsDefaultOrEmpty
            ? null : change.AfterLayoutSourceTimeUs ?? change.AfterSourceTimeUs;

        long activityFloor = Math.Max(beforeTime, beforeLayoutTime ?? -1);
        long activityStart = Math.Max(change.AfterLayoutSourceTimeUs ?? activityFloor + 1,
            activityFloor + 1);
        if (activityStart >= change.AfterSourceTimeUs) return false;
        if (!ActivityWindowContext.TryBegin(change.Id, state, activityStart, out var context)) return false;

        context = context with
        {
            Before = canonicalBefore with
            {
                Layout = beforeLayout,
                LayoutSourceTimeUs = beforeLayoutTime
            }
        };
        var after = new SettledTableEvidence(change.Id, change.Source, change.AfterSourceTimeUs,
            change.CapturedAt, change.After, Settled: true, Clear: true)
        {
            Layout = afterLayout,
            LayoutSourceTimeUs = afterLayoutTime,
            StableClearSampleCount = change.StableClearSampleCount
        };

        var comparisonResult = SettledStateReconciler.Compare(context, after);
        if (comparisonResult.Comparison is not { } comparison
            || !SettledOutcomeRuleInference.TryInfer(state, comparison, out var ruling)) return false;

        outcome = new(context, after, comparison, ruling);
        return true;
    }
}
