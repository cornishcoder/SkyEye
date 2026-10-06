using System.Security.Cryptography;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Android.Content;
using Android.OS;
using Bridge=UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Companion;

public sealed class AndroidRecordedRenderer(Context context)
{
    public async Task<JsonElement> RenderAsync(string input,string output,RecordedRenderPlan plan,CancellationToken cancellation=default,bool watermark=true)
    {
        if(Path.GetFullPath(input)==Path.GetFullPath(output)||File.Exists(output))throw new IOException("Render output must be a new derivative file.");
        plan.Validate();
        await using(var file=File.OpenRead(input))if(Convert.ToHexString(await SHA256.HashDataAsync(file,cancellation))!=plan.Source.ContentSha256)throw new IOException("Render source checksum differs from geometry.");
        string part=output+"."+Guid.NewGuid().ToString("N")+".part.mp4";
        var finished=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Bridge.RecordedVideoRenderer? renderer=null;RenderListener? listener=null;bool success=false;
        try
        {
            await MainAsync(()=>{cancellation.ThrowIfCancellationRequested();listener=new(finished);renderer=new();renderer.Start(context,input,part,JsonSerializer.Serialize(plan,JsonDefaults.Options),watermark,listener);});
            string report=await finished.Task.WaitAsync(TimeSpan.FromMinutes(20),cancellation);
            cancellation.ThrowIfCancellationRequested();
            using var parsed=JsonDocument.Parse(report);
            File.Move(part,output);success=true;return parsed.RootElement.Clone();
        }
        finally
        {
            await MainAsync(()=>{if(!success)renderer?.Cancel();renderer?.Dispose();listener?.Dispose();});
            if(File.Exists(part))File.Delete(part);
        }
    }
    static Task MainAsync(Action work)
    {
        var result=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run(){try{work();result.TrySetResult();}catch(Exception e){result.TrySetException(e);}}
        if(Looper.MyLooper()==Looper.MainLooper)Run();else{using var handler=new Handler(Looper.MainLooper!);if(!handler.Post(Run))result.TrySetException(new IOException("Render dispatcher unavailable."));}
        return result.Task;
    }
    sealed class RenderListener(TaskCompletionSource<string> result):Java.Lang.Object,Bridge.RecordedVideoRenderer.IListener
    {public void OnComplete(string? report)=>result.TrySetResult(report??"{}");public void OnError(string? error)=>result.TrySetException(new IOException(error??"Recorded render failed."));}
}
