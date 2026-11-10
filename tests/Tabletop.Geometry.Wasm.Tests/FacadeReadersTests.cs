using System.Text.Json;

namespace Tabletop.Geometry.Wasm.Tests;

/// <summary>The hand-written converters (FacadeReaders) read and write what the generated metadata alone does.</summary>
public class FacadeReadersTests
{
    static readonly JsonSerializerOptions Generated = FacadeJson.Build(readers: false);
    static readonly JsonSerializerOptions Fast = FacadeJson.Options;

    static readonly Dictionary<string, Type> RequestOf = new()
    {
        ["LandModels"] = typeof(LandRequest), ["LandPiece"] = typeof(PieceRequest), ["TurnPiece"] = typeof(TurnRequest),
        ["ModelSurfaces"] = typeof(ModelRestRequest), ["RestModel"] = typeof(ModelRestRequest), ["PieceSurfaces"] = typeof(PieceRestRequest),
        ["RestPiece"] = typeof(PieceRestRequest), ["Contacts"] = typeof(ContactsRequest), ["OverlapBases"] = typeof(PairRequest),
        ["EdgeToEdge"] = typeof(PairRequest), ["BodyToBody"] = typeof(PairRequest), ["EdgeToPoint"] = typeof(PointRequest),
        ["EdgeToMarker"] = typeof(MarkerRequest), ["NearestPair"] = typeof(NearestRequest), ["RangeOutline"] = typeof(RingRequest),
        ["ZonePolygons"] = typeof(ZoneRequest), ["ZoneContains"] = typeof(ZoneRequest), ["ZoneHoldsBase"] = typeof(ZoneRequest),
        ["ExitYaw"] = typeof(ExitRequest), ["TableHoldsBase"] = typeof(TableRequest), ["MoveBands"] = typeof(BandsRequest),
    };

    /// <summary>Read with each, written back with the generated metadata: the same text.</summary>
    static void SameRead(Type type, string json)
    {
        var generated = JsonSerializer.Serialize(JsonSerializer.Deserialize(json, Generated.GetTypeInfo(type)), Generated.GetTypeInfo(type));
        var fast = JsonSerializer.Serialize(JsonSerializer.Deserialize(json, Fast.GetTypeInfo(type)), Generated.GetTypeInfo(type));
        Assert.Equal(generated, fast);
    }

    [Fact]
    public void Only_the_options_use_the_readers()
    {
        foreach (var t in new[] { typeof(Point2), typeof(Point3), typeof(Yaw), typeof(Base), typeof(ModelBody), typeof(Placement), typeof(PlacedModel), typeof(ContactBody) })
        {
            Assert.StartsWith(typeof(FacadeReaders).FullName!, Fast.GetConverter(t).GetType().FullName);
            Assert.DoesNotContain(nameof(FacadeReaders), Generated.GetConverter(t).GetType().FullName);
        }
    }

    [Fact]
    public void Every_agreement_request_reads_the_same()
    {
        foreach (var c in AgreementCases.All) SameRead(RequestOf[c.Method], c.Request.GetRawText());
    }

    static PlacedModel RandomModel(Random r)
    {
        var b = r.Next(3) switch { 0 => Base.Circle(r.NextDouble() * 3 + 0.5), 1 => Base.Oval(r.NextDouble() * 4 + 1, r.NextDouble() * 2 + 0.5), _ => Base.Rect(r.NextDouble() * 4 + 1, r.NextDouble() * 3 + 1) };
        return new(new ModelBody(b, r.NextDouble() * 3), new Placement(new Point3(r.NextDouble() * 72, r.NextDouble() * 48, r.NextDouble() * 3), new Yaw(r.NextDouble() * 720 - 360)));
    }

    [Fact]
    public void A_crowded_request_reads_and_writes_the_same()
    {
        var r = new Random(7);
        var bodies = Enumerable.Range(0, 150).Select(i => { var m = RandomModel(r); return new ContactBody(Guid.NewGuid(), Guid.NewGuid(), m.Body, i % 9 == 0 ? null : m.Placement); }).ToList();
        var contacts = new ContactsRequest(bodies, bodies.Take(5).ToDictionary(b => b.Id, _ => RandomModel(r).Placement), [],
            new ContactTerms(1, false, true, new ContactMove(6, [Guid.NewGuid()], [], 6), new TableSize(72, 48), 6));
        var land = new LandRequest(bodies.Take(5).Select(b => new HeldModel(b.Id, RandomModel(r), new Point2(r.NextDouble(), r.NextDouble()))).ToList(),
            Enumerable.Range(0, 145).Select(_ => RandomModel(r)).ToList(), [], []);
        foreach (var (type, value) in new (Type, object)[] { (typeof(ContactsRequest), contacts), (typeof(LandRequest), land) })
        {
            var generated = JsonSerializer.Serialize(value, Generated.GetTypeInfo(type));
            Assert.Equal(generated, JsonSerializer.Serialize(value, Fast.GetTypeInfo(type)));
            SameRead(type, generated);
        }
    }

    [Theory]
    [InlineData("{\"model\":{\"placement\":{\"yaw\":{\"degrees\":5},\"position\":{\"z\":1,\"y\":2,\"x\":3}},\"body\":{\"height\":2,\"base\":{\"b\":1,\"a\":2,\"shape\":\"oval\"}}},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"body\":{\"base\":{\"shape\":\"rect\",\"a\":1,\"b\":1,\"extra\":[1,{\"x\":2}]},\"height\":1,\"note\":null},\"placement\":{\"position\":{\"x\":1,\"y\":2}},\"more\":{}},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"body\":{\"base\":{\"shape\":\"Circle\",\"A\":1,\"a\":2}},\"placement\":{\"position\":{\"X\":9,\"x\":1,\"x\":4,\"y\":2.5e1,\"z\":-0.0}}},\"terrain\":[]}")]
    [InlineData("{\"model\":{},\"terrain\":[]}")]
    public void Odd_but_valid_requests_read_the_same(string json) => SameRead(typeof(ModelRestRequest), json);

    [Theory]
    [InlineData("{\"models\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"unitId\":\"00000000-0000-0000-0000-000000000002\",\"body\":{\"base\":{\"shape\":\"circle\",\"a\":1,\"b\":1},\"height\":1},\"at\":null},null],\"proposed\":{},\"terrain\":[],\"terms\":{\"passesModels\":false,\"landing\":false}}")]
    [InlineData("{\"models\":[{\"unitId\":\"00000000-0000-0000-0000-000000000002\",\"ignored\":\"x\"}],\"proposed\":{},\"terrain\":[],\"terms\":{\"passesModels\":true,\"landing\":true}}")]
    public void Contact_rows_read_the_same(string json) => SameRead(typeof(ContactsRequest), json);

    [Theory]
    [InlineData("{\"model\":{\"body\":{\"base\":{\"shape\":0,\"a\":1,\"b\":1},\"height\":1},\"placement\":{\"position\":{\"x\":1,\"y\":2,\"z\":0},\"yaw\":{\"degrees\":0}}},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"body\":{\"base\":{\"shape\":\"hexagon\",\"a\":1,\"b\":1},\"height\":1}},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"body\":null},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"placement\":{\"position\":{\"x\":\"1\"}}},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"placement\":{\"position\":{\"x\":null}}},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"placement\":{\"position\":[1,2,3]}},\"terrain\":[]}")]
    [InlineData("{\"model\":{\"placement\":{\"yaw\":{\"degrees\":true}}},\"terrain\":[]}")]
    [InlineData("{\"model\":null,\"terrain\":[]}")]
    public void Bad_requests_are_refused_by_both(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, Generated.GetTypeInfo(typeof(ModelRestRequest))));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, Fast.GetTypeInfo(typeof(ModelRestRequest))));
    }

    [Theory]
    [InlineData("{\"models\":[{\"id\":\"nope\"}],\"proposed\":{},\"terrain\":[],\"terms\":{\"passesModels\":false,\"landing\":false}}")]
    [InlineData("{\"models\":[{\"id\":7}],\"proposed\":{},\"terrain\":[],\"terms\":{\"passesModels\":false,\"landing\":false}}")]
    public void Bad_contact_rows_are_refused_by_both(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, Generated.GetTypeInfo(typeof(ContactsRequest))));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(json, Fast.GetTypeInfo(typeof(ContactsRequest))));
    }

    [Fact]
    public void Answers_write_the_same()
    {
        var r = new Random(3);
        object[] values =
        [
            new HeldEnd[] { new(Guid.NewGuid(), new Point3(1.5, -2, 0.25), new Point3(1e-9, 1e12, -0.0)) },
            new PieceEnd(new Point3(1, 2, 3), new Point3(4, 5, 6)), new Point3(0.1, 0.2, 0.3), new Yaw(-45.5),
            new Segment[] { new(new Point2(1, 2), new Point2(3, 4)) }, new Point2[][] { [new(0.5, 0.25)] }, RandomModel(r).Body.Base,
        ];
        foreach (var v in values)
            Assert.Equal(JsonSerializer.Serialize(v, Generated.GetTypeInfo(v.GetType())), JsonSerializer.Serialize(v, Fast.GetTypeInfo(v.GetType())));
    }
}
