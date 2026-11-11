using SkyEye.Core.Analysis;

/// <summary>D3 settled-change managed maths (docs/claude/d3-settled-change-design.md, section 5.2). No OpenCV.</summary>
static class SettledChangeMathChecks
{
    public static void Run()
    {
        var s = new SettledChangeSettings();
        int w = s.RasterWidth, h = s.RasterHeight;

        // A uniform gain leaves no residual; a smooth ramp leaves a small one; one bright disc keeps the tile field.
        var before = new byte[w * h]; var gain = new byte[w * h]; var ramp = new byte[w * h]; var disc = new byte[w * h];
        var rng = new Random(7);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x; byte v = (byte)(100 + rng.Next(-6, 7));
            before[i] = v;
            gain[i] = (byte)Math.Min(255, Math.Round((v + 8) * 1.3 - 8));
            ramp[i] = (byte)Math.Min(255, Math.Round((v + 8) * (.85 + .35 * y / (double)h) - 8));
            disc[i] = (x - 200) * (x - 200) + (y - 400) * (y - 400) < 11 * 11 ? (byte)230 : v;
        }
        var uniform = SettledChangeMath.AbsoluteTileResidual(SettledChangeMath.LogRatio(before, gain), w, h, s.TilePixels);
        Require(uniform.Max() < .02, "A uniform gain of 1.3 leaves no residual above rounding");
        var smooth = SettledChangeMath.GaussianBlur3(SettledChangeMath.AbsoluteTileResidual(SettledChangeMath.LogRatio(before, ramp), w, h, s.TilePixels), w, h);
        Require(smooth.Count(v => v > s.Threshold) == 0, "A smooth 0.85 to 1.2 gain ramp crosses no threshold");
        var spot = SettledChangeMath.GaussianBlur3(SettledChangeMath.AbsoluteTileResidual(SettledChangeMath.LogRatio(before, disc), w, h, s.TilePixels), w, h);
        // ln(238/108) is about 0.79 at the disc centre, far above the 0.18 threshold.
        Require(spot[400 * w + 200] > .6 && spot[100 * w + 100] < s.Threshold, "A ball-sized bright disc stands out and the rest of the bed does not");

        // numpy median for even counts; blur preserves a constant field.
        Require(SettledChangeMath.Median([4f, 1f, 3f, 2f]) == 2.5f && SettledChangeMath.Median([5f, 1f, 3f]) == 3f, "Median matches numpy");
        var constant = Enumerable.Repeat(.5f, 25).ToArray();
        Require(SettledChangeMath.GaussianBlur3(constant, 5, 5).All(v => Math.Abs(v - .5f) < 1e-6), "3x3 Gaussian blur keeps a constant field");

        // Zones and the table mapping (Ball.Position convention).
        (double X, double Y) RasterOf(double tableX, double tableY) => (s.Margin + (1 - tableY) * s.TableWidth, s.Margin + tableX * s.TableWidth);
        var cushionMiss = RasterOf(1.48, .067);
        Require(SettledChangeMath.Zone(cushionMiss.X, cushionMiss.Y, s) == "cushion", "The cushion black miss spot (1.48, 0.067) is in the cushion zone");
        foreach (var (tx, ty) in new[] { (0.0, 0.0), (1.0, 0.0), (2.0, 0.0), (0.0, 1.0), (1.0, 1.0), (2.0, 1.0) })
        { var p = RasterOf(tx, ty); Require(SettledChangeMath.Zone(p.X, p.Y, s) == "jaw", "Every pocket point is a jaw"); }
        var centre = RasterOf(1, .5);
        Require(SettledChangeMath.Zone(centre.X, centre.Y, s) == "table", "The table centre is open table");
        var back = SettledChangeMath.RasterToTable(cushionMiss.X, cushionMiss.Y, s);
        Require(Math.Abs(back.TableX - 1.48) < 1e-9 && Math.Abs(back.TableY - .067) < 1e-9, "Raster and table coordinates round-trip");

        // Region rule.
        double r = s.BallRadius, a = s.BallArea;
        Require(SettledChangeMath.ClassifyRegion(r, a, 0, s) == (true, null), "A ball-sized disc is ball-sized");
        Require(SettledChangeMath.ClassifyRegion(.5 * r, .3 * a, 0, s) == (false, "thin"), "Half a radius is thin");
        Require(SettledChangeMath.ClassifyRegion(2 * r, 4 * a, 0, s) == (false, "oversize"), "Two radii is oversize");
        Require(SettledChangeMath.ClassifyRegion(r, 6 * a, 0, s) == (false, "oversize"), "More than five ball areas is oversize");
        Require(SettledChangeMath.ClassifyRegion(r, a, .3, s) == (false, "rail"), "A region 30 % outside the cushion edge is a rail crossing");

        // Direction and drift.
        Require(SettledChangeMath.Direction(.9, .1) == ChangeDirection.Arrival && SettledChangeMath.Direction(.1, .9) == ChangeDirection.Departure
            && SettledChangeMath.Direction(.5, .4) == ChangeDirection.Unclear, "Direction follows cloth before and after");
        var bandOnly = new float[w * h];
        for (int y = s.Margin; y < s.Margin + 10; y++) for (int x = s.Margin; x < s.Margin + s.TableWidth; x++) bandOnly[y * w + x] = 1;
        var drift = SettledChangeMath.Drift(bandOnly, s);
        Require(drift.Band > 0 && drift.Bed == 0, "Change only along a cushion counts as band drift, not bed change");

        // Change grid: one changed cell sets exactly one bit.
        var mask = new byte[w * h];
        for (int y = s.Margin; y < s.Margin + s.GridCellPixels; y++) for (int x = s.Margin; x < s.Margin + s.GridCellPixels; x++) mask[y * w + x] = 1;
        var grid = SettledChangeMath.ChangeGrid(mask, s);
        Require(grid.Columns == 20 && grid.Rows == 40 && grid.Bits[0] == 1 && grid.Bits.Skip(1).All(b => b == 0), "One changed cell sets one grid bit");

        Console.WriteLine("PASS D3 settled-change maths: gain field, blur, zones, table mapping, region rule, direction, drift and change grid");
        RunSettle(s);
    }

    /// <summary>Slice 2: motion measure, window median and the settle state machine.</summary>
    static void RunSettle(SettledChangeSettings s)
    {
        int qw = s.RasterWidth / 2, qh = s.RasterHeight / 2;
        var rng = new Random(11);
        var still = new byte[qw * qh];
        for (int i = 0; i < still.Length; i++) still[i] = (byte)(90 + rng.Next(-4, 5));
        var brighter = still.Select(v => (byte)Math.Min(255, Math.Round((v + 8) * 1.2 - 8))).ToArray();
        // A rolling ball changes two places between passes: where it was and where it is now.
        byte[] BallAt(int cx, int cy)
        {
            var image = (byte[])still.Clone();
            for (int y = 0; y < qh; y++) for (int x = 0; x < qw; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) < 6 * 6) image[y * qw + x] = 230;
            return image;
        }
        var ballHere = BallAt(110, 150); var ballThere = BallAt(110, 270);
        Require(SettledChangeMath.Motion(still, still, s) == 0, "Identical passes have no motion");
        Require(SettledChangeMath.Motion(still, brighter, s) < s.StillMotionFraction, "A global exposure step is not motion");
        double rollingMotion = SettledChangeMath.Motion(ballHere, ballThere, s);
        Require(rollingMotion > s.StillMotionFraction, $"A ball moving between passes is motion (measured {rollingMotion:F4})");
        Require(SettledChangeMath.HalveGrey([10, 20, 30, 41], 2, 2).Single() == 25, "2x2 block mean rounds half up");
        Require(SettledChangeMath.Median([[1, 9], [5, 5], [9, 1]]).SequenceEqual(new byte[] { 5, 5 }), "Window median is per pixel");
        var opened = SettledChangeMath.Open3([0, 0, 0, 0, 1, 0, 0, 0, 0], 3, 3);
        Require(opened.All(v => v == 0), "Opening removes an isolated pixel");

        var tracker = new SettledSegmentTracker(s);
        long t = 0; SettledSegmentTracker.Step step = default;
        void Pass(double? motion, double seconds = .35) { t += (long)(seconds * 1e6); step = tracker.Observe(t, motion); }
        Pass(null);
        Require(step.State == SettledChangeState.Settling, "Unknown motion is not settled");
        Pass(0); Pass(0); Pass(0);
        Require(step.State == SettledChangeState.Settling && !step.BecameSettled, "Three still passes over 0.7 s are not yet settled");
        Pass(0); Pass(0);
        Require(step.State == SettledChangeState.Settled && step.BecameSettled, "Still passes spanning 1.2 s settle");
        long segmentStart = step.SegmentStartUs, lastStill = t;
        Pass(0);
        Require(step.State == SettledChangeState.Settled && !step.BecameSettled, "Settling is reported once per segment");
        lastStill = t;
        Pass(.05);
        Require(step.State == SettledChangeState.Moving && step.LeftSettled && step.SegmentStartUs == segmentStart && step.SegmentEndUs == lastStill,
            "Motion ends the segment and reports its span");
        Pass(.05);
        Require(!step.LeftSettled, "Leaving is reported once");
        Pass(0); Pass(0); Pass(null); Pass(0); Pass(0); Pass(0);
        Require(step.State == SettledChangeState.Settling, "A source gap restarts the still count");
        Pass(0); Pass(0);
        Require(step.BecameSettled, "After the gap the table settles again");
        Console.WriteLine("PASS D3 settle detection: motion measure, exposure step, window median and settle state machine");

        // Slice 4: the live cost guard trips on the median of the last 20 passes, not on one slow comparison.
        var guard = new SettledChangeCostGuard();
        Require(Enumerable.Range(0, 19).All(_ => !guard.Add(100)), "The guard waits for a full window");
        var cheap = new SettledChangeCostGuard();
        bool tripped = false;
        for (int i = 0; i < 200; i++) tripped |= cheap.Add(i % 10 == 0 ? 90 : 18);
        Require(!tripped, "Occasional comparison passes over the limit do not trip the guard");
        var costly = new SettledChangeCostGuard();
        tripped = false;
        for (int i = 0; i < 20; i++) tripped = costly.Add(i < 9 ? 20 : 45);
        Require(tripped && costly.MedianMs > 40, "A median over 40 ms trips the guard");
        Require(SettledChangePass.Unavailable("no raw image") is { State: SettledChangeState.Unavailable, UnavailableReason: "no raw image", Evidence: null, EvidenceIsNew: false },
            "An unavailable pass carries its reason and no evidence");
        Console.WriteLine("PASS D3 live cost guard and unavailable passes");
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
