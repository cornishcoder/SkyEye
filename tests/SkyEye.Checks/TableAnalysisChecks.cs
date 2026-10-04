using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class TableAnalysisChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static void Refused(Action work) { try { work(); } catch (Exception e) when (e is InvalidOperationException or ArgumentException or ObjectDisposedException) { return; } throw new Exception("Expected edit refusal"); }
    static async Task Cancelled(Task work) { try { await work; } catch (OperationCanceledException) { return; } throw new Exception("Expected cancelled analysis"); }
    public static async Task Run()
    {
        var clock = new TestTime(); var frames = new Frames(clock); var analyzer = new Analyzer(); var storage = new Calibrations();
        using var session = new TableAnalysisSession(frames, analyzer, storage, clock);
        var camera = new CameraConnection(Guid.NewGuid(), new string('A', 64), CameraReachability.Reachable, true, CaptureState.Active,
            CameraOwnership.Owned, "fixture", 1, Guid.NewGuid().ToString("N"), 3);
        session.Bind(camera, "open"); Check(session.RestoreCalibration() && session.State.Calibration is not null, "existing mount calibration restores");
        frames.Hold(); var first = session.UpdateAsync(); await frames.Entered!.Task;
        Check(ReferenceEquals(first, session.UpdateAsync()), "duplicate polls share exactly one in-flight frame");
        session.Bind(camera, "yellow"); frames.Release(); await first;
        Check(frames.Reads == 1 && session.State.Table?.Group == "yellow" && session.State.Table.Settled == false, "late analysis uses canonical current group, not admitted group");
        var duplicate = session.State; await session.UpdateAsync();
        Check(ReferenceEquals(duplicate.Vision, session.State.Vision) && ReferenceEquals(duplicate.Jpeg, session.State.Jpeg), "duplicate source time cannot replace the tracked frame or diagnostics");
        Refused(() => session.Confirm(session.State.Table!.Version));
        clock.Advance(3); await session.UpdateAsync(); var version = session.State.Table!.Version;
        Check(session.Confirm(version).Confirmed, "fresh settled table can be reviewed");
        var corrected = session.Correct(version, "cue 1.8 0.5\nred 1.0 0.5\nyellow 0.5 0.2\nblack 0.2 0.8");
        Check(corrected.Version > version && corrected.Confirmed && corrected.Group == "yellow", "reviewed complete correction retains current group and updates layout version");
        Refused(() => session.Correct(version, "cue 1 0.5"));
        version = corrected.Version; session.Bind(camera, "red"); Refused(() => session.Correct(version, "cue 1 0.5"));
        version = session.State.Table!.Version;
        Refused(() => session.Correct(version, "cue NaN 0.5"));
        Refused(() => session.Correct(version, "cue 1 0.5\nred 1 0.5"));
        Refused(() => session.Correct(version, "red 1 0.5"));
        clock.Advance(7); Refused(() => session.Correct(version, "cue 1 0.5")); Refused(() => session.Confirm(version));
        await session.UpdateAsync(); clock.Advance(3); await session.UpdateAsync();
        analyzer.Obstructed = true; clock.Advance(1); await session.UpdateAsync();
        Refused(() => session.Correct(session.State.Table!.Version, "cue 1 0.5")); analyzer.Obstructed = false;

        frames.Hold(); var old = session.UpdateAsync(); await frames.Entered!.Task;
        camera = camera with { ConnectionId = Guid.NewGuid() }; session.Bind(camera, "red"); frames.Release(); await Cancelled(old);
        Check(session.State.Table is null && session.State.Vision is null && session.State.Calibration is not null, "same-camera reconnect rejects late frame, retains mount calibration");
        await session.UpdateAsync(); Check(session.State.Table is not null, "next current-source update recovers");
        frames.Fail = true;
        try { await session.UpdateAsync(); throw new Exception("Expected read failure"); } catch (IOException) { }
        Check(session.State.Table is null && session.State.Vision is null, "failed source read clears prior advice at the service boundary"); frames.Fail = false;
        var draft = await session.PrepareCalibrationAsync(); session.Invalidate(); Refused(() => session.ApplyCalibration(draft.Generation, draft.Proposed!));
        draft = await session.PrepareCalibrationAsync(); session.ApplyCalibration(draft.Generation, draft.Proposed!);
        Check(storage.Saves == 1 && !session.State.Picking && session.State.Table is null, "accepted versioned calibration saves once and invalidates advice");
        Refused(() => session.ApplyCalibration(draft.Generation, draft.Proposed!));
        frames.Hold(); using var cancellation = new CancellationTokenSource(); var cancelled = session.UpdateAsync(cancellation.Token);
        await frames.Entered!.Task; cancellation.Cancel(); frames.Release(); await Cancelled(cancelled);
        Check(session.State.Table is null, "caller cancellation cannot publish an analysis result");

        camera = camera with { Ownership = CameraOwnership.Available, HostGeneration = 0 }; session.Bind(camera, "red");
        int before = frames.Reads; await session.UpdateAsync(); Check(frames.Reads == before && session.State.Source is null, "guest/lost authority cannot request advice frame");
        camera = camera with { Ownership = CameraOwnership.Owned, HostGeneration = 2 }; session.Bind(camera, "red");
        camera = camera with { CameraIdentity = new string('B', 64), ConnectionId = Guid.NewGuid() }; session.Bind(camera, "red");
        Check(session.State.Calibration is null && session.State.Table is null, "different camera clears prior mount and advice");
        session.RestoreCalibration(); draft = await session.PrepareCalibrationAsync(); session.Dispose(); Refused(() => session.ApplyCalibration(draft.Generation, draft.Proposed!));
        Console.WriteLine("PASS extracted table session: single-flight frames, current group, freshness/correction gates, stale calibration, reconnect, authority loss and cancellation");
    }
    sealed class Frames(TestTime clock) : ITableFrameSource
    {
        public int Reads; public bool Fail; public TaskCompletionSource? Entered; TaskCompletionSource? release;
        public void Hold() { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public void Release() { release!.TrySetResult(); }
        public async Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
        {
            Reads++; if (Fail) throw new IOException("fixture source failure"); var held = release;
            if (held is not null) { Entered!.TrySetResult(); await held.Task; release = null; }
            // Ignore cancellation deliberately: generation/token checks must still reject a late native completion.
            return new([1, 2, 3], clock.GetUtcNow(), "fixture-timeline");
        }
    }
    sealed class Analyzer : ITableAnalyzer
    {
        public bool Obstructed;
        public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default)
            => new([new("cue", "cue", new(1.8, .5), 1), new("red", "red", new(1, .5), 1), new("yellow", "yellow", new(.5, .2), 1), new("black", "black", new(.2, .8), 1)], false, [], 1, 4, true, "fixture", [1, 1, 1], 100, Obstructed, []);
        public Point[]? FindCorners(byte[] jpeg) => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
    }
    sealed class Calibrations : ICalibrationStore
    {
        public int Saves;
        public Point[]? Load(string cameraIdentity) => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        public void Save(string cameraIdentity, Point[] corners) => Saves++;
    }
}
