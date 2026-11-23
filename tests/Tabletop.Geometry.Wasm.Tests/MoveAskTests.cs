using System.Text.Json;
using Tabletop.Geometry;

namespace Tabletop.Geometry.Wasm.Tests;

/// <summary>
/// A kept move Ask (MoveAskOpen, MoveAskArea, MoveAskFrame, MoveAskClose) answers what the one-off calls answer: its area is MoveBands' (each share
/// part of it, together all of it), each frame's routes measure what MoveRoutes measures, its allowance is the unit's (p.11's cap once a route goes
/// in difficult ground), and its contacts are Contacts' over the same terms.
/// </summary>
public class MoveAskTests
{
    static readonly JsonSerializerOptions J = FacadeJson.Options;
    static string W<T>(T v) => JsonSerializer.Serialize(v, J);
    static T R<T>(string json) => JsonSerializer.Deserialize<T>(json, J)!;

    static readonly TableSize Size = new(48, 48);
    static readonly ModelBody Cavalry = new(Base.Oval(2.362204724409449, 1.3779527559055118), 4.43);
    static readonly ModelBody Infantry = new(Base.Circle(1.26), 1.5);
    static PieceVolume Vol(VolumeKind kind, Footprint f, double top, TerrainDeclarations d) =>
        new(Guid.CreateVersion7(), new Volume(kind, f, new Point2(0, 0), new Yaw(0), 0, top, d, null));
    static Piece PieceAt(double x, double y, params PieceVolume[] v) => new(Guid.CreateVersion7(), new Point3(x, y, 0), new Yaw(0), 1, v);
    static Placement Pl(double x, double y, double yaw = 0) => new(new Point3(x, y, 0), new Yaw(yaw));

    // A 16" move of three riders past a wood (difficult, cap 6"), a pit (dangerous), a ruin (unstable over 6") and a building, among a squad.
    static readonly Piece Wood = PieceAt(20, 14, Vol(VolumeKind.Area, Footprint.Circle(2.5), 4, TerrainDeclarations.Difficult));
    static readonly Piece Pit = PieceAt(10, 20, Vol(VolumeKind.Area, Footprint.Circle(1.5), 1, TerrainDeclarations.Dangerous));
    static readonly Piece Ruin = PieceAt(14, 26, Vol(VolumeKind.Area, Footprint.Rect(3, 3), 3, TerrainDeclarations.DangerousOnRushCharge));
    static readonly Piece Building = PieceAt(26, 22, Vol(VolumeKind.Solid, Footprint.Rect(4, 4), 4, TerrainDeclarations.Impassable));
    static readonly Piece[] Terrain = [Wood, Pit, Ruin, Building];
    static readonly AskModel[] Riders = [new(Guid.CreateVersion7(), new PlacedModel(Cavalry, Pl(12, 10))), new(Guid.CreateVersion7(), new PlacedModel(Cavalry, Pl(13.6, 10))),
        new(Guid.CreateVersion7(), new PlacedModel(Cavalry, Pl(15.2, 10)))];
    static readonly (Guid Id, PlacedModel At)[] Squad = Enumerable.Range(0, 5).Select(i => (Guid.CreateVersion7(), new PlacedModel(Infantry, Pl(16 + i * 1.5, 18)))).ToArray();
    static readonly Guid RiderUnit = Guid.CreateVersion7(), SquadUnit = Guid.CreateVersion7();

    static Guid Id(Piece p) => p.Volumes[0].Id;
    static ContactBody[] Bodies() => [.. Riders.Select(r => new ContactBody(r.Id, RiderUnit, r.Start.Body, r.Start.Placement)),
        .. Squad.Select(s => new ContactBody(s.Id, SquadUnit, s.At.Body, s.At.Placement))];

    static MoveAskRequest Ask(string key, double? minimum = null) => new(key, Riders, 16, [Id(Building)], [Id(Wood)], 6, Terrain, Size, Squad.Select(s => s.At).ToList(),
        Bodies(), Separation: 1, Dangerous: [Id(Pit)], Unstable: [Id(Ruin)], UnstableOver: 6, MinDistance: minimum);

    static BandsRequest Bands(AskModel m, double? minimum = null) => new(m.Start, 16, [Id(Building)], [Id(Wood)], 6, Terrain, Size, Squad.Select(s => s.At).ToList(),
        Dangerous: [Id(Pit)], Unstable: [Id(Ruin)], UnstableOver: 6, MinDistance: minimum);

    static double[] Flat(Point2[] points) => points.SelectMany(p => new[] { p.X, p.Y }).ToArray();

    static List<string> Triangles(double[] flat) =>
        Enumerable.Range(0, flat.Length / 6).Select(i => string.Join(",", flat.Skip(6 * i).Take(6).Select(v => v.ToString("R")))).Order().ToList();

    [Theory]
    [InlineData(null)]
    [InlineData(5.0)]
    public void The_kept_area_is_the_one_off_area(double? minimum)
    {
        Facade.MoveAskOpen(W(Ask("area", minimum)));
        foreach (var m in Riders)
        {
            var whole = R<BandsAnswer>(Facade.MoveBands(W(Bands(m, minimum))));
            var kept = R<FlatBands>(Facade.MoveAskArea(W(new MoveAreaRequest("area", m.Id))));
            Assert.Equal(Flat(whole.Clear), kept.Clear);
            Assert.Equal(Flat(whole.Difficult), kept.Difficult);
            Assert.Equal(Flat(whole.Dangerous), kept.Dangerous);
            var shares = Enumerable.Range(0, 3).Select(part => R<FlatBands>(Facade.MoveAskArea(W(new MoveAreaRequest("area", m.Id, part, 3))))).ToList();
            Assert.Equal(Triangles(kept.Clear), Triangles(shares.SelectMany(s => s.Clear).ToArray()));
            Assert.Equal(Triangles(kept.Difficult), Triangles(shares.SelectMany(s => s.Difficult).ToArray()));
            Assert.Equal(Triangles(kept.Dangerous), Triangles(shares.SelectMany(s => s.Dangerous).ToArray()));
        }
    }

    [Theory]
    [InlineData(4, 0, false)]     // short moves on open ground
    [InlineData(8, 3, true)]      // the lead through the wood: the unit is held to the cap
    [InlineData(-6, 12, false)]   // round the pit and the ruin
    public void A_frame_measures_what_the_one_off_routes_measure(double dx, double dy, bool capped)
    {
        Facade.MoveAskOpen(W(Ask("frame")));
        var targets = Riders.ToDictionary(r => r.Id, r => Pl(r.Start.Placement.Position.X + dx, r.Start.Placement.Position.Y + dy));
        var frame = R<MoveFrame>(Facade.MoveAskFrame(W(new MoveFrameRequest("frame", targets))));
        var routes = Riders.Select(r => new RouteModel(r.Id, r.Start, targets[r.Id].Position.Horizontal)).ToList();
        var request = new RoutesRequest(routes, 16, [Id(Building)], [Id(Wood)], 6, Terrain, Size, Squad.Select(s => s.At).ToList(), Dangerous: [Id(Pit)],
            Unstable: [Id(Ruin)], UnstableOver: 6);
        var plain = R<RouteFound[]>(Facade.MoveRoutes(W(request)));
        var entered = plain.Any(r => r.EntersDifficult);
        Assert.Equal(capped, entered);
        Assert.Equal(entered ? 6 : 16, frame.Allowance);
        if (entered) plain = R<RouteFound[]>(Facade.MoveRoutes(W(request with { Capped = true })));
        Assert.Equal(plain.Select(r => r.Id), frame.Routes.Select(r => r.Id));
        foreach (var (a, b) in plain.Zip(frame.Routes))
        {
            Assert.Equal(a.Distance, b.Distance, 6);
            Assert.Equal(a.Dangerous, b.Dangerous);
        }
        var contacts = R<ContactFound[]>(Facade.Contacts(W(new ContactsRequest(Bodies(), targets, Terrain,
            new ContactTerms(1, false, true, new ContactMove(16, [Id(Building)], [Id(Wood)], 6), Size, entered ? 6 : null)))));
        Assert.Equal(contacts.OrderBy(c => c.ModelId), frame.Contacts.OrderBy(c => c.ModelId));
    }

    [Fact]
    public void A_frame_names_a_landing_on_a_base_and_one_through_the_squad()
    {
        Facade.MoveAskOpen(W(Ask("contacts")));
        var targets = new Dictionary<Guid, Placement>
        {
            [Riders[0].Id] = Pl(16, 18),                                    // on a squad model's base
            [Riders[1].Id] = Riders[1].Start.Placement,                     // where it stands
            [Riders[2].Id] = Pl(15.2, 21),                                  // past the squad: round it
        };
        var frame = R<MoveFrame>(Facade.MoveAskFrame(W(new MoveFrameRequest("contacts", targets))));
        Assert.Contains(frame.Contacts, c => c.ModelId == Riders[0].Id && c.Kind == ContactKind.OnBase);
        Assert.Equal([Riders[0].Id, Riders[2].Id], frame.Routes.Select(r => r.Id));
        var routesOnly = R<MoveFrame>(Facade.MoveAskFrame(W(new MoveFrameRequest("contacts", targets, Contacts: false))));
        Assert.Empty(routesOnly.Contacts);
    }

    [Fact]
    public void A_closed_ask_is_gone()
    {
        Facade.MoveAskOpen(W(Ask("closed")));
        Assert.True(R<bool>(Facade.MoveAskClose(W(new MoveKeyRequest("closed")))));
        Assert.False(R<bool>(Facade.MoveAskClose(W(new MoveKeyRequest("closed")))));
        Assert.Throws<KeyNotFoundException>(() => Facade.MoveAskArea(W(new MoveAreaRequest("closed", Riders[0].Id))));
    }
}
