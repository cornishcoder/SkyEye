using System.Collections.Immutable;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

namespace SkyEye.Core.Application;

public sealed record GameObservationEvidence(EvidenceSource Source, ImmutableArray<ObservedBall> Balls);

public static class GameObservationAdapter
{
    public static GameInputEnvelope EstablishBaseline(GameSnapshot state, TableObservation observation, Guid evidenceId)
    {
        var observed = observation.GameEvidence ?? throw new ArgumentException("Observation has no matching game evidence.");
        var counts = new SettledTableCounts(Count(BallColour.Red), Count(BallColour.Yellow), Count(BallColour.Cue), Count(BallColour.Black));
        var evidence = new SettledTableEvidence(evidenceId, observed.Source, observation.SourceTimeUs,
            observation.CapturedAt, counts, observation.Outcome == ObservationOutcome.Ready,
            observation.Quality == LayoutQuality.Clear && observed.Balls.All(b => b.Colour != BallColour.Unknown))
        {
            BoundedStillnessVerified = observation.Verification is not null,
            StableClearSampleCount = observation.Verification?.Samples.Count(s => s.Measurement.Vision is { Uncertain: false, Obstructed: false }) ?? 0
        };
        return new(evidenceId, state.Match!.Id, state.Frame!.Id, observation.CapturedAt,
            new EstablishSettledBaseline(1, evidence), observed.Source, observation.SourceTimeUs);
        int Count(BallColour colour) => observed.Balls.Count(b => b.Colour == colour);
    }

    public static GameInputEnvelope Translate(GameSnapshot state, TableObservation observation, Guid evidenceId)
    {
        var evidence = observation.GameEvidence ?? throw new ArgumentException("Observation has no matching game evidence.");
        var old = state.Table.Balls.Values.Where(b => b.Presence.Value == BallPresence.OnTable).ToArray();
        var identified = ImmutableArray.CreateBuilder<ObservedBall>();
        var used = new HashSet<string>();
        bool resolved = old.Length == 0 || old.Length == evidence.Balls.Length;
        for (int index = 0; index < evidence.Balls.Length; index++)
        {
            var observed = evidence.Balls[index];
            // Only carry identity across an unchanged, uniquely associated layout.
            // Moving/crossing same-colour balls require the future tracking mission.
            var matches = old.Where(b => b.Colour == observed.Colour && Near(b.Position, observed.Position)).ToArray();
            string id;
            if (old.Length == 0) id = $"ball-{evidenceId:N}-{index}";
            else if (matches.Length == 1 && used.Add(matches[0].Id)) id = matches[0].Id;
            else { id = $"unassociated-{evidenceId:N}-{index}"; resolved = false; }
            identified.Add(observed with { Id = id });
        }
        if (state.Shot is { Ended: false }) resolved = false;
        return new(evidenceId, state.Match!.Id, state.Frame!.Id, observation.CapturedAt,
            new SkyEye.GameState.TableObservation(identified.ToImmutable(), observation.Outcome == ObservationOutcome.Ready,
                observation.Outcome == ObservationOutcome.Unclear, resolved), evidence.Source, observation.SourceTimeUs);
    }
    static bool Near(TablePosition? a, TablePosition? b) => a is not null && b is not null && Math.Abs(a.X - b.X) <= .0075 && Math.Abs(a.Y - b.Y) <= .015;
}
