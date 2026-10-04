using Android.Graphics;
using Android.Views;
using SkyEye.Core;
using Point = SkyEye.Core.Point;
using Paint = Android.Graphics.Paint;
using Color = Android.Graphics.Color;

namespace SkyEye.Companion.Hybrid;

// The frozen image and edits render natively in the main scene. No JPEG/base64,
// live read access or local media path is sent through the WebView.
public sealed class FrozenTableSurface(Android.Content.Context context, ShellSession session) : Android.Views.View(context)
{
    Bitmap? image;
    byte[]? source;
    int dragging = -1;
    bool moving;
    readonly float density = context.Resources!.DisplayMetrics!.Density;
    public void Bind(byte[]? jpeg)
    {
        if (!ReferenceEquals(source, jpeg))
        {
            image?.Dispose(); image = null; source = jpeg;
            if (jpeg is not null) image = BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length);
        }
        Visibility = jpeg is null ? ViewStates.Gone : ViewStates.Visible;
        ContentDescription = session.TableEdit.Mode == TableEditMode.Calibration ? "Frozen table corners. Tap corners or drag proposed points, then use the review controls."
            : "Frozen ball layout. Tap a ball to select it and drag to adjust its position. Review controls are below.";
        Invalidate();
    }
    FrozenFrameFit Fit => image is null ? default : FrozenFrameFit.Fit(Width, Height, image.Width, image.Height);
    Point Screen(Point point) => Fit.Screen(point);
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas); canvas.DrawColor(Color.Black);
        if (image is null || Width <= 0 || Height <= 0) return;
        var fit = Fit; using var paint = new Paint { AntiAlias = true };
        canvas.Save(); canvas.Translate((float)(fit.Left + fit.Width), (float)fit.Top); canvas.Rotate(90);
        canvas.Scale((float)(fit.Height / image.Width), (float)(fit.Height / image.Width));
        canvas.DrawBitmap(image, 0, 0, paint); canvas.Restore();
        var draft = session.TableEdit;
        if (draft.Mode == TableEditMode.Calibration)
        {
            for (int i = 0; i < draft.Corners.Count; i++)
            {
                var point = Screen(draft.Corners[i]); paint.Color = Color.Cyan; paint.SetStyle(Paint.Style.Fill);
                canvas.DrawCircle((float)point.X, (float)point.Y, 8 * density, paint);
                paint.Color = Color.Black; paint.TextSize = 12 * density; paint.TextAlign = Paint.Align.Center;
                canvas.DrawText((i + 1).ToString(), (float)point.X, (float)point.Y + 4 * density, paint);
                if (i > 0) Line(canvas, Screen(draft.Corners[i - 1]), point, paint);
            }
            if (draft.Corners.Count == 4) Line(canvas, Screen(draft.Corners[3]), Screen(draft.Corners[0]), paint);
        }
        if (draft.Mode != TableEditMode.Correction || draft.Calibration is not { } calibration) return;
        for (int i = 0; i < draft.Balls.Count; i++)
        {
            var ball = draft.Balls[i]; var point = Screen(calibration.Unmap(ball.Position));
            var edge = Screen(calibration.Unmap(ball.Position + new Point(PoolGeometry.Radius, 0)));
            float radius = (float)Math.Max(5 * density, (edge - point).Length);
            paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = (i == draft.Selected ? 5 : 2) * density;
            paint.Color = ball.Kind switch { "red" => Color.Red, "yellow" => Color.Yellow, "cue" => Color.White, _ => Color.Cyan };
            canvas.DrawCircle((float)point.X, (float)point.Y, radius, paint);
            if (i == draft.Selected) { paint.Color = Color.Cyan; canvas.DrawCircle((float)point.X, (float)point.Y, radius + 7 * density, paint); }
        }
    }
    void Line(Canvas canvas, Point a, Point b, Paint paint)
    {
        paint.Color = Color.Cyan; paint.StrokeWidth = 2 * density;
        canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, paint);
    }
    public override bool OnTouchEvent(MotionEvent? motion)
    {
        if (motion is null || image is null || !session.EditingTable) return false;
        if (motion.PointerCount != 1) { moving = false; dragging = -1; return true; }
        var screen = new Point(motion.GetX(), motion.GetY()); var normalized = Fit.Image(screen);
        var draft = session.TableEdit;
        if (motion.ActionMasked == MotionEventActions.Down)
        {
            dragging = -1; moving = false;
            var points = draft.Mode == TableEditMode.Calibration ? draft.Corners.ToArray()
                : draft.Calibration is { } calibration ? draft.Balls.Select(b => calibration.Unmap(b.Position)).ToArray() : [];
            if (!draft.AddingBall)
            {
                var hit = points.Select((point, index) => new { Index = index, Distance = (Screen(point) - screen).Length }).MinBy(p => p.Distance);
                if (hit is not null && hit.Distance < 24 * density)
                {
                    dragging = hit.Index;
                    if (draft.Mode == TableEditMode.Correction) session.EditTable(e => e.Select(dragging));
                }
            }
            return true;
        }
        if (motion.ActionMasked == MotionEventActions.Move && dragging >= 0 && normalized is { } moved)
        {
            moving = true;
            session.EditTable(e => { if (e.Mode == TableEditMode.Calibration) e.MoveCorner(dragging, moved); else e.Place(moved); });
        }
        if (motion.ActionMasked == MotionEventActions.Up)
        {
            if (!moving && normalized is { } picked)
                session.EditTable(e => { if (e.Mode == TableEditMode.Calibration && dragging < 0) e.AddCorner(picked); else if (e.AddingBall) e.Place(picked); });
            PerformClick(); dragging = -1; moving = false;
        }
        if (motion.ActionMasked == MotionEventActions.Cancel) { dragging = -1; moving = false; }
        return true;
    }
    protected override void Dispose(bool disposing) { if (disposing) { image?.Dispose(); image = null; source = null; } base.Dispose(disposing); }
}
