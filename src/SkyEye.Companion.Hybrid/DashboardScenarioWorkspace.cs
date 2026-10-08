using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core.Application;
using SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

public sealed record DashboardScenarioSnapshot(Guid ScenarioId, string Preset, long Version,
    GameSnapshot Game, MatchView Match, int EventCount, ImmutableArray<DashboardScenarioEvent> Events, string? Error);
public sealed record DashboardScenarioEvent(long Sequence, string FrameId, DateTimeOffset At,
    string Provenance, string Event, bool ScenarioTest);
public sealed record DashboardScenarioResetRequest(Guid ScenarioId, long ExpectedVersion, string Preset);
public sealed record DashboardScenarioResetResponse(bool Accepted, string Message, DashboardScenarioSnapshot Scenario);

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
                error = null;
                return new(true, "Scenario reset to " + PresetName(preset) + ". Live match unchanged.", Current());
            }
            catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                return new(false, "Scenario could not be reset. The previous scenario remains available.", Current());
            }
        }
    }

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
        session.History.TakeLast(40).Reverse().Select(item => new DashboardScenarioEvent(item.Sequence,
            item.FrameId, item.At, item.Provenance.ToString(), item.Event.GetType().Name, true)).ToImmutableArray(), error);

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
