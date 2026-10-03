using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;
using System.Globalization;
using System.Text.Json;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    TableCalibration? calibration;
    string calibrationId = "", group = "open";
    TableState? tableState;
    Bitmap? tableBitmap;
    TextView tableLabel = null!;
    int tableGeneration;
    bool voiceMuted;
    readonly TableTracker tableTracker = new(15);
    readonly ITableAnalyzer tableAnalyzer = new VisionAnalyzer();
    Task? tableUpdate;
    VisionResult? lastVision;
    CancellationTokenSource? analysisCancellation;
    bool overlayEnabled;
    string selectedShotId = "";
    TableView tableView = null!;
    string CalibrationKey() => "calibration-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(client.Address)));
    void RestoreCalibration()
    {
        if (calibration is not null) return;
        try
        {
            var stored = GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!.GetString(CalibrationKey(), null);
            if (stored is null) return;
            var corners = JsonSerializer.Deserialize<Point[]>(stored, JsonDefaults.Options);
            if (corners is null) return;
            calibration = new TableCalibration(corners); calibrationId = Guid.NewGuid().ToString("N");
            tableView.Calibration = calibration; tableLabel.Text = "Saved calibration loaded. Recalibrate if the mount moved; confirm the current balls.";
        }
        catch { calibration = null; }
    }
    void BuildTableUi(LinearLayout root)
    {
        root.AddView(Label("Table awareness", 22));
        tableLabel = Label("Calibrate before using pool advice. Confirm or correct detections when the layout changes.", 13); root.AddView(tableLabel);
        tableView = new TableView(this); root.AddView(tableView, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, (int)(220 * Resources!.DisplayMetrics!.Density)));
        var row = Row(root); Add(row, "Calibrate", () => _ = ActionAsync(CalibrateAsync)); Add(row, "Confirm balls", () => _ = ActionAsync(async () => { await UpdateTableAsync(); tableState = tableTracker.Confirm(DateTimeOffset.UtcNow); ShowTable(); if (maeve is not null) await maeve.UpdateTableAsync(tableState); })); Add(row, "Correct layout", CorrectLayout);
        var groups = new Spinner(this); groups.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, ["Open table", "I'm reds", "I'm yellows", "I'm on the black"]);
        groups.ItemSelected += (_, e) => { group = new[] { "open", "red", "yellow", "black" }[e.Position]; if (tableState is not null) { tableState = tableTracker.SetGroup(group); ClearCandidatePresentation("Checking your selected group…"); overlayRefreshAt = 0; ShowTable(); if (maeve is not null) _ = maeve.UpdateTableAsync(tableState!); } }; root.AddView(groups);
    }
    async Task CalibrateAsync()
    {
        InvalidateTable("Selecting a new table calibration.");
        var snapshot = await client.SnapshotAsync(lifetime.Token);
        tableBitmap?.Dispose(); tableBitmap = BitmapFactory.DecodeByteArray(snapshot.Jpeg, 0, snapshot.Jpeg.Length) ?? throw new IOException("Snapshot could not be decoded.");
        tableView.Image = tableBitmap; tableView.Picks.Clear(); tableView.Picking = true; tableView.Calibration = null; tableView.Balls = []; tableView.Shots = []; tableView.Invalidate();
        Say("Tap the cloth corners where the cushion faces meet: top left, top right, bottom right, bottom left. Long edge runs left to right. This frame stays still while you calibrate.");
        void Accept(Point[] corners)
        {
            try { calibration = new TableCalibration(corners); calibrationId = Guid.NewGuid().ToString("N"); tableTracker.Reset(); tableState = null; GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!.Edit()!.PutString(CalibrationKey(), JsonSerializer.Serialize(calibration.Corners, JsonDefaults.Options))!.Apply(); tableView.Calibration = calibration; tableView.Picking = false; _ = ActionAsync(UpdateTableAsync); }
            catch (Exception e) { tableView.Picks.Clear(); Say(e.Message); }
        }
        tableView.Calibrated = Accept;
        int generation = tableGeneration;
        var proposed = await Task.Run(() => tableAnalyzer.FindCorners(snapshot.Jpeg), lifetime.Token);
        if (proposed is null || generation != tableGeneration || !tableView.Picking) return;
        tableView.Picks.AddRange(proposed); tableView.Invalidate();
        new AlertDialog.Builder(this)!.SetTitle("Check the proposed table corners")!.SetMessage("Use these four cyan points only if they match the cloth boundary. Otherwise select the corners yourself.")!
            .SetPositiveButton("Use corners", (_, _) => { if (generation == tableGeneration && tableView.Picking) Accept(proposed); })!
            .SetNegativeButton("Select manually", (_, _) => { tableView.Picks.Clear(); tableView.Invalidate(); })!
            .SetOnCancelListener(new CornerDialogCancel(tableView))!.Show();
    }
    void InvalidateTable(string reason)
    {
        ++tableGeneration; analysisCancellation?.Cancel(); tableTracker.Reset(); tableState = null; lastVision = null;
        selectedShotId = ""; tableView.Balls = []; tableView.Shots = []; tableView.Invalidate(); ClearCandidatePresentation(reason); tableLabel.Text = reason;
    }
    Task UpdateTableAsync()
    {
        // Reuse the in-flight operation; never build a queue of camera frames.
        if (tableUpdate is { IsCompleted: false }) return tableUpdate;
        return tableUpdate = AnalyzeTableAsync();
    }
    async Task AnalyzeTableAsync()
    {
        var currentCalibration = calibration; int generation = tableGeneration;
        if (currentCalibration is null || tableView.Picking) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); analysisCancellation = cancellation;
        try
        {
        var snapshot = await client.SnapshotAsync(cancellation.Token);
        var detected = await Task.Run(() => tableAnalyzer.Analyze(snapshot.Jpeg, currentCalibration, cancellation.Token), cancellation.Token);
        if (generation != tableGeneration || tableView.Picking || currentCalibration != calibration) return;
        long previousVersion = tableState?.Version ?? -1;
        tableState = tableTracker.Observe(calibrationId, snapshot.Timeline, snapshot.CapturedAt, detected.Balls, group, detected.Uncertain, detected.Obstructed); lastVision = detected;
        tableBitmap?.Dispose(); tableBitmap = BitmapFactory.DecodeByteArray(snapshot.Jpeg, 0, snapshot.Jpeg.Length); tableView.Image = tableBitmap;
        ShowTable();
        if (maeve is not null && (previousVersion != tableState.Version || tableState.Settled && tableState.Confirmed)) await maeve.UpdateTableAsync(tableState);
        }
        finally { if (analysisCancellation == cancellation) analysisCancellation = null; }
    }
    void ShowTable()
    {
        if (tableState is null) return;
        tableView.Calibration = calibration; tableView.Balls = tableState.Balls; RefreshCandidates(); tableView.Invalidate();
        var balls = tableState.Balls;
        double age = (DateTimeOffset.UtcNow - tableState.CapturedAt).TotalSeconds;
        tableLabel.Text = $"{balls.Length} balls · {balls.Count(b => b.Kind == "red")} reds · {balls.Count(b => b.Kind == "yellow")} yellows · {balls.Count(b => b.Kind == "cue")} cue · {balls.Count(b => b.Kind == "black")} black\n{group} · " + (age > 6 || age < -2 ? "frame is out of date" : tableState.Occluded ? "uncertain/obstructed" : !tableState.Settled ? "layout changing" : tableState.Confirmed ? "confirmed" : "check and confirm detections") +
            $" · frame {age:0.0}s old · analysis {lastVision?.ElapsedMs ?? 0:0}ms" + (lastVision?.Warnings.Length > 0 ? "\n" + string.Join(" ", lastVision.Warnings) : "");
#if DEBUG
        // A bounded latest-result diagnostic for device verification; no JPEG, credentials or history.
        File.WriteAllText(System.IO.Path.Combine(FilesDir!.AbsolutePath, "table-analysis.json"), JsonSerializer.Serialize(new { state = tableState, analysis = lastVision, candidates = CurrentShots(), selectedShotId }, JsonDefaults.Options));
#endif
    }
    Shot[] CurrentShots() => tableState is null ? [] : ShotPlanner.Suggest(tableState, DateTimeOffset.UtcNow);
    void RefreshCandidates()
    {
        var selected = overlayEnabled ? carousel.Current(Android.OS.SystemClock.ElapsedRealtime(), DateTimeOffset.UtcNow) : null;
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
                if (tableState?.Version != expectedVersion) throw new InvalidOperationException("The layout changed while you were editing. Check the current frame and try again.");
                var balls = (edit.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select((line, i) => { var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (fields.Length != 3 || fields[0] is not ("cue" or "red" or "yellow" or "black")) throw new ArgumentException("Each line needs a ball colour and two coordinates."); double x = double.Parse(fields[1], CultureInfo.InvariantCulture), y = double.Parse(fields[2], CultureInfo.InvariantCulture); if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 2 || y is < 0 or > 1) throw new ArgumentException("Coordinates must be within the table."); return new Ball("manual-" + i, fields[0], new(x, y), 1); }).ToArray();
                if (balls.Count(b => b.Kind == "cue") != 1 || balls.Count(b => b.Kind == "black") > 1 || balls.Count(b => b.Kind == "red") > 7 || balls.Count(b => b.Kind == "yellow") > 7 || balls.Length > 16) throw new ArgumentException("Include one cue ball, at most one black, seven reds and seven yellows.");
                tableState = tableTracker.Correct(balls, DateTimeOffset.UtcNow); ShowTable(); if (maeve is not null) _ = maeve.UpdateTableAsync(tableState);
            }
            catch (Exception e) { Say(e.Message); }
        })!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
    }
    Task<object> ExecutePoolToolAsync(string name, JsonElement arguments)
    {
        var state = tableState;
        if (state is null) return Task.FromResult<object>(new { error = "Table has not been calibrated and confirmed." });
        var shots = ShotPlanner.Suggest(state, DateTimeOffset.UtcNow);
        object output = name switch
        {
            "get_table_state" => new { state, analysis = lastVision is null ? null : new { lastVision.Warnings, lastVision.ElapsedMs, lastVision.Normalized }, ageSeconds = (DateTimeOffset.UtcNow - state.CapturedAt).TotalSeconds, rules = PoolGeometry.RulesVersion, source = PoolGeometry.RulesUrl },
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
