using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;

static class PortChecks
{
    static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static Point Point(JsonElement element) => new(element[0].GetDouble(), element[1].GetDouble());
    public static void Run()
    {
        var coordinates = new AnalysisCoordinates();
        Point[] portraitPockets = [new(0, 0), new(1000, 0), new(0, 1000), new(1000, 1000), new(0, 2000), new(1000, 2000)];
        for (int i = 0; i < 6; i++) Require((coordinates.ToTable(portraitPockets[i]) - PoolGeometry.Pockets[AnalysisCoordinates.ToSkyEyePocket(i)]).Length < 1e-9, "Portrait pocket identity");
        using var frame = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/poolgame/pixel-table.json")));
        var calibration = new TableCalibration(frame.RootElement.GetProperty("calibration").EnumerateArray().Select(p => new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToArray());
        foreach (var ball in frame.RootElement.GetProperty("expected").EnumerateArray())
        {
            var p = ball.GetProperty("image"); var image = new Point(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble());
            var table = calibration.Map(image); var roundtrip = calibration.Unmap(coordinates.ToTable(coordinates.FromTable(table)));
            Require((roundtrip - image).Length < 1e-9, "Real fixture frame/table/portrait inverse mapping");
        }
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/poolgame/geometry-reference.json")));
        int checkedShots = 0;
        foreach (var fixture in reference.RootElement.GetProperty("cases").EnumerateArray())
        foreach (var expected in fixture.GetProperty("shots").EnumerateArray())
        {
            var targetPath = expected.GetProperty("targetPath"); var cuePath = expected.GetProperty("cuePath");
            var shot = ShotMath.Create(Point(fixture.GetProperty("cue")), Point(fixture.GetProperty("target")), Point(targetPath[targetPath.GetArrayLength() - 1][1]),
                targetPath.GetArrayLength() > 1 ? Point(targetPath[0][1]) : null, cuePath.GetArrayLength() > 1 ? Point(cuePath[0][1]) : null, pythonRounding: true);
            Require(shot is not null, "Reference shot must be finite");
            Require(Math.Abs(shot!.Angle - expected.GetProperty("angle").GetDouble()) < 1e-8, "Python cut-angle parity");
            Require(Math.Abs(shot.Length - expected.GetProperty("length").GetDouble()) < 1e-6, "Python path length parity");
            Require(Math.Abs(shot.Difficulty - expected.GetProperty("difficulty").GetDouble()) < 1e-6, "Python ranking score parity");
            Require((int)shot.Ghost.X == expected.GetProperty("ghost")[0].GetInt32() && (int)shot.Ghost.Y == expected.GetProperty("ghost")[1].GetInt32(), "Python ghost contact parity");
            checkedShots++;
        }
        Require(checkedShots == 270, "All upstream geometry candidates checked");
        Require(ShotMath.Create(new(50, 50), new(0, 0), new(0, 0)) is null, "Zero target direction rejected");
        Require(ShotMath.Create(new(double.NaN, 1), new(500, 500), new(0, 0)) is null, "Non-finite geometry rejected");
        Console.WriteLine("PASS real-frame coordinate roundtrips, six pocket identities and 270 Python ghost/angle/length/ranking fixtures");
        foreach (var (pixel, kind) in new[] { (new HsvPixel(170, 245, 210), "red"), (new HsvPixel(16, 250, 240), "yellow"), (new HsvPixel(16, 76, 224), "cue"), (new HsvPixel(20, 70, 20), "black") })
        {
            var sample = Enumerable.Repeat(pixel, 90).Concat(Enumerable.Repeat(new HsvPixel(0, 240, 180), 10)).ToArray();
            var result = BallClassifier.Classify(sample);
            Require(result.Kind == kind && result.Quality >= .7, "Cream cue/orange yellow/red spot/black classification");
            Require(JsonSerializer.Serialize(result) == JsonSerializer.Serialize(BallClassifier.Classify(sample)), "Repeatable clustering");
        }
        Require(BallClassifier.Classify([]).Kind == "unknown" && BallClassifier.Classify(Enumerable.Repeat(new HsvPixel(100, 130, 120), 50).ToArray()).Kind == "unknown", "No guessed classification for empty/cloth region");
        Console.WriteLine("PASS repeatable bounded colour clustering, spotted cream cue, orange yellows and empty/cloth rejection");
        CheckPlanner(reference);
        CheckTracking();
        CheckCarousel();
    }
    static void CheckPlanner(JsonDocument reference)
    {
        int generated = 0;
        foreach (var fixture in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var candidates = ShotPlanner.Generate(Point(fixture.GetProperty("cue")), Point(fixture.GetProperty("target")), new(Radius: 23, CushionInset: 40), pythonRounding: true);
            foreach (var candidate in candidates)
            {
                var paths = candidate.Paths;
                // Bounded reflections may omit impossible upstream candidates. Each retained
                // path must match an independently generated original Python result.
                bool matched = fixture.GetProperty("shots").EnumerateArray().Any(expected =>
                    Math.Abs(paths.Length - expected.GetProperty("length").GetDouble()) < 1e-6 &&
                    Math.Abs(paths.Difficulty - expected.GetProperty("difficulty").GetDouble()) < 1e-6 &&
                    (int)paths.Ghost.X == expected.GetProperty("ghost")[0].GetInt32() && (int)paths.Ghost.Y == expected.GetProperty("ghost")[1].GetInt32());
                Require(matched, "Generated reflection, angle and ranking match Python: " + JsonSerializer.Serialize(candidate)); generated++;
            }
        }
        Require(generated > 240, "Most upstream reflection candidates retained");
        Require(ShotPlanner.Reflect(new(10, 10), new(20, 10), new(0, 0), new(0, 30)) is { X: 0, Y: 10 }, "Vertical reflection intersection");
        Require(ShotPlanner.Reflect(new(10, 90), new(20, 80), new(0, 0), new(0, 30)) is null, "Finite rail bounds reject outside bounce");
        Require(ShotPlanner.Reflect(new(0, 0), new(0, 20), new(0, 0), new(0, 30)) is null, "Parallel reflection rejected");
        var coordinate = new AnalysisCoordinates(); var now = DateTimeOffset.UtcNow;
        var state = new TableState(31, now, [new("cue", "cue", new(1.5, .3), 1), new("red", "red", new(.8, .6), 1)], "red", true, false, true, "test");
        var valid = ShotPlanner.Generate(coordinate.FromTable(state.Balls[0].Position), coordinate.FromTable(state.Balls[1].Position))
            .Where(c => ShotPlanner.Valid(c, coordinate.FromTable(state.Balls[0].Position), coordinate.FromTable(state.Balls[1].Position), [])).ToArray();
        Require(valid.Any(c => c.TargetCushion >= 0) && valid.Any(c => c.CueCushion >= 0) && valid.Any(c => c.TargetCushion < 0 && c.CueCushion < 0), "Direct, bank and kick candidates exist");
        var sample = valid[0];
        Require(!ShotPlanner.Valid(sample with { Paths = sample.Paths with { Angle = double.NaN } }, coordinate.FromTable(state.Balls[0].Position), coordinate.FromTable(state.Balls[1].Position), []), "Non-finite cut angle rejected");
        Require(!ShotPlanner.Valid(sample, coordinate.FromTable(state.Balls[0].Position), coordinate.FromTable(state.Balls[1].Position), [new(double.NaN, 500)]), "Non-finite obstacle rejected");
        Require(!ShotPlanner.Valid(sample with { Paths = sample.Paths with { CuePath = [] } }, coordinate.FromTable(state.Balls[0].Position), coordinate.FromTable(state.Balls[1].Position), []), "Missing path legs rejected");
        foreach (var candidate in valid.Where(c => c.Paths.CuePath.Length > 1 || c.Paths.TargetPath.Length > 1))
        {
            var leg = candidate.Paths.CuePath.Length > 1 ? candidate.Paths.CuePath[1] : candidate.Paths.TargetPath[1];
            Require(!ShotPlanner.Valid(candidate, coordinate.FromTable(state.Balls[0].Position), coordinate.FromTable(state.Balls[1].Position), [(leg.Start + leg.End) * .5]), "An obstacle blocks the second reflected leg");
        }
        string before = JsonSerializer.Serialize(state); var suggestions = ShotPlanner.Suggest(state, now);
        Require(suggestions.Length == 3 && suggestions.All(s => s.TargetPath.Length is 2 or 3 && s.CuePath.Length is 2 or 3 && s.TableVersion == 31), "Three complete versioned candidates");
        Require(suggestions.SequenceEqual(suggestions.OrderBy(s => s.Difficulty)), "Nonmutating source ranking");
        Require(before == JsonSerializer.Serialize(state) && JsonSerializer.Serialize(suggestions) == JsonSerializer.Serialize(ShotPlanner.Suggest(state, now)), "Deterministic ranking preserves table state");
        foreach (var invalid in new[] { state with { CapturedAt = now.AddSeconds(-7) }, state with { CapturedAt = now.AddSeconds(3) }, state with { Settled = false }, state with { Occluded = true }, state with { Confirmed = false }, state with { CalibrationId = "" }, state with { Group = "bogus" }, state with { Balls = [state.Balls[0] with { Confidence = double.NaN }, state.Balls[1]] } })
            Require(ShotPlanner.Suggest(invalid, now).Length == 0, "Freshness, motion, obstruction, confirmation and finite-quality gates");
        Require(ShotPlanner.Suggest(state with { Group = "black" }, now).Length == 0, "No guessed legal black on missing black");
        Console.WriteLine($"PASS {generated} generated Python reflections, bounded rails, direct/bank/kick candidates, second-leg obstruction, stable top-three ranking and advice gates");
    }
    static void CheckCarousel()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new TableState(31, now, [new("cue", "cue", new(1.5, .3), 1), new("red", "red", new(.8, .6), 1)], "red", true, false, true, "test");
        var carousel = new CandidateCarousel(); carousel.Start(1000); carousel.Update(state, now);
        Require(carousel.Candidates.Length == 3, "Carousel has validated candidates");
        Require(carousel.Index(2999, now) == 0 && carousel.Index(3000, now) == 1 && carousel.Index(5000, now) == 2 && carousel.Index(7000, now) == 0, "Two-second cycle boundaries and wrapping");
        Require(carousel.Current(9000, now.AddSeconds(10)) is not null && ShotPlanner.Suggest(state, now.AddSeconds(10)).Length == 0, "Presentation cache covers ten-second refresh without loosening advice freshness");
        Require(carousel.Current(9000, now.AddSeconds(15)) is null, "Presentation expires when refresh stops");
        carousel.Update(state with { Occluded = true }, now); Require(carousel.Current(10000, now) is null, "Uncertain refresh clears old paths");
        var tracker = new TableTracker(15);
        tracker.Observe("cal", "timeline", now, state.Balls, "red", false, false);
        tracker.Observe("cal", "timeline", now.AddSeconds(3), state.Balls, "red", false, false); tracker.Confirm(now.AddSeconds(3));
        Require(tracker.Observe("cal", "timeline", now.AddSeconds(13), state.Balls, "red", false, false).Confirmed, "Ten-second sampling preserves a stable layout");
        Require(!tracker.Observe("cal", "timeline", now.AddSeconds(29), state.Balls, "red", false, false).Confirmed, "A missed sampling interval invalidates confirmation");
        Console.WriteLine("PASS two-second carousel, ten-second sampling, bounded presentation expiry and independent strict advice freshness");
    }
    static void CheckTracking()
    {
        var tracker = new TableTracker(); var now = DateTimeOffset.UtcNow;
        Ball[] balls = [new("cue", "cue", new(1.5, .3), 1), new("red", "red", new(.8, .6), 1)];
        TableState Observe(double seconds, string timeline = "a", string calibration = "one", bool uncertain = false, bool obstruction = false, Ball[]? detected = null) => tracker.Observe(calibration, timeline, now.AddSeconds(seconds), detected ?? balls, "red", uncertain, obstruction);
        Require(!Observe(0).Settled && !Observe(0).Settled, "Duplicate snapshots cannot establish settlement");
        var settled = Observe(2); Require(settled.Settled && !settled.Confirmed, "Distinct stable observations establish settlement");
        var confirmed = tracker.Confirm(now.AddSeconds(2)); Require(confirmed.Confirmed, "User confirmation applies to a fresh settled frame");
        var fresh = Observe(4); Require(fresh.Confirmed && fresh.Version == confirmed.Version, "Stable layout keeps its candidate IDs");
        var interrupted = Observe(6, timeline: "b"); Require(!interrupted.Settled && !interrupted.Confirmed && interrupted.Version != fresh.Version, "Capture timeline change discards confirmation");
        Observe(8, timeline: "b"); tracker.Confirm(now.AddSeconds(8));
        Require(!Observe(10, timeline: "b", calibration: "two").Confirmed, "Calibration change discards confirmation");
        Observe(12, timeline: "b", calibration: "two", uncertain: true);
        var corrected = tracker.Correct(balls, now.AddSeconds(12));
        Require(corrected.Confirmed && !corrected.Occluded, "Manual review can resolve colour uncertainty");
        Require(Observe(14, timeline: "b", calibration: "two", uncertain: true).Confirmed, "Corrections persist only on unchanged automatic observations");
        var obscured = Observe(16, timeline: "b", calibration: "two", obstruction: true);
        Require(!obscured.Confirmed && obscured.Occluded, "Physical obstruction invalidates corrections");
        bool rejected = false; try { tracker.Correct(balls, now.AddSeconds(16)); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "Correction cannot bypass physical obstruction");
        Observe(18, timeline: "b", calibration: "two"); tracker.Confirm(now.AddSeconds(18));
        Require(!Observe(25, timeline: "b", calibration: "two").Confirmed, "Long frame gap resets settlement and confirmation");
        Observe(27, timeline: "b", calibration: "two"); tracker.Confirm(now.AddSeconds(27));
        Require(!Observe(29, timeline: "b", calibration: "two", detected: [balls[0], balls[1] with { Position = new(.9, .6) }]).Confirmed, "Movement invalidates confirmed positions");
        tracker.Reset(); Require(tracker.State is null, "Lifecycle reset clears state");
        Observe(40); Observe(42, detected: [balls[0], balls[1] with { Position = new(.811, .6) }]); tracker.Confirm(now.AddSeconds(42));
        Require(!Observe(44, detected: [balls[0], balls[1] with { Position = new(.822, .6) }]).Confirmed, "Slow cumulative movement cannot drift away from the stable anchor");
        Console.WriteLine("PASS frame settlement, duplicate/gap/timeline/calibration rejection, reviewed corrections, movement and physical-obstruction invalidation");
    }
}
