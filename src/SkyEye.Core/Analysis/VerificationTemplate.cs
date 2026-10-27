namespace SkyEye.Core.Analysis;

public sealed record TemplateDisplacement(double Dx, double Dy, double Residual);

/// <summary>Bounded colour patch translation, including smooth ball outlines that
/// do not supply four stable optical-flow corners. Per-channel affine photometric
/// normalization removes bounded exposure and white-balance changes while retaining
/// displacement and residual shape changes.</summary>
public static class VerificationTemplate
{
    public static TemplateDisplacement? Compare(byte[] before, byte[] after, int width, int height, int cx, int cy, int radius)
    {
        if (before.Length != width * height * 3 || after.Length != before.Length || radius is < 5 or > 60) throw new ArgumentException("Invalid motion patch.");
        const int reach = 4;
        if (cx - radius - reach < 0 || cy - radius - reach < 0 || cx + radius + reach >= width || cy + radius + reach >= height) return null;
        var scores = new double[9, 9]; double best = double.PositiveInfinity; int bx = 0, by = 0;
        for (int dy = -reach; dy <= reach; dy++) for (int dx = -reach; dx <= reach; dx++)
        {
            double sumA = 0, sumB = 0, squareA = 0, squareB = 0, cross = 0;
            double variance = 0, error = 0; int count = 0;
            for (int channel = 0; channel < 3; channel++)
            {
                sumA = sumB = squareA = squareB = cross = 0; count = 0;
                for (int y = -radius; y <= radius; y += 2) for (int x = -radius; x <= radius; x += 2)
                {
                    if (x * x + y * y > radius * radius) continue;
                    int a = before[((cy + y) * width + cx + x) * 3 + channel];
                    int b = after[((cy + y + dy) * width + cx + x + dx) * 3 + channel];
                    sumA += a; sumB += b; squareA += a * a; squareB += b * b; cross += a * b; count++;
                }
                double varianceA = Math.Max(0, squareA - sumA * sumA / count);
                double varianceB = Math.Max(0, squareB - sumB * sumB / count);
                double covariance = cross - sumA * sumB / count;
                double gain = varianceA > 1 ? covariance / varianceA : 1;
                double channelVariance = varianceA + varianceB;
                variance += channelVariance;
                if (!double.IsFinite(gain) || gain is < .5 or > 2)
                    error += channelVariance;
                else
                    error += Math.Max(0, varianceB - 2 * gain * covariance + gain * gain * varianceA);
            }
            if (variance / (6 * count) < 4) return null;
            double score = Math.Max(0, error) / variance;
            scores[dx + reach, dy + reach] = score;
            if (score < best) { best = score; bx = dx; by = dy; }
        }
        if (best > .05 || Math.Abs(bx) == reach || Math.Abs(by) == reach) return null;
        double Offset(double left, double centre, double right)
        {
            double denominator = left - 2 * centre + right;
            return denominator > .00001 ? Math.Clamp(.5 * (left - right) / denominator, -.5, .5) : 0;
        }
        int ix = bx + reach, iy = by + reach;
        // A flat/ambiguous minimum cannot establish zero displacement.
        if (scores[ix - 1, iy] + scores[ix + 1, iy] - 2 * best < .0001
            || scores[ix, iy - 1] + scores[ix, iy + 1] - 2 * best < .0001) return null;
        return new(bx + Offset(scores[ix - 1, iy], best, scores[ix + 1, iy]),
            by + Offset(scores[ix, iy - 1], best, scores[ix, iy + 1]), best);
    }

    /// <summary>Grayscale ball-body matching for high-rate motion analysis.</summary>
    public static TemplateDisplacement? CompareGray(byte[] before, byte[] after, int width, int height,
        int cx, int cy, int radius)
    {
        if (width <= 0 || height <= 0 || before.Length != checked(width * height)
            || after.Length != before.Length || radius is < 4 or > 60)
            throw new ArgumentException("Invalid grayscale motion patch.");
        const int reach = 12;
        if (cx - radius - reach < 0 || cy - radius - reach < 0
            || cx + radius + reach >= width || cy + radius + reach >= height) return null;

        var scores = new double[reach * 2 + 1, reach * 2 + 1];
        double best = double.PositiveInfinity;
        int bestX = 0, bestY = 0;
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            double sumA = 0, sumB = 0, squareA = 0, squareB = 0, cross = 0;
            int count = 0;
            for (int y = -radius; y <= radius; y += 2)
            for (int x = -radius; x <= radius; x += 2)
            {
                if (x * x + y * y > radius * radius) continue;
                int a = before[(cy + y) * width + cx + x];
                int b = after[(cy + y + dy) * width + cx + x + dx];
                sumA += a; sumB += b; squareA += a * a; squareB += b * b; cross += a * b; count++;
            }
            double variance = squareA + squareB - (sumA * sumA + sumB * sumB) / count;
            if (variance / (2 * count) < 4) return null;
            double error = variance - 2 * (cross - sumA * sumB / count);
            double score = Math.Max(0, error) / variance;
            scores[dx + reach, dy + reach] = score;
            if (score < best) { best = score; bestX = dx; bestY = dy; }
        }
        if (best > .08 || Math.Abs(bestX) == reach || Math.Abs(bestY) == reach) return null;
        double Offset(double left, double centre, double right)
        {
            double denominator = left - 2 * centre + right;
            return denominator > .00001 ? Math.Clamp(.5 * (left - right) / denominator, -.5, .5) : 0;
        }
        int ix = bestX + reach, iy = bestY + reach;
        if (scores[ix - 1, iy] + scores[ix + 1, iy] - 2 * best < .0001
            || scores[ix, iy - 1] + scores[ix, iy + 1] - 2 * best < .0001) return null;
        return new(bestX + Offset(scores[ix - 1, iy], best, scores[ix + 1, iy]),
            bestY + Offset(scores[ix, iy - 1], best, scores[ix, iy + 1]), best);
    }
}
