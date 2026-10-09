using System.Collections.Immutable;
using SkyEye.Core.Analysis;
using SkyEye.Core;
using SkyEye.Core.Application;
using SkyEye.Core.Rules;
using SkyEye.Companion;
using SkyEye.GameState;
using SkyEye.Vision.Android;

namespace SkyEye.Companion.Hybrid;

public sealed record SettledTableCandidateDisplay(Guid ActivityWindowId, EvidenceSource Source,
    long SourceTimeUs, DateTimeOffset CapturedAt, SettledTableCounts Before, SettledTableCounts After,
    ImmutableArray<BallCountChange> Changes, string Summary)
{
    public SettledOutcomeRecommendation? Recommendation { get; init; }
    public SettledOutcomeRulesAdvice? RulesAdvice { get; init; }
}

public sealed record MotionActivityDisplay(string Phase, string Status, long? ActivityStartSourceTimeUs,
    long? BeforeSourceTimeUs, bool VerificationPending, bool VerificationAttempted,
    SettledTableCandidateDisplay? Candidate);

public sealed partial class ShellSession
{
    TableMotionFeed? motion;
    RoiMotionProcessor? motionProcessor;
    MotionFeedSnapshot? lastMotion;
    string motionSeedSource = "unknown";
    long? motionSeedFrameId;
    double? motionSeedFrameAgeMs;
    readonly SemaphoreSlim motionGate = new(1, 1);
    readonly object motionActivityGate = new();
    ActivitySettlementLifecycle? motionActivityLifecycle;
    string motionActivityStatus = "Waiting for a trusted settled match baseline before monitoring count changes.";
    long? motionActivityLastSourceTimeUs;
    bool motionActivityVerificationPending, motionActivityVerificationAttempted;
    SettledTableCandidateDisplay? motionActivityCandidate;
    Guid motionActivityDiagnosticSessionId;
    int motionActivityLastDiagnosticSequence;
    EvidenceSource? motionActivityPendingSource;
    SettledTableCounts? motionActivityPendingCounts;
    int motionActivityPendingStableSamples;
    long? motionActivityPendingFirstChangedSourceTimeUs;
    ActivityWindowContext? motionActivityCandidateContext;
    SettledTableEvidence? motionActivityCandidateAfter;

    public MotionActivityDisplay MotionActivityMetrics
    {
        get
        {
            // A retired feed may carry an older source timestamp; only a live
            // feed is allowed to advance the activity lifecycle.
            var snapshot = motion?.Snapshot;
            Guid? verify = null;
            MotionActivityDisplay display;
            lock (motionActivityGate)
            {
                verify = AdvanceMotionActivity(snapshot);
                var lifecycle = motionActivityLifecycle;
                display = new(lifecycle?.Phase.ToString() ?? "Unavailable", motionActivityStatus,
                    lifecycle?.Context.ActivityStartSourceTimeUs, lifecycle?.Context.Before.SourceTimeUs,
                    motionActivityVerificationPending, motionActivityVerificationAttempted, motionActivityCandidate);
            }
            if (verify is { } activityId)
                _ = MainThread.InvokeOnMainThreadAsync(() => VerifySettledActivityAsync(activityId));
            return display;
        }
    }

    public MotionFeedSnapshot? MotionMetrics
    {
        get
        {
            var snapshot = motion?.Snapshot ?? lastMotion;
            return snapshot is null ? null : snapshot with
            { SeedSource = motionSeedSource, SeedFrameId = motionSeedFrameId, SeedFrameAgeMs = motionSeedFrameAgeMs };
        }
    }

    // Explicit developer/diagnostic enablement. Normal startup never starts a feed.
    // O03/O04 will supply the product observation-session behavior and ROI recentering.
    public Task SetMotionFeedAsync(bool enabled, int hz = 30, bool allowUnconfirmedDiagnosticSeed = false,
        bool preserveActivityLifecycle = false) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        await motionGate.WaitAsync();
        try
        {
            await CloseMotionAsync(); if (!enabled) return;
            if (hz is not (15 or 30 or 60)) throw new ArgumentException("Unsupported observation cadence.");
            var seed = table.State; var owner = player;
            if (owner is not null) MediaGeneration();
            if (!active || owner is null || EditingTable || !CanAnalyzeTable || seed.Source is null || seed.Calibration is null)
                throw new InvalidOperationException("Open the active calibrated live table before observing motion.");
            if (seed.Table is not { } layout || layout.Occluded && !preserveActivityLifecycle || layout.Balls.Length is < 1 or > 16)
                throw new InvalidOperationException("A clear live ball candidate is required before observing motion.");
            bool confirmedSeed = layout is { Confirmed: true, Settled: true };
            if (!confirmedSeed && !allowUnconfirmedDiagnosticSeed)
                throw new InvalidOperationException("Confirm a clear, settled live layout before observing motion.");
            Ball[] roiBalls = layout.Balls;
            var layoutAgeMs = (DateTimeOffset.UtcNow - layout.CapturedAt).TotalMilliseconds;
            long? seedFrameId = seed.Observation?.DecodedReceipt?.FrameId;
            string seedSource = preserveActivityLifecycle ? "held-settled-baseline" : confirmedSeed ? "confirmed-table-layout" : "fresh-unconfirmed-clear-diagnostic-candidate";
            VisionDiagnosticSample? diagnosticSeed = null;
            if (!confirmedSeed && !preserveActivityLifecycle && (!double.IsFinite(layoutAgeMs) || layoutAgeMs is < -200 or > 2000))
                throw new InvalidOperationException("The diagnostic seed must come from a fresh clear detector view.");
            if (confirmedSeed && (!double.IsFinite(layoutAgeMs) || layoutAgeMs is < -200 or > ObservationPolicy.ReviewFreshnessSeconds * 1000))
            {
                var diagnostic = table.VisionDiagnostics;
                diagnosticSeed = diagnostic.Active ? diagnostic.LiveFrame?.Sample : null;
                var detectorAgeMs = diagnosticSeed is null ? double.PositiveInfinity : (DateTimeOffset.UtcNow - diagnosticSeed.CapturedAt).TotalMilliseconds;
                bool sameScope = diagnosticSeed?.Source is { } evidence
                    && evidence.Camera == seed.CameraIdentity
                    && evidence.Calibration == seed.CalibrationId
                    && evidence.Connection == seed.Source.ConnectionId.ToString("N");
                bool matchesConfirmed = diagnosticSeed is { } sample
                    && !sample.Detection.Uncertain && !sample.Detection.Obstructed
                    && sample.Detection.Balls.Length is > 0 and <= 16
                    && TableTracker.SameLayout(layout.Balls, sample.Detection.Balls);
                if (!sameScope || !double.IsFinite(detectorAgeMs) || detectorAgeMs is < -200 or > ObservationPolicy.ReviewFreshnessSeconds * 1000 || !matchesConfirmed)
                    throw new InvalidOperationException("The confirmed layout is stale and the latest clear detector sample does not match it. Confirm the current balls again.");
                roiBalls = diagnosticSeed!.Detection.Balls;
                seedFrameId = diagnosticSeed.FrameId;
                layoutAgeMs = detectorAgeMs;
                seedSource = "fresh-detector-matched-to-confirmed-layout";
            }
            var frames = new PlayerTableFrameSource(() => player, () => app.State.Camera,
                () => active && sessionMode == TableSessionMode.Live && selection.Kind == ViewingSource.Live,
                decodedReadbackScale: 2);
            using (var pixels = await frames.ReadAsync(seed.Source, foreground!.Token))
            {
                var receipt = pixels.DecodedReceipt ?? throw new InvalidOperationException("A paired live frame is required.");
                if (diagnosticSeed is { } sample && (sample.MediaGeneration != receipt.MediaGeneration || sample.Source?.Timeline != pixels.Timeline
                    || (DateTimeOffset.UtcNow - sample.CapturedAt).TotalMilliseconds is < -200 or > ObservationPolicy.ReviewFreshnessSeconds * 1000))
                    throw new InvalidOperationException("The matched detector sample no longer belongs to the current live video frame.");
                var image = pixels.Image as BitmapTableImage ?? throw new InvalidOperationException("A decoded motion image is required.");
                int imageWidth = pixels.ImageWidth, imageHeight = pixels.ImageHeight;
                if (imageWidth != image.Bitmap.Width || imageHeight != image.Bitmap.Height)
                    throw new InvalidDataException("Motion readback dimensions do not match the decoded image.");
                double sourcePixelsPerImagePixelX = receipt.Width / (double)imageWidth;
                double sourcePixelsPerImagePixelY = receipt.Height / (double)imageHeight;
                var rois = roiBalls.Select((ball, index) =>
                {
                    var point = seed.Calibration.Unmap(ball.Position);
                    var radiusXPoint = seed.Calibration.Unmap(ball.Position + new SkyEye.Core.Point(PoolGeometry.Radius, 0));
                    var radiusYPoint = seed.Calibration.Unmap(ball.Position + new SkyEye.Core.Point(0, PoolGeometry.Radius));
                    int sourceSize = Math.Min(192, Math.Min(receipt.Width, receipt.Height));
                    int width = Math.Clamp((int)Math.Round(sourceSize / sourcePixelsPerImagePixelX), 32, 256);
                    int height = Math.Clamp((int)Math.Round(sourceSize / sourcePixelsPerImagePixelY), 32, 256);
                    int roiX = Math.Clamp((int)Math.Round(point.X * imageWidth) - width / 2, 0, imageWidth - width);
                    int roiY = Math.Clamp((int)Math.Round(point.Y * imageHeight) - height / 2, 0, imageHeight - height);
                    int radiusPixelsX = (int)Math.Round(Math.Abs(radiusXPoint.X - point.X) * imageWidth);
                    int radiusPixelsY = (int)Math.Round(Math.Abs(radiusYPoint.Y - point.Y) * imageHeight);
                    int templateRadius = Math.Clamp((int)Math.Round((radiusPixelsX + radiusPixelsY) / 2d), 4, 60);
                    return new PixelRoi("region-" + index, roiX, roiY, width, height)
                    {
                        TargetX = (int)Math.Round(point.X * imageWidth) - roiX,
                        TargetY = (int)Math.Round(point.Y * imageHeight) - roiY,
                        TemplateRadius = templateRadius
                    };
                }).ToArray();
                var scope = new MotionFeedScope(seed.Source, receipt.MediaGeneration, seed.CalibrationId, pixels.Timeline);
                bool preserveLifecycle = preserveActivityLifecycle && CanPreserveMotionActivityLifecycle(seed, scope);
                if (preserveActivityLifecycle && !preserveLifecycle)
                    throw new InvalidOperationException("The held activity baseline no longer matches this live camera source.");
                var processor = new RoiMotionProcessor(rois, sourcePixelsPerImagePixelX, sourcePixelsPerImagePixelY);
                bool Current()
                {
                    var snapshot = table.State;
                    return active && sessionMode == TableSessionMode.Live && !EditingTable && selection.Kind == ViewingSource.Live && player == owner && app.State.Camera.CanControl
                        && app.State.Camera.Scope == scope.Source && snapshot.Generation == seed.Generation && snapshot.CalibrationId == scope.CalibrationId
                        && Interlocked.Read(ref mediaGeneration) == scope.MediaGeneration;
                }
                motionProcessor = processor;
                motionSeedSource = seedSource;
                motionSeedFrameId = seedFrameId;
                motionSeedFrameAgeMs = layoutAgeMs;
                motion = new(frames, processor, scope, Current, new(hz), physicallyObstructed: () => table.PhysicallyObstructed);
                if (!preserveLifecycle) StartMotionActivityLifecycle(seed, scope);
            }
            // Startup's seed lease is gone before producer/worker ownership begins.
            motion.Start(); _ = ReleaseWhenEnded(motion);
        }
        finally { motionGate.Release(); }
    });
    void StopMotion() { motion?.Stop("Viewing/source session ended"); }
    async Task ReleaseWhenEnded(TableMotionFeed feed)
    {
        await feed.Completion;
        await motionGate.WaitAsync();
        try { if (ReferenceEquals(motion, feed)) await CloseMotionAsync(); }
        finally { motionGate.Release(); }
    }
    async Task CloseMotionAsync()
    {
        var previous = motion; var processor = motionProcessor; motion = null; motionProcessor = null;
        if (previous is not null) { await previous.DisposeAsync(); lastMotion = previous.Snapshot; }
        processor?.Dispose();
    }

    void StartMotionActivityLifecycle(TableAnalysisSnapshot seed, MotionFeedScope scope)
    {
        lock (motionActivityGate)
        {
            motionActivityLifecycle = null;
            motionActivityLastSourceTimeUs = null;
            motionActivityVerificationPending = false;
            motionActivityVerificationAttempted = false;
            motionActivityCandidate = null;
            motionActivityCandidateContext = null;
            motionActivityCandidateAfter = null;
            motionActivityStatus = "Waiting for a trusted settled match baseline before monitoring count changes.";
            ResetSettledDifferenceCandidate();

            var game = app.CaptureGameSession().State;
            if (game.Match is null)
            {
                motionActivityStatus = "No active match; motion remains diagnostic-only.";
                return;
            }
            if (!SettledTableEvidence.TryCapture(game, out var before))
            {
                motionActivityStatus = "No trusted settled match table; motion remains diagnostic-only.";
                return;
            }
            if (!FrozenGameContext.TryCapture(game, out var frozenGame))
            {
                motionActivityStatus = "Match context is incomplete; motion remains diagnostic-only.";
                return;
            }
            if (string.IsNullOrWhiteSpace(seed.CameraIdentity))
            {
                motionActivityStatus = "Camera identity is unavailable; motion remains diagnostic-only.";
                return;
            }

            var expectedSource = new EvidenceSource(seed.CameraIdentity, scope.Timeline, seed.Generation + 1,
                scope.CalibrationId, scope.Source.ConnectionId.ToString("N"));
            if (before.Source != expectedSource)
            {
                motionActivityStatus = "The settled match table belongs to a different camera observation; motion remains diagnostic-only.";
                return;
            }

            var context = new ActivityWindowContext(Guid.NewGuid(), frozenGame, before, null);
            if (!ActivitySettlementLifecycle.TryStart(context, out var lifecycle))
            {
                motionActivityStatus = "The settled match baseline could not start an activity window.";
                return;
            }
            motionActivityLifecycle = lifecycle;
            motionActivityLastSourceTimeUs = before.SourceTimeUs;
            motionActivityStatus = "Watching for a settled table difference or obstruction; baseline is held.";
        }
    }

    Guid? ObserveSettledDifferenceSamples(VisionDiagnosticsSnapshot diagnostics)
    {
        var sample = diagnostics.LiveFrame?.Sample;
        if (sample is null) return null;

        lock (motionActivityGate)
        {
            if (motionActivityDiagnosticSessionId != diagnostics.SessionId)
            {
                motionActivityDiagnosticSessionId = diagnostics.SessionId;
                motionActivityLastDiagnosticSequence = 0;
                ResetSettledDifferenceCandidate();
            }
            if (sample.Sequence <= motionActivityLastDiagnosticSequence) return null;
            bool consecutive = motionActivityLastDiagnosticSequence == 0
                || sample.Sequence == motionActivityLastDiagnosticSequence + 1;
            motionActivityLastDiagnosticSequence = sample.Sequence;

            if (motionActivityVerificationPending || motionActivityCandidate is not null
                || motionActivityLifecycle?.Phase is ActivitySettlementPhase.SettledCandidate or ActivitySettlementPhase.Uncertain)
                return null;

            var detection = sample.Detection;
            var ageMs = (DateTimeOffset.UtcNow - sample.CapturedAt).TotalMilliseconds;
            var counts = !detection.Uncertain && !detection.Obstructed
                && double.IsFinite(ageMs) && ageMs is >= -200 and <= ObservationPolicy.ReviewFreshnessSeconds * 1000
                ? CountRecognizedBalls(detection.Balls) : null;
            if (counts is null || !ValidRecognizedCounts(counts))
            {
                ResetSettledDifferenceCandidate();
                motionActivityStatus = "Waiting for fresh, clear, fully classified samples before comparing the table.";
                return null;
            }

            var canonical = app.CaptureGameSession();
            var game = canonical.State;
            if (!SettledTableEvidence.TryCapture(game, out var before)
                || !FrozenGameContext.TryCapture(game, out _))
            {
                ResetSettledDifferenceCandidate();
                motionActivityStatus = "Waiting for a trusted settled match baseline before comparing table counts.";
                return null;
            }
            if (sample.Source != before.Source || sample.SourceTimeUs <= before.SourceTimeUs)
            {
                ResetSettledDifferenceCandidate();
                motionActivityStatus = "The settled baseline belongs to another camera/calibration view. A fresh trusted baseline is required.";
                return null;
            }
            if (counts == before.Counts)
            {
                ResetSettledDifferenceCandidate();
                if (motionActivityLifecycle is null or { Phase: ActivitySettlementPhase.Settled })
                    motionActivityStatus = "Watching the trusted settled layout for a repeated count change.";
                return null;
            }

            var suppressed = GameRevisions.Suppressed(canonical.History);
            if (canonical.History.Any(entry => !suppressed.Contains(entry.InputId)
                && entry.Event is SettledInventoryRejectedEvent rejected
                && rejected.Context.Before.EvidenceId == before.EvidenceId
                && rejected.After.Source == sample.Source && rejected.After.Counts == counts))
            {
                ResetSettledDifferenceCandidate();
                motionActivityStatus = "This same count change was rejected; waiting for a different settled table state.";
                return null;
            }
            if (canonical.History.Any(entry => !suppressed.Contains(entry.InputId)
                && entry.InputId == before.EvidenceId && entry.Source == sample.Source
                && entry.Event is SettledInventoryCorrectedEvent { Context: not null, Observed: not null } corrected
                && corrected.Observed.Counts == counts && corrected.Counts != counts))
            {
                ResetSettledDifferenceCandidate();
                motionActivityStatus = "The same detector result was corrected; waiting for a different settled table state.";
                return null;
            }

            bool samePending = consecutive && motionActivityPendingSource == sample.Source
                && motionActivityPendingCounts == counts
                && motionActivityPendingFirstChangedSourceTimeUs is { } firstChanged
                && sample.SourceTimeUs > firstChanged;
            if (!samePending)
            {
                motionActivityPendingSource = sample.Source;
                motionActivityPendingCounts = counts;
                motionActivityPendingStableSamples = 1;
                motionActivityPendingFirstChangedSourceTimeUs = sample.SourceTimeUs;
            }
            else motionActivityPendingStableSamples++;

            if (motionActivityPendingStableSamples < 3)
            {
                motionActivityStatus = $"Settled count difference found; checking the same clear result across {motionActivityPendingStableSamples}/3 fresh samples.";
                return null;
            }

            if (motionActivityPendingFirstChangedSourceTimeUs is not { } activityStart
                || !ActivityWindowContext.TryBegin(Guid.NewGuid(), game, activityStart, out var context)
                || context.Before.Source != sample.Source
                || !ActivitySettlementLifecycle.TryStart(context, out var lifecycle))
            {
                ResetSettledDifferenceCandidate();
                motionActivityStatus = "The count change was stable, but the match/source context no longer matches its settled baseline.";
                return null;
            }

            var step = lifecycle.Observe(new(ActivitySettlementSignalKind.SettledDifferenceObserved,
                sample.Source!, sample.SourceTimeUs));
            motionActivityLifecycle = step.Lifecycle;
            motionActivityLastSourceTimeUs = sample.SourceTimeUs;
            motionActivityStatus = step.Reason;
            ResetSettledDifferenceCandidate();
            if (step.Lifecycle.Phase != ActivitySettlementPhase.Settling)
                return null;

            motionActivityVerificationPending = true;
            motionActivityVerificationAttempted = true;
            motionActivityStatus = "Three fresh clear samples agree on a table count change; starting bounded O08 stillness verification.";
            return context.Id;
        }

    }

    void ResetSettledDifferenceCandidate()
    {
        motionActivityPendingSource = null;
        motionActivityPendingCounts = null;
        motionActivityPendingStableSamples = 0;
        motionActivityPendingFirstChangedSourceTimeUs = null;
    }

    static bool ValidRecognizedCounts(SettledTableCounts counts)
        => counts.Reds is >= 0 and <= 7 && counts.Yellows is >= 0 and <= 7
            && counts.CueBalls is 0 or 1 && counts.BlackBalls is 0 or 1
            && counts.Reds + counts.Yellows + counts.CueBalls + counts.BlackBalls is > 0 and <= 16;

    bool CanPreserveMotionActivityLifecycle(TableAnalysisSnapshot seed, MotionFeedScope scope)
    {
        lock (motionActivityGate)
        {
            var source = seed.CameraIdentity is null ? null : new EvidenceSource(seed.CameraIdentity,
                scope.Timeline, seed.Generation + 1, scope.CalibrationId, scope.Source.ConnectionId.ToString("N"));
            return motionActivityLifecycle is { Phase: not (ActivitySettlementPhase.Uncertain or ActivitySettlementPhase.SettledCandidate) } lifecycle
                && source == lifecycle.Context.Before.Source;
        }
    }

    Guid? AdvanceMotionActivity(MotionFeedSnapshot? snapshot)
    {
        if (motionActivityLifecycle is not { } lifecycle || snapshot?.Latest is not { } latest
            || motionActivityLastSourceTimeUs == latest.SourceTimeUs) return null;

        var kind = snapshot.Analysis.Phase switch
        {
            MotionAnalysisPhase.Moving => ActivitySettlementSignalKind.ActivityDetected,
            MotionAnalysisPhase.Obstructed => ActivitySettlementSignalKind.Obstructed,
            MotionAnalysisPhase.Settling or MotionAnalysisPhase.Settled
                when lifecycle.Phase == ActivitySettlementPhase.WaitingForClearTable
                    => ActivitySettlementSignalKind.ClearViewAvailable,
            MotionAnalysisPhase.Settling or MotionAnalysisPhase.Settled => ActivitySettlementSignalKind.MotionQuiet,
            _ => (ActivitySettlementSignalKind?)null
        };
        if (kind is null) return null;

        var step = lifecycle.Observe(new(kind.Value, lifecycle.Context.Before.Source, latest.SourceTimeUs));
        motionActivityLifecycle = step.Lifecycle;
        motionActivityLastSourceTimeUs = latest.SourceTimeUs;
        motionActivityStatus = step.Reason;
        if (kind == ActivitySettlementSignalKind.ActivityDetected && !motionActivityVerificationPending)
            motionActivityVerificationAttempted = false;

        if (step.Lifecycle.Phase == ActivitySettlementPhase.Settling
            && snapshot.Analysis.Phase == MotionAnalysisPhase.Settled
            && !motionActivityVerificationPending && !motionActivityVerificationAttempted)
        {
            motionActivityVerificationPending = true;
            motionActivityVerificationAttempted = true;
            motionActivityStatus = "Motion is quiet; starting bounded clear-table and stillness verification.";
            return step.Lifecycle.Context.Id;
        }
        return null;
    }

    async Task VerifySettledActivityAsync(Guid activityId)
    {
        bool resumeMotion = false, resumeDiagnostics = false, candidateReady = false, outcomeAccepted = false;
        int motionHz = 30;
        long media = 0;
        TableAnalysisSnapshot admitted = table.State;
        CancellationToken cancellation = CancellationToken.None;
        try
        {
            if (!active || foreground is null || !CanAnalyzeTable || EditingTable)
                throw new InvalidOperationException("The calibrated live table closed before verification could start.");

            lock (motionActivityGate)
            {
                if (motionActivityLifecycle is not { } lifecycle || lifecycle.Context.Id != activityId
                    || lifecycle.Phase != ActivitySettlementPhase.Settling || !motionActivityVerificationPending)
                    return;
            }

            cancellation = foreground.Token;
            media = MediaGeneration();
            admitted = table.State;
            resumeMotion = motion?.Snapshot is { StopReason: null };
            var priorMotion = MotionMetrics;
            motionHz = priorMotion?.RequestedHz is 15 or 30 or 60 ? priorMotion.RequestedHz : 30;
            bool dashboardActive = VisionDashboardActive;
            if (dashboardActive) visionDashboardSamplingPaused = true;
            resumeDiagnostics = dashboardActive && VisionDiagnosticsActive;

            if (VisionDiagnosticsActive) await table.StopVisionDiagnosticsAsync();
            if (resumeMotion) await SetMotionFeedAsync(false);
            cancellation.ThrowIfCancellationRequested();

            if (!IsCurrentVerificationSource(admitted, media))
            {
                MarkActivityUncertain(activityId, ActivitySettlementSignalKind.SourceDiscontinuity,
                    "The camera source or calibration changed before O08 verification began.");
                return;
            }

            Guid operationId = Guid.NewGuid();
            await table.VerifyAsync(operationId, media, cancellation).WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();

            var verified = table.State;
            if (!IsCurrentVerificationSource(admitted, media) || verified.Generation != admitted.Generation
                || verified.Source != admitted.Source || verified.CalibrationId != admitted.CalibrationId)
            {
                MarkActivityUncertain(activityId, ActivitySettlementSignalKind.SourceDiscontinuity,
                    "The camera source or calibration changed during O08 verification.");
                return;
            }

            var observation = verified.Observation;
            var layout = verified.Table;
            if (observation is null || layout is null || verified.CameraIdentity is null
                || observation.SourceTimeUs is not { } sourceTime)
            {
                SetActivityVerificationStatus(activityId, "O08 did not return a complete after-table. The original baseline is retained.");
                return;
            }

            ActivityWindowContext? context;
            lock (motionActivityGate) context = motionActivityLifecycle?.Context.Id == activityId ? motionActivityLifecycle.Context : null;
            if (context is null) return;
            if (!context.Game.IsCompatibleWith(app.CaptureGameSession().State))
            {
                MarkActivityUncertain(activityId, ActivitySettlementSignalKind.ContextDiscontinuity,
                    "The match context changed during verification. Review the game state before another comparison.");
                return;
            }

            var source = new EvidenceSource(verified.CameraIdentity, observation.Timeline,
                verified.Generation + 1, verified.CalibrationId, observation.Source.ConnectionId.ToString("N"));
            if (source != context.Before.Source)
            {
                MarkActivityUncertain(activityId, ActivitySettlementSignalKind.SourceDiscontinuity,
                    "The verified after-table belongs to a different camera evidence source.");
                return;
            }

            if (!Enum.TryParse<AdviceTargetGroup>(layout.Group, true, out var group) || !Enum.IsDefined(group)
                || observation.Outcome != ObservationOutcome.Ready || !layout.Confirmed || !layout.Settled || layout.Occluded
                || !AppCoordinator.HasVerificationProof(observation, operationId, group))
            {
                HandleUnverifiedActivity(activityId, observation);
                return;
            }

            if (layout.CapturedAt != observation.CapturedAt || layout.Version != observation.TableVersion
                || observation.Source != admitted.Source || observation.AnalysisGeneration != admitted.Generation
                || observation.MediaGeneration != media)
            {
                MarkActivityUncertain(activityId, ActivitySettlementSignalKind.SourceDiscontinuity,
                    "The verified table does not match the admitted live observation.");
                return;
            }

            var counts = CountRecognizedBalls(layout.Balls);
            if (counts is null)
            {
                SetActivityVerificationStatus(activityId, "O08 was clear and still, but some ball labels were unresolved. The original baseline is retained.");
                return;
            }

            var after = new SettledTableEvidence(Guid.NewGuid(), source, sourceTime,
                observation.CapturedAt, counts, Settled: true, Clear: true)
            { BoundedStillnessVerified = true };
            ActivitySettlementStep step;
            lock (motionActivityGate)
            {
                if (motionActivityLifecycle is not { } lifecycle || lifecycle.Context.Id != activityId) return;
                step = lifecycle.Observe(new(ActivitySettlementSignalKind.VerifiedSettled, source, sourceTime, after));
                motionActivityLifecycle = step.Lifecycle;
                motionActivityLastSourceTimeUs = Math.Max(motionActivityLastSourceTimeUs ?? -1, sourceTime);
                motionActivityStatus = step.Reason;
                if (step.Outcome == ActivitySettlementOutcome.SettledCandidate && step.Comparison?.Comparison is { } comparison)
                {
                    string summary = SummarizeComparison(comparison);
                    motionActivityCandidate = new(activityId, comparison.After.Source,
                        comparison.After.SourceTimeUs!.Value, comparison.After.CapturedAt,
                        comparison.Context.Before.Counts, comparison.After.Counts,
                        comparison.Changes, summary) { Recommendation = comparison.Recommendation };
                    motionActivityCandidateContext = comparison.Context;
                    motionActivityCandidateAfter = comparison.After;
                    motionActivityStatus = "Settled after-table is ready for automatic pot inference or review.";
                    candidateReady = true;
                }
            }

            var canonical = app.CaptureGameSession();
            if (step.Outcome == ActivitySettlementOutcome.SettledCandidate
                && step.Comparison?.Comparison is { } settledComparison
                && SettledOutcomeRuleInference.TryInfer(canonical.State, settledComparison, out var ruling))
            {
                if (!settledComparison.Context.Game.IsCompatibleWith(canonical.State))
                {
                    SetActivityVerificationStatus(activityId, "The match changed before the settled pot could be recorded. The proposal remains available for review.");
                }
                else
                {
                    var inputId = Guid.NewGuid();
                    var at = DateTimeOffset.UtcNow < settledComparison.After.CapturedAt
                        ? settledComparison.After.CapturedAt : DateTimeOffset.UtcNow;
                    var input = new GameInputEnvelope(inputId, settledComparison.Context.Game.MatchId,
                        settledComparison.Context.Game.FrameId, at,
                        new AcceptSettledOutcome(1, settledComparison.Context, settledComparison.After, ruling),
                        settledComparison.After.Source, settledComparison.After.SourceTimeUs,
                        Provenance.Inferred, .9);
                    var receipt = await app.DispatchAsync(new(inputId, CommandOrigin.System, app.State.Version,
                        new SubmitGameInput(input, canonical.State.Version)), cancellation);
                    if (receipt.Succeeded)
                    {
                        outcomeAccepted = true;
                        candidateReady = false;
                        string outcome = settledComparison.After.Counts.CueBalls < settledComparison.Context.Before.Counts.CueBalls
                            ? "cue-ball scratch" : "pot";
                        lock (motionActivityGate)
                        {
                            if (motionActivityLifecycle?.Context.Id == activityId)
                            {
                                motionActivityCandidate = null;
                                motionActivityCandidateContext = null;
                                motionActivityCandidateAfter = null;
                                motionActivityLifecycle = null;
                                motionActivityVerificationAttempted = false;
                                motionActivityLastSourceTimeUs = null;
                                motionActivityStatus = $"Assumed the settled ball-count decrease was a {outcome}; rules updated. {ruling.Position.Reason} The pocket remains unknown.";
                                ResetSettledDifferenceCandidate();
                            }
                        }
                    }
                    else
                    {
                        lock (motionActivityGate)
                            motionActivityStatus = "Settled outcome could not be admitted automatically; it remains available for review. " + receipt.Message;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            SetActivityVerificationStatus(activityId, "O08 verification was interrupted. The original settled baseline is retained.");
        }
        catch (Exception error)
        {
            SetActivityVerificationStatus(activityId, $"O08 verification could not complete: {error.Message} The original settled baseline is retained.");
        }
        finally
        {
            if (!candidateReady && resumeMotion && active && CanAnalyzeTable && !disposed)
            {
                try { await SetMotionFeedAsync(true, motionHz, allowUnconfirmedDiagnosticSeed: true,
                    preserveActivityLifecycle: !outcomeAccepted); }
                catch (Exception) { SetActivityVerificationStatus(activityId, "O08 did not produce a reviewable candidate; motion sampling could not resume. The original baseline is retained."); }
            }

            if (VisionDashboardActive)
            {
                visionDashboardSamplingPaused = false;
                string? restartMessage = null;
                if (resumeDiagnostics && CanStartVisionDiagnostics)
                {
                    if (table.StartVisionDiagnostics(out var message))
                    {
                        visionDashboardStartedDiagnostics = true;
                        VisionDashboardStatus = "Dashboard sampling resumed after settled-table verification.";
                    }
                    else restartMessage = message;
                }
                else if (resumeDiagnostics) restartMessage = "The live table is not ready for sequential sampling.";
                if (restartMessage is not null) VisionDashboardStatus = "Dashboard sampling is waiting to resume. " + restartMessage;
            }
            lock (motionActivityGate) motionActivityVerificationPending = false;
            Changed?.Invoke();
        }
    }

    bool IsCurrentVerificationSource(TableAnalysisSnapshot admitted, long media) => active
        && CanAnalyzeTable && !EditingTable && player is not null
        && MediaGeneration() == media && table.State.Source == admitted.Source
        && table.State.CalibrationId == admitted.CalibrationId
        && table.State.Generation == admitted.Generation;

    static SettledTableCounts? CountRecognizedBalls(Ball[] balls)
    {
        int reds = 0, yellows = 0, cues = 0, blacks = 0;
        foreach (var ball in balls)
        {
            switch (ball.Kind.Trim().ToLowerInvariant())
            {
                case "red": reds++; break;
                case "yellow": yellows++; break;
                case "cue":
                case "cue ball":
                case "cue-ball": cues++; break;
                case "black": blacks++; break;
                default: return null;
            }
        }
        return new(reds, yellows, cues, blacks);
    }

    static string SummarizeComparison(SettledInventoryComparison comparison)
    {
        var before = comparison.Context.Before.Counts;
        var after = comparison.After.Counts;
        string Inventory(SettledTableCounts counts) => $"{counts.Reds} red · {counts.Yellows} yellow · cue {(counts.CueBalls == 1 ? "present" : "absent")} · black {(counts.BlackBalls == 1 ? "present" : "absent")}";
        string changes = comparison.Changes.IsEmpty ? "no net count change"
            : string.Join(", ", comparison.Changes.Select(change =>
                change.Removed > 0 ? $"{change.Removed} {change.Colour.ToString().ToLowerInvariant()} removed"
                    : $"{change.Added} {change.Colour.ToString().ToLowerInvariant()} added"));
        string interpretation = comparison.Recommendation.Kind == "settled-pot-outcome"
            ? $"{comparison.Recommendation.Summary} Rules apply to inferred pots/scratch; pocket and exact identity remain unknown."
            : $"{comparison.Recommendation.Summary} No pot is inferred from this count pattern.";
        return $"A: {Inventory(before)} → B: {Inventory(after)}; {changes}. {interpretation}";
    }

    void HandleUnverifiedActivity(Guid activityId, SkyEye.Core.Application.TableObservation observation)
    {
        if (observation.Reason == ObservationReason.Obstructed && observation.SourceTimeUs is { } obstructedAt)
        {
            lock (motionActivityGate)
            {
                if (motionActivityLifecycle is { } lifecycle && lifecycle.Context.Id == activityId)
                {
                    var step = lifecycle.Observe(new(ActivitySettlementSignalKind.Obstructed,
                        lifecycle.Context.Before.Source, obstructedAt));
                    motionActivityLifecycle = step.Lifecycle;
                    motionActivityLastSourceTimeUs = obstructedAt;
                    if (step.Outcome == ActivitySettlementOutcome.Applied) motionActivityVerificationAttempted = false;
                    motionActivityStatus = step.Outcome == ActivitySettlementOutcome.Uncertain ? step.Reason
                        : "The verification view is obstructed. The original before-table remains held.";
                }
            }
            return;
        }
        if (observation.Reason == ObservationReason.Moving && observation.SourceTimeUs is { } movingAt)
        {
            lock (motionActivityGate)
            {
                if (motionActivityLifecycle is { } lifecycle && lifecycle.Context.Id == activityId)
                {
                    var step = lifecycle.Observe(new(ActivitySettlementSignalKind.ActivityDetected,
                        lifecycle.Context.Before.Source, movingAt));
                    motionActivityLifecycle = step.Lifecycle;
                    motionActivityLastSourceTimeUs = movingAt;
                    if (step.Outcome == ActivitySettlementOutcome.Applied) motionActivityVerificationAttempted = false;
                    motionActivityStatus = step.Outcome == ActivitySettlementOutcome.Uncertain ? step.Reason
                        : "Movement continued during verification; keep the before-table and wait for the table to settle again.";
                }
            }
            return;
        }
        if (observation.Reason is ObservationReason.SourceChanged or ObservationReason.CalibrationChanged)
        {
            MarkActivityUncertain(activityId, ActivitySettlementSignalKind.SourceDiscontinuity,
                "The source or calibration changed during verification; a fresh trusted baseline is required.");
            return;
        }
        string reason = AppCoordinator.VerificationMessage(observation.Reason);
        SetActivityVerificationStatus(activityId, $"{reason} The original before-table remains held; no game state changed.");
    }

    void MarkActivityUncertain(Guid activityId, ActivitySettlementSignalKind kind, string status)
    {
        lock (motionActivityGate)
        {
            if (motionActivityLifecycle is not { } lifecycle || lifecycle.Context.Id != activityId) return;
            var sourceTime = Math.Max(lifecycle.LastSourceTimeUs + 1, 0);
            var step = lifecycle.Observe(new(kind, lifecycle.Context.Before.Source, sourceTime));
            motionActivityLifecycle = step.Lifecycle;
            motionActivityLastSourceTimeUs = sourceTime;
            motionActivityStatus = status;
            motionActivityCandidate = null;
            motionActivityCandidateContext = null;
            motionActivityCandidateAfter = null;
        }
    }

    void SetActivityVerificationStatus(Guid activityId, string status)
    {
        lock (motionActivityGate)
        {
            if (motionActivityLifecycle?.Context.Id == activityId) motionActivityStatus = status;
        }
    }

    void ReleaseMotion()
    {
        StopMotion();
        _ = Finish();
        async Task Finish() { await motionGate.WaitAsync(); try { await CloseMotionAsync(); } finally { motionGate.Release(); } }
    }
}
