using Android.App;
using Android.OS;
using Android.Widget;
using System.Text.Json;
using SkyEye.Core;
using Bridge=UK.CO.Skyeye.Companion.Nativebridge;

namespace SkyEye.Vision.Device;

[Activity(Name="uk.co.skyeye.visionchecks.RecordedRenderPreviewActivity",Label="Corrected replay check",Exported=true,Theme="@style/Theme.AppCompat.DayNight.NoActionBar")]
public sealed class RecordedRenderPreviewActivity:global::AndroidX.AppCompat.App.AppCompatActivity
{
    // The companion owns 8767; the isolated check must coexist with it.
    const int PreviewPort=8769;
    VideoView video=null!;TextView status=null!;Bridge.CastBridge cast=null!;CastListener listener=null!;LocalHttpServer? server;Handler handler=null!;
    readonly string access=Guid.NewGuid().ToString("N"),id=Guid.NewGuid().ToString("N"),request=Guid.NewGuid().ToString("N");
    protected override void OnCreate(Bundle? state)
    {
        base.OnCreate(state);string root=GetExternalFilesDir(null)!.AbsolutePath;
        var content=new LinearLayout(this){Orientation=Orientation.Vertical};content.SetPadding(24,180,24,100);
        status=new TextView(this){Text="Recorded corrected MP4 · prototype check",TextSize=16};content.AddView(status);
        video=new VideoView(this);content.AddView(video,new LinearLayout.LayoutParams(-1,0,1));
        void Button(string label,Action action){var button=new Button(this){Text=label};button.Click+=(_,_)=>action();content.AddView(button);}
        Button("Pause corrected clip",()=>video.Pause());Button("Resume corrected clip",()=>video.Start());Button("Seek corrected clip to start",()=>video.SeekTo(0));
        listener=new(this);cast=new(this,listener);content.AddView(cast.Button);
        Button("Play corrected clip on TV",()=>{var ip=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).First(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&!System.Net.IPAddress.IsLoopback(a)&&a.ToString().StartsWith("192.168."));
            using var meta=new global::Android.Media.MediaMetadataRetriever();meta.SetDataSource(Path.Combine(root,"rendered-landscape.mp4"));long duration=long.Parse(meta.ExtractMetadata(global::Android.Media.MetadataKey.Duration)!);
            cast.PlayClip($"http://{ip}:{PreviewPort}/rendered.mp4?access={access}",id,"Corrected recorded table · prototype",duration,request);});
        Button("Pause corrected TV clip",()=>cast.ControlClip(request,Guid.NewGuid().ToString("N"),"pause",0));
        Button("Resume corrected TV clip",()=>cast.ControlClip(request,Guid.NewGuid().ToString("N"),"resume",0));
        Button("Seek corrected TV clip to start",()=>cast.ControlClip(request,Guid.NewGuid().ToString("N"),"seek",0));
        SetContentView(content);
        handler=new(Looper.MainLooper!);
        server=new(PreviewPort,(r,ct)=>{
            if(r.Path=="/status"){var task=new TaskCompletionSource<HttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);handler.Post(()=>{try{using var metrics=JsonDocument.Parse(cast.Metrics()!);task.TrySetResult(HttpResponse.Json(new{phonePlaying=video.IsPlaying,phonePositionMs=video.CurrentPosition,cast=metrics.RootElement.Clone()}));}catch(Exception e){task.TrySetException(e);}});return task.Task;}
            return Task.FromResult(r.Path=="/rendered.mp4"&&r.Query.GetValueOrDefault("access")==access?new HttpResponse(200,"video/mp4",FilePath:Path.Combine(root,"rendered-landscape.mp4")):HttpResponse.Json(new{error="not found"},404));});server.Start();
        video.SetVideoPath(Path.Combine(root,"rendered-portrait.mp4"));video.Prepared+=(_,_)=>video.Start();video.Completion+=(_,_)=>{video.SeekTo(0);video.Start();};Poll();
    }
    void Poll(){if(IsFinishing)return;status.Text=$"Recorded corrected MP4 · phone {video.CurrentPosition/1000}s";handler.PostDelayed(Poll,1000);}
    protected override void OnDestroy(){video?.StopPlayback();cast?.Release();if(server is not null)_=server.DisposeAsync();base.OnDestroy();}
    sealed class CastListener(RecordedRenderPreviewActivity owner):Java.Lang.Object,Bridge.CastBridge.IListener
    {public void OnStatus(string? message)=>owner.RunOnUiThread(()=>owner.status.Text=message??"TV status changed");}
}
