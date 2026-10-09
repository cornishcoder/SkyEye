namespace SkyEye.GameState;

/// <summary>The O03 states that gate a before/after table comparison.</summary>
public enum ActivitySettlementPhase
{
    Settled,
    Active,
    WaitingForClearTable,
    Settling,
    SettledCandidate,
    Uncertain
}

/// <summary>Already-classified signals supplied by bounded motion and observation components.</summary>
public enum ActivitySettlementSignalKind
{
    ActivityDetected,
    Obstructed,
    ClearViewAvailable,
    MotionQuiet,
    VerifiedSettled,
    SourceDiscontinuity,
    ContextDiscontinuity
}

public sealed record ActivitySettlementSignal(ActivitySettlementSignalKind Kind, EvidenceSource Source,
    long SourceTimeUs, SettledTableEvidence? SettledEvidence = null);

public enum ActivitySettlementOutcome { Applied, Ignored, SettledCandidate, Uncertain }

public sealed record ActivitySettlementStep(ActivitySettlementLifecycle Lifecycle,
    ActivitySettlementOutcome Outcome, string Reason, SettledComparisonResult? Comparison = null);

/// <summary>
/// Pure O03 lifecycle. It holds the frozen before-table through obstruction without a timeout,
/// and only publishes a candidate when O08 supplies a clear, settled observation.
/// </summary>
public sealed record ActivitySettlementLifecycle(ActivityWindowContext Context,
    ActivitySettlementPhase Phase, long LastSourceTimeUs)
{
    public static bool TryStart(ActivityWindowContext? context, out ActivitySettlementLifecycle lifecycle)
    {
        lifecycle = null!;
        if (context is null || !context.IsValid) return false;
        lifecycle = new(context, ActivitySettlementPhase.Settled, context.Before.SourceTimeUs!.Value);
        return true;
    }

    public ActivitySettlementStep Observe(ActivitySettlementSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (Phase is ActivitySettlementPhase.Uncertain or ActivitySettlementPhase.SettledCandidate)
            return new(this, ActivitySettlementOutcome.Ignored,
                Phase == ActivitySettlementPhase.Uncertain
                    ? "This activity window is uncertain; establish a new trusted baseline."
                    : "This activity window already has a settled candidate awaiting review.");

        if (signal.Kind == ActivitySettlementSignalKind.SourceDiscontinuity)
            return MakeUncertain("The camera source, timeline, generation, connection or calibration changed.");
        if (signal.Kind == ActivitySettlementSignalKind.ContextDiscontinuity)
            return MakeUncertain("The match, frame, player, visit, groups or rules changed while the activity window was open.");
        if (signal.Source is null || signal.Source != Context.Before.Source)
            return MakeUncertain("The activity signal does not belong to the frozen before-table source.");
        if (signal.SourceTimeUs < 0 || signal.SourceTimeUs <= LastSourceTimeUs)
            return MakeUncertain("Activity source timestamps must increase strictly within one window.");

        var next = this with { LastSourceTimeUs = signal.SourceTimeUs };
        switch (signal.Kind)
        {
            case ActivitySettlementSignalKind.ActivityDetected:
                if (Context.ActivityStartSourceTimeUs is null)
                    next = next with { Context = Context with { ActivityStartSourceTimeUs = signal.SourceTimeUs } };
                return new(next with { Phase = ActivitySettlementPhase.Active }, ActivitySettlementOutcome.Applied,
                    "Activity detected; retain the frozen before-table and game context.");

            case ActivitySettlementSignalKind.Obstructed:
                return new(next with { Phase = ActivitySettlementPhase.WaitingForClearTable }, ActivitySettlementOutcome.Applied,
                    Phase == ActivitySettlementPhase.Settled
                        ? "Table obstructed before activity was observed; retain the settled before-table until a clear view returns. Obstruction duration does not expire it."
                        : "Table obstructed; retain the original before-table until a clear view returns. Obstruction duration does not expire it.");

            case ActivitySettlementSignalKind.ClearViewAvailable:
                if (Phase != ActivitySettlementPhase.WaitingForClearTable)
                    return new(next, ActivitySettlementOutcome.Ignored, "A clear-view transition is only needed after obstruction.");
                return new(next with { Phase = ActivitySettlementPhase.Settling }, ActivitySettlementOutcome.Applied,
                    "Clear view returned; wait for bounded stillness verification.");

            case ActivitySettlementSignalKind.MotionQuiet:
                if (Phase is not (ActivitySettlementPhase.Active or ActivitySettlementPhase.Settling))
                    return new(next, ActivitySettlementOutcome.Ignored, "Quiet motion alone cannot establish a settled table.");
                return new(next with { Phase = ActivitySettlementPhase.Settling }, ActivitySettlementOutcome.Applied,
                    "Motion is quiet; wait for O08 clear/still verification.");

            case ActivitySettlementSignalKind.VerifiedSettled:
                if (signal.SettledEvidence is null)
                    return MakeUncertain("A settled transition requires O08 table evidence.");
                if (signal.SettledEvidence.Source != signal.Source
                    || signal.SettledEvidence.SourceTimeUs != signal.SourceTimeUs)
                    return MakeUncertain("Settled evidence does not match the activity signal source timestamp.");

                var comparison = SettledStateReconciler.Compare(Context, signal.SettledEvidence);
                if (!comparison.Ready)
                {
                    var uncertain = comparison.Status is SettledComparisonStatus.SourceDiscontinuity
                        or SettledComparisonStatus.NonMonotonicSourceTime or SettledComparisonStatus.InvalidContext;
                    if (uncertain) return MakeUncertain(comparison.Reason, comparison);
                    return new(next with { Phase = ActivitySettlementPhase.WaitingForClearTable },
                        ActivitySettlementOutcome.Applied, comparison.Reason, comparison);
                }

                return new(next with { Phase = ActivitySettlementPhase.SettledCandidate },
                    ActivitySettlementOutcome.SettledCandidate,
                    Context.ActivityStartSourceTimeUs is null
                        ? "A clear settled after-table is ready for review; activity onset is unknown."
                        : "A clear settled after-table is ready for review.", comparison);

            default:
                return MakeUncertain("The activity signal kind is not supported.");
        }

        ActivitySettlementStep MakeUncertain(string reason, SettledComparisonResult? comparison = null)
            => new(this with { Phase = ActivitySettlementPhase.Uncertain }, ActivitySettlementOutcome.Uncertain,
                reason, comparison);
    }
}
