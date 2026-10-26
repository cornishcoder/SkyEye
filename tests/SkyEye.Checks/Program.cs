using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SkyEye.Core;

if (args.SequenceEqual(["--shot-samples"]))
{
    await ShotSampleChecks.Run();
    return;
}

if (args.SequenceEqual(["--game-tracking"]))
{
    await VerificationCaptureChecks.Run();
    GameJournalChecks.Run();
    await GameIntegrationChecks.Run();
    await SettledBaselineChecks.Run();
    SettledOutcomeReviewChecks.Run();
    StableTableChangeTrackerChecks.Run();
    await ManualMatchAppChecks.Run();
    InternationalRulesChecks.Run();
    await RulesApplicationChecks.Run();
    await VerificationChecks.Run();
    return;
}

await VerificationCaptureChecks.Run();
GameJournalChecks.Run();
await GameIntegrationChecks.Run();
await SettledBaselineChecks.Run();
SettledOutcomeReviewChecks.Run();
StableTableChangeTrackerChecks.Run();
await ManualMatchAppChecks.Run();
InternationalRulesChecks.Run();
await RulesApplicationChecks.Run();
await MotionFeedChecks.Run();
await VerificationChecks.Run();
ShotHudChecks.Run();

static void Require(bool yes, string reason) { if (!yes) throw new Exception(reason); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
var sceneTime = DateTimeOffset.UtcNow;
var scenePaths = new List<ScenePath> { new([new(.2, .3), new(.8, .7)], SceneInk.Cyan, true) };
var scene = new TableScene(2, 3, 4, sceneTime, TimeSpan.FromSeconds(10), scenePaths,
    [new(new(.5, .5), .02, SceneInk.Yellow, SceneMarkerKind.Ball)], [new(new(.5, .6), "Target")], "Choices", new("Alice", 2, "Bob", 1));
scenePaths.Clear();
Require(scene.Paths.Length == 1 && scene.Paths[0].Selected, "Scene must retain an immutable copy of published paths");
Require(scene.IsCurrent(3, sceneTime.AddSeconds(9)) && !scene.IsCurrent(3, sceneTime.AddSeconds(10)) && !scene.IsCurrent(4, sceneTime), "Scene expires exactly at its deadline and cannot move between media generations");
Throws<ArgumentException>(() => new TableScene(0, 3, 4, sceneTime, TimeSpan.FromSeconds(10), [], [], []));
Throws<ArgumentException>(() => new TableScene(2, 3, 4, sceneTime, TimeSpan.FromSeconds(61), [], [], []));
Throws<ArgumentException>(() => new TableScene(2, 3, 4, sceneTime, TimeSpan.FromSeconds(10), [new([new(double.NaN, .2), new(.5, .5)], SceneInk.Cyan)], [], []));
Throws<ArgumentException>(() => new TableScene(2, 3, 4, sceneTime, TimeSpan.FromSeconds(10), [], [new(new(1.1, .2), .02, SceneInk.Cyan, SceneMarkerKind.Ball)], []));
Throws<ArgumentException>(() => new TableScene(2, 3, 4, sceneTime, TimeSpan.FromSeconds(10), [], [], [], "Bad\ncaption"));
Throws<ArgumentException>(() => new TableScene(2, 3, 4, sceneTime, TimeSpan.FromSeconds(10), Enumerable.Repeat(scene.Paths[0], 17), [], []));
Throws<ArgumentException>(() => new TableScene(2, 3, 4, sceneTime, TimeSpan.FromSeconds(10), [], [], [], score: new("Alice", -1, "Bob", 1)));
Console.WriteLine("PASS immutable, scoped, expiring scene geometry, bounded content and score validation");
var time = new TestTime(); var authority = new SessionAuthority(time);
var winners = new System.Collections.Concurrent.ConcurrentBag<Lease>();
Parallel.For(0, 50, i => { try { winners.Add(authority.Claim("phone " + i)); } catch (InvalidOperationException) { } });
Require(winners.Count == 1, "Simultaneous arrivals must grant exactly one host"); var winner = winners.Single();
Throws<UnauthorizedAccessException>(() => authority.Require("guest"));
authority.Transition(CaptureState.Active); var firstSession = authority.SessionId;
authority.Transition(CaptureState.Paused); authority.Transition(CaptureState.Active);
Require(authority.SessionId == firstSession, "Resume keeps the pool session identity");
time.Advance(25); authority.Heartbeat(winner.Token); time.Advance(20); authority.Require(winner.Token);
time.Advance(11); Throws<UnauthorizedAccessException>(() => authority.Require(winner.Token));
Require(authority.State == CaptureState.Active, "Host expiry must not stop capture");
var next = authority.Claim("new host"); Throws<UnauthorizedAccessException>(() => authority.Release(winner.Token));
Require(next.Generation > winner.Generation && next.Token != winner.Token, "Handover invalidates previous authority");
authority.Transition(CaptureState.Stopped); authority.Transition(CaptureState.Active); Require(authority.SessionId != firstSession, "New recording starts a new session");
Throws<InvalidOperationException>(() => authority.Transition(CaptureState.Ready));
Console.WriteLine("PASS atomic ownership, guest rejection, lease expiry, stale-token rejection and session transitions");

var history = Enumerable.Range(0, 310).Select(i => new Segment("s" + i, i, "one", i * 2_000_000L, 2, DateTimeOffset.UnixEpoch.AddSeconds(i * 2))).ToArray();
foreach (int duration in ReplaySelection.Durations) { var selected = ReplaySelection.Select(history, duration, 620_000_000); Require(Math.Abs(selected.Sum(x => x.Duration) - duration) < .001, "Replay duration " + duration); }
var afterPause = history.Concat([new Segment("new", 400, "two", 900_000_000, 2, DateTimeOffset.UnixEpoch)]).ToArray();
Require(ReplaySelection.Select(afterPause, 600, 902_000_000).Length == 1, "Never combine footage across pause discontinuities");
Require(ReplaySelection.Select(history.Take(3), 600, 6_000_000).Sum(x => x.Duration) == 6, "Report actual available short history");
Throws<ArgumentException>(() => ReplaySelection.Select(history, 90, 620_000_000));
Console.WriteLine("PASS all replay intervals, short history and pause discontinuities");

var calibration = new TableCalibration([new(.1, .1), new(.9, .15), new(.85, .9), new(.15, .85)]);
Point[] expected = [new(0, 0), new(2, 0), new(2, 1), new(0, 1)];
for (int i = 0; i < 4; i++) Require((calibration.Map(calibration.Corners[i]) - expected[i]).Length < 1e-8, "Perspective corner mapping");
Throws<ArgumentException>(() => new TableCalibration([new(0, 0), new(1, 1), new(1, 0), new(0, 1)]));
var now = DateTimeOffset.UtcNow;
var table = new TableState(1, now, [new("cue", "cue", new(1.8, .5), 1), new("red", "red", new(1, .5), 1), new("black", "black", new(.2, .8), 1)], "red", true, false, true, "calibration");
var pots = PoolGeometry.Suggest(table, now); Require(pots.Length > 0 && pots.All(p => p.BallId == "red"), "Only on-group pots");
Require(PoolGeometry.Suggest(table with { Group = "open" }, now).All(p => p.BallId != "black"), "Black is excluded on open table");
Require(PoolGeometry.Suggest(table with { CapturedAt = now.AddSeconds(-7) }, now).Length == 0, "Stale layouts suppress advice");
Require(PoolGeometry.Suggest(table with { Settled = false }, now).Length == 0 && PoolGeometry.Suggest(table with { Occluded = true }, now).Length == 0 && PoolGeometry.Suggest(table with { Confirmed = false }, now).Length == 0, "Moving, occluded and unconfirmed layouts suppress advice");
var blocked = table with { Balls = table.Balls.Concat([new Ball("blocker", "yellow", new(.5, .25), 1)]).ToArray() };
Require(PoolGeometry.Suggest(blocked, now).All(p => p.Pocket != 0 || p.Type != "direct"), "Obstacle excludes the blocked direct route");
Console.WriteLine("PASS perspective calibration, target-group legality, obstruction and stale/uncertain state rejection");

int width = 400, height = 200; int[] image = Enumerable.Repeat(unchecked((int)0xff087eac), width * height).ToArray();
void Circle(int cx, int cy, int colour) { for (int y = cy - 6; y <= cy + 6; y++) for (int x = cx - 6; x <= cx + 6; x++) if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= 36) image[y * width + x] = colour; }
Circle(320, 100, unchecked((int)0xffeeeeee)); Circle(200, 80, unchecked((int)0xffe01818)); Circle(100, 130, unchecked((int)0xffeec820)); Circle(70, 60, unchecked((int)0xff101010));
var detected = BallDetector.Detect(image, width, height, new([new(0, 0), new(1, 0), new(1, 1), new(0, 1)]));
Require(!detected.Occluded && detected.Balls.Length == 4 && detected.Balls.Select(b => b.Kind).Distinct().Count() == 4, "Synthetic blue-cloth colour detection");
Console.WriteLine("PASS synthetic ball colours and connected components (not real-table perception evidence)");

var fragmented = new FragmentedStream(Encoding.ASCII.GetBytes("POST /test?name=pool%20table HTTP/1.1\r\nContent-Length: 2\r\nX-SkyEye-Host: token\r\n\r\n{}"));
var parsed = await LocalHttpServer.ReadRequestAsync(fragmented, default);
Require(parsed.Query["name"] == "pool table" && parsed.HostToken == "token" && Encoding.ASCII.GetString(parsed.Body) == "{}", "Fragmented HTTP parsing");
foreach (string invalid in new[] { "Content-Length: -1", "Content-Length: 100000", "Transfer-Encoding: chunked", "Content-Length: 0\r\nContent-Length: 1" })
{
    try { await LocalHttpServer.ReadRequestAsync(new MemoryStream(Encoding.ASCII.GetBytes("POST / HTTP/1.1\r\n" + invalid + "\r\n\r\n")), default); throw new Exception("Invalid header accepted"); } catch (ArgumentException) { }
}
Console.WriteLine("PASS fragmented HTTP parsing, body bounds, duplicate lengths and chunked rejection");

var allocation = new TcpListener(IPAddress.Loopback, 0); allocation.Start(); int port = ((IPEndPoint)allocation.LocalEndpoint).Port; allocation.Stop();
string fixture = Path.Combine(Path.GetTempPath(), "skyeye-" + Guid.NewGuid().ToString("N")); await File.WriteAllTextAsync(fixture, "0123456789");
try
{
    await using var server = new LocalHttpServer(port, (r, ct) => Task.FromResult(r.Path == "/echo" ? HttpResponse.Json(new { payload = r.Json<Dictionary<string, string>>(), authorized = r.HostToken == "integration-token" }) : r.Path == "/file" ? new HttpResponse(200, "video/mp4", FilePath: fixture) : HttpResponse.Json(new { error = "missing" }, 404))); server.Start();
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    using var range = new HttpRequestMessage(HttpMethod.Get, "/file"); range.Headers.Range = new(3, 5); using var result = await client.SendAsync(range);
    Require(result.StatusCode == HttpStatusCode.PartialContent && await result.Content.ReadAsStringAsync() == "345", "Resume range payload");
    Require(result.Content.Headers.ContentRange?.ToString() == "bytes 3-5/10", "Correct range metadata");
    using var beyond = new HttpRequestMessage(HttpMethod.Get, "/file"); beyond.Headers.Range = new(100, null); using var rejected = await client.SendAsync(beyond);
    Require((int)rejected.StatusCode == 416 && await rejected.Content.ReadAsStringAsync() == "", "Out-of-range has no file payload");
    using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/file")); Require(head.Content.Headers.ContentLength == 10 && (await head.Content.ReadAsByteArrayAsync()).Length == 0, "HEAD avoids file body");
    using var cameraClient = new SkyEye.Companion.CameraClient(); cameraClient.Connect($"127.0.0.1:{port}"); cameraClient.HostToken = "integration-token";
    var echoed = await cameraClient.PostAsync("echo", new { owner = "SkyEye companion" });
    Require(echoed.GetProperty("payload").GetProperty("owner").GetString() == "SkyEye companion" && echoed.GetProperty("authorized").GetBoolean(), "Companion commands interoperate with camera HTTP framing and authorization");
}
finally { File.Delete(fixture); }
Console.WriteLine("PASS real HTTP download ranges, invalid offsets and HEAD");
Console.WriteLine("PASS real companion command client against the camera HTTP server");
PortChecks.Run();
await AppCoordinatorChecks.Run();
await SceneCommandChecks.Run();
await ClipSaveChecks.Run();
await ShotSampleChecks.Run();
await CameraClipSaveChecks.Run();
await CameraCommandChecks.Run();
await CameraControlAdapterChecks.Run();
await TableAnalysisChecks.Run();
await ObservationCommandChecks.Run();
await LiveTransportChecks.Run();
BackgroundDeliveryChecks.Run();
await DecodedFrameChecks.Run();
await ClipLibraryChecks.Run();
await RecentReplayChecks.Run();
await RecordedGeometryChecks.Run();
await RecordedRenderChecks.Run();
await RecordedAnnotationChecks.Run();
await RecordedAnnotationPlanChecks.Run();
await CorrectedClipChecks.Run();
await RecentReplayHttpChecks.Run();
await CameraReadEffectChecks.Run();

sealed class TestTime : TimeProvider { DateTimeOffset current = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => current; public void Advance(int seconds) => current = current.AddSeconds(seconds); }
sealed class FragmentedStream(byte[] data) : MemoryStream(data) { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], ct); }

