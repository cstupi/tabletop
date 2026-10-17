using Tabletop.Content;
using Tabletop.Content.Tests;
using Tabletop.GameServer.Setup;
using Tabletop.Geometry;
using Tabletop.Protocol;

namespace Tabletop.GameServer.Tests;

/// <summary>Security review M4, M6 and the content-path review: what one draft may hold.</summary>
public class DraftLimitsTests
{
    static readonly TableState Empty = new(Guid.CreateVersion7(), null, new TableSize(72, 48), VisualRef.OfBuiltin("grass"), [], [], []);
    static TerrainPlacement Hill(double x = 10, double y = 10) => TableStateTests.Sample().Terrain[0] with { Id = Guid.CreateVersion7(), Position = new Point3(x, y, 0) };
    static Marker Token(string? label = "token", double diameter = 1) => new(Guid.CreateVersion7(), MarkerKind.Token, new Point3(5, 5, 0), new Yaw(0), diameter, label, VisualRef.OfBuiltin("chip"));
    static TerrainVolume Volume(double bottom = 0, double top = 2, string? notes = null) =>
        new(Guid.CreateVersion7(), new Volume(VolumeKind.Solid, Footprint.Rect(2, 2), new Point2(0, 0), new Yaw(0), bottom, top, TerrainDeclarations.None, notes));
    static string? Refusal(TableState d, SetupEdit e) { var next = DraftEditor.Apply(d, e, out var missing); Assert.True(next is null == missing is not null); return missing; }

    [Fact]
    public void Terrain_volumes_and_markers_are_capped()
    {
        var full = Empty with { Terrain = Enumerable.Range(0, DraftLimits.Default.Terrain).Select(i => Hill(i % 60 + 1, 1)).ToList(), Markers = Enumerable.Range(0, DraftLimits.Default.Markers).Select(_ => Token()).ToList() };
        Assert.NotNull(Refusal(full, new AddTerrain(Hill())));
        Assert.NotNull(Refusal(full, new AddSetupMarker(Token())));
        Assert.Null(Refusal(full, new RemoveTerrain(full.Terrain[0].Id)));                       // removing from a full draft still works
        Assert.NotNull(Refusal(Empty, new AddTerrain(Hill() with { Volumes = Enumerable.Range(0, 9).Select(_ => Volume()).ToList() })));
        Assert.NotNull(Refusal(Empty, new ReplaceDraft(full with { Terrain = [.. full.Terrain, Hill()] })));
        Assert.Null(Refusal(Empty, new ReplaceDraft(full)));
    }

    [Theory]
    [InlineData(-1, 2)] [InlineData(2, 2)] [InlineData(0, 25)] [InlineData(0, double.NaN)]
    public void A_volume_spans_upward_within_24_inches(double bottom, double top) =>
        Assert.NotNull(Refusal(Empty, new AddTerrain(Hill() with { Volumes = [Volume(bottom, top)] })));

    [Fact]
    public void Text_is_bounded()
    {
        Assert.NotNull(Refusal(Empty, new AddSetupMarker(Token(new string('a', 65)))));
        Assert.Null(Refusal(Empty, new AddSetupMarker(Token(new string('a', 64)))));
        Assert.NotNull(Refusal(Empty, new AddTerrain(Hill() with { Volumes = [Volume(notes: new string('n', 501))] })));
        Assert.NotNull(Refusal(Empty, new SetTexture(VisualRef.OfBuiltin(new string('g', 65)))));
        Assert.NotNull(Refusal(Empty, new SetTexture(VisualRef.OfBuiltin("../../etc/passwd"))));
    }

    [Theory]
    [InlineData(0.4)] [InlineData(6.1)] [InlineData(double.PositiveInfinity)]
    public void A_marker_is_half_an_inch_to_six_across(double diameter) => Assert.NotNull(Refusal(Empty, new AddSetupMarker(Token(diameter: diameter))));

    [Fact]
    public void Positions_and_yaws_are_finite_and_near_the_table()
    {
        Assert.NotNull(Refusal(Empty, new AddTerrain(Hill(double.NaN, 1))));
        Assert.NotNull(Refusal(Empty, new AddTerrain(Hill(1e9, 1))));
        Assert.Null(Refusal(Empty, new AddTerrain(Hill(72 + 30, 4))));                             // beside the table, as the generators leave pieces
        var hill = Hill();
        var d = DraftEditor.Apply(Empty, new AddTerrain(hill), out _)!;
        Assert.NotNull(Refusal(d, new MoveTerrain(hill.Id, new Point3(1, 1, 0), new Yaw(double.NaN))));
        Assert.NotNull(Refusal(d, new ScaleTerrain(hill.Id, 1000)));
    }

    [Fact]
    public void A_zone_style_names_its_own_shapes()
    {
        Assert.Null(Refusal(Empty, new SetZoneStyle(ZoneStyles.Spearhead, ZoneStyles.ShapesOf(ZoneStyles.Spearhead))));
        Assert.NotNull(Refusal(Empty, new SetZoneStyle(ZoneStyles.Spearhead, ["no-such/1", "spearhead/2"])));
        Assert.NotNull(Refusal(Empty, new SetZoneStyle("nonsense", ["frontline/1"])));
        Assert.NotNull(Refusal(Empty, new SetZoneStyle(ZoneStyles.Frontline, ["frontline/1", "frontline/1"])));
        Assert.NotNull(Refusal(Empty, new ReplaceDraft(Empty with { Zones = [new DeploymentZone(Guid.NewGuid(), "bogus/1", 1)] })));
    }

    /// <summary>The largest draft the limits allow still loads as one ReplaceDraft frame.</summary>
    [Fact]
    public void A_full_draft_fits_the_draft_frame_cap()
    {
        var l = DraftLimits.Default;
        var piece = Hill() with { Mesh = VisualRef.OfAsset(Guid.NewGuid()), Volumes = Enumerable.Range(0, l.VolumesPerPiece).Select(_ => Volume(notes: new string('n', l.Notes))).ToList() };
        var full = Empty with
        {
            Name = new string('x', 200),
            Terrain = Enumerable.Range(0, l.Terrain).Select(_ => piece with { Id = Guid.NewGuid() }).ToList(),
            Markers = Enumerable.Range(0, l.Markers).Select(_ => Token(new string('l', l.Label))).ToList(),
            Zones = ZoneStyles.ShapesOf(ZoneStyles.FreeForAll).Select((s, i) => new DeploymentZone(Guid.NewGuid(), s, i + 1)).ToList(),
        };
        Assert.Null(l.Breach(full));
        var bytes = System.Text.Encoding.UTF8.GetByteCount(WireJson.Serialize(new EditMessage(Guid.NewGuid(), new ReplaceDraft(full))));
        var cap = new Configuration.GameServerOptions { DbConnection = "", StytchProjectId = "", StytchJwksUrl = "" }.DraftFrameBytes;
        Assert.True(bytes < cap, $"{bytes} bytes against a {cap} byte cap");
    }

    [Fact]
    public void A_table_edge_over_144_inches_is_refused() => Assert.NotNull(Refusal(Empty, new SetTableSize(new TableSize(145, 48))));
}
