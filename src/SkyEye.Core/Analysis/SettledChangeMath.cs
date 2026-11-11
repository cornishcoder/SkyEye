namespace SkyEye.Core.Analysis;

/// <summary>Pure managed parts of the D3 settled-change comparison (no OpenCV), mirroring the T4 v3
/// grey rule at half scale (tools/evidence/prototypes/settled_change.py, work/evidence-audit/d3/scale_check.py).
/// Rasters are row-major, width x height, portrait (VisionAnalyzer.Warp corner order) with a margin.</summary>
public static class SettledChangeMath
{
    static readonly float[] LogTable = Enumerable.Range(0, 256).Select(i => (float)Math.Log(i + 8.0)).ToArray();

    /// <summary>log(after+8) - log(before+8) per pixel.</summary>
    public static float[] LogRatio(byte[] greyBefore, byte[] greyAfter)
    {
        if (greyBefore.Length != greyAfter.Length) throw new ArgumentException("Rasters differ in size.");
        var result = new float[greyBefore.Length];
        for (int i = 0; i < result.Length; i++) result[i] = LogTable[greyAfter[i]] - LogTable[greyBefore[i]];
        return result;
    }

    /// <summary>Removes a smooth gain field: the median of each whole tile, resized bilinearly to the
    /// raster as OpenCV's INTER_LINEAR resize does (half-pixel centres, clamped edges). Returns |residual|.</summary>
    public static float[] AbsoluteTileResidual(float[] values, int width, int height, int tile)
    {
        int tilesX = width / tile, tilesY = height / tile;
        if (tilesX < 1 || tilesY < 1) throw new ArgumentException("Raster smaller than one tile.");
        var medians = new float[tilesX * tilesY];
        var buffer = new float[tile * tile];
        for (int ty = 0; ty < tilesY; ty++)
        for (int tx = 0; tx < tilesX; tx++)
        {
            int k = 0;
            for (int y = ty * tile; y < (ty + 1) * tile; y++)
                for (int x = tx * tile; x < (tx + 1) * tile; x++) buffer[k++] = values[y * width + x];
            medians[ty * tilesX + tx] = Median(buffer);
        }
        var (x0, x1, fx) = LinearTaps(width, tilesX);
        var (y0, y1, fy) = LinearTaps(height, tilesY);
        var result = new float[values.Length];
        for (int y = 0; y < height; y++)
        {
            float wy = fy[y];
            for (int x = 0; x < width; x++)
            {
                float wx = fx[x];
                float top = medians[y0[y] * tilesX + x0[x]] * (1 - wx) + medians[y0[y] * tilesX + x1[x]] * wx;
                float bottom = medians[y1[y] * tilesX + x0[x]] * (1 - wx) + medians[y1[y] * tilesX + x1[x]] * wx;
                result[y * width + x] = Math.Abs(values[y * width + x] - (top * (1 - wy) + bottom * wy));
            }
        }
        return result;
    }

    static (int[] Low, int[] High, float[] Weight) LinearTaps(int destination, int source)
    {
        var low = new int[destination]; var high = new int[destination]; var weight = new float[destination];
        double scale = source / (double)destination;
        for (int d = 0; d < destination; d++)
        {
            double f = (d + .5) * scale - .5;
            int s = (int)Math.Floor(f); double w = f - s;
            if (s < 0) { s = 0; w = 0; }
            if (s >= source - 1) { s = source - 1; w = 0; }
            low[d] = s; high[d] = Math.Min(s + 1, source - 1); weight[d] = (float)w;
        }
        return (low, high, weight);
    }

    /// <summary>numpy.median: the mean of the two middle values for an even count. Sorts in place.</summary>
    public static float Median(float[] values)
    {
        Array.Sort(values);
        int n = values.Length;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2f;
    }

    /// <summary>OpenCV GaussianBlur with ksize 3 and sigma 0 (kernel 1/4, 1/2, 1/4; BORDER_REFLECT_101).</summary>
    public static float[] GaussianBlur3(float[] values, int width, int height)
    {
        var horizontal = new float[values.Length]; var result = new float[values.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int l = x == 0 ? 1 : x - 1, r = x == width - 1 ? width - 2 : x + 1;
            horizontal[y * width + x] = .25f * values[y * width + l] + .5f * values[y * width + x] + .25f * values[y * width + r];
        }
        for (int y = 0; y < height; y++)
        {
            int u = y == 0 ? 1 : y - 1, d = y == height - 1 ? height - 2 : y + 1;
            for (int x = 0; x < width; x++)
                result[y * width + x] = .25f * horizontal[u * width + x] + .5f * horizontal[y * width + x] + .25f * horizontal[d * width + x];
        }
        return result;
    }

    /// <summary>Fraction above threshold in the cushion band (table minus an inset) and the inner bed,
    /// and the bed's 99th percentile (numpy linear interpolation).</summary>
    public static (double Band, double Bed, double BedP99) Drift(float[] change, SettledChangeSettings s)
    {
        int width = s.RasterWidth, m = s.Margin, e = s.BandInset;
        int bandAbove = 0, bandTotal = 0, bedAbove = 0; var bed = new List<float>();
        for (int y = m; y < m + s.TableHeight; y++)
        for (int x = m; x < m + s.TableWidth; x++)
        {
            float v = change[y * width + x];
            bool inner = y >= m + e && y < m + s.TableHeight - e && x >= m + e && x < m + s.TableWidth - e;
            if (inner) { bed.Add(v); if (v > s.Threshold) bedAbove++; }
            else { bandTotal++; if (v > s.Threshold) bandAbove++; }
        }
        var sorted = bed.ToArray(); Array.Sort(sorted);
        double rank = .99 * (sorted.Length - 1); int lo = (int)Math.Floor(rank);
        double p99 = sorted.Length == 0 ? 0 : sorted[lo] + (sorted[Math.Min(lo + 1, sorted.Length - 1)] - sorted[lo]) * (rank - lo);
        return (bandTotal == 0 ? 0 : bandAbove / (double)bandTotal, bed.Count == 0 ? 0 : bedAbove / (double)bed.Count, p99);
    }

    /// <summary>T4 zone: jaw within 5 ball radii of a pocket point, cushion within 4.4 radii of a table edge, else table.</summary>
    public static string Zone(double rasterX, double rasterY, SettledChangeSettings s)
    {
        double x = rasterX - s.Margin, y = rasterY - s.Margin, w = s.TableWidth, h = s.TableHeight, r = s.BallRadius;
        (double X, double Y)[] pockets = [(0, 0), (w, 0), (0, h / 2), (w, h / 2), (0, h), (w, h)];
        if (pockets.Min(p => Math.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y))) < 5.0 * r) return "jaw";
        if (Math.Min(Math.Min(x, w - x), Math.Min(y, h - y)) < 4.4 * r) return "cushion";
        return "table";
    }

    /// <summary>Raster pixel to table coordinates (Ball.Position: X 0..2 along, Y 0..1 across),
    /// using the device harness mapping x = portraitY / width, y = 1 - portraitX / width.</summary>
    public static (double TableX, double TableY) RasterToTable(double rasterX, double rasterY, SettledChangeSettings s)
        => ((rasterY - s.Margin) / s.TableWidth, 1 - (rasterX - s.Margin) / s.TableWidth);

    /// <summary>The v3 region rule. Rail first (a hand or forearm crosses the outer cushion edge), then size.</summary>
    public static (bool BallSized, string? RejectReason) ClassifyRegion(double inscribedRadius, double area, double outsideFraction, SettledChangeSettings s)
    {
        if (outsideFraction > s.MaximumOutsideFraction) return (false, "rail");
        if (inscribedRadius > s.MaximumInscribedRadii * s.BallRadius || area > s.MaximumRegionBallAreas * s.BallArea) return (false, "oversize");
        if (inscribedRadius < s.MinimumInscribedRadii * s.BallRadius) return (false, "thin");
        return (true, null);
    }

    /// <summary>Arrival when the region was cloth before and is not after (difference above .25); departure the reverse.</summary>
    public static ChangeDirection Direction(double clothBefore, double clothAfter)
        => clothBefore - clothAfter > .25 ? ChangeDirection.Arrival : clothAfter - clothBefore > .25 ? ChangeDirection.Departure : ChangeDirection.Unclear;

    /// <summary>Per-channel median HSV of the open bed (the T4 cloth model, scaled to this raster).</summary>
    public static (double H, double S, double V) ClothModel(byte[] hsv, SettledChangeSettings s)
    {
        int w = s.RasterWidth, m = s.Margin;
        int insetY = (int)Math.Round(s.TableWidth * .25), insetX = (int)Math.Round(s.TableWidth * .1875);
        var hs = new List<float>(); var ss = new List<float>(); var vs = new List<float>();
        for (int y = m + insetY; y < m + s.TableHeight - insetY; y++)
        for (int x = m + insetX; x < m + s.TableWidth - insetX; x++)
        { int o = (y * w + x) * 3; hs.Add(hsv[o]); ss.Add(hsv[o + 1]); vs.Add(hsv[o + 2]); }
        return (Median(hs.ToArray()), Median(ss.ToArray()), Median(vs.ToArray()));
    }

    /// <summary>Fractions of the region's pixels that look like cloth, are black-ball dark, or are cue-ball white.</summary>
    public static (double Cloth, double Dark, double White) Appearance(byte[] hsv, IReadOnlyList<int> pixelIndices, (double H, double S, double V) cloth)
    {
        if (pixelIndices.Count == 0) return (0, 0, 0);
        int clothLike = 0, dark = 0, white = 0;
        foreach (int i in pixelIndices)
        {
            double h = hsv[i * 3], sat = hsv[i * 3 + 1], v = hsv[i * 3 + 2];
            double dh = Math.Abs(h - cloth.H); dh = Math.Min(dh, 180 - dh);
            if (dh < 12 && Math.Abs(sat - cloth.S) < 70 && v > .45 * cloth.V && v < 1.4 * cloth.V + 20) clothLike++;
            if (v < .35 * cloth.V) dark++;
            if (sat < 60 && v > 1.25 * cloth.V) white++;
        }
        double n = pixelIndices.Count;
        return (clothLike / n, dark / n, white / n);
    }

    /// <summary>OpenCV GaussianBlur with ksize 5 and sigma 0 (kernel 1,4,6,4,1 / 16; BORDER_REFLECT_101).</summary>
    public static float[] GaussianBlur5(float[] values, int width, int height)
    {
        static int Reflect(int i, int n) => i < 0 ? -i : i >= n ? 2 * n - 2 - i : i;
        float[] k = [1 / 16f, 4 / 16f, 6 / 16f, 4 / 16f, 1 / 16f];
        var horizontal = new float[values.Length]; var result = new float[values.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int d = -2; d <= 2; d++) sum += k[d + 2] * values[y * width + Reflect(x + d, width)];
            horizontal[y * width + x] = sum;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int d = -2; d <= 2; d++) sum += k[d + 2] * horizontal[Reflect(y + d, height) * width + x];
            result[y * width + x] = sum;
        }
        return result;
    }

    /// <summary>Morphological opening with a 3x3 square, as OpenCV with its default borders
    /// (erosion ignores pixels outside the image, dilation treats them as background).</summary>
    public static byte[] Open3(byte[] mask, int width, int height)
    {
        var eroded = new byte[mask.Length]; var result = new byte[mask.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte v = 1;
            for (int dy = -1; dy <= 1 && v != 0; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int yy = y + dy, xx = x + dx;
                if (yy < 0 || yy >= height || xx < 0 || xx >= width) continue;
                if (mask[yy * width + xx] == 0) { v = 0; break; }
            }
            eroded[y * width + x] = v;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            byte v = 0;
            for (int dy = -1; dy <= 1 && v == 0; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int yy = y + dy, xx = x + dx;
                if (yy < 0 || yy >= height || xx < 0 || xx >= width) continue;
                if (eroded[yy * width + xx] != 0) { v = 1; break; }
            }
            result[y * width + x] = v;
        }
        return result;
    }

    /// <summary>Halves a grey raster by 2x2 block means (OpenCV INTER_AREA for an exact factor of two).</summary>
    public static byte[] HalveGrey(byte[] grey, int width, int height)
    {
        int w = width / 2, h = height / 2; var result = new byte[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int a = grey[2 * y * width + 2 * x], b = grey[2 * y * width + 2 * x + 1],
                c = grey[(2 * y + 1) * width + 2 * x], d = grey[(2 * y + 1) * width + 2 * x + 1];
            result[y * w + x] = (byte)((a + b + c + d + 2) / 4);
        }
        return result;
    }

    /// <summary>T4 `scan` motion at quarter size: fraction of the table interior whose tile-gain-compensated
    /// log ratio, blurred 5x5 and opened 3x3, exceeds the motion threshold.</summary>
    public static double Motion(byte[] previousQuarterGrey, byte[] currentQuarterGrey, SettledChangeSettings s)
    {
        int w = s.RasterWidth / 2, h = s.RasterHeight / 2, m = s.Margin / 2;
        var change = GaussianBlur5(AbsoluteTileResidual(LogRatio(previousQuarterGrey, currentQuarterGrey), w, h, s.MotionTilePixels), w, h);
        var mask = new byte[change.Length];
        for (int i = 0; i < mask.Length; i++) mask[i] = change[i] > s.MotionThreshold ? (byte)1 : (byte)0;
        var opened = Open3(mask, w, h);
        int set = 0, total = 0;
        for (int y = m; y < h - m; y++)
            for (int x = m; x < w - m; x++) { total++; if (opened[y * w + x] != 0) set++; }
        return total == 0 ? 0 : set / (double)total;
    }

    /// <summary>Per-byte median of up to three equal-length rasters (one: a copy; two: the rounded mean).</summary>
    public static byte[] Median(IReadOnlyList<byte[]> rasters)
    {
        if (rasters.Count is < 1 or > 3) throw new ArgumentException("One to three rasters.");
        var result = (byte[])rasters[0].Clone();
        if (rasters.Count == 2)
            for (int i = 0; i < result.Length; i++) result[i] = (byte)((rasters[0][i] + rasters[1][i] + 1) / 2);
        else if (rasters.Count == 3)
            for (int i = 0; i < result.Length; i++)
            {
                byte a = rasters[0][i], b = rasters[1][i], c = rasters[2][i];
                result[i] = Math.Max(Math.Min(a, b), Math.Min(Math.Max(a, b), c));
            }
        return result;
    }

    /// <summary>Change grid over the table interior: one bit per cell set when at least the cell fraction is changed.</summary>
    public static (byte[] Bits, int Columns, int Rows) ChangeGrid(byte[] mask, SettledChangeSettings s)
    {
        int cell = s.GridCellPixels, columns = s.TableWidth / cell, rows = s.TableHeight / cell, w = s.RasterWidth, m = s.Margin;
        var bits = new byte[(columns * rows + 7) / 8];
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < columns; c++)
        {
            int set = 0;
            for (int y = m + r * cell; y < m + (r + 1) * cell; y++)
                for (int x = m + c * cell; x < m + (c + 1) * cell; x++) if (mask[y * w + x] != 0) set++;
            if (set >= s.GridCellFraction * cell * cell) { int bit = r * columns + c; bits[bit / 8] |= (byte)(1 << (bit % 8)); }
        }
        return (bits, columns, rows);
    }
}
