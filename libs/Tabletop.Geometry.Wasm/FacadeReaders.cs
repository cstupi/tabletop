using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Tabletop.Geometry.Wasm;

/// <summary>
/// Hand-written converters for the values a request carries by the hundred (every model's body and placement): the generated metadata reads a
/// record through its constructor-parameter path, which costs the browser's runtime several times more. Each reads and writes what the generated
/// contract does: camelCase names, matched case-sensitively, unknown names skipped, a missing member its default, null refused for a struct,
/// nulls not written, Point2's `length` written and ignored on read. BaseShape goes through the options' own enum converter.
/// </summary>
internal static class FacadeReaders
{
    static readonly Point2Converter P2 = new();
    static readonly Point3Converter P3 = new();
    static readonly YawConverter Y = new();
    static readonly BaseConverter B = new();
    static readonly ModelBodyConverter MB = new();
    static readonly PlacementConverter PL = new();
    static readonly PlacedModelConverter PM = new();
    static readonly ContactBodyConverter CB = new();

    public static void AddTo(IList<JsonConverter> converters)
    {
        foreach (var c in new JsonConverter[] { P2, P3, Y, B, MB, PL, PM, CB }) converters.Add(c);
    }

    static void Start(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null) throw new JsonException("A value type cannot be null.");
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException($"Expected an object, found {reader.TokenType}.");
    }

    /// <summary>The next member's name, the reader on its value; null at the object's end.</summary>
    static string? Next(ref Utf8JsonReader reader)
    {
        if (!reader.Read()) throw new JsonException("The object does not end.");
        if (reader.TokenType == JsonTokenType.EndObject) return null;
        if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException($"Expected a member name, found {reader.TokenType}.");
        var name = reader.GetString();
        if (!reader.Read()) throw new JsonException("A member has no value.");
        return name;
    }

    static double Number(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetDouble(out var value)) throw new JsonException($"Expected a number, found {reader.TokenType}.");
        return value;
    }

    static Guid Id(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out var value)) throw new JsonException("Expected a GUID string.");
        return value;
    }

    static JsonTypeInfo<BaseShape> Shape(JsonSerializerOptions options) => (JsonTypeInfo<BaseShape>)options.GetTypeInfo(typeof(BaseShape));

    sealed class Point2Converter : JsonConverter<Point2>
    {
        public override Point2 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            Start(ref reader);
            double x = 0, y = 0;
            while (Next(ref reader) is { } name)
            {
                switch (name)
                {
                    case "x": x = Number(ref reader); break;
                    case "y": y = Number(ref reader); break;
                    default: reader.Skip(); break;
                }
            }
            return new Point2(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Point2 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteNumber("length", value.Length);
            writer.WriteEndObject();
        }
    }

    sealed class Point3Converter : JsonConverter<Point3>
    {
        public override Point3 Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            Start(ref reader);
            double x = 0, y = 0, z = 0;
            while (Next(ref reader) is { } name)
            {
                switch (name)
                {
                    case "x": x = Number(ref reader); break;
                    case "y": y = Number(ref reader); break;
                    case "z": z = Number(ref reader); break;
                    default: reader.Skip(); break;
                }
            }
            return new Point3(x, y, z);
        }

        public override void Write(Utf8JsonWriter writer, Point3 value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.X);
            writer.WriteNumber("y", value.Y);
            writer.WriteNumber("z", value.Z);
            writer.WriteEndObject();
        }
    }

    sealed class YawConverter : JsonConverter<Yaw>
    {
        public override Yaw Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            Start(ref reader);
            double degrees = 0;
            while (Next(ref reader) is { } name)
            {
                if (name == "degrees") degrees = Number(ref reader);
                else reader.Skip();
            }
            return new Yaw(degrees);
        }

        public override void Write(Utf8JsonWriter writer, Yaw value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("degrees", value.Degrees);
            writer.WriteEndObject();
        }
    }

    sealed class BaseConverter : JsonConverter<Base>
    {
        public override Base Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            Start(ref reader);
            BaseShape shape = default; double a = 0, b = 0;
            while (Next(ref reader) is { } name)
            {
                switch (name)
                {
                    case "shape": shape = JsonSerializer.Deserialize(ref reader, Shape(options)); break;
                    case "a": a = Number(ref reader); break;
                    case "b": b = Number(ref reader); break;
                    default: reader.Skip(); break;
                }
            }
            return new Base(shape, a, b);
        }

        public override void Write(Utf8JsonWriter writer, Base value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("shape");
            JsonSerializer.Serialize(writer, value.Shape, Shape(options));
            writer.WriteNumber("a", value.A);
            writer.WriteNumber("b", value.B);
            writer.WriteEndObject();
        }
    }

    sealed class ModelBodyConverter : JsonConverter<ModelBody>
    {
        public override ModelBody Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            Start(ref reader);
            Base shape = default; double height = 0;
            while (Next(ref reader) is { } name)
            {
                switch (name)
                {
                    case "base": shape = B.Read(ref reader, typeof(Base), options); break;
                    case "height": height = Number(ref reader); break;
                    default: reader.Skip(); break;
                }
            }
            return new ModelBody(shape, height);
        }

        public override void Write(Utf8JsonWriter writer, ModelBody value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("base");
            B.Write(writer, value.Base, options);
            writer.WriteNumber("height", value.Height);
            writer.WriteEndObject();
        }
    }

    sealed class PlacementConverter : JsonConverter<Placement>
    {
        public override Placement Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            Start(ref reader);
            Point3 position = default; Yaw yaw = default;
            while (Next(ref reader) is { } name)
            {
                switch (name)
                {
                    case "position": position = P3.Read(ref reader, typeof(Point3), options); break;
                    case "yaw": yaw = Y.Read(ref reader, typeof(Yaw), options); break;
                    default: reader.Skip(); break;
                }
            }
            return new Placement(position, yaw);
        }

        public override void Write(Utf8JsonWriter writer, Placement value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("position");
            P3.Write(writer, value.Position, options);
            writer.WritePropertyName("yaw");
            Y.Write(writer, value.Yaw, options);
            writer.WriteEndObject();
        }
    }

    sealed class PlacedModelConverter : JsonConverter<PlacedModel>
    {
        public override PlacedModel Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            Start(ref reader);
            ModelBody body = default; Placement placement = default;
            while (Next(ref reader) is { } name)
            {
                switch (name)
                {
                    case "body": body = MB.Read(ref reader, typeof(ModelBody), options); break;
                    case "placement": placement = PL.Read(ref reader, typeof(Placement), options); break;
                    default: reader.Skip(); break;
                }
            }
            return new PlacedModel(body, placement);
        }

        public override void Write(Utf8JsonWriter writer, PlacedModel value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("body");
            MB.Write(writer, value.Body, options);
            writer.WritePropertyName("placement");
            PL.Write(writer, value.Placement, options);
            writer.WriteEndObject();
        }
    }

    sealed class ContactBodyConverter : JsonConverter<ContactBody>
    {
        public override ContactBody? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            Start(ref reader);
            Guid id = default, unit = default; ModelBody body = default; Placement? at = null;
            while (Next(ref reader) is { } name)
            {
                switch (name)
                {
                    case "id": id = Id(ref reader); break;
                    case "unitId": unit = Id(ref reader); break;
                    case "body": body = MB.Read(ref reader, typeof(ModelBody), options); break;
                    case "at": at = reader.TokenType == JsonTokenType.Null ? null : PL.Read(ref reader, typeof(Placement), options); break;
                    default: reader.Skip(); break;
                }
            }
            return new ContactBody(id, unit, body, at);
        }

        public override void Write(Utf8JsonWriter writer, ContactBody value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("unitId", value.UnitId);
            writer.WritePropertyName("body");
            MB.Write(writer, value.Body, options);
            if (value.At is { } at)
            {
                writer.WritePropertyName("at");
                PL.Write(writer, at, options);
            }
            writer.WriteEndObject();
        }
    }
}
