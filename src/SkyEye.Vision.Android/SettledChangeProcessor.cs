using System.Diagnostics;
using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Android;

/// <summary>D3 slice 2: per-pass settle detection, reference window and comparison on settling
/// (docs/claude/d3-settled-change-design.md, section 2.1 steps 2 to 5). Input is an already rectified
/// raster per Live Detect pass; turning a live bitmap into that raster, the reducer rules and logging are
/// later slices. Keeps only managed copies it owns: the previous quarter-size grey, up to
/// <see cref="SettledChangeSettings.WindowFrames"/> still rasters and one reference raster (about 4.5 MB).
/// Not thread-safe: call <see cref="Observe"/> from one pass at a time, as Live Detect does.</summary>
public sealed class SettledChangeProcessor(SettledChangeComparer? comparer = null)
{
    readonly SettledChangeComparer comparer = comparer ?? new();
    SettledChangeSettings S => comparer.Settings;
    readonly List<(long TimeUs, byte[] Raster)> window = [];
    SettledSegmentTracker? tracker;
    byte[]? previousQuarter; long previousTimeUs;
    (byte[] Raster, long WindowStartUs, long WindowEndUs, long SegmentStartUs, long SegmentEndUs)? reference;
    SettledChangeEvidence? evidence;
    long sequence;
    public Guid Epoch { get; private set; } = Guid.NewGuid();

    /// <summary>Clears all state and starts a new epoch (timeline, media or calibration change).</summary>
    public void Reset()
    {
        window.Clear(); tracker?.Reset(); previousQuarter = null; reference = null; evidence = null; sequence = 0;
        Epoch = Guid.NewGuid();
    }

    public SettledChangePass Observe(byte[] rasterBgr, long sourceTimeUs)
    {
        var watch = Stopwatch.StartNew();
        tracker ??= new SettledSegmentTracker(S);
        var quarter = SettledChangeMath.HalveGrey(comparer.Grey(rasterBgr), S.RasterWidth, S.RasterHeight);
        double? motion = previousQuarter is not null && sourceTimeUs > previousTimeUs
            && (sourceTimeUs - previousTimeUs) / 1e6 <= S.MaximumPassGapSeconds
                ? SettledChangeMath.Motion(previousQuarter, quarter, S) : null;
        previousQuarter = quarter; previousTimeUs = sourceTimeUs;

        var step = tracker.Observe(sourceTimeUs, motion);
        bool isNew = false;
        if (step.LeftSettled && window.Count > 0)
        {
            // The last settled view before motion becomes the reference for the next segment.
            reference = (SettledChangeMath.Median(window.Select(f => f.Raster).ToList()),
                window[0].TimeUs, window[^1].TimeUs, step.SegmentStartUs, step.SegmentEndUs);
        }
        if (step.State is SettledChangeState.Settling or SettledChangeState.Settled && motion is not null)
        {
            window.Add((sourceTimeUs, rasterBgr));
            if (window.Count > S.WindowFrames) window.RemoveAt(0);
        }
        else window.Clear();

        if (step.BecameSettled)
        {
            long currentStart = window[0].TimeUs, currentEnd = window[^1].TimeUs;
            if (reference is { } r)
            {
                var comparison = comparer.Compare(r.Raster, SettledChangeMath.Median(window.Select(f => f.Raster).ToList()));
                var gates = comparison.GateReasons.ToList();
                if ((currentStart - r.WindowEndUs) / 1e6 > S.MaximumComparisonGapSeconds) gates.Add("gap_too_long");
                evidence = new(Epoch, ++sequence, SettledChangeStatus.Compared, gates,
                    r.SegmentStartUs, r.SegmentEndUs, r.WindowStartUs, r.WindowEndUs, currentStart, currentEnd, comparison);
            }
            else
                evidence = new(Epoch, ++sequence, SettledChangeStatus.NoReference, [], null, null, null, null,
                    currentStart, currentEnd, null);
            isNew = true;
        }
        return new(step.State, motion, tracker.StillPasses, watch.Elapsed.TotalMilliseconds, evidence, isNew);
    }
}
