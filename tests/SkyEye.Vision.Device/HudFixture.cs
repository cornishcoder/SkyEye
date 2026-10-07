using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;

namespace SkyEye.Vision.Device;

static class HudFixture
{
    public static (TableCalibration Calibration, Shot Shot) From(global::Android.Content.Context context)
    {
        using var input=context.Assets!.Open("pixel-table.json");using var labels=JsonDocument.Parse(input);
        var calibration=new TableCalibration(labels.RootElement.GetProperty("calibration").EnumerateArray()
            .Select(p=>new Point(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble())).ToArray());
        var balls=labels.RootElement.GetProperty("expected").EnumerateArray().Select((b,i)=>
        {
            var p=b.GetProperty("image");return new Ball("label-"+i,b.GetProperty("kind").GetString()!,
                calibration.Map(new(p.GetProperty("x").GetDouble(),p.GetProperty("y").GetDouble())),1);
        }).ToArray();
        var table=new TableState(7,DateTimeOffset.UtcNow,balls,"open",true,false,true,"original-human-fixture");
        var shot=ShotPlanner.Suggest(table,DateTimeOffset.UtcNow).FirstOrDefault(s=>GeometricCueProjection.From(s).Direction is not null)
            ?? throw new InvalidOperationException("No supported geometric cut in labelled source fixture.");
        return(calibration,shot);
    }
}
