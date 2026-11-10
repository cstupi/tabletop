using System.Diagnostics;
using Tabletop.Client.Core;
using Tabletop.Geometry;
using Tabletop.Protocol;
using Xunit.Abstractions;

namespace Tabletop.Client.Tests;

/// <summary>
/// A 16" Rush (Fast) of five cavalry on 60 × 35 mm ovals, 4.43" tall, from 4.8" off the table's edge, on a 72 × 48 table with eighteen terrain
/// pieces and fifty other models, a squad of them standing on a 3" steep hill: the table and Ask of a game where no reach area showed for the rush.
/// The area is drawn when the thread pool has built it (MoveRegions.BandsAsync), so it has to build in seconds.
/// </summary>
public class MoveRegionsCavalryRushTests(ITestOutputHelper output)
{
    static readonly TableSize Table = new(72, 48);
    static readonly Base Oval = Base.Oval(2.362204724409449, 1.3779527559055118);
    const double CavalryHeight = 4.429133858267717;

    static WorldVolume Rect(double x, double y, double yaw, double size, double top, VolumeKind kind, TerrainDeclarations d) =>
        new(Guid.CreateVersion7(), new Volume(kind, Footprint.Rect(size, size), new Point2(0, 0), new Yaw(0), 0, top, d, null),
            new TerrainTransform(new Point3(x, y, 0), new Yaw(yaw), 1));

    static WorldVolume Round(double x, double y, double yaw, double radius, double top, VolumeKind kind, TerrainDeclarations d) =>
        new(Guid.CreateVersion7(), new Volume(kind, Footprint.Circle(radius), new Point2(0, 0), new Yaw(0), 0, top, d, null),
            new TerrainTransform(new Point3(x, y, 0), new Yaw(yaw), 1));

    const TerrainDeclarations Difficult = TerrainDeclarations.Difficult, Steep = TerrainDeclarations.Steep;

    static readonly WorldVolume Building = Rect(22.49, 24.00, 270, 6, 4, VolumeKind.Solid, TerrainDeclarations.Impassable);
    static readonly WorldVolume[] DifficultGround =
    [
        Rect(41.07, 12.12, 240, 6, 4, VolumeKind.Area, Difficult), Rect(34.75, 40.62, 60, 6, 4, VolumeKind.Area, Difficult),
        Round(17.24, 32.27, 270, 3, 6, VolumeKind.Area, Difficult), Round(50.26, 23.78, 90, 3, 6, VolumeKind.Area, Difficult),
        Round(6.44, 37.24, 0, 3, 6, VolumeKind.Area, Difficult), Round(58.65, 8.23, 180, 3, 6, VolumeKind.Area, Difficult),
        Round(35.82, 25.34, 90, 3, 6, VolumeKind.Area, Difficult),
        Rect(62.07, 28.78, 15, 4, 2, VolumeKind.Solid, Difficult), Rect(9.93, 19.22, 195, 4, 2, VolumeKind.Solid, Difficult),
    ];
    static readonly WorldVolume[] Hills =
    [
        Round(6.38, 9.53, 45, 3, 2, VolumeKind.Solid, Steep), Round(65.62, 38.47, 225, 3, 2, VolumeKind.Solid, Steep),
        Round(7.70, 27.80, 90, 3, 2, VolumeKind.Solid, Steep), Round(64.30, 20.20, 270, 3, 2, VolumeKind.Solid, Steep),
        Round(28.74, 15.62, 90, 3, 3, VolumeKind.Solid, Steep), Round(44.90, 30.80, 270, 3, 3, VolumeKind.Solid, Steep),
    ];
    static readonly WorldVolume[] Lairs = [Round(54.78, 40.65, 75, 2, 2, VolumeKind.Solid, 0), Round(17.88, 11.15, 255, 2, 2, VolumeKind.Solid, 0)];

    enum Kind { Infantry, Cavalry, Horror }

    static PlacedModel Model(Kind kind, double x, double y, double z, double yaw) => new(kind switch
    {
        Kind.Cavalry => new ModelBody(Oval, CavalryHeight),
        Kind.Horror => new ModelBody(Base.Circle(2.362204724409449), 3.5433),
        _ => new ModelBody(Base.Circle(1.2598), 1.8898),
    }, new Placement(new Point3(x, y, z), new Yaw(yaw)));

    static readonly PlacedModel[] Riders =
    [
        Model(Kind.Cavalry, 30.4838, 4.8302, 0, 0), Model(Kind.Cavalry, 32.0618, 4.8302, 0, 0), Model(Kind.Cavalry, 33.6398, 4.8302, 0, 0),
        Model(Kind.Cavalry, 35.2177, 4.8302, 0, 0), Model(Kind.Cavalry, 36.7957, 4.8302, 0, 0),
    ];

    /// <summary>Every other model on the table; the squad at z 3 stands on the 3" steep hill at (28.74, 15.62).</summary>
    static readonly PlacedModel[] Others =
    [
        Model(Kind.Infantry, 23.8161, 15.8815, 0, 0), Model(Kind.Infantry, 25.2759, 15.8815, 3, 0), Model(Kind.Infantry, 26.7358, 15.8815, 3, 0),
        Model(Kind.Infantry, 28.4535, 15.8775, 3, 0), Model(Kind.Infantry, 29.9133, 15.8775, 3, 0), Model(Kind.Infantry, 23.8161, 14.4216, 0, 0),
        Model(Kind.Infantry, 26.9979, 14.4906, 3, 0), Model(Kind.Infantry, 28.4535, 14.4176, 3, 0), Model(Kind.Infantry, 29.9133, 14.4176, 3, 0),
        Model(Kind.Infantry, 50.9530, 37.7204, 0, 185.33), Model(Kind.Infantry, 49.2007, 37.5569, 0, 185.33), Model(Kind.Infantry, 47.4485, 37.3933, 0, 185.33),
        Model(Kind.Infantry, 50.7894, 39.4727, 0, 185.33), Model(Kind.Infantry, 49.0371, 39.3091, 0, 185.33), Model(Kind.Infantry, 47.2849, 39.1455, 0, 185.33),
        Model(Kind.Infantry, 39.0227, 33.9143, 0, 187.22), Model(Kind.Infantry, 37.2768, 33.6933, 0, 187.22), Model(Kind.Infantry, 35.5309, 33.4722, 0, 187.22),
        Model(Kind.Infantry, 38.8016, 35.6602, 0, 187.22), Model(Kind.Infantry, 37.0557, 35.4392, 0, 187.22), Model(Kind.Infantry, 35.3098, 35.2181, 0, 187.22),
        Model(Kind.Infantry, 10.3135, 10.3896, 0, 0), Model(Kind.Infantry, 11.7733, 10.3896, 0, 0), Model(Kind.Infantry, 13.2332, 10.3896, 0, 0),
        Model(Kind.Infantry, 14.6930, 10.3896, 0, 0), Model(Kind.Infantry, 11.1102, 7.6988, 0, 0), Model(Kind.Infantry, 10.3135, 8.9298, 0, 0),
        Model(Kind.Infantry, 11.7733, 8.9298, 0, 0), Model(Kind.Infantry, 13.2332, 8.9298, 0, 0), Model(Kind.Infantry, 14.6930, 8.9298, 0, 0),
        Model(Kind.Infantry, 12.5258, 7.8612, 0, 0),
        Model(Kind.Infantry, 32.2346, 32.4491, 0, -176.42), Model(Kind.Infantry, 30.4782, 32.3392, 0, -176.42), Model(Kind.Infantry, 28.7218, 32.2293, 0, -176.42),
        Model(Kind.Infantry, 26.9654, 32.1193, 0, -176.42), Model(Kind.Infantry, 25.2090, 32.0094, 0, -176.42), Model(Kind.Infantry, 23.4526, 31.8995, 0, -176.42),
        Model(Kind.Infantry, 31.2612, 33.9152, 0, -176.42), Model(Kind.Infantry, 29.5048, 33.8053, 0, -176.42), Model(Kind.Infantry, 27.7484, 33.6954, 0, -176.42),
        Model(Kind.Infantry, 25.9920, 33.5855, 0, -176.42), Model(Kind.Infantry, 24.2356, 33.4756, 0, -176.42),
        Model(Kind.Horror, 49.7217, 18.9390, 0, 0),
        Model(Kind.Cavalry, 62.3925, 10.1224, 0, 6.31), Model(Kind.Cavalry, 64.2591, 10.3286, 0, 6.31), Model(Kind.Cavalry, 66.1257, 10.5349, 0, 6.31),
        Model(Kind.Cavalry, 63.5995, 7.7483, 0, 6.31), Model(Kind.Cavalry, 65.4661, 7.9545, 0, 6.31),
    ];

    static (MoveRegions Regions, Guid[] Ids) Rush()
    {
        var ids = Riders.Select(_ => Guid.CreateVersion7()).ToArray();
        var legal = new MoveRegion(ids, 16, [Building.Id], DifficultGround.Select(v => v.Id).ToList(), 6, Separation: 1, SteepVolumeIds: Hills.Select(v => v.Id).ToList());
        IReadOnlyList<WorldVolume> volumes = [Building, .. DifficultGround, .. Hills, .. Lairs];
        // Each rider's reach stops at every model of the other units (DragBodies.Stationary leaves out the Ask's models).
        var regions = new MoveRegions(ids.Zip(Riders).ToDictionary(p => p.First, p => p.Second), legal, volumes, Table, Others);
        return (regions, ids);
    }

    [Fact]
    public void The_lead_riders_rush_area_is_built_in_seconds()
    {
        var (regions, ids) = Rush();
        var watch = Stopwatch.StartNew();
        var area = regions.Bands(ids[0]);
        output.WriteLine($"lead: {area.Clear.Count / 3}/{area.Difficult.Count / 3}/{area.Dangerous.Count / 3} triangles, {watch.ElapsedMilliseconds} ms");
        Assert.NotEmpty(area.Clear);
        Assert.InRange(watch.ElapsedMilliseconds, 0, 5000);                                  // cold: about 1 s in a debug build
    }

    [Fact]
    public void The_area_reaches_sixteen_inches_and_no_farther()
    {
        var (regions, ids) = Rush();
        var start = new Point2(30.4838, 4.8302);
        var toward = (new Point2(34.9587, 20.1917) - start) * (1 / 16.0);                       // where the game's rush put the lead, 16" in a straight line
        Assert.True(regions.Reaches(ids[0], start + toward * 15.9));
        Assert.False(regions.Reaches(ids[0], start + toward * 16.2));
        Assert.True(regions.Reaches(ids[0], new Point2(30.4838 + 15, 4.8302)));                // along the table edge
        Assert.False(regions.Reaches(ids[0], new Point2(30.4838 + 17, 4.8302)));
    }

    [Fact]
    public async Task Every_riders_area_builds_at_once_in_the_order_asked()
    {
        var (regions, ids) = Rush();
        var areas = await regions.BandsAsync(ids);
        Assert.Equal(ids.Length, areas.Count);
        Assert.All(areas, a => Assert.NotEmpty(a.Clear));
        Assert.Equal(ids.Select(regions.Bands), areas);
    }
}
