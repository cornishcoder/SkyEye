using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using System.Globalization;
using System.Text.Json;
using Point = SkyEye.Core.Point;
using View = Android.Views.View;
using Color = Android.Graphics.Color;
using RectF = Android.Graphics.RectF;
using Paint = Android.Graphics.Paint;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    TableCalibration? calibration => tableAnalysis.State.Calibration;
    TableState? tableState => tableAnalysis.State.Table;
    Bitmap? tableBitmap;
    byte[]? displayedTableFrame;
    TextView tableLabel = null!;
    bool voiceMuted;
    Spinner groupSelector = null!;
    string selectedShotId = "";
    TableView tableView = null!;
    void RestoreCalibration()
    {
        try
        {
            if (!tableAnalysis.RestoreCalibration()) return;
            tableView.Calibration = calibration;
            tableLabel.Text = "Saved calibration loaded. Recalibrate if the mount moved; readiness is automatic after a clear, still check.";
        }
        catch { Say("Saved calibration could not be loaded. Calibrate this table again."); }
    }
    void BuildTableUi(LinearLayout root)
    {
        root.AddView(Label("Table awareness", 22));
        tableLabel = Label("Calibrate once. A clear, still layout becomes ready automatically; correct detections if needed.", 13); root.AddView(tableLabel);
        tableView = new TableView(this); root.AddView(tableView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)(220 * Resources!.DisplayMetrics!.Density)));
        var row = Row(root); Add(row, "Calibrate", () => _ = ActionAsync(CalibrateAsync)); Add(row, "Correct layout", CorrectLayout);
        groupSelector = new Spinner(this); groupSelector.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, ["Open table", "I'm reds", "I'm yellows", "I'm on the black"]);
        groupSelector.ItemSelected += (sender, e) => { _ = DispatchAppAsync(new SetTargetGroup((AdviceTargetGroup)e.Position)); }; root.AddView(groupSelector);
    }
    async Task CalibrateAsync()
    {
        InvalidateTable("Selecting a new table calibration.");
        var draft = await tableAnalysis.PrepareCalibrationAsync(lifetime.Token);
        if (lifetime.IsCancellationRequested || tableAnalysis.State.Generation != draft.Generation) return;
        SetTableImage(draft.Jpeg);
        tableView.Picks.Clear(); tableView.Picking = true; tableView.Calibration = null; tableView.Balls = []; tableView.Shots = []; tableView.Invalidate();
        Say("Tap the cloth corners where the cushion faces meet: top left, top right, bottom right, bottom left. Long edge runs left to right. This frame stays still while you calibrate.");
        void Accept(Point[] corners)
        {
            try
            {
                tableAnalysis.ApplyCalibration(draft.Generation, corners);
                tableView.Calibration = calibration; tableView.Picking = false; tableView.Calibrated = null;
                ClearCandidatePresentation("Checking the new calibration..."); _ = ActionAsync(() => UpdateTableAsync(ObservationMode.VerifyForAdvice));
            }
            catch (Exception e) { tableView.Picks.Clear(); Say(e.Message); }
        }
        tableView.Calibrated = Accept;
        if (draft.Proposed is null) return;
        tableView.Picks.AddRange(draft.Proposed); tableView.Invalidate();
        new AlertDialog.Builder(this)!.SetTitle("Check the proposed table corners")!.SetMessage("Use these four cyan points only if they match the cloth boundary. Otherwise select the corners yourself.")!
            .SetPositiveButton("Use corners", (_, _) => Accept(draft.Proposed))!
            .SetNegativeButton("Select manually", (_, _) => { tableView.Picks.Clear(); tableView.Invalidate(); })!
            .SetOnCancelListener(new CornerDialogCancel(tableView))!.Show();
    }
    void InvalidateTable(string reason)
    {
        tableAnalysis.Invalidate();
        selectedShotId = ""; tableView.Picking = false; tableView.Picks.Clear(); tableView.Calibrated = null;
        tableView.Image = null; displayedTableFrame = null; tableBitmap?.Dispose(); tableBitmap = null;
        tableView.Balls = []; tableView.Shots = []; tableView.Invalidate(); ClearCandidatePresentation(reason); tableLabel.Text = reason;
    }
    async Task UpdateTableAsync(ObservationMode mode = ObservationMode.Single)
    {
        long previousVersion = tableState?.Version ?? -1;
        var scope = tableAnalysis.State;
        if (scope.Source is null || scope.Calibration is null) return;
        var receipt = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
            new ObserveTable(scope.Source, scope.Generation, LegacyMediaGeneration(), ReviewFrame: true, Mode: mode)), lifetime.Token);
        if (!receipt.Succeeded) { Say(receipt.Message); return; }
        if (lifetime.IsCancellationRequested) return;
        if (tableAnalysis.State.Jpeg is { } jpeg) SetTableImage(jpeg);
        ShowTable();
        var latest = tableState;
        if (sky is not null && latest is not null && (previousVersion != latest.Version || latest.Settled && latest.Confirmed)) await sky.UpdateTableAsync(latest);
    }
    void SetTableImage(byte[] jpeg)
    {
        if (ReferenceEquals(displayedTableFrame, jpeg)) return;
        var image = BitmapFactory.DecodeByteArray(jpeg, 0, jpeg.Length) ?? throw new IOException("Snapshot could not be decoded.");
        tableView.Image = image; tableBitmap?.Dispose(); tableBitmap = image; displayedTableFrame = jpeg;
    }
    void ShowTable()
    {
        var snapshot = tableAnalysis.State; var state = snapshot.Table; var vision = snapshot.Vision;
        if (state is null) return;
        tableView.Calibration = snapshot.Calibration; tableView.Balls = state.Balls; RefreshCandidates(); tableView.Invalidate();
        var balls = state.Balls;
        double age = (DateTimeOffset.UtcNow - state.CapturedAt).TotalSeconds;
        tableLabel.Text = $"{balls.Length} balls · {balls.Count(b => b.Kind == "red")} reds · {balls.Count(b => b.Kind == "yellow")} yellows · {balls.Count(b => b.Kind == "cue")} cue balls · {balls.Count(b => b.Kind == "black")} black\n{state.Group} · " + (age > 6 || age < -2 ? "frame is out of date" : state.Occluded ? "uncertain/obstructed" : !state.Settled ? "layout changing" : state.Confirmed ? "ready" : "check for a clear, still view") +
            $" · frame {age:0.0}s old · analysis {vision?.ElapsedMs ?? 0:0}ms" + (vision?.Warnings.Length > 0 ? "\n" + string.Join(" ", vision.Warnings) : "");
#if DEBUG
        // A bounded latest-result diagnostic; no JPEG, credentials or history.
        File.WriteAllText(System.IO.Path.Combine(FilesDir!.AbsolutePath, "table-analysis.json"), JsonSerializer.Serialize(new { state, analysis = vision, candidates = CurrentShots(), selectedShotId }, JsonDefaults.Options));
#endif
    }
    Shot[] CurrentShots()
    {
        var snapshot = tableAnalysis.State; var camera = app.State.Camera;
        return !camera.CanControl || camera.Capture != CaptureState.Active || snapshot.Source != camera.Scope || snapshot.Table is null
            ? [] : ShotPlanner.Suggest(snapshot.Table, DateTimeOffset.UtcNow);
    }
    void RefreshCandidates()
    {
        var selected = overlayEnabled && app.State.Camera.CanControl ? carousel.Current(Android.OS.SystemClock.ElapsedRealtime(), DateTimeOffset.UtcNow) : null;
        if (selected is not null && (tableState?.Version != selected.TableVersion || tableState?.Group != group)) selected = null;
        selectedShotId = selected?.Id ?? ""; tableView.Shots = selected is null ? [] : [selected]; tableView.Invalidate();
    }
    void CorrectLayout()
    {
        if (calibration is null) { Say("Calibrate the table first."); return; }
        long expectedVersion = tableState?.Version ?? -1;
        var edit = new EditText(this) { Text = string.Join("\n", (tableState?.Balls ?? []).Select(b => $"{b.Kind} {b.Position.X.ToString("F3", CultureInfo.InvariantCulture)} {b.Position.Y.ToString("F3", CultureInfo.InvariantCulture)}")), Hint = "cue 1.5 0.5\nred 0.6 0.3\nyellow 1.0 0.7\nblack 0.4 0.5" };
        new AlertDialog.Builder(this)!.SetTitle("Correct the complete layout")!.SetMessage("Table coordinates: left→right 0–2, top→bottom 0–1. Include every ball. Corrections expire when the detected layout changes.")!.SetView(edit)!.SetPositiveButton("Use layout", (_, _) =>
        {
            try
            {
                var corrected = tableAnalysis.Correct(expectedVersion, edit.Text ?? "");
                ClearCandidatePresentation("Layout corrected. Request fresh choices."); ShowTable();
                if (sky is not null) _ = sky.UpdateTableAsync(corrected);
            }
            catch (Exception e) { Say(e.Message); }
        })!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
    }
    Task<object> ExecutePoolToolAsync(string name, JsonElement arguments)
    {
        var snapshot = tableAnalysis.State; var state = snapshot.Table; var vision = snapshot.Vision; var camera = app.State.Camera;
        if (state is null || !camera.CanControl || snapshot.Source != camera.Scope) return Task.FromResult<object>(new { error = "Table has not been calibrated and confirmed." });
        var shots = ShotPlanner.Suggest(state, DateTimeOffset.UtcNow);
        object output = name switch
        {
            "get_table_state" => new { state, analysis = vision is null ? null : new { vision.Warnings, vision.ElapsedMs, vision.Normalized }, ageSeconds = (DateTimeOffset.UtcNow - state.CapturedAt).TotalSeconds, rules = PoolGeometry.RulesVersion, source = PoolGeometry.RulesUrl },
            "suggest_shots" => new { shots, state.Version, rules = PoolGeometry.RulesVersion, source = PoolGeometry.RulesUrl, limitation = "Direct, bank and kick geometry with at most one rebound per ball. No spin, pace, positional prediction or complete refereeing. Scores rank difficulty; they are not pot probabilities. An empty list means uncertainty or no validated candidate." },
            "explain_shot" => (object?)shots.FirstOrDefault(s => s.Id == arguments.GetProperty("shot_id").GetString()) ?? new { error = "That candidate belongs to an old layout or is unavailable." },
            _ => new { error = "Unknown pool tool" }
        };
        return Task.FromResult(output);
    }
    sealed class CornerDialogCancel(TableView view) : Java.Lang.Object, Android.Content.IDialogInterfaceOnCancelListener
    { public void OnCancel(Android.Content.IDialogInterface? dialog) { view.Picks.Clear(); view.Invalidate(); } }
    sealed class TableView(Android.Content.Context context) : View(context)
    {
        public Bitmap? Image; public TableCalibration? Calibration; public Ball[] Balls = []; public Shot[] Shots = [];
        public bool Picking; public readonly List<Point> Picks = []; public Action<Point[]>? Calibrated;
        RectF Bounds() { if (Image is null) return new(0, 0, Width, Height); float scale = Math.Min((float)Width / Image.Width, (float)Height / Image.Height); float w = Image.Width * scale, h = Image.Height * scale; return new((Width - w) / 2, (Height - h) / 2, (Width + w) / 2, (Height + h) / 2); }
        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas); canvas.DrawColor(Color.Black); var bounds = Bounds();
            using var paint = new Paint { AntiAlias = true, StrokeWidth = 3 };
            if (Image is not null) canvas.DrawBitmap(Image, null, bounds, paint);
            paint.Color = Color.Cyan;
            foreach (var pick in Picks) canvas.DrawCircle(bounds.Left + (float)pick.X * bounds.Width(), bounds.Top + (float)pick.Y * bounds.Height(), 8, paint);
            if (Calibration is null) return;
            // Detection and calibration use this exact unrotated snapshot. The inverse
            // perspective mapping and the image's letterbox bounds also apply to overlays.
            Point Screen(Point table) { var image = Calibration.Unmap(table); return new(bounds.Left + image.X * bounds.Width(), bounds.Top + image.Y * bounds.Height()); }
            void Line(Point a, Point b) { canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y, paint); }
            using var outline = new Android.Graphics.Path();
            var first = Calibration.Corners[0]; outline.MoveTo(bounds.Left + (float)first.X * bounds.Width(), bounds.Top + (float)first.Y * bounds.Height());
            foreach (var corner in Calibration.Corners.Skip(1)) outline.LineTo(bounds.Left + (float)corner.X * bounds.Width(), bounds.Top + (float)corner.Y * bounds.Height());
            outline.Close(); paint.SetStyle(Paint.Style.Stroke); paint.Color = Color.Cyan; canvas.DrawPath(outline, paint);
            foreach (var ball in Balls)
            {
                var centre = Screen(ball.Position);
                var right = Screen(ball.Position + new Point(PoolGeometry.Radius, 0)); var bottom = Screen(ball.Position + new Point(0, PoolGeometry.Radius));
                float radius = (float)(((right - centre).Length + (bottom - centre).Length) / 2);
                paint.StrokeWidth = 5; paint.Color = ball.Kind == "black" ? Color.White : Color.Black; canvas.DrawCircle((float)centre.X, (float)centre.Y, radius, paint);
                paint.StrokeWidth = 3; paint.Color = ball.Kind switch { "red" => Color.Red, "yellow" => Color.Yellow, "cue" => Color.White, _ => Color.Black }; canvas.DrawCircle((float)centre.X, (float)centre.Y, radius, paint);
            }
            paint.StrokeWidth = 3;
            foreach (var shot in Shots.Take(1))
            {
                paint.Color = Color.White;
                for (int i = 1; i < shot.CuePath.Length; i++) Line(Screen(shot.CuePath[i - 1]), Screen(shot.CuePath[i]));
                paint.Color = Color.Cyan;
                for (int i = 1; i < shot.TargetPath.Length; i++) Line(Screen(shot.TargetPath[i - 1]), Screen(shot.TargetPath[i]));
                foreach (var bounce in shot.CuePath.Skip(1).SkipLast(1).Concat(shot.TargetPath.Skip(1).SkipLast(1))) { var p = Screen(bounce); canvas.DrawCircle((float)p.X, (float)p.Y, 6, paint); }
                var ghost = Screen(shot.GhostBall); canvas.DrawCircle((float)ghost.X, (float)ghost.Y, 7, paint);
            }
        }
        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (!Picking || e?.Action != MotionEventActions.Up) return true; var bounds = Bounds(); if (!bounds.Contains(e.GetX(), e.GetY())) return true;
            Picks.Add(new((e.GetX() - bounds.Left) / bounds.Width(), (e.GetY() - bounds.Top) / bounds.Height())); Invalidate();
            if (Picks.Count == 4) Calibrated?.Invoke(Picks.ToArray()); return true;
        }
    }
}
