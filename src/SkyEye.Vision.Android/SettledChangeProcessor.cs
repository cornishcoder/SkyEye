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
    // Still-table check: the settled window from when the current still segment began, and a pending request.
    (byte[] Raster, long WindowStartUs, long WindowEndUs)? stillAnchor;
    bool stillCheckRequested;
    public Guid Epoch { get; private set; } = Guid.NewGuid();
    /// <summary>Reference and current rasters of the most recent comparison (managed copies), for sidecars.</summary>
    public (byte[] Reference, byte[] Current)? LastComparisonRasters { get; private set; }
    /// <summary>Anchor and current rasters of the most recent still check, for sidecars.</summary>
    public (byte[] Anchor, byte[] Current)? LastStillCheckRasters { get; private set; }

    /// <summary>Clears all state and starts a new epoch (timeline, media or calibration change).</summary>
    public void Reset()
    {
        window.Clear(); tracker?.Reset(); previousQuarter = null; reference = null; evidence = null; sequence = 0;
        LastComparisonRasters = null; LastStillCheckRasters = null; stillAnchor = null; stillCheckRequested = false;
        Epoch = Guid.NewGuid();
    }

    /// <summary>Ask for one still check on a later pass, if the table is still Settled by then with a full
    /// window after the segment's anchor. Ignored once motion starts (the normal comparison covers that).</summary>
    public void RequestStillCheck() { if (stillAnchor is not null) stillCheckRequested = true; }

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
        if (step.LeftSettled) { stillAnchor = null; stillCheckRequested = false; }
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

        SettledStillCheck? stillCheck = null;
        if (step.BecameSettled)
        {
            long currentStart = window[0].TimeUs, currentEnd = window[^1].TimeUs;
            var current = SettledChangeMath.Median(window.Select(f => f.Raster).ToList());
            stillAnchor = (current, currentStart, currentEnd);
            stillCheckRequested = false;
            if (reference is { } r)
            {
                var comparison = comparer.Compare(r.Raster, current);
                LastComparisonRasters = (r.Raster, current);
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
        else if (stillCheckRequested && step.State == SettledChangeState.Settled && stillAnchor is { } anchor
            && window.Count >= S.WindowFrames && window[0].TimeUs > anchor.WindowEndUs)
        {
            var current = SettledChangeMath.Median(window.Select(f => f.Raster).ToList());
            stillCheck = new(anchor.WindowStartUs, anchor.WindowEndUs, window[0].TimeUs, window[^1].TimeUs,
                comparer.Compare(anchor.Raster, current));
            LastStillCheckRasters = (anchor.Raster, current);
            stillCheckRequested = false;
        }
        else if (step.State != SettledChangeState.Settled) stillCheckRequested = false;
        return new(step.State, motion, tracker.StillPasses, watch.Elapsed.TotalMilliseconds, evidence, isNew) { StillCheck = stillCheck };
    }
}
