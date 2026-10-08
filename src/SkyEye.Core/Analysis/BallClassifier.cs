namespace SkyEye.Core.Analysis;

public readonly record struct HsvPixel(double H, double S, double V);
public sealed record ColourCluster(HsvPixel Centre, int Count);
public sealed record BallClassification(string Kind, string Pattern, double Quality, ColourCluster[] Clusters)
{
    public double? BodyDarkFraction { get; init; }
    public double? RimDarkFraction { get; init; }
}

/// <summary>Bounded C# port of the upstream four-colour K-means/white-area classifier.
/// Kind is adapted to red/yellow eight-ball; Pattern retains upstream solid/stripe semantics.
/// Quality is a feature score, not an estimated probability.</summary>
public static class BallClassifier
{
    /// <summary>Physical inventory limits are evidence against conflicting labels,
    /// not permission to invent another colour or discard a visible body.</summary>
    public static BallClassification[] ConstrainInventory(BallClassification[] labels)
    {
        var ambiguous = labels.GroupBy(b => b.Kind).Where(g => g.Key switch
        {
            "cue" or "black" => g.Count() > 1,
            "red" or "yellow" => g.Count() > 7,
            _ => false
        }).Select(g => g.Key).ToHashSet();
        return labels.Select(b => ambiguous.Contains(b.Kind) ? b with { Kind = "unknown", Quality = 0 } : b).ToArray();
    }
    public static BallClassification Classify(HsvPixel[] samples)
    {
        var pixels = samples.Where(p => double.IsFinite(p.H) && p.H is >= 0 and <= 179 && p.S is >= 0 and <= 255 && p.V is > 5 and <= 255).ToArray();
        if (pixels.Length < 8) return new("unknown", "unknown", 0, []);
        var clusters = Cluster(pixels);
        var coloured = clusters.MaxBy(c => c.Centre.S)!; var white = clusters.MaxBy(c => c.Centre.V - c.Centre.S)!;
        double ratio = coloured.Count / (double)Math.Max(1, white.Count);
        if (white.Centre.V < 200 && white.Centre.S > 80) ratio = 10;
        string pattern = ratio < 1.1 && coloured.Centre.S < 80 ? "white" : coloured.Centre.V < 80 ? "black" : ratio < 2 ? "stripe" : "solid";
        // Do not let the cue ball's small red spot or the black's number patch choose its identity.
        // Saturated red paint remains red in shadow; low brightness alone is
        // not evidence of a black ball under warm room lighting.
        double dark = pixels.Count(p => p.V < 40 || p.V < 80 && p.S < 160) / (double)pixels.Length;
        double darkBody = clusters.Where(c => c.Centre.V < 40 || c.Centre.V < 80 && c.Centre.S < 160).Sum(c => c.Count) / (double)pixels.Length;
        double neutral = pixels.Count(p => p.S < 100 && p.V > 130) / (double)pixels.Length;
        if (dark > .45 || pattern == "black" && darkBody > .45) return new("black", pattern, Math.Min(.9, .72 + Math.Max(dark, darkBody) * .18), clusters);
        if (neutral > .55) return new("cue", pattern, Math.Min(.9, .72 + neutral * .18), clusters);
        double warmCream = pixels.Count(p => p.H is >= 8 and <= 30 && p.S < 160 && p.V > 130) / (double)pixels.Length;
        if (warmCream > .55) return new("cue", pattern, Math.Min(.9, .72 + warmCream * .18), clusters);
        var colours = clusters.Where(c => c.Centre.S > 160 && c.Centre.V > 40 || c.Centre.S > 100 && c.Centre.V > 80).OrderByDescending(c => c.Count).ToArray();
        if (colours.Length == 0) return new("unknown", pattern, 0, clusters);
        var dominant = colours[0]; double hue = dominant.Centre.H * 2;
        string kind = hue is >= 18 and <= 75 ? "yellow" : hue < 18 || hue > 325 ? "red" : "unknown";
        double fraction = colours.Where(c => kind == "red" ? c.Centre.H * 2 < 18 || c.Centre.H * 2 > 325 : c.Centre.H * 2 is >= 18 and <= 75).Sum(c => c.Count) / (double)pixels.Length;
        if (fraction < .45) kind = "unknown";
        return new(kind, pattern, kind == "unknown" ? 0 : Math.Min(.9, .72 + fraction * .18), clusters);
    }
    /// <summary>Bounded verification recovery for a black body's large cream number patch.
    /// Inventory is not used to choose a replacement label. Conflicting or unsupported rims retain the baseline label.</summary>
    public static BallClassification ClassifyWithRim(HsvPixel[] samples, HsvPixel[] rim)
    {
        var baseline = Classify(samples);
        bool Valid(HsvPixel p) => double.IsFinite(p.H) && p.H is >= 0 and <= 179 && p.V is > 5 and <= 255 && p.S is >= 0 and <= 255;
        var body = samples.Where(Valid).ToArray();
        var edge = rim.Where(Valid).ToArray();
        bool Dark(HsvPixel p) => p.V < 40 || p.V < 80 && p.S < 160;
        baseline = baseline with { BodyDarkFraction = body.Length == 0 ? null : body.Count(Dark) / (double)body.Length,
            RimDarkFraction = edge.Length == 0 ? null : edge.Count(Dark) / (double)edge.Length };
        if (body.Length >= 8 && edge.Length >= 8 && baseline.Kind is "cue" or "unknown" && baseline.Pattern == "black"
            && baseline.BodyDarkFraction >= .25 && baseline.RimDarkFraction >= .45)
            return baseline with { Kind = "black", Quality = .75 };
        return baseline;
    }
    static ColourCluster[] Cluster(HsvPixel[] pixels)
    {
        // Deterministic farthest-point seeds handle fewer than four distinct colours.
        var centres = new List<HsvPixel> { pixels[pixels.Length / 2] };
        while (centres.Count < 4)
        {
            var next = pixels.MaxBy(p => centres.Min(c => Distance(p, c)));
            if (centres.Min(c => Distance(next, c)) < 1e-6) break;
            centres.Add(next);
        }
        var labels = new int[pixels.Length]; var counts = new int[centres.Count];
        for (int iteration = 0; iteration < 30; iteration++)
        {
            Array.Clear(counts); var sums = new HsvPixel[centres.Count];
            var hueSines = new double[centres.Count]; var hueCosines = new double[centres.Count];
            for (int i = 0; i < pixels.Length; i++)
            {
                int best = 0; double nearest = double.MaxValue;
                for (int j = 0; j < centres.Count; j++) { double d = Distance(pixels[i], centres[j]); if (d < nearest) { nearest = d; best = j; } }
                labels[i] = best; counts[best]++; var sum = sums[best]; var p = pixels[i]; sums[best] = new(0, sum.S + p.S, sum.V + p.V);
                double hueRadians = p.H * Math.PI / 90;
                hueSines[best] += Math.Sin(hueRadians); hueCosines[best] += Math.Cos(hueRadians);
            }
            double movement = 0;
            for (int j = 0; j < centres.Count; j++) if (counts[j] > 0)
            {
                double angle = Math.Atan2(hueSines[j], hueCosines[j]);
                if (angle < 0) angle += 2 * Math.PI;
                var mean = new HsvPixel(angle * 90 / Math.PI, sums[j].S / counts[j], sums[j].V / counts[j]);
                movement += Distance(mean, centres[j]); centres[j] = mean;
            }
            if (movement < .01) break;
        }
        return centres.Select((c, i) => new ColourCluster(c, counts[i])).Where(c => c.Count > 0).ToArray();
    }
    static double Distance(HsvPixel a, HsvPixel b)
    {
        // OpenCV hue wraps at 180; red values close to 0 and 179 are neighbours.
        double hue = Math.Abs(a.H - b.H);
        hue = Math.Min(hue, 180 - hue);
        return hue * hue + (a.S - b.S) * (a.S - b.S) + (a.V - b.V) * (a.V - b.V);
    }
}
