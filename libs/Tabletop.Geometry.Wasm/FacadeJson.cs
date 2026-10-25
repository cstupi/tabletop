using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tabletop.Geometry.Wasm;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LandRequest))]
[JsonSerializable(typeof(HeldEnd[]))]
[JsonSerializable(typeof(PieceRequest))]
[JsonSerializable(typeof(PieceEnd))]
[JsonSerializable(typeof(TurnRequest))]
[JsonSerializable(typeof(Point3))]
[JsonSerializable(typeof(ModelRestRequest))]
[JsonSerializable(typeof(PieceRestRequest))]
[JsonSerializable(typeof(ContactsRequest))]
[JsonSerializable(typeof(ContactFound[]))]
[JsonSerializable(typeof(PairRequest))]
[JsonSerializable(typeof(PointRequest))]
[JsonSerializable(typeof(MarkerRequest))]
[JsonSerializable(typeof(NearestRequest))]
[JsonSerializable(typeof(Nearest))]
[JsonSerializable(typeof(RingRequest))]
[JsonSerializable(typeof(Segment[]))]
[JsonSerializable(typeof(ZoneRequest))]
[JsonSerializable(typeof(TableRequest))]
[JsonSerializable(typeof(ExitRequest))]
[JsonSerializable(typeof(Yaw))]
[JsonSerializable(typeof(Yaw?))]
[JsonSerializable(typeof(Piece))]
[JsonSerializable(typeof(ContactFound))]
[JsonSerializable(typeof(Base))]
[JsonSerializable(typeof(double[]))]
[JsonSerializable(typeof(Point2[][]))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(double))]
internal partial class FacadeJsonContext : JsonSerializerContext;

public static class FacadeJson
{
    /// <summary>The context's resolver, camelCase, nulls omitted, and enums as snake_case names (integers refused), as ContentJson reads and writes them.</summary>
    public static JsonSerializerOptions Options { get; } = Build(readers: true);

    /// <summary>`readers`: the hand-written converters (FacadeReaders) for the bulk values; without them, the generated metadata reads everything.</summary>
    internal static JsonSerializerOptions Build(bool readers)
    {
        var o = new JsonSerializerOptions
        {
            TypeInfoResolver = FacadeJsonContext.Default,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        o.Converters.Add(new JsonStringEnumConverter<BaseShape>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        o.Converters.Add(new JsonStringEnumConverter<FootprintKind>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        o.Converters.Add(new JsonStringEnumConverter<VolumeKind>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        o.Converters.Add(new JsonStringEnumConverter<TerrainDeclarations>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        o.Converters.Add(new JsonStringEnumConverter<ContactKind>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        if (readers) FacadeReaders.AddTo(o.Converters);
        o.MakeReadOnly();
        return o;
    }
}
