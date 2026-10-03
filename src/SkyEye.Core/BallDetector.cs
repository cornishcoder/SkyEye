namespace SkyEye.Core;

// Conservative blue-cloth prototype. Detections require visual confirmation before advice.
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
            byte kind = max < .20 ? (byte)4 : saturation < .22 && max > .72 ? (byte)3 : r > .40 && r > g * 1.35 && r > b * 1.35 ? (byte)1 : r > .5 && g > .35 && b < Math.Min(r, g) * .7 ? (byte)2 : (byte)0;
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
                foreach (int next in new[] { x > 0 ? pixel - 1 : -1, x + 1 < width ? pixel + 1 : -1, y > 0 ? pixel - width : -1, y + 1 < height ? pixel + width : -1 }) if (next >= 0 && !seen[next] && kinds[next] == kind) { seen[next] = true; queue.Enqueue(next); }
            }
            if (count < 8) continue;
            var centre = calibration.Map(new(sx / count / width, sy / count / height));
            var a = calibration.Map(new((double)xmin / width, (double)ymin / height)); var c = calibration.Map(new((double)xmax / width, (double)ymax / height));
            double dx = Math.Abs(c.X - a.X), dy = Math.Abs(c.Y - a.Y), fill = (double)count / ((xmax - xmin + 1) * (ymax - ymin + 1));
            if (dx > .09 || dy > .09) { if (centre.X is > .08 and < 1.92 && centre.Y is > .08 and < .92) occluded = true; continue; }
            if (dx < .025 || dy < .025 || dx / dy is < .55 or > 1.8 || fill < .45 || centre.X is < .02 or > 1.98 || centre.Y is < .02 or > .98) continue;
            var name = kind switch { 1 => "red", 2 => "yellow", 3 => "cue", _ => "black" };
            balls.Add(new(name + "-" + balls.Count, name, centre, Math.Min(.88, .65 + .25 * fill)));
        }
        if (balls.Count > 16 || balls.Count(b => b.Kind == "cue") != 1 || balls.Count(b => b.Kind == "black") > 1 || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7) occluded = true;
        return (balls.ToArray(), occluded);
    }
}
