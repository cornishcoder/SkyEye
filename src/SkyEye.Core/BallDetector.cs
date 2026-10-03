namespace SkyEye.Core;

// Blue-cloth prototype. Hue separates orange/yellow balls from reds; cream cue balls
// are neutral/warm rather than pure white. Detections still require confirmation.
public static class BallDetector
{
    public static (Ball[] Balls, bool Occluded) Detect(int[] pixels, int width, int height, TableCalibration calibration)
    {
        if (pixels.Length != width * height || width < 1 || height < 1) throw new ArgumentException("Invalid image.");
        var kinds = new byte[pixels.Length];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var point = calibration.Map(new((double)x / width, (double)y / height)); if (point.X is < 0 or > 2 || point.Y is < 0 or > 1) continue;
            int colour = pixels[y * width + x]; double r = ((colour >> 16) & 255) / 255.0, g = ((colour >> 8) & 255) / 255.0, b = (colour & 255) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), saturation = max == 0 ? 0 : (max - min) / max;
            double delta = max - min;
            double hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
            byte kind = max < .20 ? (byte)4
                : saturation < .40 && max > .50 && r >= b && g >= b * .95 ? (byte)3
                : saturation > .45 && max > .30 && hue is >= 18 and <= 75 ? (byte)2
                : saturation > .45 && max > .30 && (hue < 18 || hue > 325) ? (byte)1 : (byte)0;
            kinds[y * width + x] = kind;
        }
        var seen = new bool[pixels.Length]; var queue = new Queue<int>(); var balls = new List<Ball>(); bool occluded = false;
        for (int start = 0; start < kinds.Length; start++)
        {
            if (seen[start] || kinds[start] == 0) continue; byte kind = kinds[start]; seen[start] = true; queue.Enqueue(start);
            int count = 0, xmin = width, xmax = 0, ymin = height, ymax = 0; double sx = 0, sy = 0;
            while (queue.Count > 0)
            {
                int pixel = queue.Dequeue(), x = pixel % width, y = pixel / width; count++; sx += x; sy += y; xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x); ymin = Math.Min(ymin, y); ymax = Math.Max(ymax, y);
                void Add(int next) { if (!seen[next] && kinds[next] == kind) { seen[next] = true; queue.Enqueue(next); } }
                if (x > 0) Add(pixel - 1); if (x + 1 < width) Add(pixel + 1); if (y > 0) Add(pixel - width); if (y + 1 < height) Add(pixel + width);
            }
            if (count < 8) continue;
            var centre = calibration.Map(new(sx / count / width, sy / count / height));
            Point[] bounds = [calibration.Map(new((double)xmin / width, (double)ymin / height)), calibration.Map(new((double)xmax / width, (double)ymin / height)), calibration.Map(new((double)xmax / width, (double)ymax / height)), calibration.Map(new((double)xmin / width, (double)ymax / height))];
            double dx = bounds.Max(p => p.X) - bounds.Min(p => p.X), dy = bounds.Max(p => p.Y) - bounds.Min(p => p.Y), fill = (double)count / ((xmax - xmin + 1) * (ymax - ymin + 1));
            if (dx > .09 || dy > .09) { if (centre.X is > .08 and < 1.92 && centre.Y is > .08 and < .92) occluded = true; continue; }
            if (dx < .025 || dy < .025 || dx / dy is < .55 or > 1.8 || fill < .45 || centre.X < PoolGeometry.Radius || centre.X > 2 - PoolGeometry.Radius || centre.Y < PoolGeometry.Radius || centre.Y > 1 - PoolGeometry.Radius) continue;
            // Dark openings near the six pockets are not evidence of another black ball.
            if (kind == 4 && PoolGeometry.Pockets.Any(p => (centre - p).Length < .10)) continue;
            var name = kind switch { 1 => "red", 2 => "yellow", 3 => "cue", _ => "black" };
            balls.Add(new(name + "-" + balls.Count, name, centre, Math.Min(.88, .65 + .25 * fill)));
        }
        if (balls.Count > 16 || balls.Count(b => b.Kind == "cue") != 1 || balls.Count(b => b.Kind == "black") > 1 || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7) occluded = true;
        return (balls.ToArray(), occluded);
    }
}
