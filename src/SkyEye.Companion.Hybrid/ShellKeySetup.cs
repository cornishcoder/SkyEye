using Android.App;
using Android.Text;
using Android.Views;
using Android.Widget;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    AlertDialog? keyDialog;
    public bool PersonalKeyConfigured { get; private set; }
    public string KeyStatus { get; private set; } = "No personal key saved on this phone.";

    void RefreshKeyStatus()
    {
        try
        {
            PersonalKeyConfigured = !string.IsNullOrWhiteSpace(SecretStore.Get(Android.App.Application.Context, "openai"));
            KeyStatus = PersonalKeyConfigured ? "Personal key saved on this phone." : "No personal key saved on this phone.";
        }
        catch { PersonalKeyConfigured = false; KeyStatus = "Saved key unavailable. Enter it again on this phone."; }
    }
    public Task SetupKeyAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || keyDialog is not null) return;
        var token = foreground?.Token ?? new CancellationToken(true);
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity required.");
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var edit = new EditText(activity)
        {
            Hint = "Personal OpenAI API key",
            InputType = InputTypes.ClassText | InputTypes.TextVariationPassword | InputTypes.TextFlagNoSuggestions,
            ImportantForAutofill = ImportantForAutofill.NoExcludeDescendants
        };
        edit.SetSingleLine(true);
        // Android's single-line helper replaces the transformation method.
        // Restore password masking after it, including accessibility semantics.
        edit.TransformationMethod = Android.Text.Method.PasswordTransformationMethod.Instance;
        edit.ImeOptions = (Android.Views.InputMethods.ImeAction)((int)Android.Views.InputMethods.ImeAction.Done | (int)Android.Views.InputMethods.ImeFlags.NoPersonalizedLearning);
        using var builder = new AlertDialog.Builder(activity);
        var dialog = builder.SetTitle("Sky voice setup")!
            .SetMessage("Stored encrypted on this phone. Your camera and guests never receive it.")!
            .SetView(edit)!
            .SetPositiveButton("Save", (_, _) =>
            {
                if (!active || disposed || token.IsCancellationRequested) return;
                try
                {
                    var value = edit.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(value)) { ShowError("Enter your personal API key before saving."); return; }
                    SecretStore.Set(activity.ApplicationContext!, "openai", value);
                    RefreshKeyStatus(); Message = "Personal key saved on this phone."; DismissNotice(); Publish();
                }
                catch { ShowError("The key could not be saved. Try entering it again on this phone."); }
                finally { edit.Text = ""; }
            })!
            .SetNegativeButton("Cancel", (_, _) => edit.Text = "")!.Create()!;
        keyDialog = dialog;
        dialog.DismissEvent += (_, _) =>
        {
            edit.Text = "";
            if (keyDialog == dialog) keyDialog = null;
            closed.TrySetResult();
        };
        try
        {
            dialog.Window?.AddFlags(WindowManagerFlags.Secure);
            dialog.Show(); await closed.Task;
        }
        finally { edit.Text = ""; if (keyDialog == dialog) keyDialog = null; dialog.Dispose(); }
    });
    void CloseKeyEditor() => keyDialog?.Dismiss();
    public void RemoveKey()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(RemoveKey); return; }
        if (!active || disposed) return;
        try { SecretStore.Remove(Android.App.Application.Context, "openai"); RefreshKeyStatus(); Message = "Personal key removed from this phone."; Publish(); }
        catch { ShowError("The saved key could not be removed. Try again."); }
    }
}
