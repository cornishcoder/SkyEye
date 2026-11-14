// Re-run the settled-change comparer on a saved device raster pair (the reference and current JPEG sidecars
// a Live Detect comparison writes to live-detect/settled-change/) and print every region.
//
//   settled-change-rasters --before REF.jpg --after CUR.jpg [--shadow-free off]
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;

static class SettledChangeRastersCommand
{
    public static int Run(Dictionary<string, string> o)
    {
        var settings = new SettledChangeSettings
        {
            ShadowFreeCentre = !(o.TryGetValue("shadow-free", out var sf) && sf == "off")
        };
        byte[] before = Decode(o["before"], settings), after = Decode(o["after"], settings);
        var result = new SettledChangeComparer(settings).Compare(before, after);
        Console.WriteLine($"shadowFreeCentre {settings.ShadowFreeCentre} regions {result.Regions.Count} gates [{string.Join(",", result.GateReasons)}]");
        foreach (var r in result.Regions)
            Console.WriteLine($"  ({r.TableX:0.000},{r.TableY:0.000}) raster ({r.RasterX:0.0},{r.RasterY:0.0}) {r.Zone} area {r.AreaBalls:0.00} ball {r.BallSized} dir {r.Direction} dark {r.DarkBefore:0.00}->{r.DarkAfter:0.00} white {r.WhiteBefore:0.00}->{r.WhiteAfter:0.00}");
        return 0;
    }

    static byte[] Decode(string path, SettledChangeSettings s)
    {
        using var buffer = new Org.Opencv.Core.MatOfByte(File.ReadAllBytes(path));
        using var image = Org.Opencv.Imgcodecs.Imgcodecs.Imdecode(buffer, Org.Opencv.Imgcodecs.Imgcodecs.ImreadColor);
        if (image.Cols() != s.RasterWidth || image.Rows() != s.RasterHeight)
            throw new InvalidDataException($"{path}: {image.Cols()}x{image.Rows()}, expected {s.RasterWidth}x{s.RasterHeight}");
        var data = new byte[s.RasterWidth * s.RasterHeight * 3];
        image.Get(0, 0, data);
        return data;
    }
}
