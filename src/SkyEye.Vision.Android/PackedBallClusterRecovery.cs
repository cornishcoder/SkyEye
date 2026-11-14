using Org.Opencv.Core;
using Org.Opencv.Imgproc;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Cv = Org.Opencv.Core.Core;
using Point = SkyEye.Core.Point;

namespace SkyEye.Vision.Android;

public sealed partial class VisionAnalyzer
{
    readonly record struct ClusterBall(string Kind, Circle Body, Point PeakCentre, double ColourRadius,
        BallClassification Classification, double BoundarySupport);

    /// <summary>
    /// Split a compact touching group using separate red/yellow pixel masks.
    /// Every returned body needs a ball-sized colour-mask peak and a supported
    /// raw-image boundary fit; table shape and declared inventory never add one.
    /// </summary>
    PackedClusterEvidence AddPackedClusterBodies(Mat rawBgr, Mat rawHsv, Mat colourProposalHsv,
        byte[] rawPixels, BoundaryColourMask boundaryColourMask,
        List<Circle> circles, double scale,
        Dictionary<Circle, BallClassification>? packedClassifications,
        List<VisionCandidateAudit>? candidateAudits = null, string pass = "initial",
        Dictionary<Circle, BallBoundary>? acceptedPackedBlackBoundaries = null,
        List<ClusterBall>? cueHandover = null)
    {
        byte[] rawBgrPixels = Read(rawBgr);
        using var red = new MatLease(new Mat());
        using var redLowA = new Scalar(0, 100, 30); using var redHighA = new Scalar(10, 255, 255);
        using var redLowB = new Scalar(170, 100, 30); using var redHighB = new Scalar(179, 255, 255);
        using var redWrap = new MatLease(new Mat());
        // The diagnostic proposal-mask ablation may supply normalized HSV here.
        // Boundary membership and fitted-body classification still use raw HSV.
        Cv.InRange(colourProposalHsv, redLowA, redHighA, red.Value);
        Cv.InRange(colourProposalHsv, redLowB, redHighB, redWrap.Value);
        Cv.Bitwise_or(red.Value, redWrap.Value, red.Value);
        using var yellow = new MatLease(new Mat()); using var yellowLow = new Scalar(10, 100, 30); using var yellowHigh = new Scalar(40, 255, 255);
        Cv.InRange(colourProposalHsv, yellowLow, yellowHigh, yellow.Value);

        double minimumPeakRadius = 12 * scale;
        var proposals = FindColourPeakBodies(red.Value, "red", rawPixels, rawBgrPixels, scale, minimumPeakRadius,
                boundaryColourMask, candidateAudits, pass, cueHandover)
            .Concat(FindColourPeakBodies(yellow.Value, "yellow", rawPixels, rawBgrPixels, scale, minimumPeakRadius,
                boundaryColourMask, candidateAudits, pass, cueHandover)).ToArray();
        var cluster = FindDenseColourCluster(proposals, scale);
        if (candidateAudits is not null)
            for (int i = 0; i < proposals.Length; i++)
            {
                var proposal = proposals[i];
                bool selected = cluster.Any(member => member.Kind == proposal.Kind && member.Body == proposal.Body);
                candidateAudits.Add(new(pass, "packed-window", proposal.Kind, i,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius, null,
                    proposal.Classification.Kind, selected ? "selected" : "outside-selected-window"));
            }
        if (cluster.Length < 12)
        {
            // The colour peaks are also useful for well-separated balls after
            // a shot. They are not admitted as a group: each one needs the same
            // confirmed colour and a strong outer boundary, and must remain
            // separated from every other admitted body. This keeps an isolated
            // red from disappearing merely because the table no longer has a
            // rack-sized cluster.
            double minimumSparseBoundarySupport = settings.MinimumSparseColourBoundarySupport;
            var admittedSparseBodies = new List<Point>();
            for (int i = 0; i < proposals.Length; i++)
            {
                var proposal = proposals[i];
                var centre = new Point(proposal.Body.X, proposal.Body.Y);
                if (proposal.BoundarySupport < minimumSparseBoundarySupport)
                {
                    candidateAudits?.Add(new(pass, "sparse-colour-add", proposal.Kind, i,
                        new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                        new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                        proposal.BoundarySupport, proposal.Classification.Kind,
                        "rejected-insufficient-boundary-support"));
                    continue;
                }
                bool duplicate = circles.Any(c => Distance(c, proposal.Body) < settings.Separation * scale)
                    || admittedSparseBodies.Any(c => (c - centre).Length < settings.Separation * scale);
                if (duplicate)
                {
                    candidateAudits?.Add(new(pass, "sparse-colour-add", proposal.Kind, i,
                        new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                        new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                        proposal.BoundarySupport, proposal.Classification.Kind,
                        "rejected-duplicate"));
                    continue;
                }
                circles.Add(proposal.Body);
                admittedSparseBodies.Add(centre);
                if (packedClassifications is not null) packedClassifications[proposal.Body] = proposal.Classification;
                candidateAudits?.Add(new(pass, "sparse-colour-add", proposal.Kind, i,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                    proposal.BoundarySupport, proposal.Classification.Kind,
                    "added-strong-isolated-body"));
            }
            AddSparseBlackBody(rawHsv, rawBgrPixels, rawPixels, proposals, circles, scale,
                packedClassifications, candidateAudits, pass);
            return default;
        }

        var existingCentres = circles.Select(c => new Point(c.X, c.Y)).ToArray();
        var admittedBodies = new List<Point>();
        for (int clusterIndex = 0; clusterIndex < cluster.Length; clusterIndex++)
        {
            var proposal = cluster[clusterIndex];
            // A color-mask peak can shift toward a touching neighbor, especially
            // under bright rack shadows. Each candidate already has an accepted,
            // color-matched ball boundary; use those fitted body centres for the
            // same separation gate so adjacent opposite-color balls are not
            // discarded just because their interior peaks drifted together.
            var bodyCentre = new Point(proposal.Body.X, proposal.Body.Y);
            bool duplicate = existingCentres.Any(c => (c - bodyCentre).Length < settings.Separation * scale)
                || admittedBodies.Any(c => (c - bodyCentre).Length < settings.Separation * scale);
            if (!duplicate)
            {
                circles.Add(proposal.Body);
                admittedBodies.Add(bodyCentre);
                if (packedClassifications is not null) packedClassifications[proposal.Body] = proposal.Classification;
                candidateAudits?.Add(new(pass, "packed-add", proposal.Kind, clusterIndex,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                    new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius, null,
                    proposal.Classification.Kind, "added-to-proposal-set"));
            }
            else candidateAudits?.Add(new(pass, "packed-add", proposal.Kind, clusterIndex,
                new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius,
                new(proposal.Body.X, proposal.Body.Y), proposal.Body.Radius, null,
                proposal.Classification.Kind, "rejected-duplicate-existing-proposal"));
        }

        // The black ball's centre lies near its number patch, so a dark-mask
        // interior peak is only a seed. It still has to fit a visible outer edge
        // and classify as black from this frame before it can become a body.
        using var dark = new MatLease(new Mat()); using var darkLow = new Scalar(0, 0, 0);
        using var darkHigh = new Scalar(179, 255, settings.DarkBodyValueCeiling);
        Cv.InRange(rawHsv, darkLow, darkHigh, dark.Value);
        using var cleanupSize = new Size(3, 3);
        using var cleanupKernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, cleanupSize));
        Imgproc.MorphologyEx(dark.Value, dark.Value, Imgproc.MorphOpen, cleanupKernel.Value);
        var bounds = (
            Left: cluster.Min(p => p.Body.X) - 55 * scale,
            Right: cluster.Max(p => p.Body.X) + 55 * scale,
            Top: cluster.Min(p => p.Body.Y) - 55 * scale,
            Bottom: cluster.Max(p => p.Body.Y) + 55 * scale);
        bool blackObserved = false;
        var darkSeeds = FindPeakSeeds(dark.Value, scale, 8 * scale, bounds).ToArray();
        candidateAudits?.Add(new(pass, "packed-black-search", "black", darkSeeds.Length,
            new((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2), 0, null, null, null, null,
            $"cluster-window-seeds={darkSeeds.Length};component-cap=64-after-window-filter"));
        int? clothMedianValue = null;
        for (int i = 0; i < darkSeeds.Length; i++)
        {
            var seed = darkSeeds[i];
            if (seed.Centre.X < bounds.Left || seed.Centre.X > bounds.Right || seed.Centre.Y < bounds.Top || seed.Centre.Y > bounds.Bottom)
            { Audit("rejected-outside-cluster-window"); continue; }
            double expectedRadius = PoolGeometry.Radius * coordinates.Width;
            var fit = BallBoundaryRefiner.Refine(rawBgrPixels, coordinates.Width, coordinates.Height,
                seed.Centre, expectedRadius, .7);
            if (fit is null)
            { Audit("rejected-no-boundary-fit"); continue; }
            string? occlusionEvidence = null;
            bool occlusionExplained = fit.SupportedSectors < .7
                && HasOcclusionExplainedBlackBoundary(fit, seed.Centre, expectedRadius, rawBgrPixels, cluster, out occlusionEvidence);
            if (fit.SupportedSectors < .7 && !occlusionExplained)
            { Audit("rejected-insufficient-boundary-support", fit, fit.SupportedSectors, evidence: occlusionEvidence); continue; }
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var classification = Classify(body, rawPixels, inspectRim: true);
            if (classification.Kind != "black")
            { Audit("rejected-classification", fit, fit.SupportedSectors, classification.Kind); continue; }
            if (occlusionExplained && settings.PackedBlackOcclusionDarkBody)
            {
                // A black accepted on occlusion evidence must be dark relative to the cloth: the shadowed cloth
                // in the gap of a black-absent rack passes the absolute dark ceiling (V 38 vs 40) and the
                // occlusion exception (see VisionSettings.PackedBlackOcclusionDarkBody).
                clothMedianValue ??= InnerTableMedianValue(rawPixels);
                // Expected ball radius, not the fit's: a fit inside a gap can be half a ball wide.
                double bodyValue = BodyAnnulusValueP25(new Circle(body.X, body.Y, expectedRadius), rawPixels);
                occlusionEvidence = $"{occlusionEvidence};bodyAnnulusP25V={bodyValue:0.#};tableMedianV={clothMedianValue}";
                if (clothMedianValue > 0 && bodyValue > PackedBlackOcclusionMaximumBodyToCloth * clothMedianValue.Value)
                {
                    Audit("rejected-occluded-body-not-dark", fit, fit.SupportedSectors, classification.Kind, occlusionEvidence);
                    continue;
                }
            }
            if (circles.Any(c => Distance(c, body) < settings.Separation * scale))
            { Audit("rejected-duplicate-existing-proposal", fit, fit.SupportedSectors, classification.Kind); continue; }
            circles.Add(body);
            if (packedClassifications is not null) packedClassifications[body] = classification;
            blackObserved = true;
            if (occlusionExplained && acceptedPackedBlackBoundaries is not null)
                acceptedPackedBlackBoundaries[body] = fit;
            Audit("added-black-body", fit, fit.SupportedSectors, classification.Kind, occlusionEvidence);
            break;

            void Audit(string decision, BallBoundary? boundary = null, double? support = null, string? label = null, string? evidence = null)
                => candidateAudits?.Add(new(pass, "packed-black", "black", i, seed.Centre, seed.Radius,
                    boundary?.Centre, boundary?.Radius, support, label, decision) { Evidence = evidence });
        }
        return new(true, cluster.Count(p => p.Kind == "red"), cluster.Count(p => p.Kind == "yellow"), blackObserved);
    }

    void AddSparseBlackBody(Mat rawHsv, byte[] rawBgrPixels, byte[] rawPixels, ClusterBall[] colourBalls,
        List<Circle> circles, double scale,
        Dictionary<Circle, BallClassification>? packedClassifications,
        List<VisionCandidateAudit>? candidateAudits, string pass)
    {
        using var recoveryTiming = PipelineDiagnostics.Measure("sparseBlackRecoveryMs");
        (Point Centre, double Radius)[] seeds;
        double edgeMargin = settings.PackedClusterImageEdgeMargin * scale;
        var bounds = (Left: edgeMargin, Right: coordinates.Width - edgeMargin,
            Top: edgeMargin, Bottom: coordinates.Height - edgeMargin);
        using (PipelineDiagnostics.Measure("sparseBlackMaskAndSeedMs"))
        {
            using var dark = new MatLease(new Mat());
            using var darkLow = new Scalar(0, 0, 0);
            using var darkHigh = new Scalar(179, 255, settings.DarkBodyValueCeiling);
            Cv.InRange(rawHsv, darkLow, darkHigh, dark.Value);
            using var cleanupSize = new Size(3, 3);
            using var cleanupKernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, cleanupSize));
            Imgproc.MorphologyEx(dark.Value, dark.Value, Imgproc.MorphOpen, cleanupKernel.Value);
            seeds = FindPeakSeeds(dark.Value, scale, settings.SparseBlackMinimumPeakRadiusFactor * scale, bounds, settings.SparseBlackEnumerateAllSeeds)
                .Where(seed => seed.Radius <= settings.MaximumRadius * scale).ToArray();
        }
        PipelineDiagnostics.Sample("sparseBlackAvailableSeedCount", seeds.Length);
        // Prefer peaks near supported painted bodies by default. A diagnostic
        // ablation can prioritize larger dark interiors to test isolated-black
        // recovery without raising the bounded candidate-fit budget.
        var candidates = seeds.Select(seed => (Seed: seed,
            ColourDistance: colourBalls.Length == 0 ? double.PositiveInfinity : colourBalls
                .Min(ball => (new Point(ball.Body.X, ball.Body.Y) - seed.Centre).Length))).ToArray();
        var ranked = settings.SparseBlackRankByRadius
            ? candidates.OrderByDescending(candidate => candidate.Seed.Radius)
                .ThenBy(candidate => candidate.ColourDistance)
            : candidates.OrderBy(candidate => candidate.ColourDistance)
                .ThenByDescending(candidate => candidate.Seed.Radius);
        var rankedSeeds = ranked.Take(settings.SparseBlackCandidateLimit).ToArray();
        PipelineDiagnostics.Sample("sparseBlackRankedSeedCount", rankedSeeds.Length);
        candidateAudits?.Add(new(pass, "sparse-black-search", "black", seeds.Length,
            new((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2), 0, null, null, null, null,
            $"ball-sized-full-table-seeds={seeds.Length};ranking={(settings.SparseBlackRankByRadius ? "dark-radius-then-colour-proximity" : "colour-proximity-then-dark-radius")};ranked={rankedSeeds.Length};fit-cap={settings.SparseBlackCandidateLimit}"));
        double expectedRadius = PoolGeometry.Radius * coordinates.Width;
        // Rejected seeds leave circles unchanged; a successful black addition returns immediately.
        // Reuse this invariant result instead of reclassifying every existing ball for each seed.
        bool? blackAlreadyPresent = null;
        using (PipelineDiagnostics.Measure("sparseBlackFitAndClassifyMs"))
        for (int i = 0; i < rankedSeeds.Length; i++)
        {
            var seed = rankedSeeds[i].Seed;
            double nearestPocketRadius = PocketCentres().Min(pocket => (seed.Centre - pocket).Length) / expectedRadius;
            if (candidateAudits is not null)
            {
                // Offline only: inspect every seed within the already bounded
                // candidate budget. A far shadow can win the fit even when the
                // actual black is pocket-adjacent, so pocket-local probes alone
                // cannot compare their appearance evidence.
                var seedCentredBody = new Circle(seed.Centre.X, seed.Centre.Y, expectedRadius);
                var seedClassification = Classify(seedCentredBody, rawPixels, inspectRim: true);
                bool localValueContrast = HasLocalBodyValueContrast(seedCentredBody, rawPixels);
                var seedSectors = BallBoundaryRefiner.Inspect(rawBgrPixels, coordinates.Width, coordinates.Height,
                    seed.Centre, expectedRadius, seed.Centre, expectedRadius, expectedKind: "black",
                    allowDarkBodyEdgeTransitions: true, darkBodyValueCeiling: settings.DarkBodyValueCeiling);
                int normalSectors = seedSectors.Count(sector => sector.Supported && !sector.DarkBodyTransitionUsed);
                int darkTransitionSectors = seedSectors.Count(sector => sector.DarkBodyTransitionUsed);
                candidateAudits.Add(new(pass, "sparse-black-seed-body-probe", "black", i,
                    seed.Centre, seed.Radius, seed.Centre, expectedRadius, null,
                    seedClassification.Kind, "seed-centred ball-size appearance probe")
                {
                    Evidence = $"nearestPocket={nearestPocketRadius:0.###}R;quality={seedClassification.Quality:0.###};pattern={seedClassification.Pattern};bodyDark={seedClassification.BodyDarkFraction?.ToString("0.###") ?? "n/a"};rimDark={seedClassification.RimDarkFraction?.ToString("0.###") ?? "n/a"};localValueContrast={localValueContrast};fixedNormal={normalSectors}/32;fixedDarkTransitions={darkTransitionSectors}/32;fixedCombined={seedSectors.Count(sector => sector.Supported)}/32"
                });
            }
            if (PocketCentres().Any(p => (seed.Centre - p).Length < settings.PocketExclusionRadius * scale))
            { Audit("rejected-pocket-exclusion"); continue; }
            blackAlreadyPresent ??= circles.Any(c => Classify(c, rawPixels, inspectRim: true).Kind == "black");
            if (blackAlreadyPresent.Value)
            { Audit("rejected-black-already-present"); return; }
            PipelineDiagnostics.Count("sparseBlackBoundaryRefineCalls");
            var fit = BallBoundaryRefiner.Refine(rawBgrPixels, coordinates.Width, coordinates.Height,
                seed.Centre, expectedRadius, .5625,
                maximumCentreOffsetFraction: settings.SparseBlackBoundarySearchExtentFraction);
            if (candidateAudits is not null
                && PocketCentres().Min(pocket => (seed.Centre - pocket).Length) <= expectedRadius * 3.5)
            {
                // Offline audit only: test a dark-body radial transition without
                // letting the local-background rejection erase it. Never feed
                // this alternate fit into the accepted detection list.
                var darkEdgeFit = BallBoundaryRefiner.Refine(rawBgrPixels, coordinates.Width, coordinates.Height,
                    seed.Centre, expectedRadius, .5625, collectSectorEvidence: true, expectedKind: "black",
                    maximumCentreOffsetFraction: settings.SparseBlackBoundarySearchExtentFraction,
                    allowDarkBodyEdgeTransitions: true, darkBodyValueCeiling: settings.DarkBodyValueCeiling);
                if (darkEdgeFit is null)
                    candidateAudits.Add(new(pass, "sparse-black-dark-edge-probe", "black", i,
                        seed.Centre, seed.Radius, null, null, null, null, "no-fit")
                    { Evidence = "Diagnostic-only alternate fit found fewer than 12 supported sectors." });
                else
                {
                    var darkEdgeBody = new Circle(darkEdgeFit.Centre.X, darkEdgeFit.Centre.Y, darkEdgeFit.Radius);
                    var darkEdgeClassification = Classify(darkEdgeBody, rawPixels, inspectRim: true);
                    double seedDrift = (darkEdgeFit.Centre - seed.Centre).Length / expectedRadius;
                    int transitionSectors = darkEdgeFit.SectorEvidence.Count(sector => sector.DarkBodyTransitionUsed);
                    string normalSupport = fit is null ? "none" : $"{fit.SupportedSectors * 32:0}/32";
                    candidateAudits.Add(new(pass, "sparse-black-dark-edge-probe", "black", i,
                        seed.Centre, seed.Radius, darkEdgeFit.Centre, darkEdgeFit.Radius,
                        darkEdgeFit.SupportedSectors, darkEdgeClassification.Kind,
                        $"normalSupport={normalSupport};darkTransitionSectors={transitionSectors};seedDrift={seedDrift:0.###}R;classified={darkEdgeClassification.Kind}")
                    { Evidence = "Diagnostic-only: count strong dark-inside to non-dark-outside radial edges even when the local-background exterior check rejects them." });
                }
            }
            if (fit is null)
            { Audit("rejected-no-boundary-fit"); continue; }
            string? occlusionEvidence = null;
            bool occlusionExplained = fit.SupportedSectors < settings.MinimumSparseBlackBoundarySupport
                && HasOcclusionExplainedBlackBoundary(fit, seed.Centre, expectedRadius, rawBgrPixels,
                    colourBalls, out occlusionEvidence);
            if (fit.SupportedSectors < settings.MinimumSparseBlackBoundarySupport && !occlusionExplained)
            { Audit("rejected-insufficient-boundary-support", fit, fit.SupportedSectors, evidence: occlusionEvidence); continue; }
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var classification = Classify(body, rawPixels, inspectRim: true);
            if (classification.Kind != "black")
            { Audit("rejected-classification", fit, fit.SupportedSectors, classification.Kind); continue; }
            if (circles.Any(existing => Distance(existing, body) < settings.Separation * scale
                && IsSameOrUnclearKind(existing, classification.Kind)))
            { Audit("rejected-duplicate", fit, fit.SupportedSectors, classification.Kind); continue; }
            circles.Add(body);
            if (packedClassifications is not null) packedClassifications[body] = classification;
            Audit("added-strong-isolated-black", fit, fit.SupportedSectors, classification.Kind);
            return;

            bool IsSameOrUnclearKind(Circle existing, string candidateKind)
            {
                string existingKind = packedClassifications is not null && packedClassifications.TryGetValue(existing, out var known)
                    ? known.Kind : Classify(existing, rawPixels, inspectRim: true).Kind;
                return existingKind == "unknown" || candidateKind == "unknown" || existingKind == candidateKind;
            }

            void Audit(string decision, BallBoundary? boundary = null, double? support = null,
                string? classifiedKind = null, string? evidence = null)
            {
                if (candidateAudits is null) return;
                int[]? supportedSectorIndices = null;
                int? sectorEvidenceSampleCount = null;
                bool? sectorIndexCountMatchesBoundarySupport = null;
                if (boundary is not null)
                {
                    // Post-fit diagnostic sampling reports the directions at the
                    // selected regular fit without changing the optimizer's
                    // candidate search, scoring, or production support gate.
                    var sectors = BallBoundaryRefiner.Inspect(rawBgrPixels,
                        coordinates.Width, coordinates.Height, seed.Centre, expectedRadius,
                        boundary.Centre, boundary.Radius);
                    sectorEvidenceSampleCount = sectors.Length;
                    supportedSectorIndices = sectors.Where(sector => sector.Supported)
                        .Select(sector => sector.Index).ToArray();
                    if (sectors.Length == 32)
                        sectorIndexCountMatchesBoundarySupport = Math.Abs(
                            supportedSectorIndices.Length / 32d - boundary.SupportedSectors) < 1e-9;
                }
                candidateAudits.Add(new(pass, "sparse-black-add", "black", i, seed.Centre, seed.Radius,
                    boundary?.Centre, boundary?.Radius, support, classifiedKind, decision)
                {
                    Evidence = evidence,
                    SupportedSectorIndices = supportedSectorIndices,
                    SectorEvidenceSampleCount = sectorEvidenceSampleCount,
                    SectorIndexCountMatchesBoundarySupport = sectorIndexCountMatchesBoundarySupport
                });
            }
        }
    }

    // Against the inner-table median V: real blacks in saved captures measure 0.2 or less (279 of 292
    // detections; the rest are gamma-brightened copies); the gap of the black-absent dim rack 0.36 to 0.39.
    // (The hue-filtered ClothRange median swings 47 to 91 between dim frames, leaving 0.23 vs 0.30.)
    const double PackedBlackOcclusionMaximumBodyToCloth = .28;

    /// <summary>Median V over the inner 80 % of the rectified table, sampled every 10 px.</summary>
    int InnerTableMedianValue(byte[] hsv)
    {
        int w = coordinates.Width, h = coordinates.Height;
        var values = new List<int>();
        for (int y = h / 10; y < h * 9 / 10; y += 10)
            for (int x = w / 10; x < w * 9 / 10; x += 10)
                values.Add(hsv[(y * w + x) * 3 + 2]);
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>25th percentile of V on the 0.55..0.85 R annulus of a body: skips the white number disc and
    /// central highlight of a black ball.</summary>
    double BodyAnnulusValueP25(Circle body, byte[] hsv)
    {
        int w = coordinates.Width, h = coordinates.Height;
        double inner = body.Radius * .55, outer = body.Radius * .85;
        var values = new List<int>();
        for (int y = (int)Math.Floor(body.Y - outer); y <= (int)Math.Ceiling(body.Y + outer); y++)
            for (int x = (int)Math.Floor(body.X - outer); x <= (int)Math.Ceiling(body.X + outer); x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                double d = Math.Sqrt((x - body.X) * (x - body.X) + (y - body.Y) * (y - body.Y));
                if (d >= inner && d <= outer) values.Add(hsv[(y * w + x) * 3 + 2]);
            }
        if (values.Count == 0) return 0;
        values.Sort();
        return values[values.Count / 4];
    }

    bool HasOcclusionExplainedBlackBoundary(BallBoundary fit, Point proposal, double expectedRadius,
        byte[] rawBgr, ClusterBall[] packedColourBalls, out string evidence)
    {
        var sectors = BallBoundaryRefiner.Inspect(rawBgr, coordinates.Width, coordinates.Height,
            proposal, expectedRadius, fit.Centre, fit.Radius);
        var fittedNeighbours = packedColourBalls.Where(ball => ball.Kind is "red" or "yellow").ToArray();
        var neighbours = fittedNeighbours.Select(ball => new Point(ball.Body.X, ball.Body.Y)).ToArray();
        int visible = sectors.Count(sector => sector.Supported);
        int exteriorRejected = sectors.Count(sector => !sector.Supported && sector.ExteriorRejected);
        bool ExplainedByMeasuredExterior(BoundarySectorEvidence sector)
            => sector.ExteriorRejected && neighbours.Any(neighbour => (neighbour - fit.Centre).Length >= expectedRadius * 1.1
                && (sector.OuterPoint - neighbour).Length
                    <= expectedRadius * BallBoundaryRefiner.ConservativeOcclusionNeighborRadius);
        bool ExplainedByFittedBody(BoundarySectorEvidence sector)
            => !sector.ExteriorRejected && fittedNeighbours.Any(neighbour =>
            {
                var centre = new Point(neighbour.Body.X, neighbour.Body.Y);
                return neighbour.BoundarySupport >= .7
                    && (centre - fit.Centre).Length >= expectedRadius * 1.1
                    && (sector.InnerPoint - centre).Length <= neighbour.Body.Radius
                    && (sector.OuterPoint - centre).Length <= neighbour.Body.Radius;
            });
        // Opt-in (VisionSettings.PackedBlackAdjacentBallOcclusion): in dim light the black's edge against a
        // touching red or yellow has too little contrast to register, and the exterior is not rejected either.
        // Such a sector abuts a recognised packed ball: its outer point is within the conservative occlusion
        // radius of that ball's centre.
        bool ExplainedByAdjacentBall(BoundarySectorEvidence sector)
            => settings.PackedBlackAdjacentBallOcclusion && neighbours.Any(neighbour =>
                (neighbour - fit.Centre).Length >= expectedRadius * 1.1
                && (sector.OuterPoint - neighbour).Length <= expectedRadius * BallBoundaryRefiner.ConservativeOcclusionNeighborRadius);
        bool Explained(BoundarySectorEvidence sector)
            => ExplainedByMeasuredExterior(sector) || ExplainedByFittedBody(sector) || ExplainedByAdjacentBall(sector);
        int neighbourExplained = sectors.Count(sector => !sector.Supported && ExplainedByMeasuredExterior(sector));
        int fittedBodyExplained = sectors.Count(sector => !sector.Supported && ExplainedByFittedBody(sector));
        int adjacentExplained = sectors.Count(sector => !sector.Supported && !ExplainedByMeasuredExterior(sector)
            && !ExplainedByFittedBody(sector) && ExplainedByAdjacentBall(sector));
        bool accepted = BallBoundaryRefiner.HasConservativeOcclusionCoverage(sectors, fit.Centre,
            expectedRadius, neighbours, 14d / 32);
        if (!accepted && sectors.Length == 32 && visible >= 14)
            accepted = sectors.Where(sector => !sector.Supported).All(Explained);
        var uncovered = sectors.Where(sector => !sector.Supported && !Explained(sector))
            .Select(sector =>
            {
                double nearestNeighbourRadius = neighbours
                    .Where(neighbour => (neighbour - fit.Centre).Length >= expectedRadius * 1.1)
                    .Select(neighbour => (sector.OuterPoint - neighbour).Length / expectedRadius)
                    .DefaultIfEmpty(double.PositiveInfinity).Min();
                return $"{sector.Index}(edge={sector.EdgeContrast:0.#};outside={sector.ExteriorContrast:0.#};external={sector.ExteriorRejected};nearestNeighbourR={nearestNeighbourRadius:0.00})";
            });
        evidence = $"visible={visible}/32;exteriorRejected={exteriorRejected}/32;fittedNeighbourExplained={neighbourExplained}/32;fittedBodyOccluded={fittedBodyExplained}/32;adjacentBallOccluded={adjacentExplained}/32;uncoveredSectors={string.Join(',', uncovered)};accepted={accepted}";
        return accepted;
    }

    ClusterBall[] FindColourPeakBodies(Mat colourMask, string kind, byte[] rawHsv, byte[] rawBgr, double scale,
        double minimumPeakRadius, BoundaryColourMask boundaryColourMask,
        List<VisionCandidateAudit>? candidateAudits, string pass, List<ClusterBall>? cueHandover = null)
    {
        using var peakBodyTiming = PipelineDiagnostics.Measure($"{kind}ColourPeakBodiesMs");
        var candidates = new List<ClusterBall>();
        using var distance = new MatLease(new Mat());
        Imgproc.DistanceTransform(colourMask, distance.Value, Imgproc.DistL2, 3);
        using var dilated = new MatLease(new Mat());
        int neighbourhood = Math.Max(5, (int)Math.Round(17 * scale) | 1);
        using var neighbourhoodSize = new Size(neighbourhood, neighbourhood);
        using var neighbourhoodKernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphRect, neighbourhoodSize));
        Imgproc.Dilate(distance.Value, dilated.Value, neighbourhoodKernel.Value);
        using var peaks = new MatLease(new Mat()); Cv.Compare(distance.Value, dilated.Value, peaks.Value, Cv.CmpEq);
        using var minimum = new Scalar(minimumPeakRadius); using var aboveMinimum = new MatLease(new Mat());
        Cv.Compare(distance.Value, minimum, aboveMinimum.Value, Cv.CmpGt);
        Cv.Bitwise_and(peaks.Value, aboveMinimum.Value, peaks.Value);
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(peaks.Value, labels.Value, stats.Value, centres.Value);
        PipelineDiagnostics.Sample($"{kind}ColourPeakComponents", count - 1);
        double margin = settings.PackedClusterImageEdgeMargin * scale;
        for (int i = 1; i < count && candidates.Count < 32; i++)
        {
            double x = centres.Value.Get(i, 0)![0], y = centres.Value.Get(i, 1)![0];
            var seed = new Circle(x, y, 0);
            if (x < margin || y < margin || x >= coordinates.Width - margin || y >= coordinates.Height - margin)
            { Audit("rejected-margin"); continue; }
            if (PocketCentres().Any(p => (new Point(x, y) - p).Length < settings.PocketExclusionRadius * scale))
            { Audit("rejected-pocket-exclusion"); continue; }
            double interiorRadius = distance.Value.Get((int)Math.Round(y), (int)Math.Round(x))![0];
            seed = new Circle(x, y, interiorRadius);
            if (interiorRadius < minimumPeakRadius || interiorRadius > settings.MaximumRadius * scale)
            { Audit("rejected-peak-radius"); continue; }
            PipelineDiagnostics.Count($"packed{kind}BoundaryRefineCalls");
            var fit = BallBoundaryRefiner.Refine(rawBgr, coordinates.Width, coordinates.Height,
                new(x, y), PoolGeometry.Radius * coordinates.Width, .5, expectedKind: kind,
                colourMask: boundaryColourMask,
                maximumCentreOffsetFraction: settings.PackedColourBoundarySearchExtentFraction);
            if (fit is null || fit.SupportedSectors < .4)
            { Audit(fit is null ? "rejected-no-boundary-fit" : "rejected-insufficient-boundary-support", fit, fit?.SupportedSectors); continue; }
            var body = new Circle(fit.Centre.X, fit.Centre.Y, fit.Radius);
            var classification = Classify(body, rawHsv, inspectRim: true);
            if (classification.Kind == "cue" && cueHandover is not null && settings.PackedCueHandover
                && fit.SupportedSectors >= settings.MinimumHoughBoundarySupport)
            {
                // Not this colour, but a well-fitted cue. The colour fit expects yellow or red paint, so
                // refit the outline neutrally (as cue recovery does) and require the Hough-level support
                // there too. Detect decides once every other body is known.
                var neutral = BallBoundaryRefiner.Refine(rawBgr, coordinates.Width, coordinates.Height,
                    fit.Centre, PoolGeometry.Radius * coordinates.Width, settings.MinimumHoughBoundarySupport);
                var neutralBody = neutral is null ? default : new Circle(neutral.Centre.X, neutral.Centre.Y, neutral.Radius);
                var neutralClassification = neutral is null ? null : Classify(neutralBody, rawHsv, inspectRim: true);
                if (neutral is null || neutralClassification!.Kind != "cue")
                { Audit("rejected-cue-handover-neutral-refit", neutral ?? fit, neutral?.SupportedSectors, neutralClassification?.Kind ?? classification.Kind); continue; }
                cueHandover.Add(new("cue", neutralBody, new(x, y), interiorRadius, neutralClassification, neutral.SupportedSectors));
                Audit("deferred-cue-handover", neutral, neutral.SupportedSectors, neutralClassification.Kind); continue;
            }
            if (classification.Kind != kind)
            { Audit("rejected-classification", fit, fit.SupportedSectors, classification.Kind); continue; }
            // Nearby mask peaks can be duplicate seeds for one ball, but a
            // nearby seed can also belong to a touching same-colour neighbour.
            // Use the seed spacing to find duplicate candidates, then require
            // their independently fitted centres to agree within half a ball
            // diameter before discarding one. This keeps adjacent rack balls
            // when their fitted centres remain distinct.
            var peakCentre = new Point(x, y);
            if (candidates.Any(c =>
                    (c.PeakCentre - peakCentre).Length < settings.Separation * scale
                    && Distance(c.Body, body) < (c.Body.Radius + body.Radius) * .5))
            { Audit("rejected-duplicate-colour-peak", fit, fit.SupportedSectors, classification.Kind); continue; }
            candidates.Add(new(kind, body, peakCentre, interiorRadius, classification, fit.SupportedSectors));
            PipelineDiagnostics.Count($"packed{kind}AcceptedBodies");
            Audit("accepted-colour-peak", fit, fit.SupportedSectors, classification.Kind);

            void Audit(string decision, BallBoundary? boundary = null, double? support = null, string? label = null)
                => candidateAudits?.Add(new(pass, "packed-" + kind, kind, i, new(seed.X, seed.Y), seed.Radius,
                    boundary is null ? null : boundary.Centre, boundary?.Radius, support, label, decision));
        }
        return candidates.ToArray();
    }

    static double Distance(Circle first, Circle second)
        => Math.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y));

    ClusterBall[] FindDenseColourCluster(ClusterBall[] candidates, double scale)
    {
        ClusterBall[] best = [];
        double extent = 400 * scale;
        foreach (double left in candidates.Select(p => p.Body.X).Distinct())
        foreach (double top in candidates.Select(p => p.Body.Y).Distinct())
        {
            var group = candidates.Where(p => p.Body.X >= left && p.Body.X <= left + extent
                && p.Body.Y >= top && p.Body.Y <= top + extent).ToArray();
            if (group.Length > best.Length && group.Length >= 12
                && group.Count(p => p.Kind == "red") >= 5 && group.Count(p => p.Kind == "yellow") >= 5)
                best = group;
        }
        return best;
    }

    IEnumerable<(Point Centre, double Radius)> FindPeakSeeds(Mat mask, double scale, double minimumPeakRadius,
        (double Left, double Right, double Top, double Bottom) bounds, bool enumerateAllSeeds = false)
    {
        using var distance = new MatLease(new Mat()); Imgproc.DistanceTransform(mask, distance.Value, Imgproc.DistL2, 3);
        using var dilated = new MatLease(new Mat());
        int neighbourhood = Math.Max(5, (int)Math.Round(17 * scale) | 1);
        using var neighbourhoodSize = new Size(neighbourhood, neighbourhood);
        using var neighbourhoodKernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphRect, neighbourhoodSize));
        Imgproc.Dilate(distance.Value, dilated.Value, neighbourhoodKernel.Value);
        using var peaks = new MatLease(new Mat()); Cv.Compare(distance.Value, dilated.Value, peaks.Value, Cv.CmpEq);
        using var minimum = new Scalar(minimumPeakRadius); using var aboveMinimum = new MatLease(new Mat());
        Cv.Compare(distance.Value, minimum, aboveMinimum.Value, Cv.CmpGt); Cv.Bitwise_and(peaks.Value, aboveMinimum.Value, peaks.Value);
        // Connected-component labels are assigned from the top of the full image.
        // A global label cap lets unrelated cushion/pocket peaks consume the
        // budget before the packed rack is reached. Restrict to the observed
        // colour-cluster window first, then apply the same 64-seed work bound.
        int left = Math.Clamp((int)Math.Floor(bounds.Left), 0, coordinates.Width - 1);
        int right = Math.Clamp((int)Math.Ceiling(bounds.Right), left, coordinates.Width - 1);
        int top = Math.Clamp((int)Math.Floor(bounds.Top), 0, coordinates.Height - 1);
        int bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom), top, coordinates.Height - 1);
        using var searchWindow = new MatLease(peaks.Value.Submat(new Rect(left, top, right - left + 1, bottom - top + 1)));
        using var labels = new MatLease(new Mat()); using var stats = new MatLease(new Mat()); using var centres = new MatLease(new Mat());
        int count = Imgproc.ConnectedComponentsWithStats(searchWindow.Value, labels.Value, stats.Value, centres.Value);
        for (int i = 1; i < count && (enumerateAllSeeds || i <= 64); i++)
        {
            double x = centres.Value.Get(i, 0)![0] + left, y = centres.Value.Get(i, 1)![0] + top;
            double edgeMargin = settings.PackedClusterImageEdgeMargin * scale;
            if (x < edgeMargin || y < edgeMargin || x >= coordinates.Width - edgeMargin || y >= coordinates.Height - edgeMargin) continue;
            double radius = distance.Value.Get((int)Math.Round(y), (int)Math.Round(x))![0];
            if (radius >= minimumPeakRadius) yield return (new(x, y), radius);
        }
    }
}
