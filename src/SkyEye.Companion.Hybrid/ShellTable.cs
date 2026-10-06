using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Vision.Android;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    TableAnalysisSession table = null!;
    readonly TableEditDraft edit = new();
    string? restoredCamera;
    byte[]? frozenFrame;
    double editorLimit = double.PositiveInfinity;
    public TableEditDraft TableEdit => edit;
    public byte[]? FrozenFrame => frozenFrame;
    public bool EditingTable => edit.Mode != TableEditMode.None;
    public double EditorHeight => Math.Min(editorLimit, (edit.Mode == TableEditMode.Correction ? 360 : 260) * FontScale);
    public void SetEditorLimit(double height) { if (height > 0 && Math.Abs(editorLimit - height) > .5) { editorLimit = height; Changed?.Invoke(); } }
    public bool CanAnalyzeTable => app.State.Camera is { CanControl: true, Capture: CaptureState.Active } && selection.Kind == ViewingSource.Live;
    public bool HasTableCalibration => table.State.Calibration is not null;
    public bool CanCorrectTable => CanAnalyzeTable && table.State is { Calibration: not null, Table: not null };
    public bool CanApplyTableEdit => CanAnalyzeTable && edit.Matches(table.State.Generation, table.State.Table?.Version)
        && (edit.Mode == TableEditMode.Correction || edit.Corners.Count == 4);
    public AdviceTargetGroup TargetGroup => app.State.Advice.TargetGroup;
    public Task ObserveTableAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    { if (active && foreground is not null && !EditingTable) await UpdateTableAsync(foreground.Token); });
    public string TableStatus { get; private set; } = "Calibrate your table to check its balls.";
    public string EditorHint => !CanApplyTableEdit && edit.Mode == TableEditMode.Correction ? "The layout changed. Cancel and check a fresh frame."
        : edit.Mode == TableEditMode.Correction ? edit.AddingBall ? "Tap the cloth to add the selected colour." : "Tap a ball to select it. Drag it to adjust its position."
        : edit.Corners.Count == 4 ? "Use these points only if they match the cloth boundary." : "Tap the cloth corners in order: top right, bottom right, bottom left, top left.";

    void InitializeTable() => table = new(new PlayerTableFrameSource(() => player, () => app.State.Camera, () => active && selection.Kind == ViewingSource.Live), new VisionAnalyzer(),
        new CameraCalibrationStore(Android.App.Application.Context, () => client.Address, GeometryKey));
    void BindTable(SkyEyeState state) => table.Bind(state.Camera, state.Advice.TargetGroup.ToString().ToLowerInvariant());
    void ObserveTable()
    {
        var snapshot = table.State;
        if (EditingTable && (snapshot.Source is null || snapshot.Generation != edit.Generation))
        { CloseTableEdit(invalidatePicking: false); Message = "Camera changed. Table edit cancelled."; chrome.SetAttention(true); }
        if (snapshot.CameraIdentity != restoredCamera && snapshot.CameraIdentity is not null)
        {
            restoredCamera = snapshot.CameraIdentity;
            try { table.RestoreCalibration(); }
            catch { ShowError("Saved calibration could not load. Calibrate the table again."); }
            snapshot = table.State;
        }
        var state = snapshot.Table;
        TableStatus = snapshot.Source is null ? "Watch an active camera as host to check the table."
            : snapshot.Calibration is null ? "Calibrate your table to check its balls."
            : state is null ? "Ready to observe when you ask."
            : $"{state.Balls.Count(b => b.Kind == "red")} reds · {state.Balls.Count(b => b.Kind == "yellow")} yellows · {state.Balls.Count(b => b.Kind == "cue")} cue · {state.Balls.Count(b => b.Kind == "black")} black. "
                + ((DateTimeOffset.UtcNow - state.CapturedAt).TotalSeconds is > 6 or < -2 ? "Frame out of date."
                : state.Occluded ? "Uncertain or obstructed. Check the detections."
                : !state.Settled ? "Wait for the table to settle." : state.Confirmed ? "Layout confirmed." : "Check every ball before confirming.");
    }
    async Task UpdateTableAsync(CancellationToken token, bool reviewFrame = false, bool includeChoices = false)
    {
        if (!CanAnalyzeTable) return;
        try
        {
            if (player is null || table.State.Source is not { } source) return;
            var receipt = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
                new ObserveTable(source, table.State.Generation, MediaGeneration(), reviewFrame, includeChoices)), token);
            if (!receipt.Succeeded) shotMessage = receipt.Message;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested && !disposed) await MainThread.InvokeOnMainThreadAsync(() => ShowError("Table analysis interrupted. Check the camera and try again.")); }
        if (!token.IsCancellationRequested && !disposed) await MainThread.InvokeOnMainThreadAsync(async () => { await ReconcileShotCatalogAsync(token); Publish(); });
    }
    public Task CalibrateTableAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!CanAnalyzeTable || foreground is null || EditingTable) return;
        var token = foreground.Token; DismissNotice(); suppressShotLoop = true; StopShotScenes(closeRelay:false); ClearSceneCheck();
        try
        {
            table.InvalidateCalibration(forgetStored: true);
            await ClearShotCatalogAsync(token); player?.SetOverlay("{}");
            var draft = await table.PrepareCalibrationAsync(token);
            if (token.IsCancellationRequested || disposed || draft.Generation != table.State.Generation) return;
            frozenFrame = draft.Jpeg; edit.BeginCalibration(draft.Generation, draft.Proposed);
            SheetOpen = false; chrome.SetEditing(true); Message = "Review the frozen table corners."; Publish();
        }
        finally { suppressShotLoop = EditingTable; Publish(); }
    });
    public Task CorrectTableAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!CanAnalyzeTable || foreground is null || EditingTable) return;
        var token = foreground.Token; await UpdateTableAsync(token, reviewFrame: true);
        var snapshot = table.State;
        if (token.IsCancellationRequested || disposed || snapshot is not { Calibration: { } calibration, Table: { } layout, Jpeg: { } jpeg }) return;
        DismissNotice(); suppressShotLoop = true; StopShotScenes(closeRelay:false); ClearSceneCheck();
        try
        {
            await ClearShotCatalogAsync(token);
            if (token.IsCancellationRequested || disposed || snapshot.Generation != table.State.Generation) return;
            player?.SetOverlay("{}"); frozenFrame = jpeg; edit.BeginCorrection(snapshot.Generation, layout, calibration);
            SheetOpen = false; chrome.SetEditing(true); Message = "Review every ball on this frozen frame."; Publish();
        }
        finally { suppressShotLoop = EditingTable; Publish(); }
    });
    public async Task ApplyTableEditAsync()
    {
        if (!MainThread.IsMainThread) { await MainThread.InvokeOnMainThreadAsync(ApplyTableEditAsync); return; }
        if (!EditingTable || foreground is null || !active) return;
        var token = foreground.Token;
        try
        {
            if (edit.Mode == TableEditMode.Correction) await UpdateTableAsync(token);
            if (token.IsCancellationRequested || !CanApplyTableEdit) { ShowError("This edit is no longer current. Cancel and review a fresh frame."); return; }
            if (edit.Mode == TableEditMode.Calibration) table.ApplyCalibration(edit.Generation, edit.Corners.ToArray());
            else table.Correct(edit.TableVersion, edit.LayoutText());
            player?.SetOverlay("{}"); CloseTableEdit(invalidatePicking: false);
            Message = "Table update applied. Checking the current layout."; DismissNotice(); Publish();
            await UpdateTableAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException e) { ShowError(e.Message); }
        catch (InvalidOperationException e) { ShowError(e.Message); }
        catch (Exception) { ShowError("The table update could not be saved. Review it and try again."); }
    }
    public void CancelTableEdit() { CloseTableEdit(invalidatePicking: true); Message = "Table edit cancelled."; Publish(); }
    void CloseTableEdit(bool invalidatePicking)
    {
        if (invalidatePicking && edit.Mode == TableEditMode.Calibration) table.Invalidate();
        edit.Clear(); frozenFrame = null; suppressShotLoop = false; chrome.SetEditing(SheetOpen);
    }
    public void EditTable(Action<TableEditDraft> change)
    {
        if (!active || !EditingTable) return;
        try { change(edit); Publish(); }
        catch (ArgumentException e) { ShowError(e.Message); }
    }
    public Task SetTableGroupAsync(long expectedVersion, AdviceTargetGroup group) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || EditingTable) return;
        var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, expectedVersion, new SetTargetGroup(group)), foreground.Token);
        if (!result.Succeeded) ShowError(result.Message); else Publish();
    });
    public Task ConfirmTableAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!CanAnalyzeTable || foreground is null || EditingTable) return;
        await UpdateTableAsync(foreground.Token);
        try { table.Confirm(table.State.Table?.Version ?? -1); Message = "Ball layout confirmed."; Publish(); }
        catch (InvalidOperationException e) { ShowError(e.Message); }
    });
}
