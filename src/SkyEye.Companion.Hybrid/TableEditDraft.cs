using System.Globalization;
using SkyEye.Core;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion.Hybrid;

public enum TableEditMode { None, Calibration, Correction }

// A reviewed preview, never the tracked layout. Native frame bytes and remote
// access stay in ShellSession; committing requires the original service scope.
public sealed class TableEditDraft
{
    public TableEditMode Mode { get; private set; }
    public long Generation { get; private set; }
    public long TableVersion { get; private set; }
    public TableCalibration? Calibration { get; private set; }
    readonly List<Point> corners = [];
    readonly List<Ball> balls = [];
    public IReadOnlyList<Point> Corners => corners;
    public IReadOnlyList<Ball> Balls => balls;
    public int Selected { get; private set; } = -1;
    public bool AddingBall { get; private set; }
    public string Kind { get; private set; } = "red";
    long nextBall;
    public void BeginCalibration(long generation, IEnumerable<Point>? proposed)
    {
        Clear(); Mode = TableEditMode.Calibration; Generation = generation;
        if (proposed is not null) corners.AddRange(proposed.Take(4));
    }
    public void BeginCorrection(long generation, TableState table, TableCalibration calibration)
    {
        Clear(); Mode = TableEditMode.Correction; Generation = generation; TableVersion = table.Version;
        Calibration = calibration; balls.AddRange(table.Balls);
    }
    public bool Matches(long generation, long? version) => Mode != TableEditMode.None && Generation == generation
        && (Mode == TableEditMode.Calibration || TableVersion == version);
    public void Clear()
    {
        Mode = TableEditMode.None; corners.Clear(); balls.Clear(); Calibration = null;
        Selected = -1; AddingBall = false; nextBall = 0;
    }
    public void ResetCorners() { if (Mode == TableEditMode.Calibration) corners.Clear(); }
    public void UndoCorner() { if (Mode == TableEditMode.Calibration && corners.Count > 0) corners.RemoveAt(corners.Count - 1); }
    public void MoveCorner(int index, Point image) { if (Mode == TableEditMode.Calibration && index >= 0 && index < corners.Count && InImage(image)) corners[index] = image; }
    public void AddCorner(Point image)
    {
        if (Mode == TableEditMode.Calibration && corners.Count < 4 && InImage(image)) corners.Add(image);
    }
    public void Select(int index)
    {
        if (Mode != TableEditMode.Correction || index < 0 || index >= balls.Count) return;
        Selected = index; Kind = balls[index].Kind; AddingBall = false;
    }
    public void SetKind(string kind)
    {
        if (kind is not ("cue" or "red" or "yellow" or "black")) throw new ArgumentException("Choose a ball colour.");
        Kind = kind;
        if (Mode == TableEditMode.Correction && Selected >= 0 && !AddingBall) balls[Selected] = balls[Selected] with { Kind = kind };
    }
    public void AddBallMode() { if (Mode == TableEditMode.Correction) { AddingBall = true; Selected = -1; } }
    public void Place(Point image)
    {
        if (Mode != TableEditMode.Correction || Calibration is null || !InImage(image)) return;
        var point = Calibration.Map(image);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X is < 0 or > 2 || point.Y is < 0 or > 1) return;
        if (AddingBall)
        {
            if (balls.Count >= 16) throw new ArgumentException("Remove an incorrect detection before adding another ball.");
            balls.Add(new("edit-" + ++nextBall, Kind, point, 1)); Selected = balls.Count - 1; AddingBall = false;
        }
        else if (Selected >= 0) balls[Selected] = balls[Selected] with { Position = point, Confidence = 1 };
    }
    public void DeleteSelected()
    {
        if (Mode == TableEditMode.Correction && Selected >= 0) { balls.RemoveAt(Selected); Selected = -1; }
    }
    public string LayoutText() => string.Join("\n", balls.Select(b => $"{b.Kind} {b.Position.X.ToString("R", CultureInfo.InvariantCulture)} {b.Position.Y.ToString("R", CultureInfo.InvariantCulture)}"));
    static bool InImage(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && point.X is >= 0 and <= 1 && point.Y is >= 0 and <= 1;
}

// Portrait fit for the unchanged landscape camera snapshot. UI picking and
// drawing use this same inverse; letterbox space never becomes a corner.
public readonly record struct FrozenFrameFit(double Left, double Top, double Width, double Height)
{
    public static FrozenFrameFit Fit(double viewportWidth, double viewportHeight, double imageWidth, double imageHeight)
    {
        double scale = Math.Min(viewportWidth / imageHeight, viewportHeight / imageWidth);
        double width = imageHeight * scale, height = imageWidth * scale;
        return new((viewportWidth - width) / 2, (viewportHeight - height) / 2, width, height);
    }
    public Point Screen(Point image) => new(Left + (1 - image.Y) * Width, Top + image.X * Height);
    public Point? Image(Point screen) => Width <= 0 || Height <= 0 || screen.X < Left || screen.X > Left + Width || screen.Y < Top || screen.Y > Top + Height
        ? null : new((screen.Y - Top) / Height, 1 - (screen.X - Left) / Width);
}
