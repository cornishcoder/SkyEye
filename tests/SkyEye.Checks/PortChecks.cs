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
    }
}
