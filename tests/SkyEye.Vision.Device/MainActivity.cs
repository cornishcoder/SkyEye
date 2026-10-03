using Android.App;
using Android.OS;
using Android.Widget;
using Org.Opencv.Android;
using Org.Opencv.Core;
using Org.Opencv.Imgcodecs;
using Org.Opencv.Imgproc;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Text.Json;

namespace SkyEye.Vision.Device;

[Activity(Name = "uk.co.skyeye.visionchecks.MainActivity", Label = "SkyEye Vision Checks", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state); var text = new TextView(this) { Text = "Checking C# OpenCV image operations…", TextSize = 18 }; SetContentView(text);
        _ = Task.Run(() =>
        {
            object result;
            try
            {
                var watch = Stopwatch.StartNew();
                if (!OpenCVLoader.InitLocal()) throw new Exception("OpenCV library did not load");
                using var stream = Assets!.Open("pixel-table.jpg"); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
                using var encoded = new MatOfByte(bytes.ToArray()); using var frame = new MatLease(Imgcodecs.Imdecode(encoded, Imgcodecs.ImreadColor));
                using var hsv = new MatLease(new Mat()); using var mask = new MatLease(new Mat()); using var cleaned = new MatLease(new Mat()); using var circles = new MatLease(new Mat());
                Imgproc.CvtColor(frame.Value, hsv.Value, Imgproc.ColorBgr2hsv);
                using var low = new Scalar(0, 70, 70); using var high = new Scalar(179, 255, 255); Core.InRange(hsv.Value, low, high, mask.Value);
                using var size = new Size(3, 3); using var kernel = new MatLease(Imgproc.GetStructuringElement(Imgproc.MorphEllipse, size));
                Imgproc.MorphologyEx(mask.Value, cleaned.Value, Imgproc.MorphOpen, kernel.Value);
                Imgproc.HoughCircles(cleaned.Value, circles.Value, Imgproc.HoughGradient, 1.3, 35, 150, 15, 15, 35);
                using var src = new MatOfPoint2f(new Point(150, 160), new Point(1725, 181), new Point(1702, 1046), new Point(124, 929));
                using var dst = new MatOfPoint2f(new Point(1000, 0), new Point(1000, 2000), new Point(0, 2000), new Point(0, 0));
                using var transform = new MatLease(Imgproc.GetPerspectiveTransform(src, dst)); using var warped = new MatLease(new Mat()); using var outputSize = new Size(1000, 2000);
                Imgproc.WarpPerspective(frame.Value, warped.Value, transform.Value, outputSize);
                if (warped.Value.Rows() != 2000 || warped.Value.Cols() != 1000 || frame.Value.Channels() != 3) throw new Exception("Image primitive shape mismatch");
                result = new { passed = true, version = Core.Version, model = Build.Model, width = frame.Value.Cols(), height = frame.Value.Rows(), circles = circles.Value.Cols(), elapsedMs = watch.Elapsed.TotalMilliseconds };
            }
            catch (Exception e) { result = new { passed = false, error = e.ToString() }; }
            string json = JsonSerializer.Serialize(result); File.WriteAllText(Path.Combine(FilesDir!.AbsolutePath, "vision-checks.json"), json); RunOnUiThread(() => text.Text = json);
        });
    }
    sealed class MatLease(Mat value) : IDisposable { public Mat Value { get; } = value; public void Dispose() { Value.Release(); Value.Dispose(); } }
}
