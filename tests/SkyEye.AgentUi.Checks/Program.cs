using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using SkyEye.Companion.Hybrid.Probes;

static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
using var client = new ProbeChatClient(TimeSpan.Zero);
using var agent = ProbeAgent.Create(client);
var blocks = new List<ContentBlock>(); bool pending = false;
await foreach (var block in agent.SendMessageAsync(new(ChatRole.User, "fixture")))
{
    blocks.Add(block);
    if (block is ProbeShotBlock shot && !shot.HasResult) pending = true;
}
var tool = blocks.OfType<ProbeShotBlock>().Single();
Check(pending && tool.HasResult && tool.Group == "red" && tool.CandidateCount == 3, "typed tool pending/result mapping");
Check(agent.State.Value == new ProbeState(1, "red", 3) && client.CompletedOperations == 1, "application snapshot mapping");
Check(blocks.OfType<RichContentBlock>().Any(b => b.RawText.Contains("Three fixture choices")), "streamed text update");
Console.WriteLine("PASS real Blazor AI streaming, source-generated typed tool block and observable state mapping");

using var slowClient = new ProbeChatClient(TimeSpan.FromSeconds(2));
using var slowAgent = ProbeAgent.Create(slowClient);
using var cancellation = new CancellationTokenSource();
bool cancelled = false;
try
{
    await foreach (var block in slowAgent.SendMessageAsync(new(ChatRole.User, "cancel fixture"), cancellation.Token))
        if (block is ProbeShotBlock) cancellation.Cancel();
}
catch (OperationCanceledException) { cancelled = true; }
Check(cancelled && slowClient.CompletedOperations == 0 && slowAgent.State.Value.Version == 0, "cancel before fixture effect");
Console.WriteLine("PASS cancellation leaves canonical fixture state unchanged");

using var previewClient = new ProbeChatClient(TimeSpan.Zero);
using var previewAgent = ProbeAgent.Create(previewClient);
using var context = new AgentContext(previewAgent);
TaskCompletionSource<UIActionBlock> actionReady = new();
using var registration = context.RegisterOnBlockAdded((_, block) =>
{
    if (block is UIActionBlock action) actionReady.TrySetResult(action);
});
async Task<(Task Turn, UIActionBlock Action)> Propose()
{
    actionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var turn = context.SendMessageAsync("Propose a fixture group");
    var action = await actionReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!turn.IsCompleted && previewAgent.State.HasPendingPredictiveState, "boundary retains preview until human decision");
    return (turn, action);
}
async Task Decide((Task Turn, UIActionBlock Action) proposal, bool accept)
{
    previewClient.ProposalDecision = accept;
    await proposal.Action.InvokeAsync();
    await proposal.Turn.WaitAsync(TimeSpan.FromSeconds(5));
    Check(previewAgent.State.Value == previewClient.State && !previewAgent.State.HasPendingPredictiveState, "completed boundary reflects canonical state");
}
var first = await Propose();
Check(previewAgent.State.HasPendingPredictiveState && previewAgent.State.Value.Group == "yellow" && previewClient.State.Group == "open", "preview is separate from canonical fixture");
await Decide(first, false);
Check(!previewAgent.State.HasPendingPredictiveState && previewAgent.State.Value.Group == "open" && previewClient.State.Version == 0, "reject preserves original state");
var second = await Propose();
await Decide(second, true);
Check(previewClient.State.Version == 1 && previewClient.State.Group == "yellow", "accept commits once");
await second.Action.InvokeAsync();
Check(previewClient.State.Version == 1, "duplicate UI action does not commit twice");
var stale = await Propose(); previewClient.ChangeLayout();
await Decide(stale, true);
Check(previewAgent.State.Value == previewClient.State && previewClient.State.Version == 2, "obsolete proposal cannot apply or roll back a newer layout");
var rejectChanged = await Propose(); previewClient.ChangeLayout(); await Decide(rejectChanged, false);
Check(previewAgent.State.Value.Version == 3, "reject after concurrent change preserves newest canonical state");
var cancelledPreview = await Propose();
await context.CancelAsync();
await cancelledPreview.Turn;
ProbeAgent.Resolve(previewAgent, previewClient, false);
Check(previewClient.State.Version == 3 && !previewAgent.State.HasPendingPredictiveState, "cancel preview preserves canonical state");
Console.WriteLine("PASS real AgentContext predictive preview, registered UI action, accept/reject, duplicate and stale version handling");

// Drive native media effect selection through interruption and competing requests.
var media = new SkyEye.Companion.Hybrid.PlaybackSelection();
var liveGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.Live);
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, null), "inactive camera waits without cancelling live selection");
Check(media.Kind == SkyEye.Companion.Hybrid.ViewingSource.Live && media.Source is null, "waiting camera retains viewing intent");
Check(media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "active camera starts after waiting");
media.Started();
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "unchanged observation or sheet render never reloads media");
media.Suspend();
Check(media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "foreground or handler replacement restarts validated source");
media.Started();
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "private-live-source"), "resume restarts once");
media.Stop();
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "obsolete-source") && media.Source is null, "late refresh cannot undo stop");
var previewGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.Preview);
Check(!media.Observe(liveGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "obsolete-source"), "live reply cannot replace selected preview");
Check(media.Observe(previewGeneration, SkyEye.Companion.Hybrid.ViewingSource.Preview, "private-local-file"), "current preview publishes");
media.Started();
var nextGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.Live);
Check(!media.Observe(previewGeneration, SkyEye.Companion.Hybrid.ViewingSource.Preview, "late-export-file"), "late preview cannot replace reconnect");
Check(!media.Observe(nextGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, null) && media.NeedsStart, "loss of reachability removes media but permits validated recovery");
Check(media.Observe(nextGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "new-live-source"), "validated recovery can start after reachability loss");
Console.WriteLine("PASS portrait media selection: inactive-to-active, stable refresh, resume/detach, stop, obsolete live/preview and recovery");

var tvReplay = new SkyEye.Companion.Hybrid.TvReplaySelection();
var tvCamera = Guid.NewGuid(); var tvClip = Guid.NewGuid().ToString("N");
var firstTvRequest = tvReplay.Begin(tvCamera, tvClip, "Saved shot", 30);
Check(!tvReplay.Confirmed && tvReplay.Selected && tvReplay.PositionSeconds == 0, "TV load remains pending until receiver proof");
Check(!tvReplay.Observe(firstTvRequest, Guid.NewGuid().ToString("N"), 3000, 30000, 2, 0), "unrelated receiver clip rejected");
Check(tvReplay.Observe(firstTvRequest, tvClip, 3100, 30000, 2, 0) && tvReplay.PositionSeconds == 3, "receiver position accepted for current request");
Check(!tvReplay.Observe(firstTvRequest, tvClip, 3200, 30000, 2, 0), "subsecond progress does not churn UI");
var nextTvRequest = tvReplay.Begin(tvCamera, tvClip, "Replay again", 30);
Check(!tvReplay.Observe(firstTvRequest, tvClip, 8000, 30000, 2, 0) && !tvReplay.Confirmed, "late completion from same clip previous load rejected");
Check(tvReplay.Observe(nextTvRequest, tvClip, 30000, 30000, 1, 1) && tvReplay.IdleReason == 1, "receiver completion preserved without restarting clip");
Check(!tvReplay.Observe(nextTvRequest, tvClip, 0, 30000, 1, 1) && tvReplay.PositionSeconds == 30, "receiver idle reset cannot erase finished position");
tvReplay.Clear();
Check(!tvReplay.Observe(nextTvRequest, tvClip, 4000, 30000, 2, 0) && !tvReplay.Selected, "return-live and disconnect reject late replay progress");
Console.WriteLine("PASS buffered TV replay: pending load, scoped receiver progress, duplicate, same-clip replacement, finish and return-live");

var commandTime = DateTimeOffset.UnixEpoch;
var commandRequest = tvReplay.Begin(tvCamera, tvClip, "Control fixture", 30.4);
var pauseId = Guid.NewGuid().ToString("N");
Check(tvReplay.BeginCommand(commandRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime) is null, "unconfirmed load cannot receive replay commands");
tvReplay.Observe(commandRequest, tvClip, 6000, 30400, 2, 0);
Check(tvReplay.BeginCommand(firstTvRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime) is null, "old rendered request cannot control replacement clip");
var pauseCommand = tvReplay.BeginCommand(commandRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime);
Check(pauseCommand is not null && tvReplay.CommandPending, "current playing clip admits pause");
Check(tvReplay.BeginCommand(commandRequest, Guid.NewGuid().ToString("N"), SkyEye.Companion.Hybrid.TvReplayAction.Seek, 10, commandTime) is null, "competing command waits for receiver proof");
Check(!tvReplay.ObserveCommand(commandRequest, pauseId, "accepted") && tvReplay.CommandPending, "accepted pause does not invent paused state");
Check(tvReplay.Observe(commandRequest, tvClip, 6000, 30400, 3, 0) && tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "pause confirms only with receiver paused state");
Check(tvReplay.BeginCommand(commandRequest, pauseId, SkyEye.Companion.Hybrid.TvReplayAction.Resume, 0, commandTime) is null, "duplicate acknowledged command ID never resends");
var seekId = Guid.NewGuid().ToString("N");
var seekCommand = tvReplay.BeginCommand(commandRequest, seekId, SkyEye.Companion.Hybrid.TvReplayAction.Seek, 31, commandTime);
Check(seekCommand?.PositionMs == 30150, "end seek bounded inside fractional clip duration");
Check(!tvReplay.ObserveCommand(commandRequest, seekId, "accepted"), "accepted seek still waits for position");
Check(!tvReplay.Observe(commandRequest, tvClip, 6000, 30400, 3, 0) && tvReplay.CommandPending, "unchanged receiver position cannot confirm seek");
Check(tvReplay.Observe(commandRequest, tvClip, 30150, 30400, 3, 0) && tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "seek preserves pause and confirms target position");
var resumeId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, resumeId, SkyEye.Companion.Hybrid.TvReplayAction.Resume, 0, commandTime);
tvReplay.Observe(commandRequest, tvClip, 30150, 30400, 2, 0);
Check(tvReplay.CommandPending && tvReplay.ObserveCommand(commandRequest, resumeId, "accepted"), "status before callback remains pending then confirms resume");
var playingSeekId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, playingSeekId, SkyEye.Companion.Hybrid.TvReplayAction.Seek, 5, commandTime);
Check(!tvReplay.ObserveCommand(commandRequest, playingSeekId, "accepted") && tvReplay.CommandPending, "playing seek requires target proof");
Check(tvReplay.Observe(commandRequest, tvClip, 5500, 30400, 2, 0) && tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "playing seek confirms position while preserving play state");
var rejectedId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, rejectedId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime);
Check(!tvReplay.ObserveCommand(firstTvRequest, rejectedId, "rejected") && tvReplay.CommandPending, "wrong-load rejection ignored");
Check(tvReplay.ObserveCommand(commandRequest, rejectedId, "rejected") && tvReplay.PlayerState == 2, "rejection preserves actual receiver state");
Check(tvReplay.BeginCommand(commandRequest, Guid.NewGuid().ToString("N"), SkyEye.Companion.Hybrid.TvReplayAction.Seek, -1, commandTime) is null, "negative seek rejected");
Check(tvReplay.BeginCommand(commandRequest, Guid.NewGuid().ToString("N"), SkyEye.Companion.Hybrid.TvReplayAction.Seek, long.MaxValue, commandTime) is null, "overflowing seek rejected");
var timeoutId = Guid.NewGuid().ToString("N");
tvReplay.BeginCommand(commandRequest, timeoutId, SkyEye.Companion.Hybrid.TvReplayAction.Pause, 0, commandTime);
Check(!tvReplay.ExpireCommand(commandTime.AddSeconds(9)) && tvReplay.ExpireCommand(commandTime.AddSeconds(10)), "unconfirmed command expires as unknown");
Check(tvReplay.CommandOutcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Unknown && !tvReplay.ObserveCommand(commandRequest, timeoutId, "accepted"), "late acknowledgement cannot rewrite timeout outcome");
tvReplay.Clear();
Check(!tvReplay.ObserveCommand(commandRequest, timeoutId, "accepted") && !tvReplay.CanControl, "return-live/disconnect cancels pending clip commands");
var liveProof = new SkyEye.Companion.Hybrid.TvLiveSelection();
var liveRequest = liveProof.Begin(tvCamera, commandTime);
Check(!liveProof.Observe(liveRequest, "clip", 2) && !liveProof.Observe(liveRequest, "scene", 4), "return-live waits for matching live PLAYING proof");
Check(!liveProof.Observe(Guid.NewGuid().ToString("N"), "camera", 2), "obsolete live media cannot confirm current request");
Check(liveProof.Expire(commandTime.AddSeconds(20)) && liveProof.Outcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Unknown, "buffered live acknowledgement times out honestly");
Check(liveProof.Observe(liveRequest, "scene", 2) && liveProof.Outcome == SkyEye.Companion.Hybrid.TvCommandOutcome.Confirmed, "later current live proof can establish playback");
liveProof.Clear();
Check(!liveProof.Observe(liveRequest, "camera", 2), "camera switch or clip replacement clears live proof");
Console.WriteLine("PASS TV controls: scoped admission, single flight, duplicates, receiver pause/resume/seek proof, fractional end bounds, rejection, timeout and return-live confirmation");

var clipGeneration = media.Begin(SkyEye.Companion.Hybrid.ViewingSource.LocalClip);
Check(media.Observe(clipGeneration, SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "private-portrait-file"), "offline clip selection starts independently of camera reachability");
media.Started();
Check(!media.Observe(nextGeneration, SkyEye.Companion.Hybrid.ViewingSource.Live, "late-camera-source") && media.Kind == SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "camera refresh cannot replace offline playback");
media.Suspend();
Check(media.Observe(clipGeneration, SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "private-portrait-file"), "offline clip survives foreground or handler replacement");
media.Started(); media.Stop();
Check(!media.Observe(clipGeneration, SkyEye.Companion.Hybrid.ViewingSource.LocalClip, "private-portrait-file") && media.Source is null, "late local selection cannot undo stop");
Console.WriteLine("PASS offline clip selection: camera-independent playback, obsolete camera response, foreground recovery and stop");

var elapsed = TimeSpan.Zero;
var chrome = new SkyEye.Companion.Hybrid.ControlChrome(() => elapsed);
elapsed = TimeSpan.FromSeconds(30); chrome.Tick();
Check(chrome.Visible, "empty or buffering table keeps connect controls visible");
chrome.CanFade = true; chrome.Reveal(); elapsed += TimeSpan.FromSeconds(4.9); chrome.Tick();
Check(chrome.Visible, "short inactivity threshold keeps controls available");
elapsed += TimeSpan.FromSeconds(.1); chrome.Tick();
Check(!chrome.Visible, "playing table rests after five seconds");
chrome.SetEditing(true); elapsed += TimeSpan.FromMinutes(2); chrome.Tick();
Check(chrome.Visible, "sheet editing pins controls beyond inactivity timeout");
chrome.SetEditing(false); elapsed += TimeSpan.FromSeconds(4); chrome.Tick();
Check(chrome.Visible, "closing sheet gives a fresh interaction window");
elapsed += TimeSpan.FromSeconds(2); chrome.Tick(); Check(!chrome.Visible, "controls can rest again after editing");
chrome.SetAttention(true); elapsed += TimeSpan.FromMinutes(2); chrome.Tick();
Check(chrome.Visible, "actionable error pins controls");
chrome.SetAttention(false); elapsed += TimeSpan.FromSeconds(6); chrome.Tick(); Check(!chrome.Visible, "acknowledged message releases pin");
chrome.SetAccessibility(true); elapsed += TimeSpan.FromMinutes(2); chrome.Tick();
Check(chrome.Visible, "touch exploration never fades available controls");
chrome.SetAccessibility(false); elapsed += TimeSpan.FromSeconds(6); chrome.Tick();
Check(!chrome.Visible, "leaving accessibility interaction returns to normal fade behavior");
chrome.Reveal(); Check(chrome.Visible, "tap or swipe restores controls");
Console.WriteLine("PASS transient controls: idle/buffering, inactivity, editing, error acknowledgment, touch exploration and reveal");

var fit = SkyEye.Companion.Hybrid.SceneProjection.Portrait(400, 600, 1920, 1080);
var sourcePoint = new SkyEye.Core.Point(.2, .7);
var mappedPoint = fit.Screen(sourcePoint);
Check((fit.Image(mappedPoint)!.Value - sourcePoint).Length < 1e-10, "portrait frozen-frame mapping round trips touch and drawing");
Check(fit.Image(new(0, 300)) is null, "letterbox space cannot become a corner or ball");
Check(fit.Screen(new(0, 0)).X > fit.Screen(new(0, 1)).X && fit.Screen(new(1, 0)).Y > fit.Screen(new(0, 0)).Y, "camera corners rotate clockwise into portrait review order");
var corners = new SkyEye.Core.Point[] { new(.1, .1), new(.9, .1), new(.9, .9), new(.1, .9) };
var draft = new SkyEye.Companion.Hybrid.TableEditDraft();
draft.BeginCalibration(4, corners); draft.MoveCorner(0, new(.12, .11));
Check(corners[0] == new SkyEye.Core.Point(.1, .1) && draft.Corners[0] != corners[0], "adjusting proposed corners does not alter saved calibration or proposal");
Check(draft.Matches(4, null) && !draft.Matches(5, null), "obsolete calibration draft cannot match a newer service generation");
draft.ResetCorners(); draft.AddCorner(new(-.1, .5)); Check(draft.Corners.Count == 0, "manual corners reject points outside frozen image");
foreach (var corner in corners) draft.AddCorner(corner);
draft.AddCorner(new(.5, .5)); Check(draft.Corners.Count == 4, "calibration review retains exactly four corners until explicit acceptance");
draft.UndoCorner(); Check(draft.Corners.Count == 3, "manual corner review supports undo");
var calibration = new SkyEye.Core.TableCalibration(corners);
var originalLayout = new SkyEye.Core.TableState(12, DateTimeOffset.UtcNow,
    [new("cue", "cue", new(.4, .4), 1), new("red", "red", new(1.2, .5), 1)], "open", true, false, true, "mount");
draft.BeginCorrection(8, originalLayout, calibration); draft.Select(1); draft.SetKind("yellow");
draft.Place(calibration.Unmap(new(1.4, .6)));
Check(originalLayout.Balls[1].Kind == "red" && originalLayout.Balls[1].Position == new SkyEye.Core.Point(1.2, .5), "ball preview cannot mutate confirmed layout before apply");
Check(draft.Balls[1].Kind == "yellow" && (draft.Balls[1].Position - new SkyEye.Core.Point(1.4, .6)).Length < 1e-9, "drag and colour changes affect reviewed draft");
Check(draft.Matches(8, 12) && !draft.Matches(8, 13) && !draft.Matches(9, 12), "layout or source changes invalidate reviewed correction");
draft.AddBallMode(); draft.SetKind("black"); draft.Place(calibration.Unmap(new(1.6, .4)));
Check(draft.Balls.Count == 3 && draft.Selected == 2 && draft.Balls[2].Kind == "black", "adding a ball needs a cloth location and chooses it for review");
draft.DeleteSelected(); Check(draft.Balls.Count == 2 && draft.Selected == -1, "remove changes preview only");
Check(draft.LayoutText().Contains("yellow"), "reviewed draft serializes to existing validated correction parser");
draft.Clear(); Check(draft.Mode == SkyEye.Companion.Hybrid.TableEditMode.None && draft.Balls.Count == 0 && originalLayout.Balls.Length == 2, "reject clears preview while preserving canonical layout");
var unknownLayout = originalLayout with { Balls = [new("uncertain", "unknown", new(.7, .5), .2)] };
draft.BeginCorrection(8, unknownLayout, calibration); draft.Select(0); draft.AddBallMode();
Check(draft.Kind == "red" && draft.Selected == -1 && unknownLayout.Balls[0].Kind == "unknown", "adding after an uncertain detection starts with a valid colour without changing that detection");
draft.Place(calibration.Unmap(new(1.4, .5)));
Check(draft.Balls[1].Kind == "red", "new ball cannot inherit an unsupported detection colour");
draft.Clear();
Console.WriteLine("PASS frozen review: portrait inverse/letterbox mapping, manual corner adjustment/undo, isolated ball edits, apply scope and cancel");

// Perspective camera measurements share the exact matrix sent to Android.
var skewed = new SkyEye.Core.TableCalibration([new(.2, .2), new(.8, .25), new(.78, .75), new(.18, .8)]);
var framed = SkyEye.Companion.Hybrid.SceneProjection.Table(1080, 1920, 1920, 1080, skewed);
Check(framed.Framed && framed.Fallback is null, "valid calibrated view rectifies with a pocket border");
foreach (var pocket in SkyEye.Core.PoolGeometry.Pockets)
{
    var imagePoint = skewed.Unmap(pocket); var screenPoint = framed.Screen(imagePoint);
    Check(screenPoint.X > framed.Left && screenPoint.X < framed.Left + framed.Width && screenPoint.Y > framed.Top && screenPoint.Y < framed.Top + framed.Height, "all six pockets stay inside the framed border");
    Check((framed.Image(screenPoint)!.Value - imagePoint).Length < 1e-9, "perspective pocket hit test matches drawing and video");
}
var topLeft = framed.Screen(skewed.Unmap(new(0, 0))); var bottomLeft = framed.Screen(skewed.Unmap(new(2, 0)));
var topRight = framed.Screen(skewed.Unmap(new(0, 1))); var bottomRight = framed.Screen(skewed.Unmap(new(2, 1)));
Check(Math.Abs(topLeft.X - bottomLeft.X) < 1e-8 && Math.Abs(topRight.X - bottomRight.X) < 1e-8 && Math.Abs(topLeft.Y - topRight.Y) < 1e-8 && Math.Abs(bottomLeft.Y - bottomRight.Y) < 1e-8, "skewed cloth becomes a straight portrait rectangle");
var pathA = skewed.Unmap(new(.35, .27)); var pathB = skewed.Unmap(new(1.7, .75));
var middle = framed.Screen(skewed.Unmap(new(1.025, .51)));
Check(SkyEye.Core.PoolGeometry.DistanceToSegment(middle, framed.Screen(pathA), framed.Screen(pathB)) < 1e-8, "calibrated paths remain straight and agree with table geometry");
var tight = new SkyEye.Core.TableCalibration([new(.01, .01), new(.99, .01), new(.99, .99), new(.01, .99)]);
var fallback = SkyEye.Companion.Hybrid.SceneProjection.Table(1080, 1920, 1920, 1080, tight);
Check(!fallback.Framed && fallback.Fallback is not null && (fallback.Screen(sourcePoint) - SkyEye.Companion.Hybrid.SceneProjection.Portrait(1080, 1920, 1920, 1080).Screen(sourcePoint)).Length < 1e-9, "missing pocket border keeps whole-camera fallback");
Check(framed.Image(new(-1, 500)) is null, "framed picking ignores letterbox space");
Console.WriteLine("PASS scene framing: perspective rectification, six retained pockets, inverse hits, path alignment and whole-camera fallback");
foreach (bool portrait in new[] { true, false })
{
    var pockets = SkyEye.Companion.Hybrid.SceneProjection.Pockets(portrait ? 1080 : 1920, portrait ? 1920 : 1080, 1920, 1080, skewed, portrait);
    var points = skewed.Corners.Select(pockets.Screen).ToArray();
    Check(Math.Abs(points[0].X - points[portrait ? 1 : 3].X) < 1e-8 && Math.Abs(points[0].Y - points[portrait ? 3 : 1].Y) < 1e-8, "detected pocket centres define exact portrait/landscape rectangle corners");
    foreach (var p in skewed.Corners) Check((pockets.Image(pockets.Screen(p))!.Value - p).Length < 1e-9, "pocket rectangle inverse agrees with video and overlays");
}
Console.WriteLine("PASS exact pocket-centre rectangle for phone and TV with shared inverse mapping");
