namespace SkyEye.Vision.Device;

[global::Android.App.Instrumentation(Name="uk.co.skyeye.visionchecks.AnnotationClockInstrumentation",TargetPackage="uk.co.skyeye.visionchecks",FunctionalTest=true)]
[global::Android.Runtime.Preserve(AllMembers=true)]
public sealed class AnnotationClockInstrumentation : global::Android.App.Instrumentation
{
    public AnnotationClockInstrumentation() { }
    public AnnotationClockInstrumentation(IntPtr handle,global::Android.Runtime.JniHandleOwnership ownership):base(handle,ownership) { }
    public override void OnCreate(global::Android.OS.Bundle? args) { base.OnCreate(args); Start(); }
    public override void OnStart()
    {
        base.OnStart();
        var result=UK.CO.Skyeye.Companion.Nativebridge.AnnotationClockProof.Run()!;
        File.WriteAllText(Path.Combine(TargetContext!.GetExternalFilesDir(null)!.AbsolutePath,"annotation-clock-checks.json"),result);
        using var bundle=new global::Android.OS.Bundle();bundle.PutString("result",result);Finish(global::Android.App.Result.Ok,bundle);
    }
}
