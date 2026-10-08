using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Core.Rules;
using SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

public sealed record DashboardScenarioSnapshot(Guid ScenarioId, string Preset, long Version,
    GameSnapshot Game, MatchView Match, int EventCount, ImmutableArray<DashboardScenarioEvent> Events,
    DashboardScenarioProposal? PendingProposal, DashboardScenarioRulePrompt? RulePrompt, string? Error)
{
    public DashboardScenarioCameraReview CameraReview { get; init; } = new(false, "Camera review is off.", null, 0);
}
public sealed record DashboardScenarioCameraReview(bool Active, string Status, Guid? SessionId, int LastSequence);
public sealed record DashboardScenarioEvent(long Sequence, Guid InputId, string FrameId, DateTimeOffset At,
    string Provenance, string Event, bool ScenarioTest);
public sealed record DashboardScenarioChoice(string Id, string Label, string Description, bool Recommended);
public sealed record DashboardScenarioProposal(Guid Id, string Kind, string Title, string Summary, string Recommendation,
    ImmutableArray<DashboardScenarioEvent> Events, string ProposedScore, string ProposedTurn,
    ImmutableArray<DashboardScenarioChoice> Choices, RulePosition? RulePosition = null)
{
    public DashboardScenarioCameraEvidence? CameraEvidence { get; init; }
}
public sealed record DashboardScenarioCameraBall(string Id, string Colour, double X, double Y, double Confidence);
public sealed record DashboardScenarioCameraEvidence(Guid SessionId, int FirstSequence, int SettledSequence,
    long FirstSourceTimeUs, long SettledSourceTimeUs, string Camera, string Timeline, long Generation,
    string Calibration, long MediaGeneration, long FrameId, DateTimeOffset CapturedAt, bool IdentitiesResolved,
    ImmutableArray<DashboardScenarioCameraBall> Balls)
{
    public DashboardScenarioSourceTimeWindow? MotionStartWindow { get; init; }
    public DashboardScenarioSourceTimeWindow? MotionEndWindow { get; init; }
}
public sealed record DashboardScenarioSourceTimeWindow(int EarlierSampleSequence, long EarlierSourceTimeUs,
    int LaterSampleSequence, long LaterSourceTimeUs);
public sealed record DashboardScenarioRulePrompt(string ShotId, string ShooterId, string ShooterName,
    int MovedBallCount, int PotCount, bool NoPotAfterMovement, string RulesProfile);
public sealed record DashboardScenarioResetRequest(Guid ScenarioId, long ExpectedVersion, string Preset);
public sealed record DashboardScenarioResetResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);
public sealed record DashboardScenarioCommandRequest(Guid ScenarioId, long ExpectedVersion, Guid OperationId, string Action,
    string? PlayerOne = null, string? PlayerTwo = null, int Target = 3, string? PlayerId = null,
    string? Colour = null, string? BallId = null, double? X = null, double? Y = null,
    double? Confidence = null, bool? Foul = null, bool Settled = true, bool Uncertain = false,
    string? ShotId = null, string? Pocket = null);
public sealed record DashboardScenarioCommandResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);
public sealed record DashboardScenarioReviewRequest(Guid ScenarioId, long ExpectedVersion, Guid ProposalId, string ChoiceId);
public sealed record DashboardScenarioReviewResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);
public sealed record DashboardScenarioRuleRequest(Guid ScenarioId, long ExpectedVersion, Guid OperationId,
    string Legality, int RedsPotted, int YellowsPotted, bool BlackPotted = false, bool CueBallPotted = false,
    bool CueBallOffTable = false, string? FirstContact = null, bool? CallRequired = null, string? CalledGroup = null,
    int? RedsBefore = null, int? YellowsBefore = null, bool Break = false, bool? LegalBreak = null);
public sealed record DashboardScenarioRuleResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);
public sealed record DashboardScenarioLiveStartRequest(Guid ScenarioId, long ExpectedVersion);
public sealed record DashboardScenarioLiveSampleRequest(Guid ScenarioId, long ExpectedVersion, Guid SessionId, int ThroughSequence);
public sealed record DashboardScenarioLiveResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);

/// <summary>Owns a disposable, separately journaled GameState scenario for the Lab dashboard.</summary>
public sealed class DashboardScenarioWorkspace
{
    readonly object gate = new();
    readonly string root;
    readonly string activePath;
    Guid scenarioId;
    string preset = "empty";
    GameJournalStore journal = null!;
    GameSession session = GameSession.Empty;
    string? error;
    GameSession? pendingSession;
    DashboardScenarioProposal? pendingProposal;
    readonly Dictionary<string, GameSession> pendingChoices = new(StringComparer.Ordinal);
    bool cameraReviewActive;
    bool cameraBaselinePending;
    string cameraReviewStatus = "Camera review is off.";
    Guid? cameraReviewSessionId;
    int cameraReviewLastSequence;
    sealed record CameraLayoutSample(VisionDiagnosticSample Sample, ImmutableArray<ObservedBall> Balls);
    readonly List<CameraLayoutSample> cameraStableSamples = [];
    CameraLayoutSample? cameraLastScenarioMatch;
    CameraLayoutSample? cameraFirstChangedSample;
    CameraLayoutSample? cameraLastChangingSample;
    CameraLayoutSample? pendingCameraStableSample;
    ImmutableArray<ObservedBall> pendingCameraLayout = [];
    ImmutableArray<ObservedBall> rejectedCameraLayout = [];
    EvidenceSource ScenarioSource => new("dashboard-scenario", "scenario-" + scenarioId.ToString("N"), 1, "scenario-table-2x1");

    public DashboardScenarioWorkspace(string directory)
    {
        root = Path.GetFullPath(directory);
        activePath = Path.Combine(root, "active-scenario.txt");
        LoadActive();
    }

    public DashboardScenarioSnapshot Snapshot()
    {
        lock (gate) return Current();
    }

    public DashboardScenarioResetResponse Reset(DashboardScenarioResetRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (request.Preset is not ("empty" or "clean-rack" or "mid-match"))
                return new(false, "Choose a supported scenario preset.", Current());

            try
            {
                var nextId = Guid.NewGuid();
                var nextJournal = Open(nextId);
                var nextSession = GameSession.Empty;
                nextSession = Seed(nextSession, nextJournal, nextId, request.Preset);
                var scenarioDirectory = Path.Combine(root, nextId.ToString("N"));
                Directory.CreateDirectory(scenarioDirectory);
                File.WriteAllText(Path.Combine(scenarioDirectory, "preset.txt"), request.Preset);
                WriteActive(nextId);
                scenarioId = nextId;
                preset = request.Preset;
                journal = nextJournal;
                session = nextSession;
                ClearPending();
                StopCameraReview("Camera review stopped because the scenario was reset.");
                error = null;
                return new(true, "Scenario reset to " + PresetName(preset) + ". Live match unchanged.", Current());
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                return new(false, "Scenario could not be reset. The previous scenario remains available.", Current());
            }
        }
    }

    public DashboardScenarioLiveResponse StartCameraReview(DashboardScenarioLiveStartRequest request,
        GameSession live, VisionDiagnosticsSnapshot diagnostics)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (pendingProposal is not null)
                return new(false, "Review the pending scenario event before loading the live match.", Current());
            if (!diagnostics.Active || diagnostics.LiveFrame is not { } liveFrame || liveFrame.Sample.Source is not { } source)
                return new(false, "Start fresh sequential camera diagnostics, then try again.", Current());
            var sample = liveFrame.Sample;
            if (!FreshCameraSample(sample) || sample.Detection.Uncertain || sample.Detection.Obstructed
                || sample.Detection.Balls.Any(ball => !TryColour(ball.Kind, out _) || !double.IsFinite(ball.Confidence) || ball.Confidence < .5))
                return new(false, "Wait for a fresh, clear camera sample with confidently labelled balls.", Current());
            if (live.State.Match is null || live.State.Frame is null || live.State.Frame.Winner is not null
                || live.State.Shot is not null || live.State.Table.Source != source)
                return new(false, "The live match needs an active frame, no unresolved shot and the same camera source as diagnostics.", Current());

            var onTable = live.State.Table.Balls.Values.Where(ball => ball.Presence.Value == BallPresence.OnTable
                && ball.Position is not null).ToImmutableArray();
            var detected = ToObserved(sample.Detection);
            if (onTable.Length == 0 || !CanAssignBaselineInventory(onTable, detected))
                return new(false, "The camera shows an unsupported colour or more balls of a colour than the copied match inventory. Correct the test inventory before reviewing camera changes.", Current());

            bool baselinePending = live.State.Table.Motion != SkyEye.GameState.TableMotion.Settled
                || live.State.Table.Uncertain || live.State.Table.LastSettled is null
                || !SameScenarioLayout(onTable, detected);

            try
            {
                var id = Guid.NewGuid();
                var imported = GameSession.Restore(live.History);
                var nextJournal = Open(id);
                nextJournal.Save(GameSession.Empty, imported);
                Directory.CreateDirectory(Path.Combine(root, id.ToString("N")));
                File.WriteAllText(Path.Combine(root, id.ToString("N"), "preset.txt"), "live-match-copy");
                WriteActive(id);
                scenarioId = id;
                preset = "live-match-copy";
                journal = nextJournal;
                session = imported;
                ClearPending();
                cameraReviewActive = true;
                cameraBaselinePending = baselinePending;
                cameraReviewSessionId = diagnostics.SessionId;
                cameraReviewLastSequence = sample.Sequence;
                cameraReviewStatus = baselinePending
                    ? "Live match copied into the isolated scenario. Waiting for three stable frames to propose a camera baseline."
                    : "Live match copied into the isolated scenario. Waiting for a stable changed table layout.";
                cameraStableSamples.Clear();
                cameraStableSamples.Add(new(sample, detected));
                cameraLastScenarioMatch = baselinePending ? null : new(sample, detected);
                cameraFirstChangedSample = null;
                cameraLastChangingSample = null;
                pendingCameraStableSample = null;
                pendingCameraLayout = [];
                rejectedCameraLayout = [];
                error = null;
                return new(true, "Live match copied to the isolated scenario. Camera review is active; the live match is unchanged.", Current());
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                return new(false, "The live match could not be copied into the scenario journal. The previous scenario remains available.", Current());
            }
        }
    }

    public DashboardScenarioLiveResponse StopCameraReview(DashboardScenarioLiveStartRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            StopCameraReview("Camera review stopped.");
            return new(true, cameraReviewStatus, Current());
        }
    }

    void StopCameraReview(string status)
    {
        cameraReviewActive = false;
        cameraBaselinePending = false;
        cameraReviewStatus = status;
        cameraReviewSessionId = null;
        cameraReviewLastSequence = 0;
        cameraStableSamples.Clear();
        cameraLastScenarioMatch = null;
        cameraFirstChangedSample = null;
        cameraLastChangingSample = null;
        pendingCameraStableSample = null;
        pendingCameraLayout = [];
        rejectedCameraLayout = [];
    }

    public DashboardScenarioLiveResponse ReviewCameraSamples(DashboardScenarioLiveSampleRequest request,
        VisionDiagnosticsSnapshot diagnostics)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (!cameraReviewActive || request.SessionId != cameraReviewSessionId)
                return new(false, "Camera review is no longer connected to this diagnostic session. Start it again from the current live match.", Current());
            if (request.SessionId != diagnostics.SessionId)
            {
                StopCameraReview("Camera review stopped because diagnostics restarted. Start it again from the current live match.");
                return new(true, cameraReviewStatus, Current());
            }
            if (pendingProposal is not null)
                return new(true, "Review the pending camera proposal before processing more samples.", Current());
            if (!diagnostics.Active)
                return new(false, "Sequential camera diagnostics stopped. Start camera review again after sampling resumes.", Current());
            if (request.ThroughSequence <= cameraReviewLastSequence) return new(true, cameraReviewStatus, Current());

            var available = diagnostics.Samples.Where(sample => sample.Sequence > cameraReviewLastSequence
                && sample.Sequence <= request.ThroughSequence).OrderBy(sample => sample.Sequence).ToArray();
            if (available.Length == 0)
            {
                cameraStableSamples.Clear();
                cameraLastScenarioMatch = null;
                cameraFirstChangedSample = null;
                cameraLastChangingSample = null;
                cameraReviewLastSequence = request.ThroughSequence;
                cameraReviewStatus = "Camera sample history rolled over. Waiting for three fresh stable frames.";
                return new(true, cameraReviewStatus, Current());
            }
            if (available[0].Sequence != cameraReviewLastSequence + 1)
            {
                cameraStableSamples.Clear();
                cameraLastScenarioMatch = null;
                cameraFirstChangedSample = null;
                cameraLastChangingSample = null;
                cameraReviewStatus = "A camera sample gap was detected. Reacquiring a stable layout.";
            }

            foreach (var sample in available)
            {
                if (sample.Sequence != cameraReviewLastSequence + 1)
                {
                    cameraStableSamples.Clear();
                    cameraLastScenarioMatch = null;
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                }
                cameraReviewLastSequence = sample.Sequence;
                if (sample.Source != session.State.Table.Source)
                {
                    StopCameraReview("Camera review stopped because the camera or calibration source changed. Start it again from the current live match.");
                    return new(true, cameraReviewStatus, Current());
                }
                if (!UsableCameraSample(sample, out var balls, out var reason))
                {
                    cameraStableSamples.Clear();
                    cameraLastScenarioMatch = null;
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                    cameraReviewStatus = "Waiting for clear camera evidence. " + reason;
                    continue;
                }
                if (cameraBaselinePending && sample.Detection.Uncertain)
                {
                    cameraStableSamples.Clear();
                    cameraLastScenarioMatch = null;
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                    cameraReviewStatus = "Waiting for three clear frames before proposing a camera baseline.";
                    continue;
                }
                var currentTable = session.State.Table.Balls.Values.Where(ball => ball.Presence.Value == BallPresence.OnTable
                    && ball.Position is not null).ToImmutableArray();
                bool matchesScenario = !cameraBaselinePending && SameScenarioLayout(currentTable, balls);
                var previous = cameraStableSamples.Count > 0 ? cameraStableSamples[^1] : cameraLastScenarioMatch;
                if (previous is not null)
                {
                    bool sourceGap = sample.Source != previous.Sample.Source || sample.SourceTimeUs <= previous.Sample.SourceTimeUs
                        || sample.SourceTimeUs - previous.Sample.SourceTimeUs > 3_000_000;
                    bool layoutChanged = !sourceGap && !SameCameraLayout(previous.Balls, balls, .025);
                    if (sourceGap)
                    {
                        cameraStableSamples.Clear();
                        cameraLastScenarioMatch = null;
                        cameraFirstChangedSample = null;
                        cameraLastChangingSample = null;
                    }
                    else if (layoutChanged)
                    {
                        if (sample.Detection.Uncertain)
                        {
                            cameraLastScenarioMatch = null;
                            cameraFirstChangedSample = null;
                            cameraLastChangingSample = null;
                        }
                        else if (matchesScenario)
                        {
                            cameraFirstChangedSample = null;
                            cameraLastChangingSample = null;
                        }
                        else if (cameraLastScenarioMatch is not null)
                        {
                            cameraFirstChangedSample ??= new(sample, balls);
                            cameraLastChangingSample = previous;
                        }
                        cameraStableSamples.Clear();
                    }
                }
                if (sample.Detection.Uncertain)
                {
                    cameraLastScenarioMatch = null;
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                }
                else if (matchesScenario && cameraStableSamples.Count >= 2)
                {
                    cameraLastScenarioMatch = new(sample, balls);
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                }
                cameraStableSamples.Add(new(sample, balls));
                if (cameraStableSamples.Count > 3) cameraStableSamples.RemoveAt(0);
                if (cameraStableSamples.Count < 3)
                {
                    cameraReviewStatus = $"Checking that the changed layout is stable ({cameraStableSamples.Count}/3 samples).";
                    continue;
                }

                var settled = cameraStableSamples[^1];
                if (cameraBaselinePending)
                {
                    StageCameraBaselineProposal(cameraStableSamples[0], settled);
                    return new(true, "Stable camera baseline ready for review.", Current());
                }
                var onTable = session.State.Table.Balls.Values.Where(ball => ball.Presence.Value == BallPresence.OnTable
                    && ball.Position is not null).ToImmutableArray();
                if (SameScenarioLayout(onTable, settled.Balls))
                {
                    if (cameraStableSamples.Any(item => item.Sample.Detection.Uncertain))
                    {
                        cameraLastScenarioMatch = null;
                        cameraFirstChangedSample = null;
                        cameraLastChangingSample = null;
                        cameraStableSamples.Clear();
                        cameraReviewStatus = "Waiting for clear camera samples.";
                        continue;
                    }
                    cameraLastScenarioMatch = settled;
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                    cameraReviewStatus = "Camera review is active. The stable table matches the scenario.";
                    rejectedCameraLayout = [];
                    continue;
                }
                if (!rejectedCameraLayout.IsDefaultOrEmpty && SameCameraLayout(rejectedCameraLayout, settled.Balls, .025))
                {
                    cameraReviewStatus = "This stable layout was rejected. Waiting for another table change.";
                    continue;
                }
                if (session.State.Shot is not null || session.State.Table.Uncertain
                    || session.State.Table.Motion != SkyEye.GameState.TableMotion.Settled)
                {
                    cameraReviewStatus = "Resolve the scenario shot and table uncertainty before camera review continues.";
                    continue;
                }
                var firstStable = cameraStableSamples[0];
                var startWindow = BuildSourceTimeWindow(cameraLastScenarioMatch, cameraFirstChangedSample);
                var endWindow = BuildSourceTimeWindow(cameraLastChangingSample, firstStable);
                StageCameraProposal(firstStable, settled, cameraFirstChangedSample, startWindow, endWindow);
                return new(true, "Stable camera event proposal ready for review.", Current());
            }
            return new(true, cameraReviewStatus, Current());
        }
    }

    static bool UsableCameraSample(VisionDiagnosticSample sample, out ImmutableArray<ObservedBall> balls, out string reason)
    {
        balls = [];
        if (sample.Source is null || !FreshCameraSample(sample))
        { reason = "the source frame is stale or incomplete."; return false; }
        if (sample.Detection.Obstructed)
        { reason = "the detector marked this frame obstructed."; return false; }
        if (sample.Detection.Balls.Length is 0 or > 16
            || sample.Detection.Balls.Any(ball => !TryColour(ball.Kind, out _) || !double.IsFinite(ball.Confidence) || ball.Confidence < .5))
        { reason = "all visible balls need supported labels and confidence."; return false; }
        balls = ToObserved(sample.Detection);
        reason = sample.Detection.Uncertain ? "the detector marked this frame uncertain; only an uncertain proposal can be offered." : "";
        return true;
    }

    void StageCameraBaselineProposal(CameraLayoutSample first, CameraLayoutSample settled)
    {
        var onTable = session.State.Table.Balls.Values.Where(ball => ball.Presence.Value == BallPresence.OnTable)
            .ToImmutableArray();
        if (!TryAssignBaselineIdentities(onTable, settled.Balls, out var associated))
        {
            StopCameraReview("Camera baseline shows an unsupported colour or more balls of a colour than the copied match inventory. Correct the test inventory, then start review again.");
            return;
        }

        var source = settled.Sample.Source!;
        var matchId = session.State.Match!.Id;
        var frameId = session.State.Frame!.Id;
        var input = new GameInputEnvelope(Guid.NewGuid(), matchId, frameId, settled.Sample.CapturedAt,
            new SkyEye.GameState.TableObservation(associated, Settled: true, Uncertain: false, IdentitiesResolved: true),
            source, settled.Sample.SourceTimeUs, Provenance.PlayerConfirmed, .95);
        var result = session.Accept(input);
        if (result.Outcome != InputOutcome.Applied)
        {
            cameraReviewStatus = "Camera baseline could not be staged: " + result.Message;
            cameraStableSamples.Clear();
            return;
        }

        var candidate = result.Session;
        var added = candidate.History.Skip(session.History.Length).ToArray();
        var match = MatchView.From(candidate);
        var missing = onTable.Where(ball => !associated.Any(observed => observed.Id == ball.Id)).ToArray();
        var missingSummary = missing.Length == 0 ? "All saved balls are visible." :
            missing.Length == 1 ? $"One {missing[0].Colour} ball is absent from the stable camera layout." :
            $"{missing.Length} balls are absent from the stable camera layout.";
        var missingChoice = missing.Length == 0 ? "Use as scenario baseline" : "Set baseline and mark absent balls Missing";
        GameSession? confirmedPot = null;
        if (missing.Length == 1 && missing[0].Colour is BallColour.Red or BallColour.Yellow)
        {
            var correction = new GameInputEnvelope(Guid.NewGuid(), matchId, frameId,
                settled.Sample.CapturedAt.AddTicks(1),
                new PlayerCorrection(new SetBallPresence(missing[0].Id, BallPresence.Potted)), source,
                settled.Sample.SourceTimeUs, Provenance.PlayerConfirmed, .99);
            var potResult = candidate.Accept(correction);
            if (potResult.Outcome == InputOutcome.Applied) confirmedPot = potResult.Session;
        }
        var choices = ImmutableArray.CreateBuilder<DashboardScenarioChoice>();
        choices.Add(new("accept", missingChoice,
            "Set the confirmed camera positions in this test scenario. Absent balls remain Missing and do not count as pots.", true));
        if (confirmedPot is not null)
            choices.Add(new("confirm-pot", $"Confirm absent {missing[0].Colour} was potted",
                "Record a player-confirmed Potted presence for the one absent ball. Shooter, pocket, shot result, score and turn stay unchanged for now.", false));
        choices.Add(new("reject", "Keep the saved layout", "Discard this baseline and leave the scenario state unchanged.", false));
        var proposal = new DashboardScenarioProposal(Guid.NewGuid(), "camera-baseline", "Camera baseline awaiting your choice",
            $"Three clear frames agree on a {associated.Length}-ball layout. {missingSummary} Same-colour balls are assigned to scenario IDs from left to right.",
            "Choose whether any absent ball stays Missing or, if you know it was potted, confirm its presence as Potted. This baseline cannot establish the shooter, pocket, turn or shot result.",
            added.Select(ToScenarioEvent).ToImmutableArray(), match.Score,
            match.CurrentPlayer is null ? "—" : match.Name(match.CurrentPlayer),
            choices.ToImmutable())
        {
            CameraEvidence = new(cameraReviewSessionId!.Value,
                first.Sample.Sequence, settled.Sample.Sequence, first.Sample.SourceTimeUs, settled.Sample.SourceTimeUs,
                source.Camera, source.Timeline, source.Generation, source.Calibration,
                settled.Sample.MediaGeneration, settled.Sample.FrameId, settled.Sample.CapturedAt,
                true, associated.Select(ball => new DashboardScenarioCameraBall(ball.Id, ball.Colour.ToString(),
                    ball.Position?.X ?? 0, ball.Position?.Y ?? 0, ball.Confidence ?? .95)).ToImmutableArray())
        };
        pendingSession = candidate;
        pendingProposal = proposal;
        pendingChoices.Clear();
        pendingChoices["accept"] = candidate;
        if (confirmedPot is not null) pendingChoices["confirm-pot"] = confirmedPot;
        pendingCameraStableSample = settled;
        pendingCameraLayout = settled.Balls;
        cameraReviewLastSequence = settled.Sample.Sequence;
        cameraReviewStatus = "Camera baseline is waiting for your choice.";
    }

    static bool CanAssignBaselineInventory(ImmutableArray<KnownBall> existing, ImmutableArray<ObservedBall> detected)
    {
        if (detected.Length == 0 || detected.Length > existing.Length || existing.Any(ball => ball.Colour == BallColour.Unknown)
            || detected.Any(ball => ball.Colour == BallColour.Unknown)) return false;
        return new[] { BallColour.Cue, BallColour.Red, BallColour.Yellow, BallColour.Black }
            .All(colour => detected.Count(ball => ball.Colour == colour) <= existing.Count(ball => ball.Colour == colour));
    }

    static bool TryAssignBaselineIdentities(ImmutableArray<KnownBall> existing,
        ImmutableArray<ObservedBall> detected, out ImmutableArray<ObservedBall> associated)
    {
        associated = [];
        if (!CanAssignBaselineInventory(existing, detected)) return false;
        var result = ImmutableArray.CreateBuilder<ObservedBall>(detected.Length);
        foreach (var colour in new[] { BallColour.Cue, BallColour.Red, BallColour.Yellow, BallColour.Black })
        {
            var known = existing.Where(ball => ball.Colour == colour)
                .OrderBy(ball => ball.Id, StringComparer.Ordinal).ToArray();
            var observed = detected.Where(ball => ball.Colour == colour)
                .OrderBy(ball => ball.Position!.X).ThenBy(ball => ball.Position!.Y).ToArray();
            for (var i = 0; i < observed.Length; i++) result.Add(observed[i] with { Id = known[i].Id });
        }
        associated = result.ToImmutable();
        return true;
    }

    static DashboardScenarioSourceTimeWindow? BuildSourceTimeWindow(CameraLayoutSample? earlier, CameraLayoutSample? later)
    {
        if (earlier is null || later is null || earlier.Sample.Source is null
            || earlier.Sample.Source != later.Sample.Source
            || earlier.Sample.MediaGeneration != later.Sample.MediaGeneration
            || later.Sample.Sequence != earlier.Sample.Sequence + 1
            || later.Sample.SourceTimeUs <= earlier.Sample.SourceTimeUs
            || later.Sample.SourceTimeUs - earlier.Sample.SourceTimeUs > 1_500_000) return null;
        return new(earlier.Sample.Sequence, earlier.Sample.SourceTimeUs, later.Sample.Sequence, later.Sample.SourceTimeUs);
    }

    static long WindowMilliseconds(DashboardScenarioSourceTimeWindow window) =>
        (window.LaterSourceTimeUs - window.EarlierSourceTimeUs) / 1_000;

    static bool FreshCameraSample(VisionDiagnosticSample sample)
    {
        var currentAgeMs = (DateTimeOffset.UtcNow - sample.CapturedAt).TotalMilliseconds;
        return double.IsFinite(sample.FrameAgeMs) && sample.FrameAgeMs is >= 0 and <= 2500
            && double.IsFinite(currentAgeMs) && currentAgeMs is >= -2000 and <= 2500;
    }

    void StageCameraProposal(CameraLayoutSample first, CameraLayoutSample settled, CameraLayoutSample? firstChanged,
        DashboardScenarioSourceTimeWindow? motionStartWindow, DashboardScenarioSourceTimeWindow? motionEndWindow)
    {
        var table = session.State.Table.Balls.Values.Where(ball => ball.Presence.Value == BallPresence.OnTable
            && ball.Position is not null).ToImmutableArray();
        var prior = table.Select(ball => (ball.Id, ball.Colour, ball.Position!)).ToImmutableArray();
        var elapsed = Math.Max(.25, (settled.Sample.SourceTimeUs - (session.State.Table.LastSettled?.SourceTimeUs ?? first.Sample.SourceTimeUs)) / 1_000_000d);
        var movementGate = Math.Min(1.25, .05 + elapsed * 1.25);
        bool detectorCertain = cameraStableSamples.All(item => !item.Sample.Detection.Uncertain);
        ImmutableArray<ObservedBall> associated = [];
        bool resolved = detectorCertain && TryAssociate(prior, settled.Balls, movementGate, out associated);
        var observationBalls = resolved ? associated : settled.Balls.Select((ball, index) => ball with
            { Id = $"camera-unresolved-{settled.Sample.Sequence}-{index}" }).ToImmutableArray();
        var source = settled.Sample.Source!;
        var matchId = session.State.Match!.Id;
        var frameId = session.State.Frame!.Id;
        var shotId = $"camera-{settled.Sample.SourceTimeUs:x}-{first.Sample.Sequence}";
        var inferredConfidence = resolved ? .75 : .5;
        var candidate = session;
        var inputs = new List<GameInputEnvelope>();
        bool shotTimingBounded = resolved && firstChanged is not null && motionStartWindow is not null && motionEndWindow is not null;
        if (shotTimingBounded)
        {
            inputs.Add(new(Guid.NewGuid(), matchId, frameId, firstChanged!.Sample.CapturedAt,
                new MotionStarted(shotId), source, firstChanged.Sample.SourceTimeUs, Provenance.Inferred, .7));
            inputs.Add(new(Guid.NewGuid(), matchId, frameId, first.Sample.CapturedAt,
                new MotionStopped(shotId), source, first.Sample.SourceTimeUs, Provenance.Inferred, .7));
        }
        inputs.Add(new(Guid.NewGuid(), matchId, frameId, settled.Sample.CapturedAt,
            new SkyEye.GameState.TableObservation(observationBalls, Settled: true, Uncertain: !resolved, IdentitiesResolved: resolved),
            source, settled.Sample.SourceTimeUs, Provenance.Inferred, inferredConfidence));
        foreach (var input in inputs)
        {
            var result = candidate.Accept(input);
            if (result.Outcome != InputOutcome.Applied)
            {
                cameraReviewStatus = "Camera evidence could not be staged: " + result.Message;
                cameraStableSamples.Clear();
                return;
            }
            candidate = result.Session;
        }

        var added = candidate.History.Skip(session.History.Length).ToArray();
        var moved = resolved ? associated.Where(current => table.Any(old => old.Id == current.Id && old.Position is { } p
                && current.Position is { } q && Distance(p, q) > .005)).Select(ball => ball.Id).ToArray() : [];
        var missing = resolved ? table.Where(old => !associated.Any(ball => ball.Id == old.Id)).Select(ball => ball.Id).ToArray() : [];
        var choices = ImmutableArray.CreateBuilder<DashboardScenarioChoice>();
        choices.Add(new("accept", "Accept camera proposal", resolved
            ? shotTimingBounded
                ? "Record sample-bounded shot timing and this settled layout in the isolated scenario."
                : "Record this settled layout only; source timing is insufficient to propose shot boundaries."
            : "Record the detector result as uncertain without changing ball identities or inventory.", true));
        if (resolved)
            choices.Add(new("keep-uncertain", "Keep the layout uncertain", "Record the missing or moved evidence, but do not resolve the shot for rules review.", false));
        choices.Add(new("reject", "Reject this layout", "Discard the camera proposal and keep the current scenario state.", false));
        var match = MatchView.From(candidate);
        var summary = resolved
            ? shotTimingBounded
                ? $"Three clear frames agree on a changed layout. {moved.Length} moved; {missing.Length} absent. Shot start/end are bounded by adjacent samples ({WindowMilliseconds(motionStartWindow!)} ms / {WindowMilliseconds(motionEndWindow!)} ms). No pot is inferred."
                : $"Three clear frames agree on a changed layout. {moved.Length} moved; {missing.Length} absent. A source-time gap prevents shot boundaries; no pocket or pot is inferred."
            : !detectorCertain
                ? "Three stable frames show a changed layout, but the detector marked at least one frame uncertain. Accepting records an uncertain observation only; identities and inventory stay unchanged."
                : "Three clear frames agreed on a changed layout, but ball identities could not be uniquely associated with the scenario inventory. Accepting records an uncertain observation only.";
        var proposal = new DashboardScenarioProposal(Guid.NewGuid(), "camera", "Camera event awaiting your choice",
            summary, resolved
                ? shotTimingBounded
                    ? "Review the timing windows and layout. Accept records inferred boundaries in this scenario; a missing ball stays Missing until separate pocket evidence exists."
                    : "Review the layout. Accept records the settled table only; a missing ball stays Missing until separate pocket evidence exists."
                : "Keep the existing ball identities. The uncertain observation will not change inventory, score or turn.",
            added.Select(ToScenarioEvent).ToImmutableArray(), match.Score,
            match.CurrentPlayer is null ? "—" : match.Name(match.CurrentPlayer), choices.ToImmutable())
        {
            CameraEvidence = new(cameraReviewSessionId!.Value,
                first.Sample.Sequence, settled.Sample.Sequence, first.Sample.SourceTimeUs, settled.Sample.SourceTimeUs,
                source.Camera, source.Timeline, source.Generation, source.Calibration,
                settled.Sample.MediaGeneration, settled.Sample.FrameId, settled.Sample.CapturedAt,
                resolved, observationBalls.Select(ball => new DashboardScenarioCameraBall(ball.Id, ball.Colour.ToString(),
                    ball.Position?.X ?? 0, ball.Position?.Y ?? 0, ball.Confidence ?? inferredConfidence)).ToImmutableArray())
            {
                MotionStartWindow = shotTimingBounded ? motionStartWindow : null,
                MotionEndWindow = shotTimingBounded ? motionEndWindow : null
            }
        };
        pendingSession = candidate;
        pendingProposal = proposal;
        pendingChoices.Clear();
        pendingChoices["accept"] = candidate;
        if (resolved)
        {
            var uncertainCandidate = BuildCameraCandidate(first, settled, associated, uncertain: true,
                firstChanged, shotTimingBounded ? motionStartWindow : null, shotTimingBounded ? motionEndWindow : null);
            if (uncertainCandidate is not null) pendingChoices["keep-uncertain"] = uncertainCandidate;
        }
        pendingCameraStableSample = settled;
        pendingCameraLayout = settled.Balls;
        cameraReviewLastSequence = settled.Sample.Sequence;
        cameraReviewStatus = "Camera proposal is waiting for your choice.";
    }

    GameSession? BuildCameraCandidate(CameraLayoutSample first, CameraLayoutSample settled,
        ImmutableArray<ObservedBall> associated, bool uncertain, CameraLayoutSample? firstChanged,
        DashboardScenarioSourceTimeWindow? motionStartWindow, DashboardScenarioSourceTimeWindow? motionEndWindow)
    {
        var source = settled.Sample.Source!;
        var matchId = session.State.Match!.Id;
        var frameId = session.State.Frame!.Id;
        var shotId = $"camera-{settled.Sample.SourceTimeUs:x}-{first.Sample.Sequence}";
        var candidate = session;
        var inputs = new List<GameInputEnvelope>();
        if (firstChanged is not null && motionStartWindow is not null && motionEndWindow is not null)
        {
            inputs.Add(new(Guid.NewGuid(), matchId, frameId, firstChanged.Sample.CapturedAt,
                new MotionStarted(shotId), source, firstChanged.Sample.SourceTimeUs, Provenance.Inferred, .7));
            inputs.Add(new(Guid.NewGuid(), matchId, frameId, first.Sample.CapturedAt,
                new MotionStopped(shotId), source, first.Sample.SourceTimeUs, Provenance.Inferred, .7));
        }
        inputs.Add(new(Guid.NewGuid(), matchId, frameId, settled.Sample.CapturedAt,
            new SkyEye.GameState.TableObservation(associated, Settled: true, Uncertain: uncertain), source,
            settled.Sample.SourceTimeUs, Provenance.Inferred, .5));
        foreach (var input in inputs)
        {
            var result = candidate.Accept(input);
            if (result.Outcome != InputOutcome.Applied) return null;
            candidate = result.Session;
        }
        return candidate;
    }

    static bool SameScenarioLayout(ImmutableArray<KnownBall> prior, ImmutableArray<ObservedBall> detected)
    {
        if (prior.Length != detected.Length) return false;
        var known = prior.Select(ball => (ball.Id, ball.Colour, ball.Position!)).ToImmutableArray();
        return TryAssociate(known, detected, .025, out _);
    }

    static bool SameCameraLayout(ImmutableArray<ObservedBall> first, ImmutableArray<ObservedBall> second, double tolerance)
    {
        if (first.Length != second.Length) return false;
        var prior = first.Where(ball => ball.Position is not null)
            .Select(ball => (ball.Id, ball.Colour, ball.Position!)).ToImmutableArray();
        return prior.Length == first.Length && TryAssociate(prior, second, tolerance, out _);
    }

    static double Distance(TablePosition a, TablePosition b)
    {
        double dx = (a.X - b.X) * 2, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public DashboardScenarioCommandResponse Apply(DashboardScenarioCommandRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (pendingProposal is not null)
                return new(false, "Choose an action for the pending event before adding another one.", Current());
            if (request.OperationId == Guid.Empty)
                return new(false, "A unique operation ID is required.", Current());

            var matchId = session.State.Match?.Id ?? "scenario-" + scenarioId.ToString("N");
            var frameId = session.State.Frame?.Id ?? session.State.Match?.Frames.LastOrDefault()?.Id ?? "scenario-setup";
            GameInput input;
            EvidenceSource? source = null;
            long? sourceTimeUs = null;
            double? confidence = null;
            var provenance = Provenance.PlayerConfirmed;
            try
            {
                switch (request.Action)
                {
                    case "start-match":
                        if (string.IsNullOrWhiteSpace(request.PlayerOne) || string.IsNullOrWhiteSpace(request.PlayerTwo))
                            return new(false, "Enter a name for both players.", Current());
                        matchId = "scenario-" + Guid.NewGuid().ToString("N");
                        frameId = "scenario-setup";
                        input = new StartMatch([new("p1", request.PlayerOne.Trim()), new("p2", request.PlayerTwo.Trim())], request.Target);
                        break;
                    case "start-frame":
                        if (session.State.Match is not { } activeMatch)
                            return new(false, "Start a match before starting a frame.", Current());
                        var frameNumber = activeMatch.Frames.Length + 1;
                        frameId = activeMatch.Id + "-frame-" + frameNumber;
                        input = new StartFrame(request.PlayerId ?? activeMatch.Players[0].Id);
                        break;
                    case "change-turn":
                        input = new PlayerCorrection(new ChangeTurn(RequiredPlayer(request.PlayerId)));
                        break;
                    case "declare-open":
                        input = new DeclareColours();
                        break;
                    case "assign-group":
                        var group = ParseColour(request.Colour);
                        if (group is not (BallColour.Red or BallColour.Yellow))
                            return new(false, "Choose reds or yellows for the first group.", Current());
                        input = new DeclareColours(RequiredPlayer(request.PlayerId), group);
                        break;
                    case "set-foul":
                        if (request.Foul is null) return new(false, "Choose foul or no foul.", Current());
                        input = new PlayerCorrection(new SetFoul(request.Foul.Value));
                        break;
                    case "frame-winner":
                        input = new PlayerCorrection(new SetFrameWinner(RequiredPlayer(request.PlayerId)));
                        break;
                    case "finish-match":
                        input = new FinishMatch();
                        break;
                    case "undo-last":
                        if (MatchView.From(session).UndoInputId is not { } undoId)
                            return new(false, "There is no supported player decision to undo.", Current());
                        var target = session.History.Last(item => item.InputId == undoId);
                        matchId = target.MatchId;
                        frameId = target.FrameId;
                        input = new UndoDecision(undoId);
                        provenance = Provenance.PlayerCorrected;
                        break;
                    case "ball-upsert":
                        if (string.IsNullOrWhiteSpace(request.BallId) || request.X is not { } x || request.Y is not { } y
                            || !double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1
                            || request.Confidence is { } ballConfidence && (!double.IsFinite(ballConfidence) || ballConfidence is < 0 or > 1))
                            return new(false, "Enter a ball ID, table position from 0 to 1, and optional confidence from 0 to 1.", Current());
                        var colour = ParseColour(request.Colour);
                        input = new BallObserved(new(request.BallId.Trim(), colour, new(x, y), request.Confidence));
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence;
                        break;
                    case "ball-move":
                        if (string.IsNullOrWhiteSpace(request.BallId) || request.X is not { } movedX || request.Y is not { } movedY
                            || !double.IsFinite(movedX) || !double.IsFinite(movedY) || movedX is < 0 or > 1 || movedY is < 0 or > 1
                            || !session.State.Table.Balls.TryGetValue(request.BallId.Trim(), out var movedBall)
                            || movedBall.Presence.Value != BallPresence.OnTable
                            || request.Confidence is { } moveConfidence && (!double.IsFinite(moveConfidence) || moveConfidence is < 0 or > 1))
                            return new(false, "Choose a known ball and enter its new table position from 0 to 1.", Current());
                        input = new BallMoved(request.BallId.Trim(), new(movedX, movedY));
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence ?? 0.95;
                        provenance = Provenance.Observed;
                        break;
                    case "ball-missing":
                        input = new BallMissing(RequiredText(request.BallId, "Choose a ball first."));
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence ?? 0.95;
                        provenance = Provenance.Inferred;
                        break;
                    case "table-state":
                        var onTable = session.State.Table.Balls.Values
                            .Where(ball => ball.Presence.Value == BallPresence.OnTable)
                            .OrderBy(ball => ball.Id, StringComparer.Ordinal)
                            .Select(ball => new ObservedBall(ball.Id, ball.Colour, ball.Position, ball.Presence.Confidence))
                            .ToImmutableArray();
                        input = new SkyEye.GameState.TableObservation(onTable, request.Settled, request.Uncertain);
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = 0.95;
                        provenance = Provenance.PlayerConfirmed;
                        break;
                    case "shot-start":
                        input = new ShotStarted(string.IsNullOrWhiteSpace(request.ShotId) ? "scenario-shot-" + Guid.NewGuid().ToString("N") : request.ShotId.Trim());
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = 0.95;
                        break;
                    case "shot-end":
                        if (session.State.Shot is not { Ended: false } activeShot)
                            return new(false, "There is no active shot to end.", Current());
                        input = new ShotEnded(activeShot.Id);
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = 0.95;
                        break;
                    case "pocket-approach":
                        if (session.State.Shot is not { Ended: false } approachShot)
                            return new(false, "Start a shot before adding a pocket approach.", Current());
                        if (!Enum.TryParse<Pocket>(request.Pocket, true, out var pocket) || !Enum.IsDefined(pocket))
                            return new(false, "Choose a table pocket.", Current());
                        input = new PossiblePocketEvent(approachShot.Id, RequiredText(request.BallId, "Choose a ball first."), pocket);
                        source = ScenarioSource;
                        sourceTimeUs = NextSourceTime();
                        confidence = request.Confidence ?? 0.95;
                        break;
                    default:
                        return new(false, "Choose a supported scenario action.", Current());
                }
            }
            catch (ArgumentException e)
            {
                return new(false, e.Message, Current());
            }

            var envelope = new GameInputEnvelope(request.OperationId, matchId, frameId, DateTimeOffset.UtcNow,
                input, source, sourceTimeUs, provenance, confidence);
            var result = session.Accept(envelope);
            if (result.Outcome != InputOutcome.Applied)
                return new(false, result.Message, Current());

            if (request.Action is "ball-move" or "ball-missing" or "table-state" or "shot-end" or "pocket-approach")
            {
                var added = result.Session.History.Skip(session.History.Length).ToArray();
                var hasPot = added.Any(item => item.Event is BallPottedEvent);
                GameSession? uncertain = null;
                if (hasPot && input is SkyEye.GameState.TableObservation observation)
                {
                    var alternate = session.Accept(envelope with
                    {
                        Id = Guid.NewGuid(),
                        At = DateTimeOffset.UtcNow.AddTicks(1),
                        SourceTimeUs = NextSourceTime(),
                        Input = observation with { Uncertain = true }
                    });
                    if (alternate.Outcome == InputOutcome.Applied) uncertain = alternate.Session;
                }
                pendingSession = result.Session;
                pendingProposal = CreateEvidenceProposal(request.Action, result.Session, added, hasPot, uncertain is not null);
                pendingChoices.Clear();
                pendingChoices["accept"] = result.Session;
                if (uncertain is not null) pendingChoices["keep-uncertain"] = uncertain;
                error = null;
                return new(true, "Event preview ready. Choose what the scenario should do.", Current());
            }

            try { journal.Save(session, result.Session); }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                return new(false, "The scenario could not be saved; no state change was published.", Current());
            }
            session = result.Session;
            preset = "custom";
            error = null;
            try
            {
                var presetPath = Path.Combine(root, scenarioId.ToString("N"), "preset.txt");
                File.WriteAllText(presetPath + ".tmp", preset);
                File.Move(presetPath + ".tmp", presetPath, overwrite: true);
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
            { error = "The scenario state is saved; its custom label will be refreshed on the next edit."; }
            return new(true, "Scenario action recorded. Live match unchanged.", Current());
        }
    }

    public DashboardScenarioReviewResponse Resolve(DashboardScenarioReviewRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (pendingProposal is null || pendingSession is null || pendingProposal.Id != request.ProposalId)
                return new(false, "That event proposal has expired. Refresh the scenario and try again.", Current());

            if (request.ChoiceId == "edit-facts" && pendingProposal.Kind == "rules")
            {
                ClearPending();
                return new(true, "The scenario is unchanged. Review the shot facts and evaluate again.", Current());
            }

            var knownChoice = request.ChoiceId is "reject" || pendingChoices.ContainsKey(request.ChoiceId);
            var selected = pendingChoices.GetValueOrDefault(request.ChoiceId);
            if (!knownChoice)
                return new(false, "Choose one of the actions shown for this event.", Current());

            if (selected is not null)
            {
                try { journal.Save(session, selected); }
                catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
                { return new(false, "The reviewed event could not be saved; the scenario is unchanged.", Current()); }
                session = selected;
                preset = "custom";
                SavePresetLabel();
            }

            bool cameraProposal = pendingProposal.CameraEvidence is not null;
            bool cameraBaselineProposal = pendingProposal.Kind == "camera-baseline";
            bool cameraReviewWasActive = cameraReviewActive;
            var message = cameraBaselineProposal
                ? request.ChoiceId == "accept"
                    ? "Camera baseline accepted into the isolated scenario. Absent balls remain Missing; the live match is unchanged."
                    : request.ChoiceId == "confirm-pot"
                        ? "The absent ball was marked Potted by your confirmation in the isolated scenario. Shooter, pocket, shot result and turn remain undecided."
                    : "Camera baseline rejected; the isolated scenario is unchanged."
                : cameraProposal
                ? request.ChoiceId switch
                {
                    "accept" => "Camera proposal accepted into the isolated scenario. A missing ball remains Missing; no pot was inferred.",
                    "keep-uncertain" => "Camera evidence was recorded with the table uncertain. No pot was inferred.",
                    _ => "Camera proposal rejected; the scenario state is unchanged."
                }
                : request.ChoiceId switch
                {
                    "accept" => pendingProposal.Kind == "rules" ? "Rules recommendation accepted into the scenario journal." : "Event accepted into the scenario journal.",
                    "keep-uncertain" => "Missing evidence kept, with the table marked uncertain; no pot was recorded.",
                    var choice when choice.StartsWith("turn:", StringComparison.Ordinal) => "Manual turn choice recorded in the scenario journal.",
                    _ => "Proposal discarded; the scenario state is unchanged."
                };
            if (cameraProposal)
            {
                if (cameraBaselineProposal)
                {
                    if (selected is null)
                        StopCameraReview("Camera baseline rejected. The scenario stayed unchanged; start review again when ready.");
                    else
                    {
                        cameraBaselinePending = false;
                        cameraStableSamples.Clear();
                        cameraLastScenarioMatch = pendingCameraStableSample;
                        cameraFirstChangedSample = null;
                        cameraLastChangingSample = null;
                        rejectedCameraLayout = [];
                        cameraReviewStatus = "Camera baseline accepted in the isolated scenario. Waiting for the next stable table change.";
                    }
                }
                else if (selected is null)
                {
                    rejectedCameraLayout = pendingCameraLayout;
                    cameraReviewStatus = "This stable layout was rejected. Waiting for another table change.";
                    cameraStableSamples.Clear();
                }
                else if (session.State.Table.Uncertain)
                    StopCameraReview("Camera review paused because the accepted layout left the scenario uncertain. Reset or reload the live match to resume.");
                else if (cameraReviewWasActive)
                {
                    cameraLastScenarioMatch = pendingCameraStableSample;
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                    cameraReviewStatus = "Camera proposal accepted. Waiting for the next stable table change.";
                    cameraStableSamples.Clear();
                }
                else cameraReviewStatus = "Camera proposal accepted. Camera review is off.";
                pendingCameraLayout = [];
                pendingCameraStableSample = null;
                if (selected is null)
                {
                    cameraFirstChangedSample = null;
                    cameraLastChangingSample = null;
                }
            }
            ClearPending();
            return new(true, message, Current());
        }
    }

    public DashboardScenarioRuleResponse EvaluateRules(DashboardScenarioRuleRequest request)
    {
        lock (gate)
        {
            if (request.ScenarioId != scenarioId || request.ExpectedVersion != session.State.Version)
                return new(false, "The scenario changed. Refresh and try again.", Current());
            if (pendingProposal is not null)
                return new(false, "Review the pending event before evaluating shot rules.", Current());
            if (request.OperationId == Guid.Empty)
                return new(false, "A unique operation ID is required.", Current());
            if (GetRulePrompt() is not { } prompt)
                return new(false, "Finish and settle a shot before evaluating its rules.", Current());
            if (!Enum.TryParse<DeclaredLegality>(request.Legality, true, out var legality) || !Enum.IsDefined(legality)
                || !ParseOptionalColour(request.FirstContact, out var firstContact)
                || !ParseOptionalColour(request.CalledGroup, out var calledGroup))
                return new(false, "Choose a supported legality, first contact and called group.", Current());

            var facts = new DeclaredShot(request.Break, legality, request.RedsPotted, request.YellowsPotted,
                request.BlackPotted, request.CueBallPotted, request.CueBallOffTable, request.LegalBreak,
                firstContact, request.CallRequired, calledGroup, request.RedsBefore, request.YellowsBefore);
            RulePosition ruling;
            try { ruling = InternationalEightball.Evaluate(session.State, facts); }
            catch (ArgumentException e) { return new(false, e.Message, Current()); }

            var matchId = session.State.Match!.Id;
            var frameId = session.State.Frame!.Id;
            var envelope = new GameInputEnvelope(request.OperationId, matchId, frameId, DateTimeOffset.UtcNow,
                new ApplyShotRuling(new(session.State.Frame.CurrentPlayer.Value, facts, ruling)),
                Provenance: Provenance.PlayerConfirmed);
            var result = session.Accept(envelope);
            if (result.Outcome != InputOutcome.Applied)
                return new(false, result.Message, Current());

            var added = result.Session.History.Skip(session.History.Length).ToArray();
            pendingSession = result.Session;
            var proposedMatch = MatchView.From(result.Session);
            var nextPlayer = result.Session.State.Frame?.CurrentPlayer.Value;
            var nextName = nextPlayer is null ? "No player" : proposedMatch.Name(nextPlayer);
            var choices = ImmutableArray.Create(
                new DashboardScenarioChoice("accept", ruling.Pending ? "Record pending rule decision" : "Accept rules recommendation",
                    ruling.Reason + (ruling.Clauses.Length == 0 ? "" : " · rule " + string.Join(", ", ruling.Clauses)), true),
                new DashboardScenarioChoice("reject", "Keep current state", "Discard this rule proposal and leave the frame unchanged.", false))
                .AddRange(result.Session.State.Match!.Players.Select(player => new DashboardScenarioChoice(
                    "turn:" + player.Id, "Set turn to " + player.Name, "Apply this explicit player correction instead of the rules recommendation.", false)))
                .Add(new DashboardScenarioChoice("edit-facts", "Review shot facts", "Keep the frame unchanged and adjust the declared facts.", false));
            pendingChoices.Clear();
            pendingChoices["accept"] = result.Session;
            foreach (var player in result.Session.State.Match.Players)
            {
                var correction = new GameInputEnvelope(Guid.NewGuid(), matchId, frameId, DateTimeOffset.UtcNow,
                    new PlayerCorrection(new ChangeTurn(player.Id)), Provenance: Provenance.PlayerCorrected);
                var alternate = session.Accept(correction);
                if (alternate.Outcome == InputOutcome.Applied) pendingChoices["turn:" + player.Id] = alternate.Session;
            }
            pendingProposal = new(Guid.NewGuid(), "rules", "Rule decision awaiting your choice",
                $"{prompt.ShooterName}'s shot · {prompt.MovedBallCount} ball(s) moved · {prompt.PotCount} pot(s) currently recorded.",
                ruling.Reason + (ruling.Pending ? " More declared facts may be needed." : $" Proposed next player: {nextName}."),
                added.Select(ToScenarioEvent).ToImmutableArray(), proposedMatch.Score,
                nextPlayer is null ? "—" : proposedMatch.Name(nextPlayer), choices, ruling);
            return new(true, "Rules engine preview ready. Accept, keep the current state or revise the facts.", Current());
        }
    }

    long NextSourceTime() => (session.History.LastOrDefault(item => item.Source == ScenarioSource)?.SourceTimeUs ?? 0) + 1_000_000;

    string RequiredPlayer(string? value)
    {
        if (session.State.Match is not { } match || !match.Players.Any(player => player.Id == value))
            throw new ArgumentException("Choose one of the players in this match.");
        return value!;
    }

    static string RequiredText(string? value, string message)
        => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();

    static BallColour ParseColour(string? value)
        => Enum.TryParse<BallColour>(value, true, out var colour) && Enum.IsDefined(colour)
            ? colour : throw new ArgumentException("Choose a supported ball colour.");

    void LoadActive()
    {
        lock (gate)
        {
            Directory.CreateDirectory(root);
            if (File.Exists(activePath))
            {
                var value = File.ReadAllText(activePath).Trim();
                if (!Guid.TryParseExact(value, "N", out scenarioId))
                {
                    error = "The saved scenario index is invalid. Reset the scenario to start a new one.";
                    scenarioId = Guid.NewGuid();
                    journal = Open(scenarioId);
                    session = GameSession.Empty;
                    return;
                }
                journal = Open(scenarioId);
                try
                {
                    session = journal.Load();
                    var presetPath = Path.Combine(root, scenarioId.ToString("N"), "preset.txt");
                    preset = File.Exists(presetPath) ? File.ReadAllText(presetPath).Trim() : "empty";
                }
                catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
                {
                    session = GameSession.Empty;
                    error = "Saved scenario history is unavailable. Reset it to create a clean Lab scenario.";
                }
                return;
            }

            scenarioId = Guid.NewGuid();
            journal = Open(scenarioId);
            session = GameSession.Empty;
            preset = "empty";
            WriteActive(scenarioId);
        }
    }

    DashboardScenarioSnapshot Current() => new(scenarioId, preset, session.State.Version,
        session.State, MatchView.From(session), session.History.Length,
        session.History.TakeLast(40).Reverse().Select(ToScenarioEvent).ToImmutableArray(),
        pendingProposal, GetRulePrompt(), error)
        { CameraReview = new(cameraReviewActive, cameraReviewStatus, cameraReviewSessionId, cameraReviewLastSequence) };

    static ImmutableArray<ObservedBall> ToObserved(VisionResult detection)
        => detection.Balls.Where(ball => TryColour(ball.Kind, out _)).Select(ball =>
        {
            _ = TryColour(ball.Kind, out var colour);
            return new ObservedBall(ball.Id, colour, new(ball.Position.X / 2, ball.Position.Y), ball.Confidence);
        }).ToImmutableArray();

    static bool TryColour(string? value, out BallColour colour)
        => Enum.TryParse(value, true, out colour) && Enum.IsDefined(colour) && colour != BallColour.Unknown;

    static bool TryAssociate(ImmutableArray<(string Id, BallColour Colour, TablePosition Position)> prior,
        ImmutableArray<ObservedBall> detected, double maximumDistance, out ImmutableArray<ObservedBall> associated)
    {
        var result = detected.ToArray();
        foreach (var colour in new[] { BallColour.Cue, BallColour.Red, BallColour.Yellow, BallColour.Black })
        {
            var old = prior.Where(ball => ball.Colour == colour).ToArray();
            var indices = Enumerable.Range(0, result.Length).Where(index => result[index].Colour == colour).ToArray();
            if (indices.Length > old.Length) { associated = []; return false; }
            if (indices.Length == 0) continue;

            var bestCost = double.PositiveInfinity;
            var secondCost = double.PositiveInfinity;
            int[]? best = null;
            var selected = new int[indices.Length];
            var used = new bool[old.Length];
            void Search(int depth, double cost)
            {
                if (cost > secondCost) return;
                if (depth == indices.Length)
                {
                    if (cost < bestCost)
                    {
                        secondCost = bestCost;
                        bestCost = cost;
                        best = selected.ToArray();
                    }
                    else if (cost < secondCost) secondCost = cost;
                    return;
                }
                var observed = result[indices[depth]];
                for (int candidate = 0; candidate < old.Length; candidate++)
                {
                    if (used[candidate]) continue;
                    double dx = (observed.Position!.X - old[candidate].Position.X) * 2;
                    double dy = observed.Position.Y - old[candidate].Position.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance > maximumDistance) continue;
                    used[candidate] = true;
                    selected[depth] = candidate;
                    Search(depth + 1, cost + distance * distance);
                    used[candidate] = false;
                }
            }
            Search(0, 0);
            if (best is null || double.IsFinite(secondCost)
                && secondCost - bestCost <= Math.Max(.00025, bestCost * .1))
            { associated = []; return false; }
            for (int i = 0; i < indices.Length; i++) result[indices[i]] = result[indices[i]] with { Id = old[best[i]].Id };
        }
        associated = result.ToImmutableArray();
        return true;
    }

    DashboardScenarioProposal CreateEvidenceProposal(string action, GameSession proposed, EventEnvelope[] added,
        bool inferredPot, bool hasUncertainChoice)
    {
        var events = added.Select(ToScenarioEvent).ToImmutableArray();
        var pots = added.Where(item => item.Event is BallPottedEvent)
            .Select(item => (BallPottedEvent)item.Event).ToArray();
        var summary = action switch
        {
            "ball-missing" => "A ball disappeared from the observed inventory. This is missing evidence, not proof of a pot.",
            "pocket-approach" => "A ball approached a pocket during the active shot. This is evidence only.",
            "ball-move" => "A new position was recorded for a ball during the scenario.",
            "shot-end" => "The active shot was marked ended. The table must settle before an outcome can be inferred.",
            _ => "A table-state observation was checked by the canonical GameState reducer."
        };
        var recommendation = inferredPot
            ? "GameState can infer a pot from the missing ball, pocket approach, shot end and settled certain table. Review the evidence before recording it."
            : action == "ball-missing"
                ? "Keep the ball in the inventory as Missing and mark the table uncertain, or reject the observation."
                : action == "table-state" && proposed.State.Shot is { Ended: true, Resolved: true }
                    ? "The shot resolved with no pot recorded. Review the shot facts to ask the rules engine whether the turn changes."
                    : "Review the evidence before it enters the scenario history.";
        var choices = ImmutableArray.CreateBuilder<DashboardScenarioChoice>();
        choices.Add(new("accept", inferredPot ? "Accept inferred pot" : action == "ball-missing" ? "Accept missing evidence" : "Accept event",
            inferredPot ? string.Join(" · ", pots.Select(pot => $"{pot.BallId} to {pot.Pocket}")) : "Record the proposed typed GameState event.", true));
        if (hasUncertainChoice)
            choices.Add(new("keep-uncertain", "Keep it missing; mark table uncertain", "Save the missing-ball evidence without inferring a pot or resolving the shot.", false));
        choices.Add(new("reject", "Reject event", "Keep the current scenario state and discard this observation.", false));
        var match = MatchView.From(proposed);
        return new(Guid.NewGuid(), "evidence", "Game event awaiting your choice", summary, recommendation,
            events, match.Score, match.CurrentPlayer is null ? "—" : match.Name(match.CurrentPlayer), choices.ToImmutable());
    }

    DashboardScenarioRulePrompt? GetRulePrompt()
    {
        if (pendingProposal is not null || session.State.Shot is not { Ended: true, Resolved: true } shot
            || session.State.Frame is null || session.State.Match is null) return null;
        var completed = session.History.LastOrDefault(item => item.Event is ShotResolvedEvent && item.FrameId == session.State.Frame.Id);
        if (completed is null || session.History.Any(item => item.Sequence > completed.Sequence && item.FrameId == completed.FrameId
            && item.Event is RulesShotRecordedEvent or TurnChangedEvent or FrameWonEvent)) return null;
        var moved = shot.Before.Values.Count(before => before.Presence.Value == BallPresence.OnTable
            && session.State.Table.Balls.TryGetValue(before.Id, out var after) && after.Presence.Value == BallPresence.OnTable
            && before.Position is { } oldPosition && after.Position is { } newPosition
            && Math.Sqrt(Math.Pow(oldPosition.X - newPosition.X, 2) + Math.Pow(oldPosition.Y - newPosition.Y, 2)) > .005);
        var pots = session.State.Frame.Pots.Values.Count(pot => pot.ShotId == shot.Id);
        var shooter = session.State.Frame.CurrentPlayer.Value;
        return new(shot.Id, shooter, MatchView.From(session).Name(shooter), moved, pots, moved > 0 && pots == 0,
            InternationalEightball.Profile);
    }

    static DashboardScenarioEvent ToScenarioEvent(EventEnvelope item)
        => new(item.Sequence, item.InputId, item.FrameId, item.At, item.Provenance.ToString(), item.Event.GetType().Name, true);

    static bool ParseOptionalColour(string? value, out BallColour? colour)
    {
        colour = null;
        if (string.IsNullOrWhiteSpace(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        if (!Enum.TryParse<BallColour>(value, true, out var parsed) || !Enum.IsDefined(parsed)) return false;
        colour = parsed;
        return true;
    }

    void ClearPending()
    {
        pendingSession = null;
        pendingProposal = null;
        pendingChoices.Clear();
    }

    void SavePresetLabel()
    {
        try
        {
            var path = Path.Combine(root, scenarioId.ToString("N"), "preset.txt");
            File.WriteAllText(path + ".tmp", preset);
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        { error = "The scenario state is saved; its custom label will be refreshed on the next edit."; }
    }

    GameJournalStore Open(Guid id) => new(Path.Combine(root, id.ToString("N")));

    void WriteActive(Guid id)
    {
        var temporary = activePath + ".tmp";
        File.WriteAllText(temporary, id.ToString("N"));
        File.Move(temporary, activePath, overwrite: true);
    }

    static GameSession Seed(GameSession game, GameJournalStore store, Guid id, string selectedPreset)
    {
        if (selectedPreset == "empty") return game;
        var matchId = "scenario-" + id.ToString("N");
        var frameId = "frame-1";
        var now = DateTimeOffset.UtcNow;
        var step = 0;
        var source = new EvidenceSource("dashboard-scenario", "scenario-" + id.ToString("N"), 1, "scenario-table-2x1");

        void Apply(GameInput input, string targetFrame, EvidenceSource? evidenceSource = null, long? sourceTimeUs = null,
            Provenance provenance = Provenance.PlayerConfirmed, double? confidence = null)
        {
            var before = game;
            var envelope = new GameInputEnvelope(Guid.NewGuid(), matchId, targetFrame,
                now.AddMilliseconds(step++), input, evidenceSource, sourceTimeUs, provenance, confidence);
            var result = game.Accept(envelope);
            if (result.Outcome != InputOutcome.Applied) throw new InvalidOperationException(result.Message);
            store.Save(before, result.Session);
            game = result.Session;
        }

        Apply(new StartMatch([new Player("rich", "Rich"), new Player("mike", "Mike")], 3), frameId);
        Apply(new StartFrame("rich"), frameId);

        if (selectedPreset == "mid-match")
        {
            Apply(new PlayerCorrection(new SetFrameWinner("rich")), frameId);
            frameId = "frame-2";
            Apply(new StartFrame("mike"), frameId);
            Apply(new DeclareColours("rich", BallColour.Red), frameId);
        }

        Apply(new SelectSource(), frameId, source, 0, Provenance.Observed);
        Apply(new SkyEye.GameState.TableObservation(CleanRack(), Settled: true), frameId, source, 1_000_000,
            Provenance.Observed, confidence: 0.95);

        if (selectedPreset == "mid-match")
            Apply(new ShotStarted("scenario-shot-1"), frameId, source, 2_000_000, Provenance.Observed, confidence: 0.95);
        return game;
    }

    static ImmutableArray<ObservedBall> CleanRack()
    {
        var result = ImmutableArray.CreateBuilder<ObservedBall>(16);
        result.Add(new("cue-ball", BallColour.Cue, new(.22, .5), .95));
        var redId = 1;
        var yellowId = 1;
        var index = 0;
        var nonBlackIndex = 0;
        for (var row = 0; row < 5; row++)
        for (var column = 0; column < 5 - row; column++)
        {
            var colour = index == 7 ? BallColour.Black : nonBlackIndex++ % 2 == 0 ? BallColour.Red : BallColour.Yellow;
            var ballId = colour == BallColour.Black ? "black-8" : colour == BallColour.Red ? "red-" + redId++ : "yellow-" + yellowId++;
            var x = .68 + row * .025;
            var y = .38 + column * .05 + row * .025;
            result.Add(new(ballId, colour, new(x, y), .95));
            index++;
        }
        return result.MoveToImmutable();
    }

    static string PresetName(string value) => value switch
    {
        "clean-rack" => "clean rack",
        "mid-match" => "mid-match",
        _ => "blank"
    };
}
