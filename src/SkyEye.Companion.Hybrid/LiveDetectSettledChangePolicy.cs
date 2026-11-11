using System.Collections.Immutable;
using SkyEye.Core.Analysis;
using SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

/// <summary>D3 reducer use of settled-change evidence. <c>Off</c> does nothing; <c>Shadow</c> computes and
/// reports what the rules would do and applies nothing. An <c>Active</c> mode that applies rules is not built.</summary>
internal enum LiveDetectSettledChangeMode { Off, Shadow }

/// <summary>What one rule would do. <paramref name="Applied"/> is always false in shadow mode.</summary>
/// <param name="Kind">hold, infer_black, infer_cue, departure_support, pocket_unchanged, pocket_changed, unexplained_change.</param>
internal sealed record LiveDetectSettledChangeProposal(string Kind, string? Colour, double TableX, double TableY,
    long EvidenceSequence, bool Applied, string? Detail = null);

/// <summary>A ball that is not observed in this pass: held (U1), inferred (U2) or not seen (D2).
/// Never presented as observed.</summary>
internal sealed record LiveDetectSettledBallState(string Colour, double TableX, double TableY, string State,
    long? SinceSourceTimeUs, long? EvidenceSequence);

/// <summary>Per-pass shadow output, attached to the event batch for logging. Nothing in the reducer reads it.</summary>
internal sealed record LiveDetectSettledChangeShadow(LiveDetectSettledChangeMode Mode, bool ChainIntact, int ChainLength,
    long? EvidenceSequence, bool EvidenceIsNew, ImmutableArray<LiveDetectSettledChangeProposal> Proposals,
    ImmutableArray<LiveDetectSettledBallState> BallStates);

/// <summary>Read-only view of the reducer's state for one pass.</summary>
internal readonly record struct LiveDetectSettledChangeContext(
    bool BaselineEstablished, bool BaselineAdvanced, long? BaselineSourceTimeUs,
    ImmutableArray<SettledBallPosition> BaselineLayout, ImmutableArray<SettledBallPosition> CurrentLayout,
    bool CurrentLayoutTrusted, bool Obstructed, ImmutableArray<LiveDetectUncertainRegion> PocketRegions,
    bool BlackNotSeen, bool CueNotSeen, long SourceTimeUs,
    long? BaselineConfirmedSourceTimeUs = null);

/// <summary>
/// D3 rules U1 to U5 (docs/claude/d3-settled-change-design.md, section 2.3), shadow mode only. Pure and
/// image-free: it sees the reducer's state and the settled-change evidence, keeps the evidence chain since
/// the baseline, and returns proposals. It never changes counts, events, baselines or GameState.
/// Distances are in table-width units (ball radius 0.027); "near" is within 1.6 radii.
/// </summary>
internal sealed class LiveDetectSettledChangePolicy
{
    public const double BallRadius = .027;
    public const double Near = 1.6 * BallRadius;
    const int MaximumChain = 8;

    readonly List<SettledChangeEvidence> chain = [];
    bool chainBroken;
    bool transitionLink;
    long? anchorStartUs, anchorEndUs;
    Guid? anchorEpoch;
    ImmutableArray<SettledBallPosition> currentAnchorLayout = [];
    bool previousIntact;
    long? previousStartUs, previousEndUs;
    Guid? previousEpoch;
    long? previousSequence;
    ImmutableArray<SettledBallPosition> previousBaselineLayout = [];
    readonly Dictionary<int, long> lastObservedUs = [];

    public void Reset()
    {
        chain.Clear(); chainBroken = false; transitionLink = false;
        anchorStartUs = anchorEndUs = null; anchorEpoch = null; currentAnchorLayout = [];
        previousIntact = false; previousStartUs = previousEndUs = null; previousEpoch = null; previousSequence = null; previousBaselineLayout = [];
        lastObservedUs.Clear();
    }

    public LiveDetectSettledChangeShadow Observe(LiveDetectSettledChangeContext context, SettledChangePass? pass)
    {
        if (!context.BaselineEstablished || context.BaselineSourceTimeUs is null)
        {
            Reset();
        }
        else if (context.BaselineAdvanced || anchorStartUs is null)
        {
            long start = context.BaselineSourceTimeUs.Value;
            long end = context.BaselineConfirmedSourceTimeUs ?? start;
            long newStart = Math.Min(start, end), newEnd = Math.Max(start, end);

            previousIntact = anchorStartUs is not null && !chainBroken;
            previousStartUs = anchorStartUs;
            previousEndUs = anchorEndUs;
            previousEpoch = chain.Count > 0 ? chain[^1].Epoch : anchorEpoch;
            previousSequence = chain.Count > 0 ? chain[^1].Sequence : null;
            previousBaselineLayout = currentAnchorLayout;

            // Design 2.3: when the baseline advances, links whose current window ends before the new
            // baseline are absorbed and dropped; a transition link that already settled into the new
            // baseline's window stays until a post-baseline comparison replaces it.
            SettledChangeEvidence? retained = !chainBroken && chain.Count > 0
                && chain[^1].ReferenceSegmentEndUs < newStart && chain[^1].CurrentWindowEndUs >= newStart
                ? chain[^1] : null;
            chain.Clear();
            chainBroken = false;
            transitionLink = retained is not null;
            if (retained is not null) chain.Add(retained);
            anchorStartUs = newStart;
            anchorEndUs = newEnd;
            currentAnchorLayout = context.BaselineLayout;
            lastObservedUs.Clear();
        }
        else
        {
            long start = context.BaselineSourceTimeUs.Value;
            long end = context.BaselineConfirmedSourceTimeUs ?? start;
            anchorStartUs = Math.Min(anchorStartUs.Value, Math.Min(start, end));
            anchorEndUs = Math.Max(anchorEndUs!.Value, Math.Max(start, end));
            currentAnchorLayout = context.BaselineLayout;
        }
        if (pass is { EvidenceIsNew: true, Evidence: { } evidence }) Link(evidence);

        var proposals = ImmutableArray.CreateBuilder<LiveDetectSettledChangeProposal>();
        var states = ImmutableArray.CreateBuilder<LiveDetectSettledBallState>();
        bool settledPass = pass is { State: SettledChangeState.Settled };
        bool intact = context.BaselineEstablished && !chainBroken && chain.Count > 0 && settledPass;
        var postBaselineChain = transitionLink ? [] : chain;
        long? sequence = pass?.Evidence?.Sequence;
        var current = context.CurrentLayoutTrusted && !context.CurrentLayout.IsDefault ? context.CurrentLayout : [];

        if (context.BaselineEstablished && !context.BaselineLayout.IsDefault)
        {
            for (int i = 0; i < context.BaselineLayout.Length; i++)
            {
                var ball = context.BaselineLayout[i];
                if (current.Any(c => c.Colour == ball.Colour && Distance(c, ball) <= Near))
                { lastObservedUs[i] = context.SourceTimeUs; continue; }
                if (!context.CurrentLayoutTrusted || !settledPass) continue;  // nothing is missing from an untrusted or moving view
                var (tx, ty) = Table(ball);
                // U3: a ball-sized region at a missing ball's position supports a real departure.
                var departure = postBaselineChain.SelectMany(l => l.Comparison!.Regions.Select(r => (Link: l, Region: r)))
                    .FirstOrDefault(x => x.Region.BallSized && Distance(x.Region, tx, ty) <= Near);
                if (departure.Region is not null)
                {
                    proposals.Add(new("departure_support", Name(ball.Colour), tx, ty, departure.Link.Sequence, false,
                        departure.Region.Direction.ToString()));
                    continue;
                }
                // U1: held only while the chain is intact, nothing changed near it in any post-baseline link, and no obstruction.
                if (intact && !transitionLink && !context.Obstructed && !postBaselineChain.Any(l => GridChangedNear(l.Comparison!, tx, ty, Near)))
                {
                    long? since = lastObservedUs.TryGetValue(i, out var seen) ? seen : context.BaselineSourceTimeUs;
                    proposals.Add(new("hold", Name(ball.Colour), tx, ty, postBaselineChain[^1].Sequence, false));
                    states.Add(new(Name(ball.Colour), tx, ty, "held", since, postBaselineChain[^1].Sequence));
                }
            }

            // U2 / U5: infer a missing black or cue from one unexplained arrival in the newest link.
            if (intact && context.CurrentLayoutTrusted)
                foreach (var (colour, kind) in new[] { (BallColour.Black, "infer_black"), (BallColour.Cue, "infer_cue") })
                {
                    if (current.Any(c => c.Colour == colour)) continue;
                    if (states.Any(s => s.Colour == Name(colour) && s.State == "held")) continue;
                    bool notSeen = colour == BallColour.Black ? context.BlackNotSeen : context.CueNotSeen;
                    var newest = chain[^1];
                    var candidates = newest.Comparison!.Regions.Where(r => r.BallSized && r.RejectReason is null
                        && r.Direction != ChangeDirection.Departure
                        && !current.Any(c => Distance(r, c) <= Near)
                        && (colour == BallColour.Black
                            ? r.DarkAfter >= .30 && r.DarkAfter - r.DarkBefore >= .20
                            : r.WhiteAfter >= .30 && r.WhiteAfter - r.WhiteBefore >= .20)).ToList();
                    if (candidates.Count != 1) continue;
                    var region = candidates[0];
                    proposals.Add(new(kind, Name(colour), region.TableX, region.TableY, newest.Sequence, false,
                        "recheck_pending" + (notSeen ? ";upgrades_not_seen" : "")));
                    states.Add(new(Name(colour), region.TableX, region.TableY, "inferred", newest.CurrentWindowStartUs, newest.Sequence));
                }

            // U4: a D1 pocket region is unchanged or changed since the baseline.
            if (intact && !context.PocketRegions.IsDefaultOrEmpty)
                foreach (var region in context.PocketRegions)
                {
                    bool changed = chain.Any(l => GridChangedNear(l.Comparison!, region.X, region.Y, region.Radius));
                    proposals.Add(new(changed ? "pocket_changed" : "pocket_unchanged", null, region.X, region.Y, chain[^1].Sequence, false));
                }

            // Ball-sized change that no baseline or current ball explains: logged only.
            if (pass is { EvidenceIsNew: true } && intact)
                foreach (var region in chain[^1].Comparison!.Regions.Where(r => r.BallSized))
                    if (!context.BaselineLayout.Any(b => Distance(region, b) <= Near)
                        && !(transitionLink && !previousBaselineLayout.IsDefault && previousBaselineLayout.Any(b => Distance(region, b) <= Near))
                        && !current.Any(c => Distance(region, c) <= Near)
                        && !proposals.Any(p => p.Kind.StartsWith("infer_", StringComparison.Ordinal) && Math.Abs(p.TableX - region.TableX) < 1e-9 && Math.Abs(p.TableY - region.TableY) < 1e-9))
                        proposals.Add(new("unexplained_change", null, region.TableX, region.TableY, chain[^1].Sequence, false, region.Zone));
        }

        if (context.BaselineEstablished)
        {
            if (context.BlackNotSeen && !states.Any(s => s.Colour == "black")) states.Add(new("black", double.NaN, double.NaN, "not_seen", context.BaselineSourceTimeUs, null));
            if (context.CueNotSeen && !states.Any(s => s.Colour == "cue")) states.Add(new("cue", double.NaN, double.NaN, "not_seen", context.BaselineSourceTimeUs, null));
        }
        return new(LiveDetectSettledChangeMode.Shadow, intact, chain.Count, sequence, pass?.EvidenceIsNew ?? false,
            proposals.ToImmutable(), states.ToImmutable());
    }

    void Link(SettledChangeEvidence evidence)
    {
        if (chainBroken || anchorStartUs is null || anchorEndUs is null) return;

        // Design 2.3: the initial NoReference (Sequence 1) establishes the first reference window;
        // only a NoReference after motion (Sequence > 1 or after links exist) breaks the chain.
        if (evidence.Status == SettledChangeStatus.NoReference)
        {
            if (chain.Count == 0 && evidence.Sequence == 1 && (anchorEpoch is null || anchorEpoch == evidence.Epoch))
            {
                anchorEpoch = evidence.Epoch;
                return;
            }
            chainBroken = true;
            return;
        }

        // Transition comparison: settled into the new baseline's segment from the pre-baseline segment
        // (because the 3-frame layout baseline confirms ~0.5 s before the 1.2 s image settle window).
        if (chain.Count == 0 && evidence.ReferenceSegmentEndUs < anchorStartUs && evidence.CurrentWindowEndUs >= anchorStartUs)
        {
            if (evidence.Status == SettledChangeStatus.Compared && evidence.GateReasons.Count == 0 && evidence.Comparison is not null
                && previousIntact && LinksFromPrevious(evidence))
            {
                chain.Add(evidence);
                transitionLink = true;
                anchorEpoch = evidence.Epoch;
            }
            else
            {
                previousIntact = false;
            }
            return;
        }

        if (evidence.Status != SettledChangeStatus.Compared || evidence.GateReasons.Count > 0 || evidence.Comparison is null)
        {
            chainBroken = true;
            return;
        }

        if (chain.Count == 0 || transitionLink)
        {
            if (transitionLink && (evidence.Epoch != chain[^1].Epoch || evidence.Sequence != chain[^1].Sequence + 1))
            {
                chainBroken = true;
                return;
            }
            // The first post-baseline link must start from the settled segment the baseline formed in.
            if (evidence.ReferenceSegmentStartUs <= anchorEndUs && anchorStartUs <= evidence.ReferenceSegmentEndUs
                && (anchorEpoch is null || evidence.Epoch == anchorEpoch))
            {
                chain.Clear();
                chain.Add(evidence);
                transitionLink = false;
                anchorEpoch = evidence.Epoch;
            }
            else
            {
                chainBroken = true;
            }
            return;
        }

        var last = chain[^1];
        if (evidence.Epoch != last.Epoch || evidence.Sequence != last.Sequence + 1 || chain.Count >= MaximumChain)
        {
            chainBroken = true;
            return;
        }
        chain.Add(evidence);
    }

    bool LinksFromPrevious(SettledChangeEvidence evidence) =>
        previousSequence is { } seq
            ? evidence.Epoch == previousEpoch && evidence.Sequence == seq + 1
            : previousStartUs is { } start && previousEndUs is { } end
                && evidence.ReferenceSegmentStartUs <= end && start <= evidence.ReferenceSegmentEndUs
                && (previousEpoch is null || evidence.Epoch == previousEpoch);

    /// <summary>True when any change-grid cell overlapping the circle (table units) is set.</summary>
    internal static bool GridChangedNear(SettledChangeComparison comparison, double tableX, double tableY, double radius)
    {
        // Grid cells run over the portrait raster: column = across the table (1 - Y), row = along it (X).
        int columns = comparison.GridColumns, rows = comparison.GridRows;
        double cellsPerUnit = columns; // a table width spans all columns
        double col = (1 - tableY) * cellsPerUnit, row = tableX * cellsPerUnit, r = radius * cellsPerUnit;
        for (int y = Math.Max(0, (int)Math.Floor(row - r)); y <= Math.Min(rows - 1, (int)Math.Floor(row + r)); y++)
        for (int x = Math.Max(0, (int)Math.Floor(col - r)); x <= Math.Min(columns - 1, (int)Math.Floor(col + r)); x++)
        {
            double nx = Math.Clamp(col, x, x + 1), ny = Math.Clamp(row, y, y + 1);
            if ((nx - col) * (nx - col) + (ny - row) * (ny - row) > r * r) continue;
            int bit = y * columns + x;
            if ((comparison.ChangeGrid[bit / 8] & (1 << (bit % 8))) != 0) return true;
        }
        return false;
    }

    static (double X, double Y) Table(SettledBallPosition ball) => (ball.X * 2, ball.Y);
    static double Distance(SettledBallPosition a, SettledBallPosition b) => Math.Sqrt(Math.Pow((a.X - b.X) * 2, 2) + Math.Pow(a.Y - b.Y, 2));
    static double Distance(SettledChangeRegion r, double tableX, double tableY) => Math.Sqrt(Math.Pow(r.TableX - tableX, 2) + Math.Pow(r.TableY - tableY, 2));
    static double Distance(SettledChangeRegion r, SettledBallPosition b) => Distance(r, b.X * 2, b.Y);
    static string Name(BallColour colour) => colour.ToString().ToLowerInvariant();
}
