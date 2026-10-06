using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class DecodedFrameChecks
{
    public static async Task Run()
    {
        void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
        var frames = new Frames(); var analyzer = new Analyzer();
        using var session = new TableAnalysisSession(frames, analyzer, new Calibration());
        var camera = new CameraConnection(Guid.NewGuid(), new string('A', 64), CameraReachability.Reachable, true, CaptureState.Active,
            CameraOwnership.Owned, "fixture", 1, Guid.NewGuid().ToString("N"), 1);
        session.Bind(camera, "open"); session.RestoreCalibration();
        await session.UpdateAsync();
        Check(analyzer.DecodedCalls == 1 && frames.Last!.Disposed && frames.Last.Encodes == 0 && session.State.Jpeg is null,
            "decoded pixels reach vision directly and are disposed without JPEG solely for analysis");
        Check(session.State.Observation?.SourceTimeUs == 1234 && session.State.Observation.FrameSource == "decoded-live-texture",
            "source PTS and acquisition provenance survive observation");
        Check(session.State.Observation!.DecodedReceipt == Frames.Receipt,
            "exact native pixel identity/dimensions survive image disposal and observation projection");
        await session.UpdateAsync(reviewFrame: true);
        Check(frames.Last!.Disposed && frames.Last.Encodes == 1 && session.State.Jpeg?.SequenceEqual(new byte[] { 4, 5, 6 }) == true,
            "only a requested editor retains an encoded review image");
        var effect = new TableObservationEffect(() => session, () => 8, () => true);
        try
        {
            await effect.ExecuteAsync(new(camera.Scope!, session.State.Generation, 8), CancellationToken.None);
            throw new Exception("Expected native generation rejection");
        }
        catch (InvalidOperationException) { }
        Check(frames.Last!.Disposed, "a mismatched native decoder generation cannot escape through a current command");
        analyzer.Fail = true;
        try { await session.UpdateAsync(); throw new Exception("Expected native failure"); } catch (IOException) { }
        Check(frames.Last!.Disposed && session.State.Observation is null, "native error disposes image and clears old observation");
        Console.WriteLine("PASS decoded observation: no JPEG/network requirement, source timestamps, explicit review encoding and native image lifetime");
    }
    sealed class Pixels : ITableFrameImage
    {
        public bool Disposed; public int Encodes;
        public byte[] EncodeForReview() { Encodes++; return [4, 5, 6]; }
        public void Dispose() => Disposed = true;
    }
    sealed class Frames : ITableFrameSource
    {
        public static readonly DecodedFrameReceipt Receipt = new(7, 9, 456, 987654321, 1920, 1080);
        public Pixels? Last;
        public Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
            => Task.FromResult(new TableFrame([], DateTimeOffset.UtcNow, "fixture") { Image = Last = new(), SourceTimeUs = 1234, FrameSource = "decoded-live-texture", DecodedReceipt = Receipt });
    }
    sealed class Analyzer : ITableAnalyzer
    {
        public int DecodedCalls; public bool Fail;
        public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default) => throw new Exception("Unexpected JPEG analysis");
        public VisionResult Analyze(TableFrame frame, TableCalibration calibration, CancellationToken cancellation = default)
        {
            if (Fail) throw new IOException("Native fixture failure");
            DecodedCalls++; return new([], true, [], 1, 0, false, "fixture", [1, 1, 1], 100, false, []);
        }
        public Point[]? FindCorners(byte[] jpeg) => null;
    }
    sealed class Calibration : ICalibrationStore
    {
        public Point[]? Load(string cameraIdentity) => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        public void Save(string cameraIdentity, Point[] corners) { }
    }
}
