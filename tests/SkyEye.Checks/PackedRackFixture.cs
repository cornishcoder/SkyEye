using SkyEye.Core;

/// <summary>Clear, still sixteen-ball centres from the saved v0.7 normal-verification
/// receipt. The detector reported no obstruction or consensus conflict, while six
/// measured pairs fail the independent shot planner spacing gate.</summary>
static class PackedRackFixture
{
    public static Ball[] Balls =>
    [
        new("burst-0", "cue", new(.43651248474121096, .498862530517578), .9),
        new("burst-1", "red", new(1.3971499999999997, .5013500000000001), .8866019417475728),
        new("burst-2", "yellow", new(1.4436499999999999, .5261), .8976923076923077),
        new("burst-3", "red", new(1.4454, .47335000000000005), .860532319391635),
        new("burst-4", "red", new(1.4900249999999997, .55535), .8935632183908045),
        new("burst-5", "yellow", new(1.4919000000000002, .4456), .8959622195985832),
        new("burst-6", "black", new(1.4944000000000002, .5006), .872005294506949),
        new("burst-7", "yellow", new(1.5361500000000001, .5812249999999999), .9),
        new("burst-8", "red", new(1.5384, .422975), .8924686192468618),
        new("burst-9", "red", new(1.5404000000000004, .5263500000000001), .8963431151241534),
        new("burst-10", "yellow", new(1.5416500000000004, .47135000000000016), .8877405405405405),
        new("burst-11", "red", new(1.5814499999999994, .44984999999999997), .8945762711864407),
        new("burst-12", "red", new(1.5830749999999998, .6068), .8939387755102041),
        new("burst-13", "yellow", new(1.5869000000000002, .55535), .9),
        new("burst-14", "yellow", new(1.5878999999999999, .5027250000000001), .9),
        new("burst-15", "yellow", new(1.5887333333333336, .39859999999999995), .896013986013986)
    ];
}
