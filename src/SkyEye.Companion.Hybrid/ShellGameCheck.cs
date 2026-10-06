#if SKYEYE_PERFORMANCE
using SkyEye.Core;
using SkyEye.Core.Application;
using G = SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    // Opt-in device proof. Test journals are separate from the production match journal.
    async Task<HttpResponse> CheckGameStateAsync(CancellationToken cancellation)
    {
        if (ObservationRequest is not { } request) return HttpResponse.Json(new { error = "No active calibrated live table" }, 409);
        var receipt = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, request), cancellation);
        if (!receipt.Succeeded || receipt.Observation?.GameEvidence is null)
            return HttpResponse.Json(new { error = receipt.Message }, 409);
        var observation = receipt.Observation;
        var root = Path.Combine(FileSystem.AppDataDirectory, "r01-device-check", Guid.NewGuid().ToString("N"));
        var liveStore = new GameJournalStore(Path.Combine(root, "observed"));
        var scenarioStore = new GameJournalStore(Path.Combine(root, "scenario"));
        var source = new G.EvidenceSource("declared-fixture", "declared-shot", 1, "declared-rectangle");
        G.GameSession Seed()
        {
            var game = G.GameSession.Empty;
            game = Apply(game, new G.StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3), 0);
            return Apply(game, new G.StartFrame("rich"), 1);
        }
        var live = Seed();
        live = Accepted(live.Accept(GameObservationAdapter.Translate(live.State, observation, Guid.NewGuid())));
        liveStore.Save(G.GameSession.Empty, live);
        var recoveredLive = liveStore.Load();
        G.GameInputEnvelope Envelope(G.GameInput input, int second, double? confidence = null)
            => new(Guid.NewGuid(), "r01-check", "frame", DateTimeOffset.UnixEpoch.AddSeconds(second), input,
                input is G.StartMatch or G.StartFrame or G.PlayerCorrection ? null : source, second * 1_000_000L,
                input is G.StartMatch or G.StartFrame ? G.Provenance.PlayerConfirmed : G.Provenance.Observed, confidence);
        G.GameSession Apply(G.GameSession game, G.GameInput input, int second, double? confidence = null) => Accepted(game.Accept(Envelope(input, second, confidence)));
        G.GameSession Accepted(G.InputResult result) => result.Accepted ? result.Session : throw new InvalidOperationException(result.Message);
        var scenario = Seed();
        scenario = Apply(scenario, new G.TableObservation([new("Y3", G.BallColour.Yellow, new(.4, .5), .9), new("R1", G.BallColour.Red, new(.2, .5), .9)], true), 2);
        scenario = Apply(scenario, new G.ShotStarted("shot"), 3);
        scenario = Apply(scenario, new G.PossiblePocketEvent("shot", "Y3", G.Pocket.BottomRight), 4, .92);
        scenario = Apply(scenario, new G.ShotEnded("shot"), 5);
        scenario = Apply(scenario, new G.TableObservation([new("R1", G.BallColour.Red, new(.2, .5), .9)], true), 6);
        var pot = scenario.History.Single(e => e.Event is G.BallPottedEvent);
        int? before = scenario.State.Table.Remaining(G.BallColour.Yellow);
        var correction = Envelope(new G.PlayerCorrection(new G.SetBallPresence("Y3", G.BallPresence.OnTable, pot.EventId)), 7);
        scenario = Accepted(scenario.Accept(correction));
        scenarioStore.Save(G.GameSession.Empty, scenario);
        var recovered = scenarioStore.Load();
        var result = new
        {
            observedAt = DateTimeOffset.UtcNow,
            liveObservation = new { observation.Outcome, observation.SourceTimeUs, observation.FrameSource,
                detectedBalls = observation.GameEvidence.Balls.Length, canonicalBalls = live.State.Table.Balls.Count,
                live.State.Table.Uncertain, timestampPreserved = live.History.Last().SourceTimeUs == observation.SourceTimeUs,
                rebuilt = G.GameJson.WriteState(recoveredLive.State) == G.GameJson.WriteState(live.State) },
            declaredScenario = new { inferredPot = pot.Event is G.BallPottedEvent { BallId: "Y3", Pocket: G.Pocket.BottomRight },
                pot.Provenance, pot.Confidence, remainingBeforeCorrection = before, remainingAfterCorrection = recovered.State.Table.Remaining(G.BallColour.Yellow),
                correctedVisitPots = recovered.State.Frame!.Pots.Count, historyEvents = recovered.History.Length,
                rebuilt = G.GameJson.WriteState(recovered.State) == G.GameJson.WriteState(scenario.State),
                durableDuplicate = recovered.Accept(correction).Outcome == G.InputOutcome.Duplicate },
            productionJournalUntouched = app.State.Game.Match is null,
            capture = app.State.Camera.Capture.ToString()
        };
        if (!result.liveObservation.rebuilt || !result.declaredScenario.rebuilt || !result.declaredScenario.inferredPot
            || before != 0 || result.declaredScenario.remainingAfterCorrection != 1 || result.declaredScenario.correctedVisitPots != 0
            || !result.declaredScenario.durableDuplicate) throw new InvalidOperationException("Game-state device scenario failed.");
        return HttpResponse.Json(result);
    }
}
#endif
