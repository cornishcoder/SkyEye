using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using SkyEye.Core;
using System.Globalization;
using System.Text.Json;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    TableCalibration? calibration;
    string calibrationId = "", group = "open", tableTimeline = "";
    TableState? tableState;
    Bitmap? tableBitmap;
    TextView tableLabel = null!;
    long version;
    bool voiceMuted;
    Ball[]? lastDetections;
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
        var row = Row(root); Add(row, "Calibrate", () => _ = ActionAsync(CalibrateAsync)); Add(row, "Confirm balls", () => _ = ActionAsync(async () => { await UpdateTableAsync(); if (tableState is null || !tableState.Settled || tableState.Occluded) throw new InvalidOperationException("Wait for a settled, unobstructed view and check the detected balls."); tableState = tableState with { Confirmed = true }; ShowTable(); if (maeve is not null) await maeve.UpdateTableAsync(tableState); })); Add(row, "Correct layout", CorrectLayout);
        var groups = new Spinner(this); groups.Adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerDropDownItem, ["Open table", "I'm reds", "I'm yellows", "I'm on the black"]);
        groups.ItemSelected += (_, e) => { group = new[] { "open", "red", "yellow", "black" }[e.Position]; if (tableState is not null) { tableState = tableState with { Group = group, Version = ++version }; ShowTable(); } }; root.AddView(groups);
        Add(root, "Show candidate shots", () => { if (tableState is null) { Say("Calibrate and confirm the current layout first."); return; } var shots = PoolGeometry.Suggest(tableState, DateTimeOffset.UtcNow); tableView.Shots = shots; tableView.Invalidate(); Say(shots.Length == 0 ? "No confident direct pot in this fresh layout. Check positions and group, or discuss a safety." : shots[0].Explanation); });
    }
    async Task CalibrateAsync()
    {
        var snapshot = await client.SnapshotAsync(lifetime.Token);
        tableBitmap?.Dispose(); tableBitmap = BitmapFactory.DecodeByteArray(snapshot.Jpeg, 0, snapshot.Jpeg.Length) ?? throw new IOException("Snapshot could not be decoded.");
        tableView.Image = tableBitmap; tableView.Picks.Clear(); tableView.Picking = true; tableView.Calibration = null; tableView.Balls = []; tableView.Shots = []; tableView.Invalidate();
        Say("Tap playable-surface corners: top left, top right, bottom right, bottom left. Long table edge runs left to right.");
        tableView.Calibrated = corners =>
        {
            try { calibration = new TableCalibration(corners); calibrationId = Guid.NewGuid().ToString("N"); tableState = null; lastDetections = null; GetSharedPreferences("skyeye", Android.Content.FileCreationMode.Private)!.Edit()!.PutString(CalibrationKey(), JsonSerializer.Serialize(calibration.Corners, JsonDefaults.Options))!.Apply(); tableView.Calibration = calibration; tableView.Picking = false; _ = ActionAsync(UpdateTableAsync); }
            catch (Exception e) { tableView.Picks.Clear(); Say(e.Message); }
        };
    }
    async Task UpdateTableAsync()
    {
        if (calibration is null) return;
        var snapshot = await client.SnapshotAsync(lifetime.Token);
        using var full = BitmapFactory.DecodeByteArray(snapshot.Jpeg, 0, snapshot.Jpeg.Length) ?? throw new IOException("Snapshot unavailable");
        using var small = Bitmap.CreateScaledBitmap(full, 640, (int)Math.Round(640.0 * full.Height / full.Width), true)!;
        int[] pixels = new int[small.Width * small.Height]; small.GetPixels(pixels, 0, small.Width, 0, 0, small.Width, small.Height);
        var detected = await Task.Run(() => BallDetector.Detect(pixels, small.Width, small.Height, calibration), lifetime.Token);
        bool same = tableState is not null && lastDetections is not null && snapshot.Timeline == tableTimeline && SameLayout(lastDetections, detected.Balls);
        bool settled = same && (tableState!.Settled || (snapshot.CapturedAt - tableState.CapturedAt).TotalSeconds >= 1);
        bool confirmed = same && tableState!.Confirmed;
        bool changed = !same;
        var balls = same && confirmed ? tableState!.Balls : detected.Balls;
        long stateVersion = same ? tableState!.Version : ++version;
        tableState = new(stateVersion, snapshot.CapturedAt, balls, group, settled, detected.Occluded, confirmed, calibrationId); tableTimeline = snapshot.Timeline; lastDetections = detected.Balls;
        tableBitmap?.Dispose(); tableBitmap = BitmapFactory.DecodeByteArray(snapshot.Jpeg, 0, snapshot.Jpeg.Length); tableView.Image = tableBitmap;
        ShowTable();
        if (maeve is not null && (changed || settled && confirmed)) await maeve.UpdateTableAsync(tableState);
    }
    static bool SameLayout(Ball[] previous, Ball[] next)
    {
        if (previous.Length != next.Length) return false; var remaining = next.ToList();
        foreach (var ball in previous) { var match = remaining.Where(b => b.Kind == ball.Kind).OrderBy(b => (b.Position - ball.Position).Length).FirstOrDefault(); if (match is null || (match.Position - ball.Position).Length > .008) return false; remaining.Remove(match); }
        return true;
    }
    void ShowTable()
    {
        if (tableState is null) return;
        tableView.Calibration = calibration; tableView.Balls = tableState.Balls; tableView.Shots = []; tableView.Invalidate();
        tableLabel.Text = $"{tableState.Balls.Length} balls · {group} · " + (tableState.Occluded ? "uncertain/obstructed" : !tableState.Settled ? "layout changing" : tableState.Confirmed ? "confirmed" : "check and confirm detections");
    }
    void CorrectLayout()
    {
        if (calibration is null) { Say("Calibrate the table first."); return; }
        var edit = new EditText(this) { Text = string.Join("\n", (tableState?.Balls ?? []).Select(b => $"{b.Kind} {b.Position.X.ToString("F3", CultureInfo.InvariantCulture)} {b.Position.Y.ToString("F3", CultureInfo.InvariantCulture)}")), Hint = "cue 1.5 0.5\nred 0.6 0.3\nyellow 1.0 0.7\nblack 0.4 0.5" };
        new AlertDialog.Builder(this)!.SetTitle("Correct the complete layout")!.SetMessage("Table coordinates: left→right 0–2, top→bottom 0–1. Include every ball. Corrections expire when the detected layout changes.")!.SetView(edit)!.SetPositiveButton("Use layout", (_, _) =>
        {
            try
            {
                var balls = (edit.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select((line, i) => { var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (fields.Length != 3 || fields[0] is not ("cue" or "red" or "yellow" or "black")) throw new ArgumentException("Each line needs a ball colour and two coordinates."); double x = double.Parse(fields[1], CultureInfo.InvariantCulture), y = double.Parse(fields[2], CultureInfo.InvariantCulture); if (!double.IsFinite(x) || !double.IsFinite(y) || x is < 0 or > 2 || y is < 0 or > 1) throw new ArgumentException("Coordinates must be within the table."); return new Ball("manual-" + i, fields[0], new(x, y), 1); }).ToArray();
                if (balls.Count(b => b.Kind == "cue") != 1 || balls.Length > 16) throw new ArgumentException("Include exactly one cue ball and no more than 16 balls.");
                tableState = new(++version, tableState?.CapturedAt ?? DateTimeOffset.MinValue, balls, group, true, false, true, calibrationId); ShowTable();
            }
            catch (Exception e) { Say(e.Message); }
        })!.SetNegativeButton("Cancel", (_, _) => { })!.Show();
    }
    Task<object> ExecutePoolToolAsync(string name, JsonElement arguments)
    {
        var state = tableState;
        if (state is null) return Task.FromResult<object>(new { error = "Table has not been calibrated and confirmed." });
        var shots = PoolGeometry.Suggest(state, DateTimeOffset.UtcNow);
        object output = name switch
        {
            "get_table_state" => new { state, ageSeconds = (DateTimeOffset.UtcNow - state.CapturedAt).TotalSeconds, rules = PoolGeometry.RulesVersion, source = PoolGeometry.RulesUrl },
            "suggest_shots" => new { shots, state.Version, rules = PoolGeometry.RulesVersion, source = PoolGeometry.RulesUrl, limitation = "Direct geometric pots only; no spin, pace, positional prediction or complete refereeing. An empty list means uncertainty or no clear direct pot." },
            "explain_shot" => (object?)shots.FirstOrDefault(s => s.Id == arguments.GetProperty("shot_id").GetString()) ?? new { error = "That candidate belongs to an old layout or is unavailable." },
            _ => new { error = "Unknown pool tool" }
        };
        return Task.FromResult(output);
    }
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
            // Draw in calibrated table space beside the source image to keep coordinates inspectable.
            float scale = Math.Min(Width / 2f, Height); float ox = (Width - 2 * scale) / 2, oy = (Height - scale) / 2;
            paint.Color = Color.Argb(185, 8, 67, 83); canvas.DrawRect(ox, oy, ox + 2 * scale, oy + scale, paint);
            foreach (var ball in Balls) { paint.Color = ball.Kind switch { "red" => Color.Red, "yellow" => Color.Yellow, "cue" => Color.White, _ => Color.Black }; canvas.DrawCircle(ox + (float)ball.Position.X * scale, oy + (float)ball.Position.Y * scale, (float)PoolGeometry.Radius * scale, paint); }
            paint.Color = Color.Cyan;
            foreach (var shot in Shots.Take(1)) { canvas.DrawLine(ox + (float)shot.Target.X * scale, oy + (float)shot.Target.Y * scale, ox + (float)shot.Destination.X * scale, oy + (float)shot.Destination.Y * scale, paint); var cue = Balls.FirstOrDefault(b => b.Kind == "cue"); if (cue is not null) canvas.DrawLine(ox + (float)cue.Position.X * scale, oy + (float)cue.Position.Y * scale, ox + (float)shot.GhostBall.X * scale, oy + (float)shot.GhostBall.Y * scale, paint); }
        }
        public override bool OnTouchEvent(MotionEvent? e)
        {
            if (!Picking || e?.Action != MotionEventActions.Up) return true; var bounds = Bounds(); if (!bounds.Contains(e.GetX(), e.GetY())) return true;
            Picks.Add(new((e.GetX() - bounds.Left) / bounds.Width(), (e.GetY() - bounds.Top) / bounds.Height())); Invalidate();
            if (Picks.Count == 4) Calibrated?.Invoke(Picks.ToArray()); return true;
        }
    }
}
