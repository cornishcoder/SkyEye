using System.Text.Json;
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
    public bool CanAnalyzeTable => sessionMode == TableSessionMode.Live
        && app.State.Camera is { CanControl: true, Capture: CaptureState.Active } && selection.Kind == ViewingSource.Live;
    public bool HasTableCalibration => table.State.Calibration is not null;
    public bool CanCorrectTable => CanAnalyzeTable && table.VisionDiagnostics.Active == false && table.State is { Calibration: not null, Table: not null };
    public bool CanApplyTableEdit => CanAnalyzeTable && edit.Matches(table.State.Generation, table.State.Table?.Version)
        && (edit.Mode == TableEditMode.Correction || edit.Corners.Count == 4);
    public AdviceTargetGroup TargetGroup => app.State.Advice.TargetGroup;
    public bool VisionDiagnosticsActive => table.VisionDiagnostics.Active;
    public bool CanOpenVisionDashboard => CanAnalyzeTable && HasTableCalibration && !EditingTable
        && (VisionDiagnosticsActive || CanStartVisionDiagnostics);
    public Guid VisionDiagnosticSessionId => table.VisionDiagnostics.SessionId;
    public string VisionDiagnosticRecordingSessionId => table.VisionDiagnostics.RecordingSessionId;
    public bool VisionDiagnosticLabelCapturePending => table.VisionDiagnostics.LabelCapturePending;
    public bool CanStartVisionDiagnostics => CanAnalyzeTable && HasTableCalibration && !EditingTable && !VisionDiagnosticsActive
        && !table.HasPendingWork && (visionDiagnosticSavedLabels == 0 || visionDiagnosticLabelSessionFinalized);
    public string VisionDiagnosticsStatus { get; private set; } = "Diagnostics are off.";
    public string VisionDiagnosticsDump
    {
        get
        {
            var snapshot = table.VisionDiagnostics;
            if (snapshot.LabelFrame is not null || snapshot.LabelCapturePending) return "Detector output is hidden while a frame is being labelled.";
            var recent = snapshot.Samples.TakeLast(8).ToArray();
            return JsonSerializer.Serialize(new
            {
                active = snapshot.Active,
                sessionId = snapshot.SessionId,
                snapshot.StartedAt,
                snapshot.Status,
                snapshot.TotalSamples,
                shownSamples = recent
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
    public Task ObserveTableAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    { if (active && foreground is not null && !EditingTable) await UpdateTableAsync(foreground.Token); });
    public string TableStatus { get; private set; } = "Calibrate your table to check its balls.";
    public string EditorHint => !CanApplyTableEdit && edit.Mode == TableEditMode.Correction ? "The layout changed. Cancel and check a fresh frame."
        : edit.Mode == TableEditMode.Correction ? edit.AddingBall ? "Tap the cloth to add the selected colour." : "Tap a ball to select it. Drag it to adjust its position."
        : edit.Corners.Count == 4 ? "Use these points only if they match the cloth boundary." : "Tap the cloth corners in order: top right, bottom right, bottom left, top left.";

    void InitializeTable()
    {
        table = new(new PlayerTableFrameSource(() => player, () => app.State.Camera,
            () => active && sessionMode == TableSessionMode.Live && selection.Kind == ViewingSource.Live,
            token => MainThread.InvokeOnMainThreadAsync(() => RefreshAsync(token))), new VisionAnalyzer(),
            new CameraCalibrationStore(Android.App.Application.Context, () => client.Address, GeometryKey));
        table.VisionDiagnosticsChanged += OnVisionDiagnosticsChanged;
    }
    void OnVisionDiagnosticsChanged()
    {
        if (disposed) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (disposed) return;
            var snapshot = table.VisionDiagnostics;
            SyncVisionDiagnosticLabelState(snapshot);
            VisionDiagnosticsStatus = snapshot.Status;
            if (!snapshot.Active && VisionDashboardActive)
            {
                VisionDashboardStatus = "Dashboard is available; sampling paused. " + snapshot.Status;
            }
            Changed?.Invoke();
        });
    }
    public Task StartVisionDiagnosticsAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (!CanStartVisionDiagnostics)
        {
            VisionDiagnosticsStatus = visionDiagnosticSavedLabels > 0 && !visionDiagnosticLabelSessionFinalized
                ? "Finish the current reviewed label session before starting another."
                : "Open the active calibrated live table and finish any table edit or analysis first.";
            Changed?.Invoke(); return;
        }
        if (table.StartVisionDiagnostics(out var message))
        {
            VisionDiagnosticsStatus = message;
            visionDashboardSamplingPaused = false;
            if (VisionDashboardActive) VisionDashboardStatus = "Dashboard is live; fresh sampling resumed.";
        }
        else VisionDiagnosticsStatus = message;
        Changed?.Invoke();
    });
    public async Task StopVisionDiagnosticsAsync()
    {
        bool hadDashboard = VisionDashboardActive;
        if (hadDashboard) await MainThread.InvokeOnMainThreadAsync(() => visionDashboardSamplingPaused = true);
        await table.StopVisionDiagnosticsAsync().ConfigureAwait(false);
        if (!disposed) await MainThread.InvokeOnMainThreadAsync(() =>
        {
            VisionDiagnosticsStatus = table.VisionDiagnostics.Status;
            if (hadDashboard) VisionDashboardStatus = "Dashboard is live; sampling paused.";
            Changed?.Invoke();
        });
    }
    void BindTable(SkyEyeState state) => table.Bind(state.Camera, state.Advice.TargetGroup.ToString().ToLowerInvariant());
    void ObserveTable()
    {
        if (sessionMode == TableSessionMode.Dark)
        { TableStatus = "Dark session · table image analysis and shot physics are unavailable."; return; }
        var snapshot = table.State;
        if (EditingTable && (snapshot.Source is null || snapshot.Generation != edit.Generation))
        { CloseTableEdit(invalidatePicking: false); Message = "Camera changed. Table edit cancelled."; chrome.SetAttention(true); }
        if (snapshot.CameraIdentity != restoredCamera)
        {
            restoredCamera = snapshot.CameraIdentity;
            try { if (restoredCamera is not null) table.RestoreCalibration(); }
            catch { ShowError("Saved calibration could not load. Calibrate the table again."); }
            snapshot = table.State;
        }
        var state = snapshot.Table;
        TableStatus = snapshot.Source is null ? "Watch an active camera as host to check the table."
            : snapshot.VerificationProgress is { } progress ? $"Checking table · {progress.IndependentFrames} fresh views."
            : snapshot.Calibration is null ? "Calibrate your table to check its balls."
            : state is null ? "Ready to observe when you ask."
            : $"{state.Balls.Count(b => b.Kind == "red")} reds · {state.Balls.Count(b => b.Kind == "yellow")} yellows · {state.Balls.Count(b => b.Kind == "cue")} cue balls · {state.Balls.Count(b => b.Kind == "black")} black. "
                + ((DateTimeOffset.UtcNow - state.CapturedAt).TotalSeconds is > 6 or < -2 ? "Frame out of date."
                : state.Occluded ? "Uncertain or obstructed. Check the detections."
                : !state.Settled ? "Wait for a fresh, clear still view." : state.Confirmed ? "Ready." : "Run a clear, still table check.");
    }
    async Task UpdateTableAsync(CancellationToken token, bool reviewFrame = false, bool includeChoices = false,
        ObservationMode mode = ObservationMode.Single, Guid? operationId = null, CommandOrigin origin = CommandOrigin.Touch)
    {
        if (!CanAnalyzeTable) return;
        try
        {
            if (player is null || table.State.Source is not { } source) return;
            var receipt = await app.DispatchAsync(new(operationId ?? Guid.NewGuid(), origin, app.State.Version,
                new ObserveTable(source, table.State.Generation, MediaGeneration(), reviewFrame, includeChoices, mode)), token);
            if (mode == ObservationMode.VerifyForAdvice) lastVerificationStatus = shotMessage = receipt.Message;
            if (!receipt.Succeeded)
            {
                shotMessage = receipt.Message;
                Android.Util.Log.Warn("SkyEyeShell", "Requested observation refused: " + receipt.Message);
                await MainThread.InvokeOnMainThreadAsync(() => ShowError(receipt.Message));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested && !disposed) await MainThread.InvokeOnMainThreadAsync(() => ShowError("Table analysis interrupted. Check the camera and try again.")); }
        if (!token.IsCancellationRequested && !disposed) await MainThread.InvokeOnMainThreadAsync(async () => { await ReconcileShotCatalogAsync(token); Publish(); });
    }
    async Task VerifyTableWithDiagnosticsPausedAsync(CancellationToken token, Guid? operationId = null,
        CommandOrigin origin = CommandOrigin.Touch)
    {
        var diagnostics = table.VisionDiagnostics;
        bool resumeDiagnostics = diagnostics.Active;
        if (resumeDiagnostics && (diagnostics.LabelFrame is not null || diagnostics.LabelCapturePending))
            throw new InvalidOperationException("Finish the labelled frame before checking table readiness.");
        if (resumeDiagnostics && VisionDashboardActive) visionDashboardSamplingPaused = true;
        try
        {
            // The verifier and sequential sampler share native vision resources.
            // Pause sampling only; keep the laptop dashboard server available.
            if (resumeDiagnostics) await table.StopVisionDiagnosticsAsync();
            await UpdateTableAsync(token, includeChoices: true, mode: ObservationMode.VerifyForAdvice,
                operationId: operationId, origin: origin);
        }
        finally
        {
            if (resumeDiagnostics)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!disposed && CanStartVisionDiagnostics)
                    {
                        if (table.StartVisionDiagnostics(out var status))
                        {
                            VisionDiagnosticsStatus = status;
                            if (VisionDashboardActive) VisionDashboardStatus = "Dashboard is live; fresh sampling resumed.";
                        }
                        else VisionDiagnosticsStatus = status;
                    }
                    visionDashboardSamplingPaused = false;
                    Changed?.Invoke();
                });
            }
        }
    }
    public Task CalibrateTableAsync() => PrepareTableCalibrationAsync(CancellationToken.None);
    async Task PrepareTableCalibrationAsync(CancellationToken cancellation) => await MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!CanAnalyzeTable || foreground is null || EditingTable) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, cancellation);
        var token = linked.Token; DismissNotice(); suppressShotLoop = true; StopShotScenes(closeRelay:false); ClearSceneCheck();
        try
        {
            table.InvalidateCalibration(forgetStored: true);
            await ClearShotCatalogAsync(token); player?.SetOverlay("{}");
            var draft = await table.PrepareCalibrationAsync(token);
            if (token.IsCancellationRequested || disposed || draft.Generation != table.State.Generation) return;
            frozenFrame = draft.Jpeg; edit.BeginCalibration(draft.Generation, draft.Proposed);
            chrome.SetEditing(true); Message = edit.Corners.Count == 4
                ? "Review the automatically detected table corners."
                : "Review the frozen frame and select the four table corners."; Publish();
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
            chrome.SetEditing(true); Message = "Review every ball on this frozen frame."; Publish();
        }
        finally { suppressShotLoop = EditingTable; Publish(); }
    });
    public Task ApplyTableEditAsync() => ApplyTableEditAsync(CommandOrigin.Touch, null, CancellationToken.None);
    async Task ApplyTableEditAsync(CommandOrigin origin, Guid? operationId, CancellationToken cancellation)
    {
        if (!MainThread.IsMainThread) { await MainThread.InvokeOnMainThreadAsync(() => ApplyTableEditAsync(origin, operationId, cancellation)); return; }
        if (!EditingTable || foreground is null || !active) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, cancellation);
        var token = linked.Token;
        try
        {
            var editMode = edit.Mode;
            if (editMode == TableEditMode.Correction) await UpdateTableAsync(token);
            if (token.IsCancellationRequested || !CanApplyTableEdit) { ShowError("This edit is no longer current. Cancel and review a fresh frame."); return; }
            if (editMode == TableEditMode.Calibration) table.ApplyCalibration(edit.Generation, edit.Corners.ToArray());
            else table.Correct(edit.TableVersion, edit.LayoutText());
            player?.SetOverlay("{}"); CloseTableEdit(invalidatePicking: false);
            Message = editMode == TableEditMode.Calibration
                ? "Corners applied. Checking several fresh table views."
                : "Table update applied. Checking the current layout.";
            DismissNotice(); Publish();
            if (editMode == TableEditMode.Calibration)
                await VerifyTableWithDiagnosticsPausedAsync(token, operationId, origin);
            else await UpdateTableAsync(token, mode: ObservationMode.Single, origin: origin);
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
}
